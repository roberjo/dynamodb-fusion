# Performance Guide

Comprehensive guide for optimizing and tuning DynamoDB Fusion performance across different scenarios and environments.

## Table of Contents

1. [Performance Overview](#performance-overview)
2. [DynamoDB Optimization](#dynamodb-optimization)
3. [Caching Strategies](#caching-strategies)
4. [Application Performance](#application-performance)
5. [Infrastructure Tuning](#infrastructure-tuning)
6. [Monitoring & Profiling](#monitoring--profiling)
7. [Load Testing](#load-testing)
8. [Performance Troubleshooting](#performance-troubleshooting)

## Performance Overview

### Performance Targets

| Metric | Target | Acceptable | Poor |
|--------|--------|------------|------|
| **Response Time (Cached)** | < 50ms | < 100ms | > 200ms |
| **Response Time (Uncached)** | < 500ms | < 1000ms | > 2000ms |
| **Throughput** | > 1000 RPS | > 500 RPS | < 100 RPS |
| **Cache Hit Rate** | > 80% | > 60% | < 40% |
| **Memory Usage** | < 200MB | < 500MB | > 1GB |
| **CPU Usage** | < 50% | < 70% | > 90% |

### Performance Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Request Flow                             │
└─────────────────────────────────────────────────────────────┘

Request → Input Validation → Authorization → L1 Cache Check → L2 Cache Check → DynamoDB → Response

         ↑ 1-5ms            ↑ 5-10ms      ↑ < 1ms         ↑ 5-15ms        ↑ 50-200ms
```

## DynamoDB Optimization

### 1. Table Design Best Practices

#### Partition Key Distribution
```csharp
// ❌ Poor: Hotspotting due to sequential keys
public class OrderDto
{
    public string OrderId { get; set; } = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
}

// ✅ Good: Even distribution with compound key
public class OrderDto
{
    public string OrderId { get; set; } = $"{Guid.NewGuid()}-{DateTime.UtcNow:yyyyMMdd}";
}
```

#### Global Secondary Index (GSI) Strategy
```csharp
public class UserQueryOptimizer
{
    // Recommend GSI based on common query patterns
    public List<GSIRecommendation> AnalyzeQueryPatterns(List<DynamoDbQueryRequest> queries)
    {
        var filterUsage = queries
            .SelectMany(q => q.Filters.Keys)
            .GroupBy(k => k)
            .OrderByDescending(g => g.Count())
            .Take(5);

        return filterUsage.Select(usage => new GSIRecommendation
        {
            PartitionKey = usage.Key,
            Usage = usage.Count(),
            EstimatedCostReduction = CalculateCostReduction(usage.Count())
        }).ToList();
    }
}
```

### 2. Query Optimization

#### Efficient Query Construction
```csharp
public class OptimizedQueryBuilder
{
    public DynamoDbQueryRequest OptimizeQuery(DynamoDbQueryRequest request)
    {
        // 1. Use Query instead of Scan when possible
        if (CanUseQuery(request))
        {
            return ConvertToQuery(request);
        }

        // 2. Minimize projected attributes
        if (request.ProjectionAttributes.Count == 0)
        {
            request.ProjectionAttributes = GetEssentialAttributes(request);
        }

        // 3. Optimize filter order (most selective first)
        request.Filters = OrderFiltersBySelectivity(request.Filters);

        return request;
    }

    private bool CanUseQuery(DynamoDbQueryRequest request)
    {
        // Check if we have partition key or suitable GSI
        return !string.IsNullOrEmpty(request.PartitionKeyValue) ||
               GetSuitableGSI(request) != null;
    }
}
```

#### Batch Query Optimization
```csharp
public class BatchQueryOptimizer
{
    private readonly IDynamoDbQueryService _queryService;
    private readonly SemaphoreSlim _semaphore;

    public BatchQueryOptimizer(IDynamoDbQueryService queryService)
    {
        _queryService = queryService;
        _semaphore = new SemaphoreSlim(10); // Limit concurrent requests
    }

    public async Task<List<TDto>> ExecuteBatchAsync<TDto>(
        List<DynamoDbQueryRequest> requests)
        where TDto : class, new()
    {
        // Group by table for better connection reuse
        var groupedRequests = requests.GroupBy(r => r.TableName);
        var tasks = new List<Task<List<TDto>>>();

        foreach (var group in groupedRequests)
        {
            tasks.Add(ExecuteTableBatchAsync<TDto>(group.ToList()));
        }

        var results = await Task.WhenAll(tasks);
        return results.SelectMany(r => r).ToList();
    }

    private async Task<List<TDto>> ExecuteTableBatchAsync<TDto>(
        List<DynamoDbQueryRequest> requests)
        where TDto : class, new()
    {
        var semaphoreTasks = requests.Select(async request =>
        {
            await _semaphore.WaitAsync();
            try
            {
                var result = await _queryService.QueryAsync<TDto>(request);
                return result.Data?.Items ?? Enumerable.Empty<TDto>();
            }
            finally
            {
                _semaphore.Release();
            }
        });

        var results = await Task.WhenAll(semaphoreTasks);
        return results.SelectMany(r => r).ToList();
    }
}
```

### 3. Connection Pool Optimization

```csharp
public class DynamoDbClientConfiguration
{
    public static AmazonDynamoDBClient CreateOptimizedClient(DynamoDbOptions options)
    {
        var config = new AmazonDynamoDBConfig
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region),
            MaxErrorRetry = 3,
            Timeout = TimeSpan.FromSeconds(30),
            ReadWriteTimeout = TimeSpan.FromSeconds(30),
            
            // Connection pooling optimization
            ConnectionLimit = 50,
            MaxIdleTime = TimeSpan.FromMinutes(5),
            
            // HTTP client optimization
            UseHttp2 = true,
            DisableLogging = true // Disable SDK logging in production
        };

        return new AmazonDynamoDBClient(config);
    }
}
```

## Caching Strategies

### 1. Multi-Level Cache Optimization

```csharp
public class OptimizedCacheService : ICacheService
{
    private readonly IMemoryCache _l1Cache;
    private readonly IDistributedCache _l2Cache;
    private readonly CacheMetrics _metrics;

    public async Task<T?> GetAsync<T>(string key)
    {
        // L1 Cache (fastest)
        if (_l1Cache.TryGetValue(key, out T? l1Value))
        {
            _metrics.RecordL1Hit();
            return l1Value;
        }

        // L2 Cache (fast)
        var l2Data = await _l2Cache.GetAsync(key);
        if (l2Data != null)
        {
            var l2Value = JsonSerializer.Deserialize<T>(l2Data);
            
            // Populate L1 cache for next time
            _l1Cache.Set(key, l2Value, TimeSpan.FromMinutes(5));
            
            _metrics.RecordL2Hit();
            return l2Value;
        }

        _metrics.RecordCacheMiss();
        return default(T);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan expiry)
    {
        // Set in both caches
        _l1Cache.Set(key, value, TimeSpan.FromMinutes(5));
        
        var jsonData = JsonSerializer.SerializeToUtf8Bytes(value);
        await _l2Cache.SetAsync(key, jsonData, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiry
        });
    }
}
```

### 2. Cache Key Optimization

```csharp
public class OptimizedCacheKeyGenerator
{
    private readonly IMemoryCache _keyCache;

    public string GenerateKey(DynamoDbQueryRequest request)
    {
        // Use a lightweight hash for frequently used patterns
        var quickKey = $"{request.TableName}:{request.PartitionKeyValue}";
        
        if (_keyCache.TryGetValue(quickKey, out string? cachedKey))
        {
            return cachedKey!;
        }

        // Full hash for complex queries
        var keyComponents = new
        {
            Table = request.TableName,
            PartitionKey = request.PartitionKeyValue,
            SortKey = request.SortKeyValue,
            Filters = request.Filters
                .OrderBy(f => f.Key)
                .ToDictionary(f => f.Key, f => f.Value),
            PageSize = request.Pagination.PageSize,
            IndexName = request.IndexName
        };

        var json = JsonSerializer.Serialize(keyComponents, JsonOptions.Optimized);
        var hash = ComputeFastHash(json);
        var fullKey = $"ddb:v1:{request.TableName}:{hash}";

        // Cache the key mapping for future use
        _keyCache.Set(quickKey, fullKey, TimeSpan.FromMinutes(10));

        return fullKey;
    }

    private static string ComputeFastHash(string input)
    {
        // Use xxHash or similar fast hashing algorithm
        var hash = XxHash64.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(BitConverter.GetBytes(hash))[..16];
    }
}
```

### 3. Cache Warming Strategies

```csharp
public class CacheWarmingService : BackgroundService
{
    private readonly IDynamoDbQueryService _queryService;
    private readonly ICacheService _cacheService;
    private readonly ILogger<CacheWarmingService> _logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await WarmCriticalQueries();
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cache warming failed");
            }
        }
    }

    private async Task WarmCriticalQueries()
    {
        var criticalQueries = GetCriticalQueries();
        
        var warmingTasks = criticalQueries.Select(async query =>
        {
            try
            {
                await _queryService.QueryAsync<object>(query);
                _logger.LogDebug("Warmed cache for table {TableName}", query.TableName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to warm cache for table {TableName}", query.TableName);
            }
        });

        await Task.WhenAll(warmingTasks);
    }

    private List<DynamoDbQueryRequest> GetCriticalQueries()
    {
        // Return list of high-frequency queries that should be pre-cached
        return new List<DynamoDbQueryRequest>
        {
            new() { TableName = "Users", /* common user queries */ },
            new() { TableName = "Products", /* popular product queries */ }
        };
    }
}
```

## Application Performance

### 1. JSON Serialization Optimization

```csharp
public static class JsonOptions
{
    public static readonly JsonSerializerOptions Optimized = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        
        // Performance optimizations
        IncludeFields = false,
        IgnoreReadOnlyFields = true,
        IgnoreReadOnlyProperties = true,
        
        // Use source generators for better performance
        TypeInfoResolver = JsonSerializer.IsReflectionEnabledByDefault 
            ? new DefaultJsonTypeInfoResolver() 
            : null
    };
}

// Use source generators for DTOs
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(UserDto))]
[JsonSerializable(typeof(OrderDto))]
[JsonSerializable(typeof(ProductDto))]
public partial class DtoJsonContext : JsonSerializerContext
{
}
```

### 2. Memory Management

```csharp
public class MemoryOptimizedMapper
{
    private readonly ObjectPool<StringBuilder> _stringBuilderPool;
    private readonly ArrayPool<byte> _byteArrayPool;

