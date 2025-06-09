using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace DynamoDbFusion.Core.Services;

/// <summary>
/// Service for collecting and exposing performance metrics
/// </summary>
public class MetricsService : IDisposable
{
    private readonly ILogger<MetricsService> _logger;
    private readonly Meter _meter;
    private readonly ConcurrentDictionary<string, PerformanceCounter> _performanceCounters;
    private readonly ConcurrentDictionary<string, ErrorMetrics> _errorMetrics;

    // Metrics instruments
    private readonly Counter<long> _requestCounter;
    private readonly Counter<long> _errorCounter;
    private readonly Histogram<double> _requestDuration;
    private readonly Histogram<double> _queryExecutionTime;
    private readonly Histogram<double> _cacheOperationTime;
    private readonly Counter<long> _cacheHits;
    private readonly Counter<long> _cacheMisses;
    private readonly ObservableGauge<int> _activeConnections;
    private readonly ObservableGauge<long> _memoryUsage;

    public MetricsService(ILogger<MetricsService> logger)
    {
        _logger = logger;
        _meter = new Meter("DynamoDbFusion", "1.0.0");
        _performanceCounters = new ConcurrentDictionary<string, PerformanceCounter>();
        _errorMetrics = new ConcurrentDictionary<string, ErrorMetrics>();

        // Initialize metrics instruments
        _requestCounter = _meter.CreateCounter<long>("requests_total", "requests", "Total number of requests");
        _errorCounter = _meter.CreateCounter<long>("errors_total", "errors", "Total number of errors");
        _requestDuration = _meter.CreateHistogram<double>("request_duration_ms", "milliseconds", "Request duration in milliseconds");
        _queryExecutionTime = _meter.CreateHistogram<double>("query_execution_time_ms", "milliseconds", "DynamoDB query execution time");
        _cacheOperationTime = _meter.CreateHistogram<double>("cache_operation_time_ms", "milliseconds", "Cache operation time");
        _cacheHits = _meter.CreateCounter<long>("cache_hits_total", "hits", "Total cache hits");
        _cacheMisses = _meter.CreateCounter<long>("cache_misses_total", "misses", "Total cache misses");
        _activeConnections = _meter.CreateObservableGauge<int>("active_connections", "connections", "Number of active connections");
        _memoryUsage = _meter.CreateObservableGauge<long>("memory_usage_bytes", "bytes", "Current memory usage");
    }

