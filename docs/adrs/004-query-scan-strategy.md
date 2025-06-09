# ADR-004: Query vs Scan Strategy

## Status
**Accepted** - 2024-01-15

## Context

DynamoDB provides two primary operations for retrieving data:
- **Query**: Efficient retrieval using partition key (and optionally sort key)
- **Scan**: Examines every item in table or index

The choice between Query and Scan significantly impacts:
- **Performance**: Query is O(log n), Scan is O(n)
- **Cost**: Query consumes fewer RCUs, Scan can be very expensive
- **Scalability**: Query scales well, Scan performance degrades with table size

We need a strategy that:
- Automatically selects the most efficient operation
- Provides override capability for specific use cases
- Optimizes for cost and performance
- Supports both GSI and LSI operations
- Handles edge cases gracefully

## Decision

We will implement an **intelligent operation selection strategy** with the following logic:

1. **Primary Strategy**: Use Query when partition key is provided and available
2. **Index Strategy**: Automatically detect and use appropriate indexes (GSI/LSI)
3. **Fallback Strategy**: Use Scan only when Query is not possible
4. **Override Capability**: Allow explicit operation selection via `ForceQuery` parameter
5. **Cost Estimation**: Warn users about potentially expensive Scan operations

## Rationale

### Operation Selection Logic:
```csharp
public enum QueryStrategy
{
    AutoDetect,    // Default: intelligent selection
    ForceQuery,    // Force Query operation (will fail if no partition key)
    ForceScan,     // Force Scan operation
    CostOptimized  // Always choose least expensive option
}
```

### Decision Tree:
```
1. Is partition key provided?
   ├─ YES: Can we use Query?
   │   ├─ YES: Use Query (most efficient)
   │   └─ NO: Check for suitable GSI
   │       ├─ GSI available: Use Query on GSI
   │       └─ No GSI: Use Scan with warning
   └─ NO: Check for GSI opportunities
       ├─ Filter matches GSI partition key: Use Query on GSI
       └─ No GSI match: Use Scan with cost warning
```

### Cost Estimation:
- **Query Cost**: Predictable, based on items returned
- **Scan Cost**: Proportional to table size, can be expensive
- **Warning Threshold**: Warn when estimated Scan cost > $0.10

## Implementation Details

### Strategy Selector:
```csharp
public interface IQueryStrategySelector
{
    Task<QueryExecutionPlan> SelectStrategyAsync(DynamoDbQueryRequest request);
}

public class QueryExecutionPlan
{
    public QueryOperation Operation { get; set; } // Query or Scan
    public string? IndexName { get; set; }
    public string KeyConditionExpression { get; set; }
    public string? FilterExpression { get; set; }
    public Dictionary<string, AttributeValue> ExpressionAttributeValues { get; set; }
    public Dictionary<string, string> ExpressionAttributeNames { get; set; }
    public decimal EstimatedCost { get; set; }
    public string CostWarning { get; set; }
}

public class DefaultQueryStrategySelector : IQueryStrategySelector
{
    public async Task<QueryExecutionPlan> SelectStrategyAsync(DynamoDbQueryRequest request)
    {
        // 1. Check if partition key is available
        if (HasPartitionKey(request))
        {
            return await BuildQueryPlan(request);
        }
        
        // 2. Check for GSI opportunities
        var gsiPlan = await CheckGlobalSecondaryIndexes(request);
        if (gsiPlan != null)
        {
            return gsiPlan;
        }
        
        // 3. Fallback to Scan with cost warning
        return await BuildScanPlan(request);
    }
}
```

### Index Detection:
```csharp
public class IndexAnalyzer
{
    public async Task<List<IndexMetadata>> GetAvailableIndexesAsync(string tableName)
    {
        var tableDescription = await _dynamoClient.DescribeTableAsync(tableName);
        var indexes = new List<IndexMetadata>();
        
        // Add GSIs
        foreach (var gsi in tableDescription.Table.GlobalSecondaryIndexes ?? [])
        {
            indexes.Add(new IndexMetadata
            {
                Name = gsi.IndexName,
                Type = IndexType.GlobalSecondaryIndex,
                PartitionKey = gsi.KeySchema.First(k => k.KeyType == KeyType.HASH).AttributeName,
                SortKey = gsi.KeySchema.FirstOrDefault(k => k.KeyType == KeyType.RANGE)?.AttributeName
            });
        }
        
        // Add LSIs
        foreach (var lsi in tableDescription.Table.LocalSecondaryIndexes ?? [])
        {
            indexes.Add(new IndexMetadata
            {
                Name = lsi.IndexName,
                Type = IndexType.LocalSecondaryIndex,
                PartitionKey = tableDescription.Table.KeySchema.First(k => k.KeyType == KeyType.HASH).AttributeName,
                SortKey = lsi.KeySchema.First(k => k.KeyType == KeyType.RANGE).AttributeName
            });
        }
        
        return indexes;
    }
}
```

