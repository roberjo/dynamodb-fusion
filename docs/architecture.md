# DynamoDB Fusion Architecture Overview

This document describes the system architecture, design patterns, and component relationships within the DynamoDB Fusion service.

## System Overview

DynamoDB Fusion is designed as a **layered architecture** with clear separation of concerns, following **Domain-Driven Design** principles and implementing **Cross-Cutting Concerns** as middleware and services.

```
┌─────────────────────────────────────────────────────────────┐
│                    HTTP/API Layer                           │
├─────────────────────────────────────────────────────────────┤
│                 Application Layer                           │
├─────────────────────────────────────────────────────────────┤
│                  Domain Layer                               │
├─────────────────────────────────────────────────────────────┤
│               Infrastructure Layer                          │
└─────────────────────────────────────────────────────────────┘
```

## Component Architecture

### High-Level Component Diagram

```
┌─────────────────┐    ┌──────────────────┐    ┌─────────────────┐
│   Controllers   │    │   Minimal APIs   │    │  Background     │
│                 │    │                  │    │  Services       │
└─────────┬───────┘    └─────────┬────────┘    └─────────┬───────┘
          │                      │                       │
          └──────────────────────┼───────────────────────┘
                                 │
                    ┌────────────▼────────────┐
                    │ IDynamoDbQueryService   │
                    └────────────┬────────────┘
                                 │
        ┌────────────────────────┼────────────────────────┐
        │                       │                        │
┌───────▼──────┐    ┌───────────▼────────┐    ┌─────────▼──────┐
│ Query        │    │ Cache              │    │ Authorization  │
│ Strategy     │    │ Service            │    │ Service        │
│ Selector     │    │                    │    │                │
└───────┬──────┘    └───────────┬────────┘    └─────────┬──────┘
        │                       │                       │
┌───────▼──────┐    ┌───────────▼────────┐    ┌─────────▼──────┐
│ Expression   │    │ Cache Key          │    │ Input          │
│ Builder      │    │ Generator          │    │ Validator      │
└───────┬──────┘    └────────────────────┘    └────────────────┘
        │
┌───────▼──────┐    ┌─────────────────────┐    ┌─────────────────┐
│ DynamoDB     │    │ Result Mapper       │    │ Audit Logger    │
│ Client       │    │                     │    │                 │
└──────────────┘    └─────────────────────┘    └─────────────────┘
```

## Layer Descriptions

### 1. HTTP/API Layer

**Responsibility**: Handle HTTP requests and responses, route to appropriate handlers.

**Components**:
- **Controllers**: Traditional MVC controllers for complex scenarios
- **Minimal APIs**: Lightweight endpoints for simple operations
- **Middleware**: Cross-cutting concerns (logging, error handling, rate limiting)

```csharp
// Example Controller
[ApiController]
[Route("api/[controller]")]
public class DynamoQueryController : ControllerBase
{
    private readonly IDynamoDbQueryService _queryService;
    
    [HttpGet("{tableName}")]
    public async Task<ActionResult<ApiResponse<PagedResult<object>>>> Query(
        string tableName)
    {
        var request = HttpContext.Request.ToDynamoDbQueryRequest(tableName);
        var result = await _queryService.QueryAsync<object>(request);
        return Ok(result);
    }
}

// Example Minimal API
app.MapGet("/api/query/{tableName}", async (
    string tableName,
    HttpContext context,
    IDynamoDbQueryService queryService) =>
{
    var request = context.Request.ToDynamoDbQueryRequest(tableName);
    var result = await queryService.QueryAsync<object>(request);
    return Results.Ok(result);
});
```

### 2. Application Layer

**Responsibility**: Orchestrate business operations, coordinate between domain services.

**Components**:
- **Query Service**: Main orchestration service
- **Request Handlers**: Process specific types of requests
- **Response Builders**: Construct API responses

