# Migration Guide

Complete guide for migrating from direct AWS DynamoDB SDK usage to DynamoDB Fusion, with step-by-step instructions and code examples.

## Table of Contents

1. [Migration Overview](#migration-overview)
2. [Pre-Migration Assessment](#pre-migration-assessment)
3. [Basic SDK to Fusion Migration](#basic-sdk-to-fusion-migration)
4. [Advanced Migration Scenarios](#advanced-migration-scenarios)
5. [Data Access Layer Migration](#data-access-layer-migration)
6. [Testing Your Migration](#testing-your-migration)
7. [Performance Comparison](#performance-comparison)
8. [Rollback Strategy](#rollback-strategy)

## Migration Overview

### Why Migrate to DynamoDB Fusion?

| Benefit | Description |
|---------|-------------|
| **Simplified Querying** | HTTP query string to DynamoDB filter translation |
| **Built-in Caching** | Multi-level caching with configurable TTL |
| **Automatic Optimization** | Query vs Scan strategy selection |
| **Type Safety** | Strong typing with automatic DTO mapping |
| **Observability** | Built-in metrics, logging, and health checks |
| **Security** | Input validation, rate limiting, audit logging |

### Migration Phases

```
Phase 1: Assessment & Planning
    ↓
Phase 2: Setup & Configuration
    ↓
Phase 3: Basic Operations Migration
    ↓
Phase 4: Advanced Features Migration
    ↓
Phase 5: Testing & Validation
    ↓
Phase 6: Production Deployment
```

## Pre-Migration Assessment

### 1. Current Usage Analysis

Run this assessment script to understand your current DynamoDB usage:

```csharp
public class DynamoDbUsageAnalyzer
{
    public class UsageReport
    {
        public List<string> Tables { get; set; } = new();
        public List<string> Operations { get; set; } = new();
        public List<string> IndexesUsed { get; set; } = new();
        public Dictionary<string, int> QueryPatterns { get; set; } = new();
        public List<string> CustomLogic { get; set; } = new();
    }

    public async Task<UsageReport> AnalyzeCurrentUsageAsync()
    {
        var report = new UsageReport();

        // Analyze your existing codebase
        await AnalyzeTableUsage(report);
        await AnalyzeOperations(report);
        await AnalyzeIndexUsage(report);
        await AnalyzeQueryPatterns(report);
        await AnalyzeCustomLogic(report);

        return report;
    }

    private async Task AnalyzeTableUsage(UsageReport report)
    {
        // Scan your codebase for DynamoDB table references
        // Example: grep -r "TableName" . --include="*.cs"
        report.Tables.AddRange(new[] { "Users", "Orders", "Products" });
    }

    private async Task AnalyzeOperations(UsageReport report)
    {
        // Identify which DynamoDB operations you're using
        report.Operations.AddRange(new[] 
        { 
            "Query", "Scan", "GetItem", "PutItem", "UpdateItem", "DeleteItem",
            "BatchGetItem", "BatchWriteItem", "TransactWriteItems"
        });
    }
}
```

### 2. Compatibility Assessment

| DynamoDB Feature | Fusion Support | Migration Notes |
|------------------|----------------|-----------------|
| **Query Operations** | ✅ Full | Direct migration possible |
| **Scan Operations** | ✅ Full | Automatic optimization |
| **GetItem** | ✅ Via Query | Use partition key query |
| **BatchGetItem** | ✅ Via Batch | Use BatchQueryAsync |
| **Projections** | ✅ Full | Use ProjectionAttributes |
| **GSI/LSI** | ✅ Full | Automatic detection |
| **Transactions** | ❌ Not Supported | Keep direct SDK calls |
| **Streams** | ❌ Not Supported | Keep direct SDK calls |
| **PartiQL** | ❌ Not Supported | Migrate to filter syntax |

## Basic SDK to Fusion Migration

### 1. Setup and Configuration

#### Before (Direct SDK):
```csharp
// Startup.cs or Program.cs
services.AddSingleton<IAmazonDynamoDB>(provider =>
{
    var config = new AmazonDynamoDBConfig
    {
        RegionEndpoint = RegionEndpoint.USEast1
    };
    return new AmazonDynamoDBClient(config);
});

services.AddSingleton<IDynamoDBContext>(provider =>
{
    var client = provider.GetService<IAmazonDynamoDB>();
    return new DynamoDBContext(client);
});
```

#### After (DynamoDB Fusion):
```csharp
// Program.cs
builder.Services.AddDynamoDbQueryService(options =>
{
    options.DefaultPageSize = 20;
    options.MaxPageSize = 100;
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    
    // Optional: Configure for specific tables
    options.TableCacheDurations["Users"] = TimeSpan.FromMinutes(10);
    options.TableCacheDurations["Products"] = TimeSpan.FromMinutes(30);
});
```

### 2. Simple Query Migration

#### Before (Direct SDK):
```csharp
public class UserService
{
    private readonly IAmazonDynamoDB _dynamoDb;

    public async Task<List<User>> GetUsersByStatusAsync(string status)
    {
        var request = new ScanRequest
        {
            TableName = "Users",
            FilterExpression = "#status = :status",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#status"] = "Status"
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":status"] = new AttributeValue { S = status }
            }
        };

        var response = await _dynamoDb.ScanAsync(request);
        
        return response.Items.Select(item => new User
        {
            UserId = item["UserId"].S,
            Name = item["Name"].S,
            Status = item["Status"].S,
            Email = item.ContainsKey("Email") ? item["Email"].S : null
        }).ToList();
    }
}
```

#### After (DynamoDB Fusion):
```csharp
public class UserService
{
    private readonly IDynamoDbQueryService _queryService;

    public async Task<List<UserDto>> GetUsersByStatusAsync(string status)
    {
        var request = new DynamoDbQueryRequest
        {
            TableName = "Users",
            Filters = new Dictionary<string, FilterValue>
            {
                ["Status"] = new FilterValue 
                { 
                    Value = status, 
                    Operator = FilterOperator.Equals 
                }
            }
        };

        var result = await _queryService.QueryAsync<UserDto>(request);
        return result.Success ? result.Data?.Items.ToList() ?? new List<UserDto>() : new List<UserDto>();
    }
}
```

### 3. Query with Partition Key Migration

#### Before (Direct SDK):
```csharp
public async Task<List<Order>> GetUserOrdersAsync(string userId, DateTime? fromDate = null)
{
    var request = new QueryRequest
    {
        TableName = "Orders",
        KeyConditionExpression = "UserId = :userId",
        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
        {
            [":userId"] = new AttributeValue { S = userId }
        }
    };

    if (fromDate.HasValue)
    {
        request.FilterExpression = "CreatedDate >= :fromDate";
        request.ExpressionAttributeValues[":fromDate"] = new AttributeValue 
        { 
            S = fromDate.Value.ToString("yyyy-MM-dd") 
        };
    }

    var response = await _dynamoDb.QueryAsync(request);
    
    return response.Items.Select(MapToOrder).ToList();
}
```

#### After (DynamoDB Fusion):
```csharp
public async Task<List<OrderDto>> GetUserOrdersAsync(string userId, DateTime? fromDate = null)
{
    var request = new DynamoDbQueryRequest
    {
        TableName = "Orders",
        PartitionKey = "UserId",
        PartitionKeyValue = userId
    };

    if (fromDate.HasValue)
    {
        request.Filters["CreatedDate"] = new FilterValue
        {
            Value = fromDate.Value.ToString("yyyy-MM-dd"),
            Operator = FilterOperator.GreaterThanOrEqual
        };
    }

    var result = await _queryService.QueryAsync<OrderDto>(request);
    return result.Success ? result.Data?.Items.ToList() ?? new List<OrderDto>() : new List<OrderDto>();
}
```

### 4. HTTP Controller Migration

#### Before (Manual Parameter Handling):
```csharp
[HttpGet("users")]
public async Task<ActionResult<List<User>>> GetUsers(
    [FromQuery] string? status = null,
    [FromQuery] string? city = null,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 20)
{
    // Manual filter building
    var filters = new List<string>();
    var values = new Dictionary<string, AttributeValue>();
    
    if (!string.IsNullOrEmpty(status))
    {
        filters.Add("#status = :status");
        values[":status"] = new AttributeValue { S = status };
    }
    
    if (!string.IsNullOrEmpty(city))
    {
        filters.Add("#city = :city");
        values[":city"] = new AttributeValue { S = city };
    }

    var request = new ScanRequest
    {
        TableName = "Users",
        FilterExpression = string.Join(" AND ", filters),
        ExpressionAttributeValues = values,
        Limit = pageSize
    };

    var response = await _dynamoDb.ScanAsync(request);
    var users = response.Items.Select(MapToUser).ToList();
    
    return Ok(users);
}
```

#### After (Automatic Query String Processing):
```csharp
[HttpGet("users")]
public async Task<ActionResult<ApiResponse<PagedResult<UserDto>>>> GetUsers()
{
    var request = new DynamoDbQueryRequest
    {
        TableName = "Users",
        Filters = HttpContext.Request.Query.ToDynamoFilters(),
        Pagination = HttpContext.Request.Query.ToPaginationRequest()
    };

    var result = await _queryService.QueryAsync<UserDto>(request);
    return Ok(result);
}

// Usage examples:
// GET /users?status=active&city=Seattle&pageSize=25
// GET /users?age__gte=18&name__contains=John
// GET /users?createdDate__between=2023-01-01,2023-12-31
```

## Advanced Migration Scenarios

### 1. Complex Filtering Migration

#### Before (Complex Filter Expression):
```csharp
public async Task<List<Product>> GetProductsAsync(ProductFilter filter)
{
    var filterExpressions = new List<string>();
    var attributeValues = new Dictionary<string, AttributeValue>();
    var attributeNames = new Dictionary<string, string>();

    if (!string.IsNullOrEmpty(filter.Category))
    {
        filterExpressions.Add("#category = :category");
        attributeNames["#category"] = "Category";
        attributeValues[":category"] = new AttributeValue { S = filter.Category };
    }

    if (filter.MinPrice.HasValue)
    {
        filterExpressions.Add("#price >= :minPrice");
        attributeNames["#price"] = "Price";
        attributeValues[":minPrice"] = new AttributeValue { N = filter.MinPrice.Value.ToString() };
    }

    if (filter.MaxPrice.HasValue)
    {
        filterExpressions.Add("#price <= :maxPrice");
        attributeNames["#price"] = "Price";
        attributeValues[":maxPrice"] = new AttributeValue { N = filter.MaxPrice.Value.ToString() };
    }

    if (filter.Tags?.Any() == true)
    {
        filterExpressions.Add("contains(#tags, :tag)");
        attributeNames["#tags"] = "Tags";
        attributeValues[":tag"] = new AttributeValue { S = filter.Tags.First() };
    }

    var request = new ScanRequest
    {
        TableName = "Products",
        FilterExpression = string.Join(" AND ", filterExpressions),
        ExpressionAttributeNames = attributeNames,
        ExpressionAttributeValues = attributeValues
    };

    var response = await _dynamoDb.ScanAsync(request);
    return response.Items.Select(MapToProduct).ToList();
}
```

#### After (Simplified Filter Object):
```csharp
public async Task<List<ProductDto>> GetProductsAsync(ProductFilter filter)
{
    var request = new DynamoDbQueryRequest
    {
        TableName = "Products",
        Filters = new Dictionary<string, FilterValue>()
    };

    if (!string.IsNullOrEmpty(filter.Category))
    {
        request.Filters["Category"] = new FilterValue 
        { 
            Value = filter.Category, 
            Operator = FilterOperator.Equals 
        };
    }

    if (filter.MinPrice.HasValue && filter.MaxPrice.HasValue)
    {
        request.Filters["Price"] = new FilterValue
        {
            Value = filter.MinPrice.Value,
            Operator = FilterOperator.Between,
            AdditionalValues = new List<object> { filter.MaxPrice.Value }
        };
    }
    else if (filter.MinPrice.HasValue)
    {
        request.Filters["Price"] = new FilterValue
        {
            Value = filter.MinPrice.Value,
            Operator = FilterOperator.GreaterThanOrEqual
        };
    }

    if (filter.Tags?.Any() == true)
    {
        request.Filters["Tags"] = new FilterValue
        {
            Value = filter.Tags.First(),
            Operator = FilterOperator.Contains
        };
    }

    var result = await _queryService.QueryAsync<ProductDto>(request);
    return result.Success ? result.Data?.Items.ToList() ?? new List<ProductDto>() : new List<ProductDto>();
}
```

### 2. Pagination Migration

#### Before (Manual Pagination):
```csharp
public async Task<PagedResult<Order>> GetOrdersPagedAsync(
    string? lastEvaluatedKey = null, 
    int pageSize = 20)
{
    var request = new ScanRequest
    {
        TableName = "Orders",
        Limit = pageSize
    };

    if (!string.IsNullOrEmpty(lastEvaluatedKey))
    {
        // Manually decode the pagination token
        var keyBytes = Convert.FromBase64String(lastEvaluatedKey);
        var keyJson = Encoding.UTF8.GetString(keyBytes);
        request.ExclusiveStartKey = JsonSerializer.Deserialize<Dictionary<string, AttributeValue>>(keyJson);
    }

    var response = await _dynamoDb.ScanAsync(request);
    
    var nextToken = response.LastEvaluatedKey?.Any() == true
        ? Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response.LastEvaluatedKey)))
        : null;

    return new PagedResult<Order>
    {
        Items = response.Items.Select(MapToOrder).ToList(),
        NextPageToken = nextToken,
        HasNextPage = !string.IsNullOrEmpty(nextToken)
    };
}
```

#### After (Automatic Pagination):
```csharp
public async Task<ApiResponse<PagedResult<OrderDto>>> GetOrdersPagedAsync(
    string? nextToken = null, 
    int pageSize = 20)
{
    var request = new DynamoDbQueryRequest
    {
        TableName = "Orders",
        Pagination = new PaginationRequest
        {
            PageSize = pageSize,
            NextToken = nextToken
        }
    };

    return await _queryService.QueryAsync<OrderDto>(request);
}
```

### 3. Batch Operations Migration

#### Before (BatchGetItem):
```csharp
public async Task<List<User>> GetUsersBatchAsync(List<string> userIds)
{
    var requestItems = new Dictionary<string, KeysAndAttributes>
    {
        ["Users"] = new KeysAndAttributes
        {
            Keys = userIds.Select(id => new Dictionary<string, AttributeValue>
            {
                ["UserId"] = new AttributeValue { S = id }
            }).ToList()
        }
    };

    var request = new BatchGetItemRequest { RequestItems = requestItems };
    var response = await _dynamoDb.BatchGetItemAsync(request);
    
    return response.Responses["Users"].Select(MapToUser).ToList();
}
```

#### After (Batch Query):
```csharp
public async Task<List<UserDto>> GetUsersBatchAsync(List<string> userIds)
{
    var requests = userIds.Select(id => new DynamoDbQueryRequest
    {
        TableName = "Users",
        PartitionKey = "UserId",
        PartitionKeyValue = id
    }).ToList();

    var result = await _queryService.BatchQueryAsync<UserDto>(requests);
    return result.Success ? result.Data?.Results.SelectMany(r => r.Items).ToList() ?? new List<UserDto>() : new List<UserDto>();
}
```

## Data Access Layer Migration

### 1. Repository Pattern Migration

#### Before (Direct SDK Repository):
```csharp
public class UserRepository : IUserRepository
{
    private readonly IAmazonDynamoDB _dynamoDb;

    public async Task<User?> GetByIdAsync(string userId)
    {
        var request = new GetItemRequest
        {
            TableName = "Users",
            Key = new Dictionary<string, AttributeValue>
            {
                ["UserId"] = new AttributeValue { S = userId }
            }
        };

        var response = await _dynamoDb.GetItemAsync(request);
        return response.Item?.Any() == true ? MapToUser(response.Item) : null;
    }

    public async Task<List<User>> GetByStatusAsync(string status)
    {
        var request = new ScanRequest
        {
            TableName = "Users",
            FilterExpression = "#status = :status",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#status"] = "Status" },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":status"] = new AttributeValue { S = status } }
        };

        var response = await _dynamoDb.ScanAsync(request);
        return response.Items.Select(MapToUser).ToList();
    }
}
```

#### After (Fusion-based Repository):
```csharp
public class UserRepository : IUserRepository
{
    private readonly IDynamoDbQueryService _queryService;

    public async Task<UserDto?> GetByIdAsync(string userId)
    {
        var request = new DynamoDbQueryRequest
        {
            TableName = "Users",
            PartitionKey = "UserId",
            PartitionKeyValue = userId
        };

        var result = await _queryService.QueryAsync<UserDto>(request);
        return result.Success ? result.Data?.Items.FirstOrDefault() : null;
    }

    public async Task<List<UserDto>> GetByStatusAsync(string status)
    {
        var request = new DynamoDbQueryRequest
        {
            TableName = "Users",
            Filters = new Dictionary<string, FilterValue>
            {
                ["Status"] = new FilterValue { Value = status, Operator = FilterOperator.Equals }
            }
        };

        var result = await _queryService.QueryAsync<UserDto>(request);
        return result.Success ? result.Data?.Items.ToList() ?? new List<UserDto>() : new List<UserDto>();
    }
}
```

### 2. Service Layer Migration

#### Before (Complex Service Logic):
```csharp
public class OrderService
{
    private readonly IAmazonDynamoDB _dynamoDb;

    public async Task<OrderSummary> GetOrderSummaryAsync(string userId, DateTime fromDate, DateTime toDate)
    {
        // Multiple queries with complex logic
        var queryRequest = new QueryRequest
        {
            TableName = "Orders",
            KeyConditionExpression = "UserId = :userId",
            FilterExpression = "CreatedDate BETWEEN :fromDate AND :toDate",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":userId"] = new AttributeValue { S = userId },
                [":fromDate"] = new AttributeValue { S = fromDate.ToString("yyyy-MM-dd") },
                [":toDate"] = new AttributeValue { S = toDate.ToString("yyyy-MM-dd") }
            }
        };

        var response = await _dynamoDb.QueryAsync(queryRequest);
        var orders = response.Items.Select(MapToOrder).ToList();

        return new OrderSummary
        {
            TotalOrders = orders.Count,
            TotalAmount = orders.Sum(o => o.Amount),
            AverageOrderValue = orders.Any() ? orders.Average(o => o.Amount) : 0
        };
    }
}
```

#### After (Simplified Service):
```csharp
public class OrderService
{
    private readonly IDynamoDbQueryService _queryService;

    public async Task<OrderSummary> GetOrderSummaryAsync(string userId, DateTime fromDate, DateTime toDate)
    {
        var request = new DynamoDbQueryRequest
        {
            TableName = "Orders",
            PartitionKey = "UserId",
            PartitionKeyValue = userId,
            Filters = new Dictionary<string, FilterValue>
            {
                ["CreatedDate"] = new FilterValue
                {
                    Value = fromDate.ToString("yyyy-MM-dd"),
                    Operator = FilterOperator.Between,
                    AdditionalValues = new List<object> { toDate.ToString("yyyy-MM-dd") }
                }
            }
        };

        var result = await _queryService.QueryAsync<OrderDto>(request);
        
        if (!result.Success || result.Data?.Items == null)
            return new OrderSummary();

        var orders = result.Data.Items.ToList();
        return new OrderSummary
        {
            TotalOrders = orders.Count,
            TotalAmount = orders.Sum(o => o.Amount),
            AverageOrderValue = orders.Any() ? orders.Average(o => o.Amount) : 0
        };
    }
}
```

## Testing Your Migration

### 1. Migration Test Suite

```csharp
public class MigrationTests
{
    private readonly IDynamoDbQueryService _fusionService;
    private readonly IAmazonDynamoDB _legacyClient;

    [Fact]
    public async Task CompareQueryResults_ShouldReturnSameData()
    {
        // Arrange
        var userId = "test-user-123";

        // Legacy implementation
        var legacyRequest = new QueryRequest
        {
            TableName = "Users",
            KeyConditionExpression = "UserId = :userId",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":userId"] = new AttributeValue { S = userId }
            }
        };
        var legacyResponse = await _legacyClient.QueryAsync(legacyRequest);
        var legacyUsers = legacyResponse.Items.Select(MapToUser).ToList();

        // Fusion implementation
        var fusionRequest = new DynamoDbQueryRequest
        {
            TableName = "Users",
            PartitionKey = "UserId",
            PartitionKeyValue = userId
        };
        var fusionResult = await _fusionService.QueryAsync<UserDto>(fusionRequest);
        var fusionUsers = fusionResult.Data?.Items.ToList() ?? new List<UserDto>();

        // Assert
        Assert.Equal(legacyUsers.Count, fusionUsers.Count);
        
        for (int i = 0; i < legacyUsers.Count; i++)
        {
            Assert.Equal(legacyUsers[i].UserId, fusionUsers[i].UserId);
            Assert.Equal(legacyUsers[i].Name, fusionUsers[i].Name);
            Assert.Equal(legacyUsers[i].Email, fusionUsers[i].Email);
        }
    }

    [Fact]
    public async Task ComparePerformance_FusionShouldBeFasterWithCaching()
    {
        var request = new DynamoDbQueryRequest
        {
            TableName = "Products",
            Filters = new Dictionary<string, FilterValue>
            {
                ["Category"] = new FilterValue { Value = "Electronics", Operator = FilterOperator.Equals }
            }
        };

        // First call - cache miss
        var stopwatch1 = Stopwatch.StartNew();
        await _fusionService.QueryAsync<ProductDto>(request);
        stopwatch1.Stop();

        // Second call - cache hit
        var stopwatch2 = Stopwatch.StartNew();
        await _fusionService.QueryAsync<ProductDto>(request);
        stopwatch2.Stop();

        // Assert cache provides performance benefit
        Assert.True(stopwatch2.ElapsedMilliseconds < stopwatch1.ElapsedMilliseconds / 2);
    }
}
```

### 2. A/B Testing Strategy

```csharp
public class ABTestingController : ControllerBase
{
    private readonly IDynamoDbQueryService _fusionService;
    private readonly ILegacyUserService _legacyService;
    private readonly IFeatureToggle _featureToggle;

    [HttpGet("users/{userId}")]
    public async Task<ActionResult> GetUser(string userId)
    {
        if (_featureToggle.IsEnabled("UseDynamoDbFusion", userId))
        {
            // New Fusion implementation
            var request = new DynamoDbQueryRequest
            {
                TableName = "Users",
                PartitionKey = "UserId",
                PartitionKeyValue = userId
            };
            var result = await _fusionService.QueryAsync<UserDto>(request);
            return Ok(result);
        }
        else
        {
            // Legacy implementation
            var user = await _legacyService.GetUserAsync(userId);
            return Ok(user);
        }
    }
}
```

## Performance Comparison

### 1. Benchmarking Framework

```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class MigrationBenchmarks
{
    private IDynamoDbQueryService _fusionService;
    private IAmazonDynamoDB _legacyClient;

    [GlobalSetup]
    public void Setup()
    {
        // Initialize services
    }

    [Benchmark(Baseline = true)]
    public async Task<List<User>> LegacyQuery()
    {
        var request = new ScanRequest
        {
            TableName = "Users",
            FilterExpression = "#status = :status",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#status"] = "Status" },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":status"] = new AttributeValue { S = "active" } }
        };

        var response = await _legacyClient.ScanAsync(request);
        return response.Items.Select(MapToUser).ToList();
    }

    [Benchmark]
    public async Task<List<UserDto>> FusionQuery()
    {
        var request = new DynamoDbQueryRequest
        {
            TableName = "Users",
            Filters = new Dictionary<string, FilterValue>
            {
                ["Status"] = new FilterValue { Value = "active", Operator = FilterOperator.Equals }
            }
        };

        var result = await _fusionService.QueryAsync<UserDto>(request);
        return result.Data?.Items.ToList() ?? new List<UserDto>();
    }
}
```

## Rollback Strategy

### 1. Feature Toggle Implementation

```csharp
public class MigrationFeatureToggle
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<MigrationFeatureToggle> _logger;

    public bool ShouldUseFusion(string operation, string? userId = null)
    {
        // Global toggle
        if (!_configuration.GetValue<bool>("FeatureToggles:DynamoDbFusion:Enabled"))
            return false;

        // Per-operation toggle
        var operationKey = $"FeatureToggles:DynamoDbFusion:Operations:{operation}";
        if (!_configuration.GetValue<bool>(operationKey, true))
            return false;

        // User-specific toggle (for gradual rollout)
        if (!string.IsNullOrEmpty(userId))
        {
            var userHash = Math.Abs(userId.GetHashCode());
            var rolloutPercentage = _configuration.GetValue<int>("FeatureToggles:DynamoDbFusion:RolloutPercentage", 100);
            return (userHash % 100) < rolloutPercentage;
        }

        return true;
    }
}
```

### 2. Dual-Write Implementation

```csharp
public class DualModeService
{
    private readonly IDynamoDbQueryService _fusionService;
    private readonly ILegacyService _legacyService;
    private readonly MigrationFeatureToggle _featureToggle;

    public async Task<List<UserDto>> GetUsersAsync(string status)
    {
        if (_featureToggle.ShouldUseFusion("GetUsers"))
        {
            try
            {
                return await GetUsersVieFusion(status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fusion service failed, falling back to legacy");
                return await GetUsersViaLegacy(status);
            }
        }
        else
        {
            return await GetUsersViaLegacy(status);
        }
    }

    private async Task<List<UserDto>> GetUsersVieFusion(string status)
    {
        var request = new DynamoDbQueryRequest
        {
            TableName = "Users",
            Filters = new Dictionary<string, FilterValue>
            {
                ["Status"] = new FilterValue { Value = status, Operator = FilterOperator.Equals }
            }
        };

        var result = await _fusionService.QueryAsync<UserDto>(request);
        return result.Success ? result.Data?.Items.ToList() ?? new List<UserDto>() : new List<UserDto>();
    }

    private async Task<List<UserDto>> GetUsersViaLegacy(string status)
    {
        var users = await _legacyService.GetUsersByStatusAsync(status);
        return users.Select(MapToDto).ToList();
    }
}
```

This migration guide provides a systematic approach to transitioning from direct DynamoDB SDK usage to DynamoDB Fusion while maintaining reliability and performance throughout the migration process. 