    public MemoryOptimizedMapper()
    {
        _stringBuilderPool = new DefaultObjectPool<StringBuilder>(
            new StringBuilderPooledObjectPolicy());
        _byteArrayPool = ArrayPool<byte>.Shared;
    }

    public TDto MapFromDynamoDb<TDto>(Dictionary<string, AttributeValue> item)
        where TDto : class, new()
    {
        // Use object pooling to reduce GC pressure
        var stringBuilder = _stringBuilderPool.Get();
        try
        {
            // Build JSON using pooled StringBuilder
            var json = BuildJsonFromAttributeValues(item, stringBuilder);
            
            // Deserialize using optimized options
            return JsonSerializer.Deserialize<TDto>(json, JsonOptions.Optimized)!;
        }
        finally
        {
            _stringBuilderPool.Return(stringBuilder);
        }
    }

    public async Task<List<TDto>> MapBatchAsync<TDto>(
        IEnumerable<Dictionary<string, AttributeValue>> items)
        where TDto : class, new()
    {
        // Process in parallel for large batches
        var itemArray = items.ToArray();
        if (itemArray.Length > 100)
        {
            return await MapLargeBatchAsync<TDto>(itemArray);
        }

        return itemArray.Select(MapFromDynamoDb<TDto>).ToList();
    }