```csharp
public class DynamoDbQueryService : IDynamoDbQueryService
{
    private readonly IQueryStrategySelector _strategySelector;
    private readonly ICacheService _cacheService;
    private readonly IAuthorizationService _authService;
    private readonly IInputValidator _validator;
    private readonly IAuditLogger _auditLogger;

    public async Task<ApiResponse<PagedResult<TDto>>> QueryAsync<TDto>(
        DynamoDbQueryRequest request,
        CancellationToken cancellationToken = default)
        where TDto : class, new()
    {
        // 1. Validate input
        var validationResult = await _validator.ValidateAsync(request);
        if (!validationResult.IsValid)
            return ApiResponse<PagedResult<TDto>>.ValidationError(validationResult.Errors);

        // 2. Authorize request
        var authResult = await _authService.AuthorizeAsync(request);
        if (!authResult.Authorized)
            return ApiResponse<PagedResult<TDto>>.Forbidden();

        // 3. Check cache
        var cacheKey = _cacheService.GenerateKey(request);
        var cachedResult = await _cacheService.GetAsync<PagedResult<TDto>>(cacheKey);
        if (cachedResult != null)
        {
            await _auditLogger.LogAsync(AuditEvent.CacheHit(request));
            return ApiResponse<PagedResult<TDto>>.Success(cachedResult);
        }

        // 4. Execute query
        var strategy = await _strategySelector.SelectStrategyAsync(request);
        var result = await strategy.ExecuteAsync<TDto>(request, cancellationToken);

        // 5. Cache result
        await _cacheService.SetAsync(cacheKey, result, GetCacheDuration(request));

        // 6. Audit log
        await _auditLogger.LogAsync(AuditEvent.QueryExecuted(request, result));

        return ApiResponse<PagedResult<TDto>>.Success(result);
    }
}
```

### 3. Domain Layer

**Responsibility**: Core business logic, domain models, and business rules.

**Components**:
- **Domain Models**: Core entities and value objects
- **Business Rules**: Domain-specific validation and logic
- **Domain Services**: Complex business operations

```csharp
// Domain Models
public class QueryRequest
{
    public TableName TableName { get; private set; }
    public PartitionKey? PartitionKey { get; private set; }
    public FilterCollection Filters { get; private set; }
    public PaginationSpec Pagination { get; private set; }

    public QueryRequest(string tableName)
    {
        TableName = new TableName(tableName);
        Filters = new FilterCollection();
        Pagination = PaginationSpec.Default();
    }

    public void AddFilter(string attributeName, FilterOperator op, object value)
    {
        // Business rules for filter validation
        if (Filters.Count >= MaxFiltersPerRequest)
            throw new DomainException("Too many filters");

        Filters.Add(new Filter(attributeName, op, value));
    }
}

// Value Objects
public class TableName
{
    public string Value { get; }

    public TableName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Table name cannot be empty");

        if (!IsValidTableName(value))
            throw new ArgumentException("Invalid table name format");

        Value = value;
    }

    private static bool IsValidTableName(string name) =>
        Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+$") && name.Length <= 255;
}
```

### 4. Infrastructure Layer

**Responsibility**: External concerns like data access, caching, logging.

**Components**:
- **DynamoDB Client**: AWS SDK integration
- **Cache Providers**: Redis, Memory cache implementations
- **Logging**: Structured logging implementation
- **Configuration**: Settings and options management

```csharp
// Infrastructure Services
public class DynamoDbClientWrapper : IDynamoDbClient
{
    private readonly AmazonDynamoDBClient _client;
    private readonly ILogger<DynamoDbClientWrapper> _logger;

    public async Task<QueryResponse> QueryAsync(QueryRequest request)
    {
        using var activity = Activity.StartActivity("DynamoDB.Query");
        activity?.SetTag("TableName", request.TableName);

        try
        {
            var dynamoRequest = request.ToDynamoDbRequest();
            var response = await _client.QueryAsync(dynamoRequest);
            
            _logger.LogInformation("Query executed successfully for table {TableName}", 
                request.TableName);
            
            return response.ToQueryResponse();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Query failed for table {TableName}", request.TableName);
            throw;
        }
    }
}
```

