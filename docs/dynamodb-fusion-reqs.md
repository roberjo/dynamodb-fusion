# DynamoDB Query Service Requirements Document

## Overview
A generic ASP.NET Core 8 service for querying DynamoDB tables with filtering, pagination, and automatic DTO mapping from HTTP GET request query strings.

## Core Components

### 1. Main Service Interface
```csharp
public interface IDynamoDbQueryService
{
    Task<ApiResponse<PagedResult<TDto>>> QueryAsync<TDto>(
        DynamoDbQueryRequest request,
        CancellationToken cancellationToken = default
    ) where TDto : class, new();
}
```

### 2. Request Model
```csharp
public class DynamoDbQueryRequest
{
    public string TableName { get; set; }
    public string? PartitionKey { get; set; }
    public string? PartitionKeyValue { get; set; }
    public string? SortKey { get; set; }
    public string? SortKeyValue { get; set; }
    public Dictionary<string, FilterValue> Filters { get; set; } = new();
    public PaginationRequest Pagination { get; set; } = new();
    public bool ForceQuery { get; set; } = false; // true = Query, false = Scan
}
```

### 3. Response Models
```csharp
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public T? Data { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

public class PagedResult<T>
{
    public IEnumerable<T> Items { get; set; } = new List<T>();
    public PaginationMetadata Pagination { get; set; } = new();
}

public class PaginationMetadata
{
    public int PageSize { get; set; }
    public string? NextPageToken { get; set; }
    public bool HasNextPage { get; set; }
    public int? TotalCount { get; set; } // Estimated for large datasets
}
```

## Functional Requirements

### 1. Query String Processing
- **Filter Parsing**: Extract filters from query string parameters
- **Supported Operators**: 
  - `eq` (equals): `?name=John`
  - `contains`: `?name__contains=John`
  - `gte` (greater than equal): `?age__gte=18`
  - `lte` (less than equal): `?age__lte=65`
  - `between`: `?age__between=18,65`
- **Pagination Parameters**:
  - `pageSize`: Number of items per page (default: 20, max: 100)
  - `nextToken`: Base64 encoded LastEvaluatedKey for next page

### 2. Data Type Conversion Strategy
- **String to Target Type Mapping**:
  - Numbers: Parse using invariant culture
  - Booleans: Accept "true/false", "1/0", "yes/no" (case-insensitive)
  - Dates: Support ISO 8601 format, convert to Unix timestamp for DynamoDB
  - Lists: Comma-separated values for `between` operations
- **Type Detection**: Use DTO property types to determine target conversion
- **Validation**: Validate converted values against expected ranges/formats

### 3. Security & Validation
- **Input Sanitization**:
  - Validate table names against allowed characters (alphanumeric, underscore, hyphen)
  - Sanitize filter values to prevent injection attacks
  - Limit filter complexity (max 10 filters per request)
- **Value Validation**:
  - Validate numeric ranges
  - Validate date formats
  - Validate string lengths (max 1000 characters per filter value)

### 4. DynamoDB Operations
- **Query vs Scan Decision Logic**:
  - Use Query when PartitionKey is provided
  - Use Scan when no PartitionKey or ForceQuery is false
  - Log operation type for monitoring
- **Filter Expression Building**:
  - Build FilterExpression for Scan operations
  - Build KeyConditionExpression for Query operations
  - Use ExpressionAttributeNames and ExpressionAttributeValues
- **Pagination**:
  - Use DynamoDB's LastEvaluatedKey for pagination tokens
  - Base64 encode/decode pagination tokens for API safety
  - Handle empty results gracefully

### 5. DTO Mapping
- **Auto-mapping Strategy**:
  - Map DynamoDB attribute names to DTO property names (case-insensitive)
  - Support nested objects using dot notation in DynamoDB
  - Handle DynamoDB data types (S, N, B, SS, NS, BS, M, L, BOOL, NULL)