    /// <summary>
    /// Records a request metric
    /// </summary>
    public void RecordRequest(string endpoint, string method, int statusCode, double durationMs)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("endpoint", endpoint),
            new("method", method),
            new("status_code", statusCode.ToString())
        };

        _requestCounter.Add(1, tags);
        _requestDuration.Record(durationMs, tags);

        var key = $"{method}:{endpoint}";
        _performanceCounters.AddOrUpdate(key, 
            new PerformanceCounter { TotalRequests = 1, TotalDuration = durationMs },
            (_, existing) => 
            {
                existing.TotalRequests++;
                existing.TotalDuration += durationMs;
                existing.AverageDuration = existing.TotalDuration / existing.TotalRequests;
                existing.LastRequestTime = DateTime.UtcNow;
                return existing;
            });

        _logger.LogDebug("Recorded request metric: {Method} {Endpoint} -> {StatusCode} in {Duration}ms", 
            method, endpoint, statusCode, durationMs);
    }

    /// <summary>
    /// Records an error metric
    /// </summary>
    public void RecordError(string errorType, string operation, string? tableName = null, Exception? exception = null)
    {
        var tags = new List<KeyValuePair<string, object?>>
        {
            new("error_type", errorType),
            new("operation", operation)
        };

        if (!string.IsNullOrEmpty(tableName))
        {
            tags.Add(new("table_name", tableName));
        }

        _errorCounter.Add(1, tags.ToArray());

        var key = $"{operation}:{errorType}";
        _errorMetrics.AddOrUpdate(key,
            new ErrorMetrics 
            { 
                ErrorType = errorType,
                Operation = operation,
                Count = 1,
                LastOccurrence = DateTime.UtcNow,
                LastException = exception?.Message
            },
            (_, existing) =>
            {
                existing.Count++;
                existing.LastOccurrence = DateTime.UtcNow;
                if (exception != null)
                {
                    existing.LastException = exception.Message;
                }
                return existing;
            });

        _logger.LogWarning("Recorded error metric: {ErrorType} in {Operation} for table {TableName}", 
            errorType, operation, tableName ?? "N/A");
    }

    /// <summary>
    /// Records query execution time
    /// </summary>
    public void RecordQueryExecution(string tableName, string operationType, double durationMs, bool cached = false)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("table_name", tableName),
            new("operation_type", operationType),
            new("cached", cached.ToString())
        };

        _queryExecutionTime.Record(durationMs, tags);

        _logger.LogDebug("Recorded query execution: {OperationType} on {TableName} in {Duration}ms (cached: {Cached})", 
            operationType, tableName, durationMs, cached);
    }

    /// <summary>
    /// Records cache operation metrics
    /// </summary>
    public void RecordCacheOperation(string operation, string level, double durationMs, bool hit)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("operation", operation),
            new("cache_level", level)
        };

        _cacheOperationTime.Record(durationMs, tags);

        if (hit)
        {
            _cacheHits.Add(1, tags);
        }
        else
        {
            _cacheMisses.Add(1, tags);
        }

        _logger.LogDebug("Recorded cache operation: {Operation} on {Level} in {Duration}ms (hit: {Hit})", 
            operation, level, durationMs, hit);
    }

    /// <summary>
    /// Updates active connections count
    /// </summary>
    public void UpdateActiveConnections(int count)
    {
        _activeConnections.Record(count);
    }

    /// <summary>
    /// Updates memory usage
    /// </summary>
    public void UpdateMemoryUsage(long bytes)
    {
        _memoryUsage.Record(bytes);
    }

    /// <summary>
    /// Gets performance summary for all tracked operations
    /// </summary>
    public Dictionary<string, PerformanceCounter> GetPerformanceSummary()
    {
        return new Dictionary<string, PerformanceCounter>(_performanceCounters);
    }

    /// <summary>
    /// Gets error summary for all tracked operations
    /// </summary>
    public Dictionary<string, ErrorMetrics> GetErrorSummary()
    {
        return new Dictionary<string, ErrorMetrics>(_errorMetrics);
    }

    /// <summary>
    /// Gets current system metrics
    /// </summary>
    public SystemMetrics GetSystemMetrics()
    {
        var process = Process.GetCurrentProcess();
        
        return new SystemMetrics
        {
            ProcessId = process.Id,
            WorkingSetMemory = process.WorkingSet64,
            PrivateMemory = process.PrivateMemorySize64,
            VirtualMemory = process.VirtualMemorySize64,
            ThreadCount = process.Threads.Count,
            HandleCount = process.HandleCount,
            StartTime = process.StartTime,
            TotalProcessorTime = process.TotalProcessorTime,
            UserProcessorTime = process.UserProcessorTime,
            PrivilegedProcessorTime = process.PrivilegedProcessorTime
        };
    }

    /// <summary>
    /// Gets comprehensive metrics report
    /// </summary>
    public MetricsReport GetMetricsReport()
    {
        return new MetricsReport
        {
            GeneratedAt = DateTime.UtcNow,
            PerformanceCounters = GetPerformanceSummary(),
            ErrorMetrics = GetErrorSummary(),
            SystemMetrics = GetSystemMetrics(),
            TopEndpointsByRequests = GetTopEndpointsByRequests(10),
            TopEndpointsByDuration = GetTopEndpointsByDuration(10),
            TopErrorsByFrequency = GetTopErrorsByFrequency(10)
        };
    }

    /// <summary>
    /// Resets all metrics (use with caution)
    /// </summary>
    public void Reset()
    {
        _performanceCounters.Clear();
        _errorMetrics.Clear();
        _logger.LogWarning("All metrics have been reset");
    }

    private List<EndpointMetrics> GetTopEndpointsByRequests(int count)
    {
        return _performanceCounters
            .OrderByDescending(kvp => kvp.Value.TotalRequests)
            .Take(count)
            .Select(kvp => new EndpointMetrics
            {
                Endpoint = kvp.Key,
                TotalRequests = kvp.Value.TotalRequests,
                AverageDuration = kvp.Value.AverageDuration,
                LastRequestTime = kvp.Value.LastRequestTime
            })
            .ToList();
    }

    private List<EndpointMetrics> GetTopEndpointsByDuration(int count)
    {
        return _performanceCounters
            .OrderByDescending(kvp => kvp.Value.AverageDuration)
            .Take(count)
            .Select(kvp => new EndpointMetrics
            {
                Endpoint = kvp.Key,
                TotalRequests = kvp.Value.TotalRequests,
                AverageDuration = kvp.Value.AverageDuration,
                LastRequestTime = kvp.Value.LastRequestTime
            })
            .ToList();
    }

    private List<ErrorSummary> GetTopErrorsByFrequency(int count)
    {
        return _errorMetrics
            .OrderByDescending(kvp => kvp.Value.Count)
            .Take(count)
            .Select(kvp => new ErrorSummary
            {
                Key = kvp.Key,
                ErrorType = kvp.Value.ErrorType,
                Operation = kvp.Value.Operation,
                Count = kvp.Value.Count,
                LastOccurrence = kvp.Value.LastOccurrence,
                LastException = kvp.Value.LastException
            })
            .ToList();
    }

    public void Dispose()
    {
        _meter?.Dispose();
    }
}

