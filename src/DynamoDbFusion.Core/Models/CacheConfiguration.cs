namespace DynamoDbFusion.Core.Models;

/// <summary>
/// Cache configuration settings
/// </summary>
public class CacheConfiguration
{
    /// <summary>
    /// Enables or disables caching globally
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// L1 cache configuration
    /// </summary>
    public L1CacheConfiguration L1 { get; set; } = new();

    /// <summary>
    /// L2 cache configuration
    /// </summary>
    public L2CacheConfiguration L2 { get; set; } = new();

    /// <summary>
    /// Cache key prefix for namespacing
    /// </summary>
    public string KeyPrefix { get; set; } = "dynamodb-fusion";

    /// <summary>
    /// Default cache expiration time
    /// </summary>
    public TimeSpan DefaultExpiration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum cache key length
    /// </summary>
    public int MaxKeyLength { get; set; } = 250;

    /// <summary>
    /// Cache invalidation strategy
    /// </summary>
    public CacheInvalidationStrategy InvalidationStrategy { get; set; } = CacheInvalidationStrategy.TimeBasedExpiration;
}

/// <summary>
/// L1 (in-memory) cache configuration
/// </summary>
public class L1CacheConfiguration
{
    /// <summary>
    /// Enables L1 cache
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum number of entries in L1 cache
    /// </summary>
    public int MaxEntries { get; set; } = 1000;

    /// <summary>
    /// Maximum memory usage in MB for L1 cache
    /// </summary>
    public int MaxMemoryMB { get; set; } = 100;

    /// <summary>
    /// L1 cache expiration time
    /// </summary>
    public TimeSpan Expiration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Cache eviction policy when limits are reached
    /// </summary>
    public EvictionPolicy EvictionPolicy { get; set; } = EvictionPolicy.LeastRecentlyUsed;

    /// <summary>
    /// Background cleanup interval
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>
/// L2 (distributed) cache configuration
/// </summary>
public class L2CacheConfiguration
{
    /// <summary>
    /// Enables L2 cache
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// L2 cache expiration time
    /// </summary>
    public TimeSpan Expiration { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Redis connection string (if using Redis)
    /// </summary>
    public string? RedisConnectionString { get; set; }

    /// <summary>
    /// Redis database number
    /// </summary>
    public int RedisDatabase { get; set; } = 0;

    /// <summary>
    /// Connection timeout for L2 cache operations
    /// </summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Operation timeout for L2 cache operations
    /// </summary>
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Maximum number of retry attempts for failed operations
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Compression settings for L2 cache
    /// </summary>
    public CompressionConfiguration Compression { get; set; } = new();
}

/// <summary>
/// Cache eviction policies
/// </summary>
public enum EvictionPolicy
{
    /// <summary>
    /// Least Recently Used - evict oldest accessed items first
    /// </summary>
    LeastRecentlyUsed,

    /// <summary>
    /// Least Frequently Used - evict least accessed items first
    /// </summary>
    LeastFrequentlyUsed,

    /// <summary>
    /// First In, First Out - evict oldest items first
    /// </summary>
    FirstInFirstOut,

    /// <summary>
    /// Random eviction
    /// </summary>
    Random
}

/// <summary>
/// Cache invalidation strategies
/// </summary>
public enum CacheInvalidationStrategy
{
    /// <summary>
    /// Time-based expiration only
    /// </summary>
    TimeBasedExpiration,

    /// <summary>
    /// Invalidate based on DynamoDB table updates
    /// </summary>
    TableBasedInvalidation,

    /// <summary>
    /// Manual invalidation through API calls
    /// </summary>
    ManualInvalidation,

    /// <summary>
    /// Combination of time-based and event-based invalidation
    /// </summary>
    Hybrid
}

/// <summary>
/// Compression configuration for cache values
/// </summary>
public class CompressionConfiguration
{
    /// <summary>
    /// Enables compression for cached values
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Minimum size in bytes before compression is applied
    /// </summary>
    public int MinimumSize { get; set; } = 1024;

    /// <summary>
    /// Compression algorithm to use
    /// </summary>
    public CompressionAlgorithm Algorithm { get; set; } = CompressionAlgorithm.Gzip;

    /// <summary>
    /// Compression level (1-9, higher = better compression but slower)
    /// </summary>
    public int CompressionLevel { get; set; } = 6;
}

/// <summary>
/// Available compression algorithms
/// </summary>
public enum CompressionAlgorithm
{
    /// <summary>
    /// No compression
    /// </summary>
    None,

    /// <summary>
    /// Gzip compression
    /// </summary>
    Gzip,

    /// <summary>
    /// Deflate compression
    /// </summary>
    Deflate,

    /// <summary>
    /// Brotli compression (best compression ratio)
    /// </summary>
    Brotli
} 