### Cost Estimation:
```csharp
public class CostEstimator
{
    public decimal EstimateQueryCost(QueryExecutionPlan plan, int estimatedResults)
    {
        // Query cost: (RCUs consumed) * (RCU price)
        var rcuConsumed = Math.Ceiling(estimatedResults * 0.5); // Assuming 4KB average item size
        return rcuConsumed * 0.00000025m; // $0.25 per million RCUs
    }
    
    public decimal EstimateScanCost(string tableName, long estimatedTableSize)
    {
        // Scan cost: (Table size in RCUs) * (RCU price)
        var rcuConsumed = Math.Ceiling(estimatedTableSize / 4096.0); // 4KB per RCU
        return rcuConsumed * 0.00000025m;
    }
}
```

## Alternatives Considered

### Always Use Query
- **Pros**: Best performance and cost when possible
- **Cons**: Limited functionality, fails when no partition key available
- **Verdict**: Rejected - Too restrictive for generic service

### Always Use Scan
- **Pros**: Maximum flexibility, works with any filter combination
- **Cons**: Poor performance, high cost, doesn't scale
- **Verdict**: Rejected - Unacceptable performance characteristics

### Manual Operation Selection Only
- **Pros**: Full control to developers, predictable behavior
- **Cons**: Requires deep DynamoDB knowledge, error-prone
- **Verdict**: Rejected - Defeats purpose of abstraction layer

### Query-First with Automatic Fallback
- **Pros**: Good balance of performance and flexibility
- **Cons**: May hide expensive operations from developers
- **Verdict**: Accepted with cost warnings

## Configuration Options

```csharp
public class QueryStrategyOptions
{
    public QueryStrategy DefaultStrategy { get; set; } = QueryStrategy.AutoDetect;
    public bool EnableGsiDetection { get; set; } = true;
    public bool EnableLsiDetection { get; set; } = true;
    public decimal ScanCostWarningThreshold { get; set; } = 0.10m; // $0.10
    public bool AllowExpensiveScans { get; set; } = false;
    public Dictionary<string, QueryStrategy> TableSpecificStrategies { get; set; } = new();
    public bool EnableCostEstimation { get; set; } = true;
}
```

## Performance Considerations

### Query Performance:
- **Partition Key + Sort Key**: O(1) - Fastest possible
- **Partition Key Only**: O(log n) - Very fast
- **GSI Query**: O(log n) - Fast but additional cost
- **LSI Query**: O(log n) - Fast, same partition

### Scan Performance:
- **Full Table Scan**: O(n) - Slow and expensive
- **Filtered Scan**: O(n) - Still scans entire table
- **Parallel Scan**: O(n/segments) - Faster but more expensive

### Optimization Strategies:
1. **Index Recommendations**: Suggest GSI creation for common filter patterns
2. **Query Caching**: Cache Query results more aggressively than Scan results
3. **Result Limiting**: Automatically limit Scan results to prevent runaway costs

## Monitoring & Metrics

Essential metrics to track:
- Query vs Scan operation ratios
- Average operation costs
- Performance metrics (latency, throughput)
- Index utilization rates
- Cost warning trigger rates

## Consequences

### Positive:
- **Automatic Optimization**: Users get best performance without DynamoDB expertise
- **Cost Control**: Warnings prevent unexpected high bills
- **Flexibility**: Supports both efficient and flexible query patterns
- **Transparency**: Clear indication of operation type and estimated costs

### Negative:
- **Complexity**: More logic to maintain and test
- **Hidden Costs**: Automatic fallback to Scan might surprise users
- **Performance Variance**: Same API call may have very different performance characteristics

### Neutral:
- **Learning Curve**: Users need to understand when operations are efficient
- **Monitoring Requirements**: Need to track operation patterns and costs

## Usage Examples

### Efficient Query (Partition Key Available):
```csharp
// Will use Query operation
var request = new DynamoDbQueryRequest
{
    TableName = "Users",
    PartitionKey = "UserId",
    PartitionKeyValue = "user123",
    Filters = new Dictionary<string, FilterValue>
    {
        ["Status"] = new FilterValue { Value = "active", Operator = FilterOperator.Equals }
    }
};
```

### GSI Query (Filter matches GSI):
```csharp
// Will use Query on GSI if Email-GSI exists
var request = new DynamoDbQueryRequest
{
    TableName = "Users",
    Filters = new Dictionary<string, FilterValue>
    {
        ["Email"] = new FilterValue { Value = "user@example.com", Operator = FilterOperator.Equals }
    }
};
```

### Scan with Warning (No partition key or GSI match):
```csharp
// Will use Scan with cost warning
var request = new DynamoDbQueryRequest
{
    TableName = "Users",
    Filters = new Dictionary<string, FilterValue>
    {
        ["LastLoginDate"] = new FilterValue { Value = "2024-01-01", Operator = FilterOperator.GreaterThanOrEqual }
    }
};
// Response will include: Metadata["CostWarning"] = "This operation requires a table scan..."
```

## Review Date
This decision should be reviewed after 3 months of production usage to analyze operation patterns and optimize strategy selection based on real-world usage. 