## Design Patterns

### 1. Strategy Pattern

Used for query strategy selection (Query vs Scan) and cache providers.

```csharp
public interface IQueryStrategy
{
    Task<QueryResult> ExecuteAsync(QueryRequest request);
    bool CanHandle(QueryRequest request);
}

public class QueryStrategySelector : IQueryStrategySelector
{
    private readonly IEnumerable<IQueryStrategy> _strategies;

    public async Task<IQueryStrategy> SelectAsync(QueryRequest request)
    {
        return _strategies.FirstOrDefault(s => s.CanHandle(request))
            ?? throw new InvalidOperationException("No suitable strategy found");
    }
}
```

### 2. Factory Pattern

Used for creating configured services and clients.

```csharp
public interface IDynamoDbClientFactory
{
    IAmazonDynamoDB CreateClient(DynamoDbConnectionOptions options);
}

public class DynamoDbClientFactory : IDynamoDbClientFactory
{
    public IAmazonDynamoDB CreateClient(DynamoDbConnectionOptions options)
    {
        var config = new AmazonDynamoDBConfig
        {
            ServiceURL = options.ServiceUrl,
            RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region)
        };

        return options.UseLocalDynamoDb
            ? new AmazonDynamoDBClient("dummy", "dummy", config)
            : new AmazonDynamoDBClient(config);
    }
}
```

### 3. Decorator Pattern

Used for adding cross-cutting concerns like caching, logging, and metrics.

```csharp
public class CachedQueryService : IDynamoDbQueryService
{
    private readonly IDynamoDbQueryService _inner;
    private readonly ICacheService _cache;

    public async Task<ApiResponse<PagedResult<TDto>>> QueryAsync<TDto>(
        DynamoDbQueryRequest request,
        CancellationToken cancellationToken = default)
        where TDto : class, new()
    {
        var cacheKey = GenerateCacheKey(request);
        var cached = await _cache.GetAsync<ApiResponse<PagedResult<TDto>>>(cacheKey);
        
        if (cached != null)
            return cached;

        var result = await _inner.QueryAsync<TDto>(request, cancellationToken);
        
        if (result.Success)
            await _cache.SetAsync(cacheKey, result, GetCacheDuration(request));

        return result;
    }
}
```

### 4. Repository Pattern

Abstraction over DynamoDB operations.

```csharp
public interface IQueryRepository
{
    Task<QueryResult> QueryAsync(QuerySpecification spec);
    Task<ScanResult> ScanAsync(ScanSpecification spec);
}

public class DynamoDbQueryRepository : IQueryRepository
{
    private readonly IAmazonDynamoDB _client;
    private readonly IExpressionBuilder _expressionBuilder;

    public async Task<QueryResult> QueryAsync(QuerySpecification spec)
    {
        var request = new QueryRequest
        {
            TableName = spec.TableName,
            KeyConditionExpression = _expressionBuilder.BuildKeyCondition(spec),
            FilterExpression = _expressionBuilder.BuildFilterExpression(spec.Filters),
            ExpressionAttributeNames = _expressionBuilder.GetAttributeNames(spec),
            ExpressionAttributeValues = _expressionBuilder.GetAttributeValues(spec),
            Limit = spec.Limit,
            ExclusiveStartKey = spec.ExclusiveStartKey
        };

        var response = await _client.QueryAsync(request);
        return new QueryResult(response);
    }
}
```

## Cross-Cutting Concerns

### 1. Caching Architecture

```
┌─────────────────┐    ┌─────────────────┐
│   L1 Cache      │    │   L2 Cache      │
│  (In-Memory)    │    │ (Distributed)   │
└─────────┬───────┘    └─────────┬───────┘
          │                      │
          └──────────┬───────────┘
                     │
          ┌──────────▼───────────┐
          │  Cache Coordinator   │
          └──────────────────────┘
```

