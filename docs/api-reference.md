# DynamoDB Fusion API Reference

Complete API reference for the DynamoDB Fusion service with detailed interfaces, models, and usage examples.

## Core Interfaces

### IDynamoDbQueryService

The main service interface for querying DynamoDB tables.

```csharp
public interface IDynamoDbQueryService
{
    /// <summary>
    /// Executes a query against a DynamoDB table with automatic DTO mapping
    /// </summary>
    /// <typeparam name="TDto">The DTO type to map results to</typeparam>
    /// <param name="request">The query request parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated results wrapped in an API response</returns>
    Task<ApiResponse<PagedResult<TDto>>> QueryAsync<TDto>(
        DynamoDbQueryRequest request,
        CancellationToken cancellationToken = default
    ) where TDto : class, new();

    /// <summary>
    /// Streams query results for large datasets
    /// </summary>
    /// <typeparam name="TDto">The DTO type to map results to</typeparam>
    /// <param name="request">The query request parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Async enumerable of DTOs</returns>
    IAsyncEnumerable<TDto> StreamAsync<TDto>(
        DynamoDbQueryRequest request,
        CancellationToken cancellationToken = default
    ) where TDto : class, new();

    /// <summary>
    /// Executes multiple queries in batch
    /// </summary>
    /// <typeparam name="TDto">The DTO type to map results to</typeparam>
    /// <param name="requests">Collection of query requests</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Batch results</returns>
    Task<ApiResponse<BatchResult<TDto>>> BatchQueryAsync<TDto>(
        IEnumerable<DynamoDbQueryRequest> requests,
        CancellationToken cancellationToken = default
    ) where TDto : class, new();
}
```

## Request Models

### DynamoDbQueryRequest

Primary request model for all query operations.

```csharp
public class DynamoDbQueryRequest
{
    /// <summary>
    /// Name of the DynamoDB table to query
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Partition key attribute name
    /// </summary>
    public string? PartitionKey { get; set; }

    /// <summary>
    /// Partition key value for Query operations
    /// </summary>
    public string? PartitionKeyValue { get; set; }

    /// <summary>
    /// Sort key attribute name
    /// </summary>
    public string? SortKey { get; set; }

    /// <summary>
    /// Sort key value for Query operations
    /// </summary>
    public string? SortKeyValue { get; set; }

    /// <summary>
    /// Additional filters to apply
    /// </summary>
    public Dictionary<string, FilterValue> Filters { get; set; } = new();

    /// <summary>
    /// Pagination parameters
    /// </summary>
    public PaginationRequest Pagination { get; set; } = new();

    /// <summary>
    /// Query strategy override
    /// </summary>
    public QueryStrategy Strategy { get; set; } = QueryStrategy.AutoDetect;

    /// <summary>
    /// Global or Local Secondary Index name
    /// </summary>
    public string? IndexName { get; set; }

    /// <summary>
    /// Attributes to include in projection
    /// </summary>
    public List<string> ProjectionAttributes { get; set; } = new();

    /// <summary>
    /// Enable consistent read (Query operations only)
    /// </summary>
    public bool ConsistentRead { get; set; } = false;

    /// <summary>
    /// Sorting configuration
    /// </summary>
    public Dictionary<string, SortDirection> OrderBy { get; set; } = new();
}
```

### FilterValue

Represents a filter condition with operator and value.

```csharp
public class FilterValue
{
    /// <summary>
    /// The filter value
    /// </summary>
    public object Value { get; set; } = null!;

    /// <summary>
    /// The filter operator
    /// </summary>
    public FilterOperator Operator { get; set; } = FilterOperator.Equals;

    /// <summary>
    /// Additional values for operators like Between, In
    /// </summary>
    public List<object> AdditionalValues { get; set; } = new();
}

public enum FilterOperator
{
    Equals,
    NotEquals,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    Between,
    In,
    NotIn,
    Contains,
    BeginsWith,
    Exists,
    NotExists
}
```

### PaginationRequest

Pagination parameters for query requests.

```csharp
public class PaginationRequest
{
    /// <summary>
    /// Number of items per page (max: configured MaxPageSize)
    /// </summary>
    public int PageSize { get; set; } = 20;

    /// <summary>
    /// Base64 encoded pagination token from previous response
    /// </summary>
    public string? NextToken { get; set; }
}
```

## Response Models

### ApiResponse<T>

Standard response wrapper for all API operations.

```csharp
public class ApiResponse<T>
{
    /// <summary>
    /// Indicates if the operation was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Human-readable message describing the result
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// The response data
    /// </summary>
    public T? Data { get; set; }

    /// <summary>
    /// Validation errors (if any)
    /// </summary>
    public List<ValidationError> Errors { get; set; } = new();

    /// <summary>
    /// Additional metadata about the operation
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}
```

### PagedResult<T>

Paginated result set for query operations.