    private async Task<List<TDto>> MapLargeBatchAsync<TDto>(
        Dictionary<string, AttributeValue>[] items)
        where TDto : class, new()
    {
        var tasks = items
            .AsParallel()
            .WithDegreeOfParallelism(Environment.ProcessorCount)
            .Select(item => Task.Run(() => MapFromDynamoDb<TDto>(item)))
            .ToArray();

        return (await Task.WhenAll(tasks)).ToList();
    }
}
```

### 3. Async/Await Optimization

```csharp
public class AsyncOptimizedService
{
    private readonly IDynamoDbQueryService _queryService;
    private readonly SemaphoreSlim _throttle;

    public AsyncOptimizedService(IDynamoDbQueryService queryService)
    {
        _queryService = queryService;
        _throttle = new SemaphoreSlim(10); // Limit concurrent operations
    }

    // ✅ Good: ConfigureAwait(false) for library code
    public async Task<List<TDto>> GetMultipleAsync<TDto>(List<string> ids)
        where TDto : class, new()
    {
        var tasks = ids.Select(id => GetSingleAsync<TDto>(id)).ToArray();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.Where(r => r != null).ToList()!;
    }

    // ✅ Good: Proper exception handling with async
    private async Task<TDto?> GetSingleAsync<TDto>(string id)
        where TDto : class, new()
    {
        await _throttle.WaitAsync().ConfigureAwait(false);
        try
        {
            var request = CreateRequest(id);
            var result = await _queryService.QueryAsync<TDto>(request).ConfigureAwait(false);
            return result.Success ? result.Data?.Items.FirstOrDefault() : null;
        }
        catch (Exception ex) when (IsTransientError(ex))
        {
            // Log and return null for transient errors
            return null;
        }
        finally
        {
            _throttle.Release();
        }
    }