### 2. Security Pipeline

```
Request → Authentication → Authorization → Input Validation → Rate Limiting → Query Execution
```

### 3. Monitoring & Observability

```csharp
public class ObservabilityMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        using var activity = Activity.StartActivity("DynamoQuery.Request");
        
        // Add telemetry tags
        activity?.SetTag("http.method", context.Request.Method);
        activity?.SetTag("http.route", context.Request.Path);
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            await next(context);
            
            // Record metrics
            Metrics.RequestDuration.Record(stopwatch.ElapsedMilliseconds,
                new KeyValuePair<string, object>("status", context.Response.StatusCode));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
```

## Configuration Architecture

### Hierarchical Configuration

```
Default Settings → Environment Variables → appsettings.json → Command Line Arguments
```

```csharp
public class DynamoDbFusionConfiguration
{
    public DynamoDbOptions DynamoDb { get; set; } = new();
    public CacheOptions Cache { get; set; } = new();
    public SecurityOptions Security { get; set; } = new();
    public ObservabilityOptions Observability { get; set; } = new();
}

// Usage in DI container
services.Configure<DynamoDbFusionConfiguration>(
    builder.Configuration.GetSection("DynamoDbFusion"));
```

## Scalability Considerations

### 1. Horizontal Scaling
- **Stateless Design**: No server-side session state
- **Load Balancer Compatible**: Health checks and graceful shutdown
- **Distributed Caching**: Shared cache across instances

### 2. Vertical Scaling
- **Async Operations**: Non-blocking I/O throughout
- **Connection Pooling**: Efficient resource utilization
- **Memory Management**: Proper disposal and garbage collection

### 3. Data Partitioning
- **Cache Sharding**: Distribute cache load across multiple Redis instances
- **Table-Specific Configuration**: Different settings per table
- **Regional Deployment**: Deploy close to DynamoDB regions

## Error Handling Architecture

### Error Classification

```csharp
public abstract class DynamoFusionException : Exception
{
    public abstract string ErrorCode { get; }
    public abstract bool IsRetryable { get; }
}

public class ValidationException : DynamoFusionException
{
    public override string ErrorCode => "VALIDATION_ERROR";
    public override bool IsRetryable => false;
}

public class ThrottlingException : DynamoFusionException
{
    public override string ErrorCode => "THROTTLING_ERROR";
    public override bool IsRetryable => true;
}
```

### Global Error Handler

```csharp
public class GlobalExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var response = exception switch
        {
            ValidationException ex => CreateValidationErrorResponse(ex),
            UnauthorizedException ex => CreateUnauthorizedResponse(ex),
            ThrottlingException ex => CreateThrottledResponse(ex),
            _ => CreateGenericErrorResponse(exception)
        };

        httpContext.Response.StatusCode = response.StatusCode;
        await httpContext.Response.WriteAsJsonAsync(response.Body);
        
        return true;
    }
}
```

## Performance Architecture

### Query Optimization Pipeline

```
Request → Cache Check → Query Strategy Selection → Expression Optimization → DynamoDB Execution → Result Caching
```

### Metrics Collection

```csharp
public static class Metrics
{
    public static readonly Histogram<double> QueryDuration = 
        Meter.CreateHistogram<double>("dynamodb_fusion_query_duration_ms");
    
    public static readonly Counter<long> CacheHits = 
        Meter.CreateCounter<long>("dynamodb_fusion_cache_hits");
    
    public static readonly Counter<long> CacheMisses = 
        Meter.CreateCounter<long>("dynamodb_fusion_cache_misses");
}
```

This architecture provides a solid foundation for a scalable, maintainable, and performant DynamoDB query service while maintaining clear separation of concerns and following established design patterns. 