/// <summary>
/// Performance counter for tracking operation metrics
/// </summary>
public class PerformanceCounter
{
    public long TotalRequests { get; set; }
    public double TotalDuration { get; set; }
    public double AverageDuration { get; set; }
    public DateTime LastRequestTime { get; set; }
}

/// <summary>
/// Error metrics tracking
/// </summary>
public class ErrorMetrics
{
    public string ErrorType { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public long Count { get; set; }
    public DateTime LastOccurrence { get; set; }
    public string? LastException { get; set; }
}

/// <summary>
/// System-level metrics
/// </summary>
public class SystemMetrics
{
    public int ProcessId { get; set; }
    public long WorkingSetMemory { get; set; }
    public long PrivateMemory { get; set; }
    public long VirtualMemory { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public DateTime StartTime { get; set; }
    public TimeSpan TotalProcessorTime { get; set; }
    public TimeSpan UserProcessorTime { get; set; }
    public TimeSpan PrivilegedProcessorTime { get; set; }
}

/// <summary>
/// Endpoint metrics summary
/// </summary>
public class EndpointMetrics
{
    public string Endpoint { get; set; } = string.Empty;
    public long TotalRequests { get; set; }
    public double AverageDuration { get; set; }
    public DateTime LastRequestTime { get; set; }
}

/// <summary>
/// Error summary
/// </summary>
public class ErrorSummary
{
    public string Key { get; set; } = string.Empty;
    public string ErrorType { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public long Count { get; set; }
    public DateTime LastOccurrence { get; set; }
    public string? LastException { get; set; }
}

/// <summary>
/// Comprehensive metrics report
/// </summary>
public class MetricsReport
{
    public DateTime GeneratedAt { get; set; }
    public Dictionary<string, PerformanceCounter> PerformanceCounters { get; set; } = new();
    public Dictionary<string, ErrorMetrics> ErrorMetrics { get; set; } = new();
    public SystemMetrics SystemMetrics { get; set; } = new();
    public List<EndpointMetrics> TopEndpointsByRequests { get; set; } = new();
    public List<EndpointMetrics> TopEndpointsByDuration { get; set; } = new();
    public List<ErrorSummary> TopErrorsByFrequency { get; set; } = new();
} 