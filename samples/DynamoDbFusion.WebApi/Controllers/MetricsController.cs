using DynamoDbFusion.Core.Models;
using DynamoDbFusion.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace DynamoDbFusion.WebApi.Controllers;

/// <summary>
/// Metrics and monitoring endpoints
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class MetricsController : ControllerBase
{
    private readonly MetricsService _metricsService;
    private readonly CircuitBreakerService _circuitBreakerService;
    private readonly QueryOptimizationService _queryOptimizationService;
    private readonly ILogger<MetricsController> _logger;

    public MetricsController(
        MetricsService metricsService,
        CircuitBreakerService circuitBreakerService,
        QueryOptimizationService queryOptimizationService,
        ILogger<MetricsController> logger)
    {
        _metricsService = metricsService;
        _circuitBreakerService = circuitBreakerService;
        _queryOptimizationService = queryOptimizationService;
        _logger = logger;
    }

    /// <summary>
    /// Gets comprehensive metrics report
    /// </summary>
    /// <returns>Detailed metrics report</returns>
    [HttpGet("report")]
    public ActionResult<ApiResponse<MetricsReport>> GetMetricsReport()
    {
        try
        {
            var report = _metricsService.GetMetricsReport();
            return Ok(ApiResponse<MetricsReport>.CreateSuccess(report, "Metrics report retrieved successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving metrics report");
            return StatusCode(500, ApiResponse<MetricsReport>.CreateFailure("An error occurred while retrieving metrics"));
        }
    }

    /// <summary>
    /// Gets performance summary
    /// </summary>
    /// <returns>Performance metrics summary</returns>
    [HttpGet("performance")]
    public ActionResult<ApiResponse<Dictionary<string, PerformanceCounter>>> GetPerformanceSummary()
    {
        try
        {
            var performance = _metricsService.GetPerformanceSummary();
            return Ok(ApiResponse<Dictionary<string, PerformanceCounter>>.CreateSuccess(performance, "Performance summary retrieved successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving performance summary");
            return StatusCode(500, ApiResponse<Dictionary<string, PerformanceCounter>>.CreateFailure("An error occurred while retrieving performance metrics"));
        }
    }

    /// <summary>
    /// Gets error summary
    /// </summary>
    /// <returns>Error metrics summary</returns>
    [HttpGet("errors")]
    public ActionResult<ApiResponse<Dictionary<string, ErrorMetrics>>> GetErrorSummary()
    {
        try
        {
            var errors = _metricsService.GetErrorSummary();
            return Ok(ApiResponse<Dictionary<string, ErrorMetrics>>.CreateSuccess(errors, "Error summary retrieved successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving error summary");
            return StatusCode(500, ApiResponse<Dictionary<string, ErrorMetrics>>.CreateFailure("An error occurred while retrieving error metrics"));
        }
    }

    /// <summary>
    /// Gets system metrics
    /// </summary>
    /// <returns>System performance metrics</returns>
    [HttpGet("system")]
    public ActionResult<ApiResponse<SystemMetrics>> GetSystemMetrics()
    {
        try
        {
            var systemMetrics = _metricsService.GetSystemMetrics();
            return Ok(ApiResponse<SystemMetrics>.CreateSuccess(systemMetrics, "System metrics retrieved successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving system metrics");
            return StatusCode(500, ApiResponse<SystemMetrics>.CreateFailure("An error occurred while retrieving system metrics"));
        }
    }

    /// <summary>
    /// Gets circuit breaker status
    /// </summary>
    /// <returns>Circuit breaker states</returns>
    [HttpGet("circuit-breakers")]
    public ActionResult<ApiResponse<Dictionary<string, CircuitBreakerState>>> GetCircuitBreakerStates()
    {
        try
        {
            var states = _circuitBreakerService.GetAllStates();
            return Ok(ApiResponse<Dictionary<string, CircuitBreakerState>>.CreateSuccess(states, "Circuit breaker states retrieved successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving circuit breaker states");
            return StatusCode(500, ApiResponse<Dictionary<string, CircuitBreakerState>>.CreateFailure("An error occurred while retrieving circuit breaker states"));
        }
    }

    /// <summary>
    /// Resets a specific circuit breaker
    /// </summary>
    /// <param name="operationKey">The operation key of the circuit breaker to reset</param>
    /// <returns>Success response</returns>
    [HttpPost("circuit-breakers/{operationKey}/reset")]
    public ActionResult<ApiResponse<string>> ResetCircuitBreaker(string operationKey)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(operationKey))
            {
                return BadRequest(ApiResponse<string>.CreateSingleValidationError("operationKey", "Operation key cannot be empty"));
            }

            _circuitBreakerService.Reset(operationKey);
            _logger.LogInformation("Circuit breaker for operation {OperationKey} was reset via API", operationKey);
            
            return Ok(ApiResponse<string>.CreateSuccess("Circuit breaker reset", $"Circuit breaker for operation '{operationKey}' has been reset"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting circuit breaker for operation {OperationKey}", operationKey);
            return StatusCode(500, ApiResponse<string>.CreateFailure("An error occurred while resetting the circuit breaker"));
        }
    }

    /// <summary>
    /// Gets query pattern analysis for a table
    /// </summary>
    /// <param name="tableName">Table name to analyze</param>
    /// <returns>Query pattern analysis</returns>
    [HttpGet("query-patterns/{tableName}")]
    public ActionResult<ApiResponse<QueryPatternAnalysis>> GetQueryPatterns(string tableName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(tableName))
            {
                return BadRequest(ApiResponse<QueryPatternAnalysis>.CreateSingleValidationError("tableName", "Table name cannot be empty"));
            }

            var analysis = _queryOptimizationService.AnalyzeQueryPatterns(tableName);
            return Ok(ApiResponse<QueryPatternAnalysis>.CreateSuccess(analysis, $"Query pattern analysis for table '{tableName}' retrieved successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving query patterns for table {TableName}", tableName);
            return StatusCode(500, ApiResponse<QueryPatternAnalysis>.CreateFailure("An error occurred while retrieving query patterns"));
        }
    }

    /// <summary>
    /// Resets all metrics (use with caution)
    /// </summary>
    /// <returns>Success response</returns>
    [HttpPost("reset")]
    public ActionResult<ApiResponse<string>> ResetMetrics()
    {
        try
        {
            _metricsService.Reset();
            _logger.LogWarning("All metrics were reset via API");
            
            return Ok(ApiResponse<string>.CreateSuccess("Metrics reset", "All metrics have been reset successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting metrics");
            return StatusCode(500, ApiResponse<string>.CreateFailure("An error occurred while resetting metrics"));
        }
    }
} 