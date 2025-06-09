using DynamoDbFusion.Core.Interfaces;
using DynamoDbFusion.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace DynamoDbFusion.Core.Services;

/// <summary>
/// Multi-level cache service that combines L1 (memory) and L2 (distributed) caches
/// </summary>
public class MultiLevelCacheService : IMultiLevelCacheService
{
    private readonly ICacheService _l1Cache;
    private readonly ICacheService? _l2Cache;
    private readonly ILogger<MultiLevelCacheService> _logger;
    private readonly CacheConfiguration _config;
    private readonly MultiLevelCacheStatistics _statistics;

    public MultiLevelCacheService(
        ICacheService l1Cache,
        ILogger<MultiLevelCacheService> logger,
        IOptions<CacheConfiguration> config,
        ICacheService? l2Cache = null)
    {
        _l1Cache = l1Cache ?? throw new ArgumentNullException(nameof(l1Cache));
        _l2Cache = l2Cache;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config.Value ?? throw new ArgumentNullException(nameof(config));
        
        _statistics = new MultiLevelCacheStatistics();
        
        _logger.LogInformation("MultiLevelCacheService initialized. L1: {L1Enabled}, L2: {L2Enabled}", 
            _config.L1.Enabled, _config.L2.Enabled && _l2Cache != null);
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        var result = await GetWithMetadataAsync<T>(key, cancellationToken);
        return result.Value;
    }

    public async Task<CacheResult<T>> GetWithMetadataAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        if (!_config.Enabled)
        {
            return new CacheResult<T> { Level = CacheLevel.Miss };
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Try L1 cache first
            if (_config.L1.Enabled)
            {
                var l1Value = await _l1Cache.GetAsync<T>(key, cancellationToken);
                if (l1Value != null)
                {
                    stopwatch.Stop();
                    _statistics.L1AverageResponseTime = UpdateAverageResponseTime(_statistics.L1AverageResponseTime, stopwatch.ElapsedMilliseconds);
                    
                    _logger.LogDebug("Cache hit in L1 for key: {Key}", key);
                    return new CacheResult<T>
                    {
                        Value = l1Value,
                        Level = CacheLevel.L1,
                        CachedAt = DateTime.UtcNow // Approximate
                    };
                }
            }

            // Try L2 cache if L1 missed
            if (_config.L2.Enabled && _l2Cache != null)
            {
                var l2Value = await _l2Cache.GetAsync<T>(key, cancellationToken);
                if (l2Value != null)
                {
                    stopwatch.Stop();
                    _statistics.L2AverageResponseTime = UpdateAverageResponseTime(_statistics.L2AverageResponseTime, stopwatch.ElapsedMilliseconds);
                    
                    // Promote to L1 cache for faster future access
                    await PromoteToL1Async(key, l2Value, _config.L1.Expiration, cancellationToken);
                    
                    _logger.LogDebug("Cache hit in L2 for key: {Key}, promoted to L1", key);
                    return new CacheResult<T>
                    {
                        Value = l2Value,
                        Level = CacheLevel.L2,
                        CachedAt = DateTime.UtcNow // Approximate
                    };
                }
            }

            stopwatch.Stop();
            _logger.LogDebug("Cache miss for key: {Key}", key);
            return new CacheResult<T> { Level = CacheLevel.Miss };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving value from multi-level cache for key: {Key}", key);
            return new CacheResult<T> { Level = CacheLevel.Miss };
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default) where T : class
    {
        if (!_config.Enabled || value == null)
        {
            return;
        }

        var tasks = new List<Task>();

        try
        {
            // Set in L1 cache
            if (_config.L1.Enabled)
            {
                var l1Expiration = TimeSpan.FromTicks(Math.Min(expiration.Ticks, _config.L1.Expiration.Ticks));
                tasks.Add(_l1Cache.SetAsync(key, value, l1Expiration, cancellationToken));
            }

            // Set in L2 cache
            if (_config.L2.Enabled && _l2Cache != null)
            {
                var l2Expiration = TimeSpan.FromTicks(Math.Min(expiration.Ticks, _config.L2.Expiration.Ticks));
                tasks.Add(_l2Cache.SetAsync(key, value, l2Expiration, cancellationToken));
            }

            await Task.WhenAll(tasks);
            _logger.LogDebug("Cached value in multi-level cache for key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting value in multi-level cache for key: {Key}", key);
        }
    }