    // ✅ Good: ValueTask for frequently called methods that may complete synchronously
    public ValueTask<bool> IsCachedAsync(string key)
    {
        if (_memoryCache.TryGetValue(key, out _))
        {
            return ValueTask.FromResult(true);
        }

        return CheckDistributedCacheAsync(key);
    }

    private async ValueTask<bool> CheckDistributedCacheAsync(string key)
    {
        var result = await _distributedCache.GetAsync(key).ConfigureAwait(false);
        return result != null;
    }
}
```

## Infrastructure Tuning

### 1. Container Optimization

```dockerfile
# Multi-stage build for smaller image
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

# Copy and restore - leverage Docker layer caching
COPY ["*.sln", "./"]
COPY ["src/DynamoDbFusion.Api/*.csproj", "src/DynamoDbFusion.Api/"]
COPY ["src/DynamoDbFusion.Core/*.csproj", "src/DynamoDbFusion.Core/"]
RUN dotnet restore

# Copy source and build
COPY . .
RUN dotnet publish "src/DynamoDbFusion.Api/DynamoDbFusion.Api.csproj" \
    -c Release \
    -o /app/publish \
    --self-contained false \
    --no-restore \
    /p:PublishTrimmed=true \
    /p:PublishSingleFile=false

# Runtime stage - use minimal image
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine
WORKDIR /app

# Install curl for health checks
RUN apk add --no-cache curl

# Create non-root user
RUN adduser --disabled-password --gecos '' appuser
USER appuser

COPY --from=build /app/publish .

# Performance environment variables
ENV DOTNET_TieredPGO=1
ENV DOTNET_TC_QuickJitForLoops=1
ENV DOTNET_ReadyToRun=0
ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080
ENTRYPOINT ["dotnet", "DynamoDbFusion.Api.dll"]
```

### 2. Kubernetes Resource Optimization

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: dynamodb-fusion
spec:
  replicas: 3
  template:
    spec:
      containers:
      - name: dynamodb-fusion
        image: dynamodb-fusion:latest
        resources:
          requests:
            memory: "128Mi"      # Minimum required
            cpu: "100m"          # 0.1 CPU cores
          limits:
            memory: "512Mi"      # Maximum allowed
            cpu: "500m"          # 0.5 CPU cores
        env:
        # .NET performance tuning
        - name: DOTNET_TieredPGO
          value: "1"
        - name: DOTNET_TC_QuickJitForLoops
          value: "1"
        - name: DOTNET_gcServer
          value: "1"
        - name: DOTNET_gcConcurrent
          value: "1"
        # Application settings
        - name: ASPNETCORE_ENVIRONMENT
          value: "Production"
        livenessProbe:
          httpGet:
            path: /health
            port: 8080
          initialDelaySeconds: 30
          periodSeconds: 30
          timeoutSeconds: 5
        readinessProbe:
          httpGet:
            path: /health/ready
            port: 8080
          initialDelaySeconds: 5
          periodSeconds: 10
          timeoutSeconds: 3
---
apiVersion: v1
kind: Service
metadata:
  name: dynamodb-fusion-service
spec:
  type: ClusterIP
  ports:
  - port: 80
    targetPort: 8080
  selector:
    app: dynamodb-fusion
---
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: dynamodb-fusion-hpa
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: dynamodb-fusion
  minReplicas: 2
  maxReplicas: 20
  metrics:
  - type: Resource
    resource:
      name: cpu
      target:
        type: Utilization
        averageUtilization: 70
  - type: Resource
    resource:
      name: memory
      target:
        type: Utilization
        averageUtilization: 80
  behavior:
    scaleDown:
      stabilizationWindowSeconds: 300
      policies:
      - type: Percent
        value: 10
        periodSeconds: 60
    scaleUp:
      stabilizationWindowSeconds: 0
      policies:
      - type: Percent
        value: 100
        periodSeconds: 15
```