```csharp
public class PagedResult<T>
{
    /// <summary>
    /// The result items
    /// </summary>
    public IEnumerable<T> Items { get; set; } = new List<T>();

    /// <summary>
    /// Pagination metadata
    /// </summary>
    public PaginationMetadata Pagination { get; set; } = new();

    /// <summary>
    /// Query execution metadata
    /// </summary>
    public QueryMetadata Query { get; set; } = new();
}
```

### PaginationMetadata

Metadata about pagination state.

```csharp
public class PaginationMetadata
{
    /// <summary>
    /// Current page size
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Token for retrieving the next page
    /// </summary>
    public string? NextPageToken { get; set; }

    /// <summary>
    /// Indicates if more pages are available
    /// </summary>
    public bool HasNextPage { get; set; }

    /// <summary>
    /// Estimated total count (may be null for large datasets)
    /// </summary>
    public int? TotalCount { get; set; }

    /// <summary>
    /// Current page number (estimated)
    /// </summary>
    public int? CurrentPage { get; set; }
}
```

### QueryMetadata

Metadata about query execution.

```csharp
public class QueryMetadata
{
    /// <summary>
    /// Type of operation executed (Query or Scan)
    /// </summary>
    public string OperationType { get; set; } = string.Empty;

    /// <summary>
    /// Index used (if any)
    /// </summary>
    public string? IndexUsed { get; set; }

    /// <summary>
    /// Estimated cost of the operation
    /// </summary>
    public decimal EstimatedCost { get; set; }

    /// <summary>
    /// Execution time in milliseconds
    /// </summary>
    public long ExecutionTimeMs { get; set; }

    /// <summary>
    /// Number of items examined by DynamoDB
    /// </summary>
    public int ItemsExamined { get; set; }

    /// <summary>
    /// Number of items returned
    /// </summary>
    public int ItemsReturned { get; set; }

    /// <summary>
    /// Cache status
    /// </summary>
    public CacheStatus CacheStatus { get; set; }

    /// <summary>
    /// Cost or performance warnings
    /// </summary>
    public List<string> Warnings { get; set; } = new();
}

public enum CacheStatus
{
    Miss,
    Hit,
    Refresh,
    Disabled
}
```

## Extension Methods

### Query String Extensions

Extension methods for converting HTTP query strings to DynamoDB requests.

```csharp
public static class QueryStringExtensions
{
    /// <summary>
    /// Converts IQueryCollection to DynamoDB filters
    /// </summary>
    /// <param name="query">HTTP query collection</param>
    /// <returns>Dictionary of filters</returns>
    public static Dictionary<string, FilterValue> ToDynamoFilters(this IQueryCollection query)
    {
        var filters = new Dictionary<string, FilterValue>();
        
        foreach (var kvp in query)
        {
            if (IsReservedParameter(kvp.Key))
                continue;
                
            var (fieldName, operatorName) = ParseFieldName(kvp.Key);
            var filterOperator = ParseOperator(operatorName);
            
            filters[fieldName] = new FilterValue
            {
                Value = kvp.Value.First(),
                Operator = filterOperator,
                AdditionalValues = ParseAdditionalValues(kvp.Value, filterOperator)
            };
        }
        
        return filters;
    }

    /// <summary>
    /// Converts IQueryCollection to pagination request
    /// </summary>
    /// <param name="query">HTTP query collection</param>
    /// <returns>Pagination request</returns>
    public static PaginationRequest ToPaginationRequest(this IQueryCollection query)
    {
        var pagination = new PaginationRequest();
        
        if (query.TryGetValue("pageSize", out var pageSize) && int.TryParse(pageSize, out var size))
        {
            pagination.PageSize = size;
        }
        
        if (query.TryGetValue("nextToken", out var token))
        {
            pagination.NextToken = token;
        }
        
        return pagination;
    }
}
```

## Configuration

### DynamoDbQueryOptions

Main configuration options for the service.

```csharp
public class DynamoDbQueryOptions : IValidateOptions<DynamoDbQueryOptions>
{
    /// <summary>
    /// Default page size for queries
    /// </summary>
    public int DefaultPageSize { get; set; } = 20;

    /// <summary>
    /// Maximum allowed page size
    /// </summary>
    public int MaxPageSize { get; set; } = 100;

    /// <summary>
    /// Maximum number of filters per request
    /// </summary>
    public int MaxFiltersPerRequest { get; set; } = 10;

    /// <summary>
    /// Default cache duration
    /// </summary>
    public TimeSpan DefaultCacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Table-specific cache durations
    /// </summary>
    public Dictionary<string, TimeSpan> TableCacheDurations { get; set; } = new();

    /// <summary>
    /// Enable detailed error messages in responses
    /// </summary>
    public bool EnableDetailedErrors { get; set; } = false;

    /// <summary>
    /// Maximum retry attempts for failed operations
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Query strategy options
    /// </summary>
    public QueryStrategyOptions QueryStrategy { get; set; } = new();

    /// <summary>
    /// Security options
    /// </summary>
    public SecurityOptions Security { get; set; } = new();

    /// <summary>
    /// Cache options
    /// </summary>
    public CacheOptions Cache { get; set; } = new();

    /// <summary>
    /// Validates the configuration options
    /// </summary>
    public ValidateOptionsResult Validate(string name, DynamoDbQueryOptions options)
    {
        var failures = new List<string>();

        if (options.DefaultPageSize <= 0 || options.DefaultPageSize > options.MaxPageSize)
            failures.Add("DefaultPageSize must be between 1 and MaxPageSize");

        if (options.MaxPageSize <= 0 || options.MaxPageSize > 1000)
            failures.Add("MaxPageSize must be between 1 and 1000");

        if (options.MaxFiltersPerRequest <= 0 || options.MaxFiltersPerRequest > 50)
            failures.Add("MaxFiltersPerRequest must be between 1 and 50");

        return failures.Any() 
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
```