    public async Task PromoteToL1Async<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default) where T : class
    {
        if (!_config.L1.Enabled || value == null)
        {
            return;
        }

        try
        {
            await _l1Cache.SetAsync(key, value, expiration, cancellationToken);
            _statistics.PromotionCount++;
            _logger.LogDebug("Promoted value to L1 cache for key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error promoting value to L1 cache for key: {Key}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var tasks = new List<Task>();

        try
        {
            if (_config.L1.Enabled)
            {
                tasks.Add(_l1Cache.RemoveAsync(key, cancellationToken));
            }

            if (_config.L2.Enabled && _l2Cache != null)
            {
                tasks.Add(_l2Cache.RemoveAsync(key, cancellationToken));
            }

            await Task.WhenAll(tasks);
            _logger.LogDebug("Removed cache entry from all levels for key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache entry for key: {Key}", key);
        }
    }

    public async Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        var tasks = new List<Task>();

        try
        {
            if (_config.L1.Enabled)
            {
                tasks.Add(_l1Cache.RemoveByPatternAsync(pattern, cancellationToken));
            }

            if (_config.L2.Enabled && _l2Cache != null)
            {
                tasks.Add(_l2Cache.RemoveByPatternAsync(pattern, cancellationToken));
            }

            await Task.WhenAll(tasks);
            _logger.LogDebug("Removed cache entries from all levels matching pattern: {Pattern}", pattern);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache entries by pattern: {Pattern}", pattern);
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled)
        {
            return false;
        }

        try
        {
            // Check L1 first as it's faster
            if (_config.L1.Enabled && await _l1Cache.ExistsAsync(key, cancellationToken))
            {
                return true;
            }

            // Check L2 if not found in L1
            if (_config.L2.Enabled && _l2Cache != null && await _l2Cache.ExistsAsync(key, cancellationToken))
            {
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking cache existence for key: {Key}", key);
            return false;
        }
    }

    public async Task<CacheStatistics> GetStatisticsAsync()
    {
        try
        {
            var l1Stats = _config.L1.Enabled ? await _l1Cache.GetStatisticsAsync() : new CacheStatistics();
            var l2Stats = _config.L2.Enabled && _l2Cache != null ? await _l2Cache.GetStatisticsAsync() : new CacheStatistics();

            return new CacheStatistics
            {
                Hits = l1Stats.Hits + l2Stats.Hits,
                Misses = l1Stats.Misses + l2Stats.Misses,
                EntryCount = l1Stats.EntryCount + l2Stats.EntryCount,
                MemoryUsage = l1Stats.MemoryUsage + l2Stats.MemoryUsage,
                StartTime = l1Stats.StartTime < l2Stats.StartTime ? l1Stats.StartTime : l2Stats.StartTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving cache statistics");
            return new CacheStatistics();
        }
    }

    public async Task<MultiLevelCacheStatistics> GetMultiLevelStatisticsAsync()
    {
        try
        {
            _statistics.L1 = _config.L1.Enabled ? await _l1Cache.GetStatisticsAsync() : new CacheStatistics();
            _statistics.L2 = _config.L2.Enabled && _l2Cache != null ? await _l2Cache.GetStatisticsAsync() : new CacheStatistics();
            
            _statistics.Overall = new CacheStatistics
            {
                Hits = _statistics.L1.Hits + _statistics.L2.Hits,
                Misses = _statistics.L1.Misses + _statistics.L2.Misses,
                EntryCount = _statistics.L1.EntryCount + _statistics.L2.EntryCount,
                MemoryUsage = _statistics.L1.MemoryUsage + _statistics.L2.MemoryUsage,
                StartTime = _statistics.L1.StartTime < _statistics.L2.StartTime ? _statistics.L1.StartTime : _statistics.L2.StartTime
            };

            return _statistics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving multi-level cache statistics");
            return new MultiLevelCacheStatistics();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        var tasks = new List<Task>();

        try
        {
            if (_config.L1.Enabled)
            {
                tasks.Add(_l1Cache.ClearAsync(cancellationToken));
            }

            if (_config.L2.Enabled && _l2Cache != null)
            {
                tasks.Add(_l2Cache.ClearAsync(cancellationToken));
            }

            await Task.WhenAll(tasks);
            
            // Reset statistics
            _statistics.PromotionCount = 0;
            _statistics.L1AverageResponseTime = 0;
            _statistics.L2AverageResponseTime = 0;
            
            _logger.LogInformation("Cleared all cache levels");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing multi-level cache");
        }
    }

    private static double UpdateAverageResponseTime(double currentAverage, long newValue)
    {
        // Simple moving average calculation
        return currentAverage == 0 ? newValue : (currentAverage * 0.9) + (newValue * 0.1);
    }
} 