- **Complex Type Handling**:
  - Deserialize DynamoDB Maps (M) to nested DTO objects
  - Deserialize DynamoDB Lists (L) to collections
  - Handle nullable properties appropriately

### 6. Caching Strategy
- **Cache Key Strategy**: 
  - Hash of table name + filters + pagination parameters
  - Include cache version for invalidation
- **Cache Duration**: 
  - Default: 5 minutes for queries
  - Configurable per table via options
- **Cache Provider**: 
  - Support IMemoryCache and IDistributedCache
  - Prefer distributed cache for multi-instance deployments

### 7. Error Handling
- **DynamoDB Errors**:
  - Throttling: Implement exponential backoff retry
  - ResourceNotFound: Return appropriate 404 response
  - AccessDenied: Log security event, return generic error
- **Validation Errors**:
  - Return detailed validation messages for development
  - Return generic messages for production
- **Mapping Errors**:
  - Log detailed errors for debugging
  - Continue processing other items when possible

## Technical Requirements

### 1. Dependencies
- **Required Packages**:
  - AWSSDK.DynamoDBv2
  - Microsoft.Extensions.DependencyInjection
  - Microsoft.Extensions.Logging
  - Microsoft.Extensions.Caching.Memory
  - Microsoft.Extensions.Options
- **Framework**: ASP.NET Core 8.0

### 2. Configuration
```csharp
public class DynamoDbQueryOptions
{
    public int DefaultPageSize { get; set; } = 20;
    public int MaxPageSize { get; set; } = 100;
    public int MaxFiltersPerRequest { get; set; } = 10;
    public TimeSpan DefaultCacheDuration { get; set; } = TimeSpan.FromMinutes(5);
    public Dictionary<string, TimeSpan> TableCacheDurations { get; set; } = new();
    public bool EnableDetailedErrors { get; set; } = false;
    public int MaxRetryAttempts { get; set; } = 3;
}
```

### 3. Dependency Injection Setup
```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDynamoDbQueryService(
        this IServiceCollection services,
        Action<DynamoDbQueryOptions>? configureOptions = null)
    {
        // Register services and configurations
    }
}
```

### 4. Logging Requirements
- **Info Level**: Successful operations, cache hits/misses
- **Warning Level**: Throttling, retries, validation warnings
- **Error Level**: DynamoDB errors, mapping failures, unexpected exceptions
- **Debug Level**: Detailed filter expressions, timing information

### 5. Performance Requirements
- **Response Time**: < 500ms for cached results, < 2s for DynamoDB queries
- **Memory Usage**: Efficient object allocation, dispose resources properly
- **Concurrent Requests**: Support multiple simultaneous requests per table

## Usage Examples

### 1. Controller Integration
```csharp
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IDynamoDbQueryService _queryService;

    public UsersController(IDynamoDbQueryService queryService)
    {
        _queryService = queryService;
    }

    [HttpGet]
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
}
```

### 2. Query String Examples
```
GET /api/users?name=John&age__gte=18&status=active&pageSize=25
GET /api/users?email__contains=@company.com&createdDate__between=2023-01-01,2023-12-31
GET /api/orders?customerId=123&orderDate__gte=2023-01-01&nextToken=eyJ...
```

## Testing Requirements

### 1. Unit Tests
- Filter parsing and validation
- Data type conversion
- DTO mapping logic
- Error handling scenarios
- Cache behavior

### 2. Integration Tests
- DynamoDB operations (using LocalStack or DynamoDB Local)
- End-to-end controller scenarios
- Performance benchmarks

### 3. Mock Requirements
- IDynamoDBContext for unit testing
- IMemoryCache/IDistributedCache for caching tests

## Deployment Considerations

### 1. Configuration
- AWS credentials management
- DynamoDB endpoint configuration (local vs AWS)
- Cache provider selection
- Environment-specific options

### 2. Monitoring
- CloudWatch metrics integration
- Custom performance counters
- Health check endpoints

### 3. Security
- IAM role permissions for DynamoDB access
- Input validation and sanitization
- Rate limiting considerations
