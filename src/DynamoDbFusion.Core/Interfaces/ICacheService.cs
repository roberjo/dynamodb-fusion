namespace DynamoDbFusion.Core.Interfaces;

/// <summary>
/// Core caching service interface for DynamoDB query results
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Gets a cached value by key
    /// </summary>
    /// <typeparam name="T">Type of cached value</typeparam>
    /// <param name="key">Cache key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Cached value or null if not found</returns>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Sets a value in cache with expiration
    /// </summary>
    /// <typeparam name="T">Type of value to cache</typeparam>
    /// <param name="key">Cache key</param>
    /// <param name="value">Value to cache</param>
    /// <param name="expiration">Cache expiration time</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Removes a value from cache
    /// </summary>
    /// <param name="key">Cache key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes multiple values from cache by pattern
    /// </summary>
    /// <param name="pattern">Key pattern to match</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a key exists in cache
    /// </summary>
    /// <param name="key">Cache key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if key exists</returns>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets cache statistics
    /// </summary>
    /// <returns>Cache statistics</returns>
    Task<CacheStatistics> GetStatisticsAsync();

    /// <summary>
    /// Clears all cache entries
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Multi-level caching service interface
/// </summary>
public interface IMultiLevelCacheService : ICacheService
{
    /// <summary>
    /// Gets a value with cache level information
    /// </summary>
    /// <typeparam name="T">Type of cached value</typeparam>
    /// <param name="key">Cache key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Cache result with level information</returns>
    Task<CacheResult<T>> GetWithMetadataAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Promotes a value from L2 to L1 cache
    /// </summary>
    /// <typeparam name="T">Type of value</typeparam>
    /// <param name="key">Cache key</param>
    /// <param name="value">Value to promote</param>
    /// <param name="expiration">L1 expiration time</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task PromoteToL1Async<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Gets cache statistics for all levels
    /// </summary>
    /// <returns>Multi-level cache statistics</returns>
    Task<MultiLevelCacheStatistics> GetMultiLevelStatisticsAsync();
}

/// <summary>
/// Cache result with metadata
/// </summary>
/// <typeparam name="T">Type of cached value</typeparam>
public class CacheResult<T> where T : class
{
    /// <summary>
    /// The cached value
    /// </summary>
    public T? Value { get; set; }

    /// <summary>
    /// Cache level where value was found
    /// </summary>
    public CacheLevel Level { get; set; }

    /// <summary>
    /// Time when value was cached
    /// </summary>
    public DateTime CachedAt { get; set; }

    /// <summary>
    /// Time when cache entry expires
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Indicates if value was found in cache
    /// </summary>
    public bool Hit => Value != null;
}

/// <summary>
/// Cache level enumeration
/// </summary>
public enum CacheLevel
{
    /// <summary>
    /// Cache miss - value not found
    /// </summary>
    Miss = 0,

    /// <summary>
    /// L1 cache (in-memory, fastest)
    /// </summary>
    L1 = 1,

    /// <summary>
    /// L2 cache (distributed, slower but shared)
    /// </summary>
    L2 = 2
}

/// <summary>
/// Basic cache statistics
/// </summary>
public class CacheStatistics
{
    /// <summary>
    /// Total number of cache hits
    /// </summary>
    public long Hits { get; set; }

    /// <summary>
    /// Total number of cache misses
    /// </summary>
    public long Misses { get; set; }

    /// <summary>
    /// Cache hit ratio (0.0 to 1.0)
    /// </summary>
    public double HitRatio => Hits + Misses > 0 ? (double)Hits / (Hits + Misses) : 0.0;

    /// <summary>
    /// Number of entries currently in cache
    /// </summary>
    public long EntryCount { get; set; }

    /// <summary>
    /// Estimated memory usage in bytes
    /// </summary>
    public long MemoryUsage { get; set; }

    /// <summary>
    /// Cache service start time
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Cache service uptime
    /// </summary>
    public TimeSpan Uptime => DateTime.UtcNow - StartTime;
}

/// <summary>
/// Multi-level cache statistics
/// </summary>
public class MultiLevelCacheStatistics
{
    /// <summary>
    /// L1 cache statistics
    /// </summary>
    public CacheStatistics L1 { get; set; } = new();

    /// <summary>
    /// L2 cache statistics
    /// </summary>
    public CacheStatistics L2 { get; set; } = new();

    /// <summary>
    /// Overall cache statistics
    /// </summary>
    public CacheStatistics Overall { get; set; } = new();

    /// <summary>
    /// L1 to L2 promotion count
    /// </summary>
    public long PromotionCount { get; set; }

    /// <summary>
    /// Average response time for L1 cache in milliseconds
    /// </summary>
    public double L1AverageResponseTime { get; set; }

    /// <summary>
    /// Average response time for L2 cache in milliseconds
    /// </summary>
    public double L2AverageResponseTime { get; set; }
} 