## Monitoring & Profiling

### 1. Performance Metrics

```csharp
public class PerformanceMetrics
{
    private static readonly ActivitySource ActivitySource = new("DynamoDbFusion");
    private static readonly Meter Meter = new("DynamoDbFusion");

    // Counters
    public static readonly Counter<long> RequestsTotal = 
        Meter.CreateCounter<long>("requests_total");
    
    public static readonly Counter<long> CacheHits = 
        Meter.CreateCounter<long>("cache_hits_total");

    // Histograms
    public static readonly Histogram<double> RequestDuration = 
        Meter.CreateHistogram<double>("request_duration_ms");
    
    public static readonly Histogram<double> DynamoDbDuration = 
        Meter.CreateHistogram<double>("dynamodb_duration_ms");

    // Gauges
    private static readonly ObservableGauge<long> ActiveConnections = 
        Meter.CreateObservableGauge<long>("active_connections", GetActiveConnections);

    public static void RecordRequest(string tableName, string operation, double durationMs, bool cached)
    {
        RequestsTotal.Add(1, 
            new KeyValuePair<string, object?>("table", tableName),
            new KeyValuePair<string, object?>("operation", operation));

        RequestDuration.Record(durationMs,
            new KeyValuePair<string, object?>("table", tableName),
            new KeyValuePair<string, object?>("cached", cached));

        if (cached)
        {
            CacheHits.Add(1, new KeyValuePair<string, object?>("table", tableName));
        }
    }

    private static long GetActiveConnections()
    {
        // Return current active connection count
        return ConnectionPoolMetrics.ActiveConnections;
    }
}
```

### 2. Application Insights Integration

```csharp
public class PerformanceTelemetryProcessor : ITelemetryProcessor
{
    private readonly ITelemetryProcessor _next;

    public void Process(ITelemetry item)
    {
        if (item is RequestTelemetry request)
        {
            // Add custom performance properties
            request.Properties["QueryComplexity"] = CalculateQueryComplexity(request);
            request.Properties["CacheHitRate"] = GetCacheHitRate().ToString("F2");
        }

        if (item is DependencyTelemetry dependency && dependency.Type == "DynamoDB")
        {
            // Track DynamoDB performance
            dependency.Properties["TableName"] = ExtractTableName(dependency.Name);
            dependency.Properties["OperationType"] = ExtractOperationType(dependency.Name);
        }

        _next.Process(item);
    }
}
```

### 3. Custom Performance Counters

```csharp
public class CustomPerformanceCounters
{
    private readonly PerformanceCounter _cpuCounter;
    private readonly PerformanceCounter _memoryCounter;
    private readonly Timer _timer;

    public CustomPerformanceCounters()
    {
        _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        _memoryCounter = new PerformanceCounter("Memory", "Available MBytes");
        
        _timer = new Timer(CollectMetrics, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
    }

    private void CollectMetrics(object? state)
    {
        var cpu = _cpuCounter.NextValue();
        var memory = _memoryCounter.NextValue();

        // Record to metrics
        Metrics.CpuUsage.Record(cpu);
        Metrics.MemoryUsage.Record(memory);
    }
}
```

## Load Testing

### 1. NBomber Load Tests

```csharp
public class LoadTests
{
    [Fact]
    public void DynamoDbFusion_LoadTest()
    {
        var scenario = Scenario.Create("query_load", async context =>
        {
            var client = new HttpClient();
            var tableName = "Users";
            var userId = $"user{Random.Shared.Next(1, 10000)}";
            
            var response = await client.GetAsync(
                $"http://localhost:5000/api/query/{tableName}?userId={userId}");

            return response.IsSuccessStatusCode ? Response.Ok() : Response.Fail();
        })
        .WithLoadSimulations(
            Simulation.InjectPerSec(rate: 100, during: TimeSpan.FromMinutes(5)),
            Simulation.KeepConstant(copies: 50, during: TimeSpan.FromMinutes(10))
        );

        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .WithReportFolder("load-test-results")
            .Run();

        // Assert performance requirements
        Assert.True(stats.AllScenarios[0].Ok.Response.Mean < 500); // < 500ms mean response time
        Assert.True(stats.AllScenarios[0].Ok.Response.StdDev < 200); // Low deviation
    }
}
```

