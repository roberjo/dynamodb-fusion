using DynamoDbFusion.Core.Interfaces;
using DynamoDbFusion.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace DynamoDbFusion.WebApi.Controllers;

/// <summary>
/// Cache management and statistics endpoints
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class CacheController : ControllerBase
{
    private readonly IMultiLevelCacheService _cacheService;
    private readonly ILogger<CacheController> _logger;

    public CacheController(IMultiLevelCacheService cacheService, ILogger<CacheController> logger)
    {
        _cacheService = cacheService;
        _logger = logger;
    }

    /// <summary>
    /// Gets cache statistics
    /// </summary>
    /// <returns>Multi-level cache statistics</returns>
    [HttpGet("statistics")]
    public async Task<ActionResult<ApiResponse<MultiLevelCacheStatistics>>> GetCacheStatisticsAsync()
    {
        try
        {
            var statistics = await _cacheService.GetMultiLevelStatisticsAsync();
            
            return Ok(ApiResponse<MultiLevelCacheStatistics>.CreateSuccess(statistics, "Cache statistics retrieved successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving cache statistics");
            return StatusCode(500, ApiResponse<MultiLevelCacheStatistics>.CreateFailure("An error occurred while retrieving cache statistics"));
        }
    }

    /// <summary>
    /// Gets basic cache statistics (for compatibility)
    /// </summary>
    /// <returns>Basic cache statistics</returns>
    [HttpGet("statistics/basic")]
    public async Task<ActionResult<ApiResponse<CacheStatistics>>> GetBasicCacheStatisticsAsync()
    {
        try
        {
            var statistics = await _cacheService.GetStatisticsAsync();
            
            return Ok(ApiResponse<CacheStatistics>.CreateSuccess(statistics, "Basic cache statistics retrieved successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving basic cache statistics");
            return StatusCode(500, ApiResponse<CacheStatistics>.CreateFailure("An error occurred while retrieving cache statistics"));
        }
    }

    /// <summary>
    /// Clears all cache entries
    /// </summary>
    /// <returns>Success response</returns>
    [HttpDelete("clear")]
    public async Task<ActionResult<ApiResponse<string>>> ClearCacheAsync()
    {
        try
        {
            await _cacheService.ClearAsync();
            
            _logger.LogInformation("Cache cleared by API request");
            return Ok(ApiResponse<string>.CreateSuccess("Cache cleared", "All cache entries have been cleared successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing cache");
            return StatusCode(500, ApiResponse<string>.CreateFailure("An error occurred while clearing the cache"));
        }
    }

    /// <summary>
    /// Removes cache entries by pattern
    /// </summary>
    /// <param name="pattern">Pattern to match cache keys</param>
    /// <returns>Success response</returns>
    [HttpDelete("pattern/{pattern}")]
    public async Task<ActionResult<ApiResponse<string>>> RemoveCacheByPatternAsync(string pattern)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return BadRequest(ApiResponse<string>.CreateSingleValidationError("pattern", "Pattern cannot be empty"));
            }

            await _cacheService.RemoveByPatternAsync(pattern);
            
            _logger.LogInformation("Cache entries removed by pattern: {Pattern}", pattern);
            return Ok(ApiResponse<string>.CreateSuccess("Cache entries removed", $"Cache entries matching pattern '{pattern}' have been removed"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache entries by pattern: {Pattern}", pattern);
            return StatusCode(500, ApiResponse<string>.CreateFailure("An error occurred while removing cache entries"));
        }
    }

    /// <summary>
    /// Checks if a specific cache key exists
    /// </summary>
    /// <param name="key">Cache key to check</param>
    /// <returns>Boolean indicating if key exists</returns>
    [HttpGet("exists/{key}")]
    public async Task<ActionResult<ApiResponse<bool>>> CheckCacheKeyExistsAsync(string key)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return BadRequest(ApiResponse<bool>.CreateSingleValidationError("key", "Cache key cannot be empty"));
            }

            var exists = await _cacheService.ExistsAsync(key);
            
            return Ok(ApiResponse<bool>.CreateSuccess(exists, $"Cache key '{key}' existence checked"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking cache key existence: {Key}", key);
            return StatusCode(500, ApiResponse<bool>.CreateFailure("An error occurred while checking cache key existence"));
        }
    }

    /// <summary>
    /// Gets cache health status
    /// </summary>
    /// <returns>Cache health information</returns>
    [HttpGet("health")]
    public async Task<ActionResult<ApiResponse<object>>> GetCacheHealthAsync()
    {
        try
        {
            var statistics = await _cacheService.GetMultiLevelStatisticsAsync();
            
            var health = new
            {
                Status = "Healthy",
                L1Cache = new
                {
                    Enabled = statistics.L1.EntryCount >= 0,
                    EntryCount = statistics.L1.EntryCount,
                    HitRatio = statistics.L1.HitRatio,
                    MemoryUsage = statistics.L1.MemoryUsage,
                    Uptime = statistics.L1.Uptime.ToString(@"dd\.hh\:mm\:ss")
                },
                L2Cache = new
                {
                    Enabled = statistics.L2.EntryCount >= 0,
                    EntryCount = statistics.L2.EntryCount,
                    HitRatio = statistics.L2.HitRatio,
                    MemoryUsage = statistics.L2.MemoryUsage
                },
                Overall = new
                {
                    TotalEntries = statistics.Overall.EntryCount,
                    HitRatio = statistics.Overall.HitRatio,
                    PromotionCount = statistics.PromotionCount,
                    L1AverageResponseTime = $"{statistics.L1AverageResponseTime:F2}ms",
                    L2AverageResponseTime = $"{statistics.L2AverageResponseTime:F2}ms"
                }
            };
            
            return Ok(ApiResponse<object>.CreateSuccess(health, "Cache health retrieved successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving cache health");
            return StatusCode(500, ApiResponse<object>.CreateFailure("An error occurred while retrieving cache health"));
        }
    }
} 