using DynamoDbFusion.Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace DynamoDbFusion.Core.Middleware;

/// <summary>
/// Middleware for monitoring performance metrics of all requests
/// </summary>
public class PerformanceMonitoringMiddleware
{
    private readonly RequestDelegate _next;
    private readonly MetricsService _metricsService;
    private readonly ILogger<PerformanceMonitoringMiddleware> _logger;

    public PerformanceMonitoringMiddleware(
        RequestDelegate next,
        MetricsService metricsService,
        ILogger<PerformanceMonitoringMiddleware> logger)
    {
        _next = next;
        _metricsService = metricsService;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var originalBodyStream = context.Response.Body;

        try
        {
            // Capture response for analysis
            using var responseBodyStream = new MemoryStream();
            context.Response.Body = responseBodyStream;

            // Process the request
            await _next(context);

            stopwatch.Stop();

            // Copy response back to original stream
            responseBodyStream.Seek(0, SeekOrigin.Begin);
            await responseBodyStream.CopyToAsync(originalBodyStream);

            // Record metrics
            RecordRequestMetrics(context, stopwatch.ElapsedMilliseconds);

            // Update system metrics periodically
            UpdateSystemMetrics();
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            // Record error metrics
            _metricsService.RecordError(
                ex.GetType().Name,
                "HTTP_REQUEST",
                context.Request.Path,
                ex
            );

            RecordRequestMetrics(context, stopwatch.ElapsedMilliseconds);
            
            throw;
        }
        finally
        {
            context.Response.Body = originalBodyStream;
        }
    }

    private void RecordRequestMetrics(HttpContext context, long durationMs)
    {
        var endpoint = GetEndpointName(context);
        var method = context.Request.Method;
        var statusCode = context.Response.StatusCode;

        _metricsService.RecordRequest(endpoint, method, statusCode, durationMs);

        // Log slow requests
        if (durationMs > 5000) // > 5 seconds
        {
            _logger.LogWarning("Slow request detected: {Method} {Path} took {Duration}ms with status {StatusCode}",
                method, endpoint, durationMs, statusCode);
        }
        else if (durationMs > 1000) // > 1 second
        {
            _logger.LogInformation("Request completed: {Method} {Path} took {Duration}ms with status {StatusCode}",
                method, endpoint, durationMs, statusCode);
        }
        else
        {
            _logger.LogDebug("Request completed: {Method} {Path} took {Duration}ms with status {StatusCode}",
                method, endpoint, durationMs, statusCode);
        }
    }

    private void UpdateSystemMetrics()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            _metricsService.UpdateMemoryUsage(process.WorkingSet64);
            
            // Update active connections (approximate)
            var threadCount = process.Threads.Count;
            _metricsService.UpdateActiveConnections(Math.Max(0, threadCount - 10)); // Subtract base threads
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update system metrics");
        }
    }

    private static string GetEndpointName(HttpContext context)
    {
        // Try to get the endpoint name from routing
        var endpoint = context.GetEndpoint();
        if (endpoint?.DisplayName != null)
        {
            return endpoint.DisplayName;
        }

        // Fall back to path
        var path = context.Request.Path.Value ?? "/unknown";
        
        // Clean up the path for better grouping
        path = CleanPath(path);
        
        return path;
    }

    private static string CleanPath(string path)
    {
        // Replace common dynamic segments with placeholders for better grouping
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var cleanedSegments = new List<string>();

        foreach (var segment in segments)
        {
            // Replace GUIDs, numbers, and other dynamic segments
            if (IsGuid(segment))
            {
                cleanedSegments.Add("{id}");
            }
            else if (IsNumeric(segment))
            {
                cleanedSegments.Add("{number}");
            }
            else if (segment.Length > 20) // Long segments are likely IDs
            {
                cleanedSegments.Add("{id}");
            }
            else
            {
                cleanedSegments.Add(segment);
            }
        }

        return "/" + string.Join("/", cleanedSegments);
    }

    private static bool IsGuid(string value)
    {
        return Guid.TryParse(value, out _);
    }

    private static bool IsNumeric(string value)
    {
        return long.TryParse(value, out _);
    }
} 