### 2. K6 Load Testing Script

```javascript
import http from 'k6/http';
import { check, sleep } from 'k6';

export let options = {
  stages: [
    { duration: '2m', target: 100 },  // Ramp up
    { duration: '5m', target: 100 },  // Stay at 100 users
    { duration: '2m', target: 200 },  // Ramp up to 200 users
    { duration: '5m', target: 200 },  // Stay at 200 users
    { duration: '2m', target: 0 },    // Ramp down
  ],
  thresholds: {
    http_req_duration: ['p(95)<500'], // 95% of requests under 500ms
    http_req_failed: ['rate<0.1'],    // Error rate under 10%
  },
};

export default function() {
  let userId = `user${Math.floor(Math.random() * 10000)}`;
  let response = http.get(`http://localhost:5000/api/query/Users?userId=${userId}`);
  
  check(response, {
    'status is 200': (r) => r.status === 200,
    'response time < 500ms': (r) => r.timings.duration < 500,
    'has data': (r) => JSON.parse(r.body).data !== null,
  });
  
  sleep(1);
}
```

## Performance Troubleshooting

### 1. Common Performance Issues

#### High Response Times
```csharp
// Diagnostic middleware to identify slow operations
public class PerformanceDiagnosticMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PerformanceDiagnosticMiddleware> _logger;

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        
        await _next(context);
        
        stopwatch.Stop();
        var duration = stopwatch.ElapsedMilliseconds;
        
        if (duration > 1000) // Log slow requests
        {
            _logger.LogWarning("Slow request detected: {Method} {Path} took {Duration}ms",
                context.Request.Method,
                context.Request.Path,
                duration);
        }
        
        // Add performance headers
        context.Response.Headers.Add("X-Response-Time", $"{duration}ms");
    }
}
```

#### Memory Leaks
```csharp
public class MemoryDiagnostics
{
    private readonly Timer _timer;
    private readonly ILogger<MemoryDiagnostics> _logger;

    public MemoryDiagnostics(ILogger<MemoryDiagnostics> logger)
    {
        _logger = logger;
        _timer = new Timer(CheckMemoryUsage, null, 
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    private void CheckMemoryUsage(object? state)
    {
        var memoryBefore = GC.GetTotalMemory(false);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var memoryAfter = GC.GetTotalMemory(true);

        var freedMemory = memoryBefore - memoryAfter;
        
        if (memoryAfter > 500_000_000) // 500MB threshold
        {
            _logger.LogWarning("High memory usage detected: {MemoryMB}MB, freed: {FreedMB}MB",
                memoryAfter / 1_000_000,
                freedMemory / 1_000_000);
        }
    }
}
```

### 2. Performance Monitoring Dashboard

```csharp
public class PerformanceDashboard
{
    public class Metrics
    {
        public double AverageResponseTime { get; set; }
        public double ThroughputRps { get; set; }
        public double CacheHitRate { get; set; }
        public double ErrorRate { get; set; }
        public long MemoryUsageMb { get; set; }
        public double CpuUsagePercent { get; set; }
    }

    public async Task<Metrics> GetCurrentMetricsAsync()
    {
        return new Metrics
        {
            AverageResponseTime = await GetAverageResponseTimeAsync(),
            ThroughputRps = await GetThroughputAsync(),
            CacheHitRate = await GetCacheHitRateAsync(),
            ErrorRate = await GetErrorRateAsync(),
            MemoryUsageMb = GC.GetTotalMemory(false) / 1_000_000,
            CpuUsagePercent = await GetCpuUsageAsync()
        };
    }
}
```

This performance guide provides comprehensive strategies for optimizing DynamoDB Fusion across all layers of the application stack. Regular monitoring and profiling should be used to identify bottlenecks and validate optimization efforts. 