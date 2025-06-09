using DynamoDbFusion.Core.Interfaces;
using DynamoDbFusion.Core.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text.Json;

namespace DynamoDbFusion.Core.Services;

/// <summary>
/// L1 in-memory cache service implementation
/// </summary>
public class MemoryCacheService : ICacheService, IDisposable
{
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<MemoryCacheService> _logger;
    private readonly CacheConfiguration _config;
    private readonly CacheStatistics _statistics;
    private readonly ConcurrentDictionary<string, DateTime> _accessTimes;
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    public MemoryCacheService(
        IMemoryCache memoryCache,
        ILogger<MemoryCacheService> logger,
        IOptions<CacheConfiguration> config)
    {
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config.Value ?? throw new ArgumentNullException(nameof(config));
        
        _statistics = new CacheStatistics
        {
            StartTime = DateTime.UtcNow
        };
        
        _accessTimes = new ConcurrentDictionary<string, DateTime>();
        
        // Setup cleanup timer
        _cleanupTimer = new Timer(PerformCleanup, null, 
            _config.L1.CleanupInterval, 
            _config.L1.CleanupInterval);
            
        _logger.LogInformation("MemoryCacheService initialized with L1 cache enabled: {Enabled}", _config.L1.Enabled);
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        if (!_config.Enabled || !_config.L1.Enabled)
        {
            _statistics.Misses++;
            return null;
        }

        try
        {
            var cacheKey = BuildCacheKey(key);
            
            if (_memoryCache.TryGetValue(cacheKey, out var cachedValue))
            {
                _statistics.Hits++;
                _accessTimes[cacheKey] = DateTime.UtcNow;
                
                if (cachedValue is string jsonValue)
                {
                    var result = JsonSerializer.Deserialize<T>(jsonValue);
                    _logger.LogDebug("Cache hit for key: {Key}", key);
                    return result;
                }
                else if (cachedValue is T directValue)
                {
                    _logger.LogDebug("Cache hit for key: {Key}", key);
                    return directValue;
                }
            }

            _statistics.Misses++;
            _logger.LogDebug("Cache miss for key: {Key}", key);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving value from cache for key: {Key}", key);
            _statistics.Misses++;
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default) where T : class
    {
        if (!_config.Enabled || !_config.L1.Enabled || value == null)
        {
            return;
        }

        try
        {
            var cacheKey = BuildCacheKey(key);
            
            // Check if we're approaching memory limits
            if (ShouldEvictEntries())
            {
                await EvictOldestEntriesAsync();
            }

            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration,
                Priority = CacheItemPriority.Normal,
                Size = EstimateSize(value)
            };

            // Add eviction callback to track removals
            cacheOptions.RegisterPostEvictionCallback((evictedKey, evictedValue, reason, state) =>
            {
                if (evictedKey is string keyStr)
                {
                    _accessTimes.TryRemove(keyStr, out _);
                    _statistics.EntryCount = Math.Max(0, _statistics.EntryCount - 1);
                }
            });

            // Serialize complex objects to JSON for consistent storage
            object cacheValue = typeof(T) == typeof(string) ? value : JsonSerializer.Serialize(value);
            
            _memoryCache.Set(cacheKey, cacheValue, cacheOptions);
            _accessTimes[cacheKey] = DateTime.UtcNow;
            _statistics.EntryCount++;
            
            _logger.LogDebug("Cached value for key: {Key} with expiration: {Expiration}", key, expiration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting cache value for key: {Key}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var cacheKey = BuildCacheKey(key);
            _memoryCache.Remove(cacheKey);
            _accessTimes.TryRemove(cacheKey, out _);
            
            _logger.LogDebug("Removed cache entry for key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache entry for key: {Key}", key);
        }
    }

    public async Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        try
        {
            // Since IMemoryCache doesn't support pattern matching directly,
            // we'll need to track keys separately or use a workaround
            var keysToRemove = _accessTimes.Keys
                .Where(key => IsPatternMatch(key, pattern))
                .ToList();

            foreach (var key in keysToRemove)
            {
                _memoryCache.Remove(key);
                _accessTimes.TryRemove(key, out _);
            }

            _logger.LogDebug("Removed {Count} cache entries matching pattern: {Pattern}", keysToRemove.Count, pattern);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache entries by pattern: {Pattern}", pattern);
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled || !_config.L1.Enabled)
        {
            return false;
        }

        var cacheKey = BuildCacheKey(key);
        return _memoryCache.TryGetValue(cacheKey, out _);
    }

