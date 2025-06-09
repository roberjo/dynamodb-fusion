# Troubleshooting Guide

Comprehensive guide for diagnosing and resolving common issues with DynamoDB Fusion.

## Table of Contents

1. [General Troubleshooting](#general-troubleshooting)
2. [Connection Issues](#connection-issues)
3. [Query Performance Problems](#query-performance-problems)
4. [Caching Issues](#caching-issues)
5. [Security and Authorization](#security-and-authorization)
6. [Memory and Resource Issues](#memory-and-resource-issues)
7. [Configuration Problems](#configuration-problems)
8. [Error Messages Guide](#error-messages-guide)
9. [Monitoring and Diagnostics](#monitoring-and-diagnostics)

## General Troubleshooting

### Diagnostic Information Collection

Before troubleshooting any issue, collect the following diagnostic information:

```csharp
public class DiagnosticCollector
{
    public async Task<DiagnosticReport> CollectDiagnosticsAsync()
    {
        return new DiagnosticReport
        {
            Timestamp = DateTime.UtcNow,
            Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown",
            Version = GetAssemblyVersion(),
            Configuration = GetSanitizedConfiguration(),
            HealthChecks = await GetHealthCheckResults(),
            SystemInfo = GetSystemInfo(),
            RecentErrors = GetRecentErrors()
        };
    }

    private SystemInfo GetSystemInfo()
    {
        return new SystemInfo
        {
            MachineName = Environment.MachineName,
            OSVersion = Environment.OSVersion.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            WorkingSet = Environment.WorkingSet,
            TotalMemory = GC.GetTotalMemory(false),
            DotNetVersion = Environment.Version.ToString()
        };
    }
}
```

### Enable Debug Logging

Add this to your `appsettings.Development.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "DynamoDbFusion": "Debug",
      "Microsoft.AspNetCore": "Warning",
      "AWSSDK": "Information"
    }
  },
  "DynamoDbFusion": {
    "EnableDetailedErrors": true,
    "EnableQueryLogging": true
  }
}
```

## Connection Issues

### Issue: "Unable to connect to DynamoDB"

**Symptoms:**
- `AmazonServiceException: Unable to connect to the remote server`
- Timeout errors
- Connection refused errors

**Diagnosis:**
```csharp
public class ConnectionDiagnostics
{
    public async Task<ConnectionStatus> TestDynamoDbConnectionAsync()
    {
        try
        {
            var client = new AmazonDynamoDBClient();
            var response = await client.ListTablesAsync();
            
            return new ConnectionStatus
            {
                IsConnected = true,
                TableCount = response.TableNames.Count,
                Region = client.Config.RegionEndpoint?.SystemName ?? "Unknown"
            };
        }
        catch (Exception ex)
        {
            return new ConnectionStatus
            {
                IsConnected = false,
                Error = ex.Message,
                ErrorType = ex.GetType().Name
            };
        }
    }
}
```

**Solutions:**

1. **Check AWS Credentials:**
```powershell
# Verify AWS credentials
aws sts get-caller-identity

# Check environment variables
$env:AWS_ACCESS_KEY_ID
$env:AWS_SECRET_ACCESS_KEY
$env:AWS_REGION
```

2. **Verify Network Connectivity:**
```powershell
# Test DynamoDB endpoint
Test-NetConnection -ComputerName dynamodb.us-east-1.amazonaws.com -Port 443

# Check local DynamoDB (if using)
Test-NetConnection -ComputerName localhost -Port 8000
```

3. **IAM Permissions:**
```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "dynamodb:Query",
        "dynamodb:Scan",
        "dynamodb:GetItem",
        "dynamodb:DescribeTable",
        "dynamodb:ListTables"
      ],
      "Resource": "*"
    }
  ]
}
```

### Issue: "DynamoDB Local not responding"

**Symptoms:**
- Connection timeouts to localhost:8000
- "Connection refused" errors in development

**Solutions:**

1. **Start DynamoDB Local:**
```powershell
# Using Docker
docker run -p 8000:8000 amazon/dynamodb-local

# Using Docker Compose
docker-compose up dynamodb-local
```

2. **Verify DynamoDB Local is running:**
```powershell
# Check if container is running
docker ps | Select-String dynamodb

# Test endpoint
Invoke-RestMethod -Uri "http://localhost:8000" -Method GET
```

3. **Check port conflicts:**
```powershell
# Check what's using port 8000
netstat -ano | Select-String ":8000"
```

## Query Performance Problems

### Issue: "Queries are slow"

**Symptoms:**
- Response times > 2 seconds
- High CPU usage
- Memory growth over time

**Diagnosis:**
```csharp
public class PerformanceDiagnostics
{
    public async Task<PerformanceReport> AnalyzeQueryPerformanceAsync(DynamoDbQueryRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var memoryBefore = GC.GetTotalMemory(false);
        
        try
        {
            var result = await _queryService.QueryAsync<object>(request);
            stopwatch.Stop();
            
            var memoryAfter = GC.GetTotalMemory(false);
            
            return new PerformanceReport
            {
                Duration = stopwatch.Elapsed,
                MemoryUsed = memoryAfter - memoryBefore,
                Success = result.Success,
                ItemCount = result.Data?.Items?.Count() ?? 0,
                QueryType = result.Metadata?["OperationType"]?.ToString() ?? "Unknown"
            };
        }
        catch (Exception ex)
        {
            return new PerformanceReport
            {
                Duration = stopwatch.Elapsed,
                Success = false,
                Error = ex.Message
            };
        }
    }
}
```

**Solutions:**

1. **Check Query Strategy:**
```csharp
// Add this to see what operation is being used
var request = new DynamoDbQueryRequest
{
    TableName = "Users",
    // ... your filters
};

var result = await _queryService.QueryAsync<UserDto>(request);

// Check metadata for operation type
var operationType = result.Metadata?["OperationType"]?.ToString();
Console.WriteLine($"Operation used: {operationType}");

if (operationType == "Scan")
{
    Console.WriteLine("Warning: Using Scan operation - consider adding partition key or GSI");
}
```

2. **Add Partition Key if possible:**
```csharp
// ❌ Slow - will use Scan
var request = new DynamoDbQueryRequest
{
    TableName = "Users",
    Filters = new Dictionary<string, FilterValue>
    {
        ["Status"] = new FilterValue { Value = "active", Operator = FilterOperator.Equals }
    }
};

// ✅ Fast - will use Query
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

3. **Check for missing indexes:**
```csharp
public class IndexAnalyzer
{
    public async Task<List<IndexRecommendation>> AnalyzeTableAsync(string tableName)
    {
        var client = new AmazonDynamoDBClient();
        var tableDescription = await client.DescribeTableAsync(tableName);
        
        // Analyze existing indexes
        var recommendations = new List<IndexRecommendation>();
        
        // Check if commonly filtered attributes have indexes
        var commonFilters = GetCommonFilters(tableName); // Your implementation
        
        foreach (var filter in commonFilters)
        {
            var hasIndex = tableDescription.Table.GlobalSecondaryIndexes
                .Any(gsi => gsi.KeySchema.Any(k => k.AttributeName == filter));
                
            if (!hasIndex)
            {
                recommendations.Add(new IndexRecommendation
                {
                    AttributeName = filter,
                    RecommendedAction = "Create GSI",
                    EstimatedImprovement = "Query instead of Scan"
                });
            }
        }
        
        return recommendations;
    }
}
```

### Issue: "High memory usage during queries"

**Solutions:**

1. **Use Streaming for large result sets:**
```csharp
// ❌ Memory intensive - loads all results
var result = await _queryService.QueryAsync<UserDto>(request);
var allUsers = result.Data?.Items.ToList();

// ✅ Memory efficient - streams results
await foreach (var user in _queryService.StreamAsync<UserDto>(request))
{
    // Process user individually
    await ProcessUserAsync(user);
}
```

2. **Implement pagination:**
```csharp
public async Task<List<UserDto>> GetAllUsersAsync()
{
    var allUsers = new List<UserDto>();
    string? nextToken = null;
    
    do
    {
        var request = new DynamoDbQueryRequest
        {
            TableName = "Users",
            Pagination = new PaginationRequest
            {
                PageSize = 100, // Keep page size reasonable
                NextToken = nextToken
            }
        };
        
        var result = await _queryService.QueryAsync<UserDto>(request);
        
        if (result.Success && result.Data?.Items != null)
        {
            allUsers.AddRange(result.Data.Items);
            nextToken = result.Data.Pagination.NextPageToken;
        }
        else
        {
            break;
        }
        
    } while (!string.IsNullOrEmpty(nextToken));
    
    return allUsers;
}
```

## Caching Issues

### Issue: "Cache not working / Low cache hit rate"

**Symptoms:**
- All requests hitting DynamoDB
- High response times despite caching enabled
- Cache hit rate < 20%

**Diagnosis:**
```csharp
public class CacheDiagnostics
{
    public async Task<CacheReport> AnalyzeCacheAsync()
    {
        var stats = await _cacheService.GetStatisticsAsync();
        
        return new CacheReport
        {
            HitRate = stats.HitRate,
            MissRate = stats.MissRate,
            TotalRequests = stats.TotalRequests,
            L1CacheSize = stats.L1CacheSize,
            L2CacheConnected = await TestL2CacheConnection(),
            RecentKeys = stats.RecentKeys.Take(10).ToList()
        };
    }
}
```

**Solutions:**

1. **Verify cache configuration:**
```json
{
  "DynamoDbFusion": {
    "Cache": {
      "EnableL1Cache": true,
      "EnableL2Cache": true,
      "ConnectionString": "localhost:6379",
      "DefaultTtl": "00:05:00"
    }
  }
}
```

2. **Check Redis connectivity:**
```powershell
# Test Redis connection
redis-cli -h localhost -p 6379 ping

# Check Redis logs
docker logs redis-container-name
```

3. **Monitor cache keys:**
```csharp
public class CacheKeyDebugger
{
    public string GenerateDebugKey(DynamoDbQueryRequest request)
    {
        var key = _cacheKeyGenerator.GenerateKey(request);
        
        _logger.LogDebug("Generated cache key: {CacheKey} for request: {@Request}", 
            key, request);
            
        return key;
    }
}
```

### Issue: "Cache invalidation not working"

**Solutions:**

1. **Manual cache invalidation:**
```csharp
public async Task InvalidateCacheAsync(string tableName, string? partitionKeyValue = null)
{
    if (!string.IsNullOrEmpty(partitionKeyValue))
    {
        // Invalidate specific partition
        var pattern = $"dynamo:*:{tableName}:*{partitionKeyValue}*";
        await _cacheService.InvalidatePatternAsync(pattern);
    }
    else
    {
        // Invalidate entire table
        var pattern = $"dynamo:*:{tableName}:*";
        await _cacheService.InvalidatePatternAsync(pattern);
    }
}
```

2. **Automatic invalidation on data changes:**
```csharp
public class CacheInvalidationHandler
{
    public async Task HandleDataChangeAsync(string tableName, string partitionKeyValue)
    {
        // Invalidate cached queries for this partition
        await _cacheInvalidationService.InvalidatePartitionAsync(tableName, partitionKeyValue);
        
        // Log the invalidation
        _logger.LogInformation("Invalidated cache for table {TableName}, partition {PartitionKey}", 
            tableName, partitionKeyValue);
    }
}
```

## Security and Authorization

### Issue: "Unauthorized access errors"

**Symptoms:**
- HTTP 401 or 403 responses
- "Access Denied" errors
- Authentication failures

**Diagnosis:**
```csharp
public class SecurityDiagnostics
{
    public async Task<SecurityReport> DiagnoseSecurityAsync(HttpContext context)
    {
        return new SecurityReport
        {
            IsAuthenticated = context.User.Identity?.IsAuthenticated ?? false,
            UserName = context.User.Identity?.Name,
            Claims = context.User.Claims.Select(c => new { c.Type, c.Value }).ToList(),
            AuthenticationScheme = context.User.Identity?.AuthenticationType,
            RequestHeaders = context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString())
        };
    }
}
```

**Solutions:**

1. **Check authentication configuration:**
```csharp
// In Program.cs
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        options.Authority = "https://your-auth-server";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = false
        };
    });

builder.Services.AddAuthorization();
```

2. **Verify JWT token:**
```csharp
public class TokenValidator
{
    public async Task<TokenValidationResult> ValidateTokenAsync(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadJwtToken(token);
            
            return new TokenValidationResult
            {
                IsValid = true,
                ExpiresAt = jsonToken.ValidTo,
                Subject = jsonToken.Subject,
                Claims = jsonToken.Claims.ToDictionary(c => c.Type, c => c.Value)
            };
        }
        catch (Exception ex)
        {
            return new TokenValidationResult
            {
                IsValid = false,
                Error = ex.Message
            };
        }
    }
}
```

### Issue: "Rate limiting errors"

**Solutions:**

1. **Check rate limiting configuration:**
```json
{
  "DynamoDbFusion": {
    "Security": {
      "RateLimiting": {
        "RequestsPerMinute": 100,
        "RequestsPerHour": 1000,
        "EnableDistributedRateLimiting": true
      }
    }
  }
}
```

2. **Monitor rate limiting:**
```csharp
public class RateLimitMonitor
{
    public async Task<RateLimitStatus> GetRateLimitStatusAsync(string clientId)
    {
        var status = await _rateLimitService.GetStatusAsync(clientId);
        
        return new RateLimitStatus
        {
            ClientId = clientId,
            RequestsInCurrentWindow = status.RequestCount,
            WindowResetTime = status.WindowResetTime,
            IsLimited = status.IsLimited,
            RemainingRequests = status.Limit - status.RequestCount
        };
    }
}
```

## Memory and Resource Issues

### Issue: "Memory leaks"

**Symptoms:**
- Continuously increasing memory usage
- OutOfMemoryException
- GC pressure

**Diagnosis:**
```csharp
public class MemoryDiagnostics
{
    private readonly Timer _timer;
    
    public MemoryDiagnostics()
    {
        _timer = new Timer(CheckMemoryUsage, null, 
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }
    
    private void CheckMemoryUsage(object? state)
    {
        var memoryBefore = GC.GetTotalMemory(false);
        var gen0 = GC.CollectionCount(0);
        var gen1 = GC.CollectionCount(1);
        var gen2 = GC.CollectionCount(2);
        
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var memoryAfter = GC.GetTotalMemory(true);
        var freedMemory = memoryBefore - memoryAfter;
        
        _logger.LogInformation("Memory: {MemoryMB}MB, Freed: {FreedMB}MB, GC: Gen0={Gen0}, Gen1={Gen1}, Gen2={Gen2}",
            memoryAfter / 1_000_000, freedMemory / 1_000_000, gen0, gen1, gen2);
    }
}
```

**Solutions:**

1. **Proper disposal patterns:**
```csharp
public class ProperResourceManagement
{
    public async Task<List<UserDto>> GetUsersAsync()
    {
        using var httpClient = new HttpClient(); // ❌ Don't create HttpClient in method
        
        // ✅ Use injected HttpClient or factory
        var result = await _queryService.QueryAsync<UserDto>(request);
        return result.Data?.Items.ToList() ?? new List<UserDto>();
    }
    
    // ✅ Implement IDisposable for services that hold resources
    public class CacheService : ICacheService, IDisposable
    {
        private readonly ConnectionMultiplexer _redis;
        
        public void Dispose()
        {
            _redis?.Dispose();
        }
    }
}
```

2. **Configure garbage collection:**
```json
{
  "runtimeOptions": {
    "configProperties": {
      "System.GC.Server": true,
      "System.GC.Concurrent": true,
      "System.GC.RetainVM": true
    }
  }
}
```

### Issue: "High CPU usage"

**Solutions:**

1. **Profile CPU usage:**
```powershell
# Install dotnet-trace
dotnet tool install --global dotnet-trace

# Capture CPU trace
dotnet-trace collect --process-id <pid> --duration 00:00:30

# Analyze with Visual Studio or PerfView
```

2. **Optimize hot paths:**
```csharp
// ❌ Inefficient string concatenation
public string BuildCacheKey(DynamoDbQueryRequest request)
{
    var key = "dynamo:";
    key += request.TableName + ":";
    key += string.Join(",", request.Filters.Keys);
    return key;
}

// ✅ Efficient StringBuilder usage
public string BuildCacheKey(DynamoDbQueryRequest request)
{
    var sb = StringBuilderPool.Get();
    try
    {
        sb.Append("dynamo:");
        sb.Append(request.TableName);
        sb.Append(':');
        sb.AppendJoin(",", request.Filters.Keys);
        return sb.ToString();
    }
    finally
    {
        StringBuilderPool.Return(sb);
    }
}
```

## Configuration Problems

### Issue: "Configuration not loading"

**Symptoms:**
- Default values being used instead of configured values
- Settings not applied
- Configuration exceptions

**Diagnosis:**
```csharp
public class ConfigurationDiagnostics
{
    public ConfigurationReport DiagnoseConfiguration(IConfiguration configuration)
    {
        var report = new ConfigurationReport();
        
        // Check if DynamoDB Fusion section exists
        var fusionSection = configuration.GetSection("DynamoDbFusion");
        report.HasFusionSection = fusionSection.Exists();
        
        if (report.HasFusionSection)
        {
            report.DynamoDbRegion = fusionSection["DynamoDb:Region"];
            report.CacheProvider = fusionSection["Cache:Provider"];
            report.EnableAuthentication = fusionSection.GetValue<bool>("Security:EnableAuthentication");
        }
        
        // Check environment variables
        report.EnvironmentVariables = Environment.GetEnvironmentVariables()
            .Cast<DictionaryEntry>()
            .Where(e => e.Key.ToString()!.StartsWith("DynamoDbFusion"))
            .ToDictionary(e => e.Key.ToString()!, e => e.Value?.ToString());
            
        return report;
    }
}
```

**Solutions:**

1. **Verify configuration file format:**
```json
{
  "DynamoDbFusion": {
    "DynamoDb": {
      "Region": "us-east-1",
      "ServiceUrl": null
    },
    "Cache": {
      "Provider": "Redis",
      "ConnectionString": "localhost:6379",
      "EnableL1Cache": true,
      "EnableL2Cache": true
    }
  }
}
```

2. **Check configuration binding:**
```csharp
// In Program.cs
var dynamoOptions = builder.Configuration.GetSection("DynamoDbFusion");
builder.Services.Configure<DynamoDbQueryOptions>(dynamoOptions);

// Validate configuration
builder.Services.AddSingleton<IValidateOptions<DynamoDbQueryOptions>, DynamoDbQueryOptionsValidator>();
```

## Error Messages Guide

### Common Error Messages and Solutions

#### "Table 'TableName' not found"
```
Cause: Table doesn't exist or incorrect table name
Solution: 
1. Verify table exists: aws dynamodb list-tables
2. Check table name spelling
3. Verify AWS region
```

#### "ValidationException: The provided key element does not match the schema"
```
Cause: Partition key or sort key mismatch
Solution:
1. Check table schema: aws dynamodb describe-table --table-name TableName
2. Verify key names and types match
```

#### "ThrottlingException: Rate exceeded"
```
Cause: Too many requests to DynamoDB
Solution:
1. Implement exponential backoff
2. Increase table capacity
3. Use caching to reduce requests
```

#### "ResourceNotFoundException: Requested resource not found"
```
Cause: Table or index doesn't exist
Solution:
1. Create missing table/index
2. Check region configuration
3. Verify IAM permissions
```

## Monitoring and Diagnostics

### Health Check Implementation

```csharp
public class DynamoDbFusionHealthCheck : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Test DynamoDB connectivity
            var dynamoStatus = await TestDynamoDbAsync();
            
            // Test cache connectivity
            var cacheStatus = await TestCacheAsync();
            
            // Test memory usage
            var memoryMb = GC.GetTotalMemory(false) / 1_000_000;
            
            var data = new Dictionary<string, object>
            {
                ["dynamodb"] = dynamoStatus.IsHealthy ? "healthy" : "unhealthy",
                ["cache"] = cacheStatus.IsHealthy ? "healthy" : "unhealthy",
                ["memory_mb"] = memoryMb
            };
            
            if (dynamoStatus.IsHealthy && cacheStatus.IsHealthy && memoryMb < 1000)
            {
                return HealthCheckResult.Healthy("All systems operational", data);
            }
            else
            {
                return HealthCheckResult.Degraded("Some systems degraded", data);
            }
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Health check failed", ex);
        }
    }
}
```

### Custom Metrics Collection

```csharp
public class MetricsCollector
{
    private static readonly Counter<long> ErrorCounter = 
        Meter.CreateCounter<long>("dynamodb_fusion_errors_total");
    
    private static readonly Histogram<double> QueryDuration = 
        Meter.CreateHistogram<double>("dynamodb_fusion_query_duration_ms");
    
    public void RecordError(string errorType, string tableName)
    {
        ErrorCounter.Add(1,
            new KeyValuePair<string, object?>("error_type", errorType),
            new KeyValuePair<string, object?>("table", tableName));
    }
    
    public void RecordQueryDuration(TimeSpan duration, string tableName, bool cached)
    {
        QueryDuration.Record(duration.TotalMilliseconds,
            new KeyValuePair<string, object?>("table", tableName),
            new KeyValuePair<string, object?>("cached", cached));
    }
}
```

This troubleshooting guide should help diagnose and resolve most common issues with DynamoDB Fusion. For issues not covered here, enable debug logging and examine the detailed error messages and stack traces. 