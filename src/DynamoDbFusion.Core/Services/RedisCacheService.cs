using DynamoDbFusion.Core.Interfaces;
using DynamoDbFusion.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace DynamoDbFusion.Core.Services;

/// <summary>
/// Redis-based L2 cache service implementation
/// </summary>
public class RedisCacheService : ICacheService, IDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _database;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly CacheConfiguration _config;
    private readonly CacheStatistics _statistics;
    private bool _disposed;

    public RedisCacheService(
        IConnectionMultiplexer redis,
        ILogger<RedisCacheService> logger,
        IOptions<CacheConfiguration> config)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config.Value ?? throw new ArgumentNullException(nameof(config));
        
        _database = _redis.GetDatabase(_config.L2.RedisDatabase);
        _statistics = new CacheStatistics
        {
            StartTime = DateTime.UtcNow
        };
        
        _logger.LogInformation("RedisCacheService initialized with database: {Database}", _config.L2.RedisDatabase);
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        if (!_config.Enabled || !_config.L2.Enabled)
        {
            _statistics.Misses++;
            return null;
        }

        try
        {
            var cacheKey = BuildCacheKey(key);
            var redisValue = await RetryOperationAsync(async () => 
                await _database.StringGetAsync(cacheKey));

            if (redisValue.HasValue)
            {
                _statistics.Hits++;
                
                var jsonValue = await DecompressIfNeededAsync(redisValue!);
                var result = JsonSerializer.Deserialize<T>(jsonValue);
                
                _logger.LogDebug("Cache hit in Redis for key: {Key}", key);
                return result;
            }

            _statistics.Misses++;
            _logger.LogDebug("Cache miss in Redis for key: {Key}", key);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving value from Redis cache for key: {Key}", key);
            _statistics.Misses++;
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default) where T : class
    {
        if (!_config.Enabled || !_config.L2.Enabled || value == null)
        {
            return;
        }

        try
        {
            var cacheKey = BuildCacheKey(key);
            var jsonValue = JsonSerializer.Serialize(value);
            
            var compressedValue = await CompressIfNeededAsync(jsonValue);
            
            await RetryOperationAsync(async () => 
                await _database.StringSetAsync(cacheKey, compressedValue, expiration));
                
            _statistics.EntryCount++;
            _logger.LogDebug("Cached value in Redis for key: {Key} with expiration: {Expiration}", key, expiration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting cache value in Redis for key: {Key}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var cacheKey = BuildCacheKey(key);
            var removed = await RetryOperationAsync(async () => 
                await _database.KeyDeleteAsync(cacheKey));
                
            if (removed)
            {
                _statistics.EntryCount = Math.Max(0, _statistics.EntryCount - 1);
            }
            
            _logger.LogDebug("Removed cache entry from Redis for key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache entry from Redis for key: {Key}", key);
        }
    }

    public async Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        try
        {
            var searchPattern = BuildCacheKey(pattern);
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            
            var keys = server.Keys(_config.L2.RedisDatabase, searchPattern).ToArray();
            
            if (keys.Length > 0)
            {
                var deletedCount = await RetryOperationAsync(async () => 
                    await _database.KeyDeleteAsync(keys));
                    
                _statistics.EntryCount = Math.Max(0, _statistics.EntryCount - deletedCount);
                
                _logger.LogDebug("Removed {Count} cache entries from Redis matching pattern: {Pattern}", 
                    deletedCount, pattern);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache entries from Redis by pattern: {Pattern}", pattern);
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled || !_config.L2.Enabled)
        {
            return false;
        }

        try
        {
            var cacheKey = BuildCacheKey(key);
            return await RetryOperationAsync(async () => 
                await _database.KeyExistsAsync(cacheKey));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking key existence in Redis for key: {Key}", key);
            return false;
        }
    }

    public async Task<CacheStatistics> GetStatisticsAsync()
    {
        try
        {
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var info = await server.InfoAsync("memory");
            
            _statistics.MemoryUsage = ExtractMemoryUsage(info.ToString());
            
            // Get approximate key count (this is expensive for large databases)
            var keyCount = await RetryOperationAsync(async () => 
                await _database.ExecuteAsync("DBSIZE"));
                
            _statistics.EntryCount = (long)keyCount;
            
            return _statistics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving Redis cache statistics");
            return _statistics;
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            await server.FlushDatabaseAsync(_config.L2.RedisDatabase);
            
            _statistics.EntryCount = 0;
            _logger.LogInformation("Cleared all entries from Redis database: {Database}", _config.L2.RedisDatabase);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing Redis cache");
        }
    }

    private string BuildCacheKey(string key)
    {
        var fullKey = $"{_config.KeyPrefix}:l2:{key}";
        
        if (fullKey.Length > _config.MaxKeyLength)
        {
            // Hash long keys to keep them under the limit
            var hash = fullKey.GetHashCode();
            fullKey = $"{_config.KeyPrefix}:l2:hash:{Math.Abs(hash)}";
        }
        
        return fullKey;
    }

    private async Task<T> RetryOperationAsync<T>(Func<Task<T>> operation)
    {
        var attempts = 0;
        while (attempts < _config.L2.MaxRetryAttempts)
        {
            try
            {
                using var cts = new CancellationTokenSource(_config.L2.OperationTimeout);
                return await operation();
            }
            catch (Exception ex) when (attempts < _config.L2.MaxRetryAttempts - 1)
            {
                attempts++;
                _logger.LogWarning(ex, "Redis operation failed, attempt {Attempt}/{MaxAttempts}", 
                    attempts, _config.L2.MaxRetryAttempts);
                
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Pow(2, attempts) * 100));
            }
        }
        
        throw new InvalidOperationException($"Redis operation failed after {_config.L2.MaxRetryAttempts} attempts");
    }

    private async Task<string> CompressIfNeededAsync(string value)
    {
        if (!_config.L2.Compression.Enabled || 
            Encoding.UTF8.GetByteCount(value) < _config.L2.Compression.MinimumSize)
        {
            return value;
        }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            
            using var output = new MemoryStream();
            
            Stream compressionStream = _config.L2.Compression.Algorithm switch
            {
                CompressionAlgorithm.Gzip => new GZipStream(output, CompressionLevel.Optimal),
                CompressionAlgorithm.Deflate => new DeflateStream(output, CompressionLevel.Optimal),
                CompressionAlgorithm.Brotli => new BrotliStream(output, CompressionLevel.Optimal),
                _ => throw new NotSupportedException($"Compression algorithm {_config.L2.Compression.Algorithm} not supported")
            };

            using (compressionStream)
            {
                await compressionStream.WriteAsync(bytes);
            }

            var compressedBytes = output.ToArray();
            var compressedValue = Convert.ToBase64String(compressedBytes);
            
            // Add compression marker
            return $"COMPRESSED:{_config.L2.Compression.Algorithm}:{compressedValue}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to compress cache value, storing uncompressed");
            return value;
        }
    }

    private async Task<string> DecompressIfNeededAsync(string value)
    {
        if (!value.StartsWith("COMPRESSED:"))
        {
            return value;
        }

        try
        {
            var parts = value.Split(':', 3);
            if (parts.Length != 3)
            {
                return value;
            }

            var algorithmStr = parts[1];
            var compressedData = Convert.FromBase64String(parts[2]);
            
            if (!Enum.TryParse<CompressionAlgorithm>(algorithmStr, out var algorithm))
            {
                return value;
            }

            using var input = new MemoryStream(compressedData);
            using var output = new MemoryStream();
            
            Stream decompressionStream = algorithm switch
            {
                CompressionAlgorithm.Gzip => new GZipStream(input, CompressionMode.Decompress),
                CompressionAlgorithm.Deflate => new DeflateStream(input, CompressionMode.Decompress),
                CompressionAlgorithm.Brotli => new BrotliStream(input, CompressionMode.Decompress),
                _ => throw new NotSupportedException($"Compression algorithm {algorithm} not supported")
            };

            using (decompressionStream)
            {
                await decompressionStream.CopyToAsync(output);
            }

            return Encoding.UTF8.GetString(output.ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decompress cache value");
            throw;
        }
    }

    private static long ExtractMemoryUsage(string info)
    {
        try
        {
            var lines = info.Split('\n');
            var memoryLine = lines.FirstOrDefault(line => line.StartsWith("used_memory:"));
            if (memoryLine != null)
            {
                var value = memoryLine.Split(':')[1].Trim();
                if (long.TryParse(value, out var memoryUsage))
                {
                    return memoryUsage;
                }
            }
        }
        catch
        {
            // Ignore parsing errors
        }
        
        return 0;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _redis?.Dispose();
            _disposed = true;
        }
    }
} 