    public async Task<CacheStatistics> GetStatisticsAsync()
    {
        _statistics.EntryCount = _accessTimes.Count;
        _statistics.MemoryUsage = EstimateMemoryUsage();
        return _statistics;
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Since IMemoryCache doesn't have a clear method, we'll dispose and recreate
            var keysToRemove = _accessTimes.Keys.ToList();
            
            foreach (var key in keysToRemove)
            {
                _memoryCache.Remove(key);
            }
            
            _accessTimes.Clear();
            _statistics.EntryCount = 0;
            
            _logger.LogInformation("Cleared all cache entries");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing cache");
        }
    }

    private string BuildCacheKey(string key)
    {
        var fullKey = $"{_config.KeyPrefix}:l1:{key}";
        
        if (fullKey.Length > _config.MaxKeyLength)
        {
            // Hash long keys to keep them under the limit
            var hash = fullKey.GetHashCode();
            fullKey = $"{_config.KeyPrefix}:l1:hash:{Math.Abs(hash)}";
        }
        
        return fullKey;
    }

    private bool ShouldEvictEntries()
    {
        return _accessTimes.Count >= _config.L1.MaxEntries ||
               EstimateMemoryUsage() >= _config.L1.MaxMemoryMB * 1024 * 1024;
    }

    private async Task EvictOldestEntriesAsync()
    {
        var evictionCount = Math.Max(1, _config.L1.MaxEntries / 10); // Evict 10% of entries
        
        var oldestEntries = _accessTimes
            .OrderBy(kvp => kvp.Value)
            .Take(evictionCount)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in oldestEntries)
        {
            _memoryCache.Remove(key);
            _accessTimes.TryRemove(key, out _);
        }

        _logger.LogDebug("Evicted {Count} cache entries due to memory pressure", oldestEntries.Count);
    }

    private void PerformCleanup(object? state)
    {
        try
        {
            // Remove expired entries from our tracking
            var expiredKeys = _accessTimes
                .Where(kvp => DateTime.UtcNow - kvp.Value > _config.L1.Expiration)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                _accessTimes.TryRemove(key, out _);
            }

            if (expiredKeys.Any())
            {
                _logger.LogDebug("Cleaned up {Count} expired cache entry references", expiredKeys.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cache cleanup");
        }
    }

    private static int EstimateSize<T>(T value) where T : class
    {
        if (value is string strValue)
        {
            return strValue.Length * 2; // Approximate string size in bytes
        }
        
        try
        {
            var json = JsonSerializer.Serialize(value);
            return json.Length * 2; // Approximate serialized size
        }
        catch
        {
            return 1024; // Default estimate
        }
    }

    private long EstimateMemoryUsage()
    {
        // This is a rough estimate since .NET memory cache doesn't provide exact memory usage
        return _accessTimes.Count * 1024; // Rough estimate of 1KB per entry
    }

    private static bool IsPatternMatch(string key, string pattern)
    {
        // Simple pattern matching - can be enhanced with regex if needed
        if (pattern.Contains("*"))
        {
            var parts = pattern.Split('*');
            return key.StartsWith(parts[0]) && (parts.Length == 1 || key.EndsWith(parts[^1]));
        }
        
        return key.Contains(pattern);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _cleanupTimer?.Dispose();
            _disposed = true;
        }
    }
} 