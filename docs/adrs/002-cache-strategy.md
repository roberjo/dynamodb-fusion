# ADR-002: Cache Strategy

## Status
**Accepted** - 2024-01-15

## Context

DynamoDB queries can be expensive in terms of:
- **Cost**: Read Capacity Units (RCUs) consumption
- **Performance**: Network latency for remote calls
- **Scalability**: Rate limiting and throttling concerns

We need a caching strategy that:
- Reduces DynamoDB costs and improves response times
- Supports both single-instance and distributed deployments
- Handles cache invalidation appropriately
- Maintains data consistency requirements
- Provides configurable cache behavior per table/query type

## Decision

We will implement a **multi-tiered caching strategy** with the following components:

1. **L1 Cache**: In-memory cache using `IMemoryCache` (per-instance)
2. **L2 Cache**: Distributed cache using `IDistributedCache` (shared across instances)
3. **Smart Cache Key Generation**: Hash-based keys with query fingerprinting
4. **Configurable TTL**: Per-table and per-query-type expiration policies
5. **Cache-Aside Pattern**: Application manages cache population and invalidation

## Rationale

### Multi-Tiered Approach Benefits:
1. **Performance**: L1 cache provides sub-millisecond access for frequently accessed data
2. **Scalability**: L2 cache enables sharing across multiple application instances
3. **Resilience**: Graceful degradation when distributed cache is unavailable
4. **Cost Efficiency**: Dramatic reduction in DynamoDB read operations

### Cache Key Strategy:
```csharp
// Cache key format: dynamo:{version}:{table}:{hash}
// Example: dynamo:v1:Users:a1b2c3d4e5f6
string cacheKey = $"dynamo:v{CacheVersion}:{tableName}:{ComputeHash(request)}";
```

### TTL Configuration:
- **Default TTL**: 5 minutes for standard queries
- **High-frequency queries**: 15 minutes (e.g., user profiles, product catalogs)
- **Real-time data**: 30 seconds (e.g., inventory levels, live metrics)
- **Reference data**: 1 hour (e.g., configuration settings, lookup tables)

## Alternatives Considered

### Single-Tier In-Memory Only
- **Pros**: Simplest implementation, fastest access
- **Cons**: Not suitable for multi-instance deployments, memory pressure
- **Verdict**: Rejected - Insufficient for production scalability

### Single-Tier Distributed Only
- **Pros**: Shared across instances, consistent state
- **Cons**: Network overhead for every cache access, single point of failure
- **Verdict**: Rejected - Performance impact too significant

### Write-Through Caching
- **Pros**: Always consistent, automatic cache population
- **Cons**: Complex to implement with DynamoDB's eventual consistency
- **Verdict**: Rejected - DynamoDB doesn't support traditional write-through patterns

### No Caching
- **Pros**: Always fresh data, simple implementation
- **Cons**: High DynamoDB costs, poor performance, scalability issues
- **Verdict**: Rejected - Core requirement for production viability

## Implementation Details

### Cache Configuration:
```csharp
public class DynamoDbCacheOptions
{
    public bool EnableL1Cache { get; set; } = true;
    public bool EnableL2Cache { get; set; } = true;
    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromMinutes(5);
    public Dictionary<string, TimeSpan> TableSpecificTtl { get; set; } = new();
    public Dictionary<string, TimeSpan> QueryTypeSpecificTtl { get; set; } = new();
    public int MaxL1CacheSize { get; set; } = 1000; // Number of entries
    public int MaxCacheKeyLength { get; set; } = 250; // Redis limitation
    public string CacheVersion { get; set; } = "v1"; // For cache invalidation
}
```

### Cache Key Generation:
```csharp
public class CacheKeyGenerator
{
    public string GenerateKey(DynamoDbQueryRequest request)
    {
        var keyComponents = new
        {
            Table = request.TableName,
            PartitionKey = request.PartitionKey,
            PartitionKeyValue = request.PartitionKeyValue,
            SortKey = request.SortKey,
            SortKeyValue = request.SortKeyValue,
            Filters = request.Filters.OrderBy(f => f.Key).ToDictionary(f => f.Key, f => f.Value),
            PageSize = request.Pagination.PageSize,
            IndexName = request.IndexName
            // Note: NextToken excluded to allow page caching
        };
        
        var json = JsonSerializer.Serialize(keyComponents);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        var hashString = Convert.ToHexString(hash)[..16]; // First 16 chars
        
        return $"dynamo:{CacheVersion}:{request.TableName}:{hashString}";
    }
}
```

### Cache Invalidation Strategy:
```csharp
public interface ICacheInvalidationService
{
    Task InvalidateTableAsync(string tableName);
    Task InvalidatePartitionAsync(string tableName, string partitionKeyValue);
    Task InvalidatePatternAsync(string pattern);
    Task InvalidateAllAsync();
}
```

## Consequences

### Positive:
- **Cost Reduction**: 70-90% reduction in DynamoDB read costs for cached queries
- **Performance**: Sub-100ms response times for cached data
- **Scalability**: Reduced load on DynamoDB, better handling of traffic spikes
- **Flexibility**: Configurable per table/query type requirements

### Negative:
- **Complexity**: Additional infrastructure dependency (Redis/ElastiCache)
- **Consistency**: Potential for stale data during cache TTL window
- **Memory Usage**: Additional memory requirements for L1 cache
- **Debugging**: Cache-related issues can be harder to troubleshoot

### Neutral:
- **Operational Overhead**: Need to monitor cache hit rates and performance
- **Configuration**: Additional configuration complexity for optimal tuning

## Monitoring & Metrics

Essential metrics to track:
- Cache hit/miss ratios (L1 and L2)
- Cache response times
- DynamoDB request reduction percentage
- Memory usage for L1 cache
- Distributed cache connection health

## Cache Warming Strategy

For critical high-traffic queries, implement proactive cache warming:
```csharp
public interface ICacheWarmingService
{
    Task WarmCacheAsync(string tableName, List<DynamoDbQueryRequest> commonQueries);
    Task ScheduleWarmupAsync(TimeSpan interval);
}
```

## Review Date
This decision should be reviewed after 6 months of production usage to assess effectiveness and optimize configuration based on real-world usage patterns. 