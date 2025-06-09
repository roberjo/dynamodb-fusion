using DynamoDbFusion.Core.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json;

namespace DynamoDbFusion.Core.Extensions;

/// <summary>
/// Extension methods for configuring the DynamoDB Fusion request pipeline
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds DynamoDB Fusion middleware to the request pipeline
    /// </summary>
    /// <param name="app">The application builder</param>
    /// <returns>The application builder for chaining</returns>
    public static IApplicationBuilder UseDynamoDbFusion(this IApplicationBuilder app)
    {
        // Add global exception handling
        app.UseMiddleware<GlobalExceptionMiddleware>();

        // Add performance monitoring middleware
        app.UseMiddleware<PerformanceMonitoringMiddleware>();

        return app;
    }

    /// <summary>
    /// Adds DynamoDB Fusion health check endpoints
    /// </summary>
    /// <param name="app">The application builder</param>
    /// <param name="pattern">The health check endpoint pattern (default: "/health")</param>
    /// <returns>The application builder for chaining</returns>
    public static IApplicationBuilder UseDynamoDbFusionHealthChecks(this IApplicationBuilder app, string pattern = "/health")
    {
        // Detailed health check endpoint
        app.UseHealthChecks($"{pattern}/detailed", new HealthCheckOptions
        {
            ResponseWriter = WriteDetailedHealthCheckResponse,
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
            }
        });

        // Simple health check endpoint
        app.UseHealthChecks(pattern, new HealthCheckOptions
        {
            ResponseWriter = WriteSimpleHealthCheckResponse,
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
            }
        });

        // Ready check endpoint
        app.UseHealthChecks($"{pattern}/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteSimpleHealthCheckResponse
        });

        // Live check endpoint
        app.UseHealthChecks($"{pattern}/live", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live"),
            ResponseWriter = WriteSimpleHealthCheckResponse
        });

        return app;
    }

    /// <summary>
    /// Adds a metrics endpoint for monitoring
    /// </summary>
    /// <param name="app">The application builder</param>
    /// <param name="pattern">The metrics endpoint pattern (default: "/metrics")</param>
    /// <returns>The application builder for chaining</returns>
    public static IApplicationBuilder UseDynamoDbFusionMetrics(this IApplicationBuilder app, string pattern = "/metrics")
    {
        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapGet(pattern, async context =>
            {
                var metricsService = context.RequestServices.GetRequiredService<MetricsService>();
                var metricsReport = metricsService.GetMetricsReport();

                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(metricsReport, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));
            });
        });

        return app;
    }

    private static async Task WriteDetailedHealthCheckResponse(HttpContext context, HealthReport report)
    {
        var response = new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration.TotalMilliseconds,
            results = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    duration = entry.Value.Duration.TotalMilliseconds,
                    description = entry.Value.Description,
                    data = entry.Value.Data,
                    exception = entry.Value.Exception?.Message,
                    tags = entry.Value.Tags
                }
            ),
            timestamp = DateTime.UtcNow
        };

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        }));
    }

    private static async Task WriteSimpleHealthCheckResponse(HttpContext context, HealthReport report)
    {
        var response = new
        {
            status = report.Status.ToString(),
            timestamp = DateTime.UtcNow
        };

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }
} 