## Usage Examples

### Basic Query with Partition Key

```csharp
[HttpGet("users/{userId}")]
public async Task<ActionResult<ApiResponse<PagedResult<UserDto>>>> GetUserOrders(string userId)
{
    var request = new DynamoDbQueryRequest
    {
        TableName = "Orders",
        PartitionKey = "UserId",
        PartitionKeyValue = userId,
        Filters = HttpContext.Request.Query.ToDynamoFilters(),
        Pagination = HttpContext.Request.Query.ToPaginationRequest()
    };

    var result = await _queryService.QueryAsync<OrderDto>(request);
    return Ok(result);
}

// Example request: GET /users/123?status=active&orderDate__gte=2024-01-01&pageSize=25
```

### Complex Filtering with Multiple Operators

```csharp
[HttpGet("products")]
public async Task<ActionResult<ApiResponse<PagedResult<ProductDto>>>> GetProducts()
{
    var request = new DynamoDbQueryRequest
    {
        TableName = "Products",
        Filters = new Dictionary<string, FilterValue>
        {
            ["Category"] = new FilterValue 
            { 
                Value = "Electronics", 
                Operator = FilterOperator.Equals 
            },
            ["Price"] = new FilterValue 
            { 
                Value = 100, 
                Operator = FilterOperator.Between,
                AdditionalValues = new List<object> { 500 }
            },
            ["Tags"] = new FilterValue 
            { 
                Value = "smartphone", 
                Operator = FilterOperator.Contains 
            },
            ["InStock"] = new FilterValue 
            { 
                Value = true, 
                Operator = FilterOperator.Equals 
            }
        },
        Pagination = new PaginationRequest { PageSize = 50 },
        OrderBy = new Dictionary<string, SortDirection> 
        { 
            ["Price"] = SortDirection.Ascending 
        }
    };

    var result = await _queryService.QueryAsync<ProductDto>(request);
    return Ok(result);
}
```

### Streaming Large Result Sets

```csharp
[HttpGet("export/users")]
public async Task<IActionResult> ExportUsers()
{
    var request = new DynamoDbQueryRequest
    {
        TableName = "Users",
        Filters = new Dictionary<string, FilterValue>
        {
            ["Status"] = new FilterValue { Value = "active", Operator = FilterOperator.Equals }
        }
    };

    var stream = new MemoryStream();
    var writer = new StreamWriter(stream);
    
    await foreach (var user in _queryService.StreamAsync<UserDto>(request))
    {
        await writer.WriteLineAsync(JsonSerializer.Serialize(user));
    }
    
    await writer.FlushAsync();
    stream.Position = 0;
    
    return File(stream, "application/json", "users.jsonl");
}
```

### Batch Queries

```csharp
[HttpPost("batch-query")]
public async Task<ActionResult<ApiResponse<BatchResult<object>>>> BatchQuery(
    [FromBody] List<DynamoDbQueryRequest> requests)
{
    var result = await _queryService.BatchQueryAsync<object>(requests);
    return Ok(result);
}
```

## Error Handling

### Common Error Responses

```csharp
// Validation Error
{
  "success": false,
  "message": "Validation failed",
  "data": null,
  "errors": [
    {
      "field": "TableName",
      "message": "Table name is required",
      "errorCode": "REQUIRED_FIELD"
    }
  ]
}

// Resource Not Found
{
  "success": false,
  "message": "Table not found",
  "data": null,
  "errors": [],
  "metadata": {
    "tableName": "NonExistentTable"
  }
}

// Rate Limited
{
  "success": false,
  "message": "Request rate exceeded",
  "data": null,
  "errors": [],
  "metadata": {
    "retryAfter": 60,
    "limitType": "requests_per_minute"
  }
}
```

## HTTP Status Codes

| Status Code | Description | When Used |
|-------------|-------------|-----------|
| 200 | OK | Successful query with results |
| 400 | Bad Request | Invalid request parameters |
| 401 | Unauthorized | Authentication required |
| 403 | Forbidden | Insufficient permissions |
| 404 | Not Found | Table or resource not found |
| 429 | Too Many Requests | Rate limit exceeded |
| 500 | Internal Server Error | Unexpected server error |
| 503 | Service Unavailable | DynamoDB or cache unavailable | 