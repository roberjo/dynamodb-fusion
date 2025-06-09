using Amazon.DynamoDBv2;
using DynamoDbFusion.Core.Interfaces;
using DynamoDbFusion.Core.Models;
using DynamoDbFusion.Core.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace DynamoDbFusion.Core.Extensions;

/// <summary>
/// Extension methods for registering DynamoDB Fusion services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds DynamoDB Fusion services to the service collection
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddDynamoDbFusion(this IServiceCollection services)
    {
        // Register the main query service
        services.TryAddScoped<IDynamoDbQueryService, DynamoDbQueryService>();

        // Register AWS DynamoDB client if not already registered
        services.TryAddSingleton<IAmazonDynamoDB>(provider =>
        {
            return new AmazonDynamoDBClient();
        });

        return services;
    }

    /// <summary>
    /// Adds DynamoDB Fusion services with a custom DynamoDB client
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="dynamoDbClient">Custom DynamoDB client instance</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddDynamoDbFusion(
        this IServiceCollection services,
        IAmazonDynamoDB dynamoDbClient)
    {
        services.TryAddScoped<IDynamoDbQueryService, DynamoDbQueryService>();
        services.TryAddSingleton(dynamoDbClient);

        return services;
    }

    /// <summary>
    /// Adds DynamoDB Fusion services with a custom DynamoDB client factory
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="clientFactory">Factory function to create DynamoDB client</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddDynamoDbFusion(
        this IServiceCollection services,
        Func<IServiceProvider, IAmazonDynamoDB> clientFactory)
    {
        services.TryAddScoped<IDynamoDbQueryService, DynamoDbQueryService>();
        services.TryAddSingleton(clientFactory);

        return services;
    }

    /// <summary>
    /// Adds DynamoDB Fusion services with caching support
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureCache">Cache configuration action</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddDynamoDbFusionWithCaching(
        this IServiceCollection services,
        Action<CacheConfiguration>? configureCache = null)
    {
        // Add base DynamoDB Fusion services
        services.AddDynamoDbFusion();

        // Configure cache settings
        services.Configure<CacheConfiguration>(config =>
        {
            configureCache?.Invoke(config);
        });

        // Add L1 (memory) cache
        services.AddMemoryCache();
        services.TryAddSingleton<MemoryCacheService>();

        // Add multi-level cache service
        services.TryAddSingleton<IMultiLevelCacheService>(provider =>
        {
            var memoryCacheService = provider.GetRequiredService<MemoryCacheService>();
            var logger = provider.GetRequiredService<ILogger<MultiLevelCacheService>>();
            var config = provider.GetRequiredService<IOptions<CacheConfiguration>>();
            
            // Try to get L2 cache if configured
            RedisCacheService? l2Cache = null;
            if (config.Value.L2.Enabled && !string.IsNullOrEmpty(config.Value.L2.RedisConnectionString))
            {
                try
                {
                    l2Cache = provider.GetService<RedisCacheService>();
                }
                catch
                {
                    // L2 cache not available, continue with L1 only
                }
            }

            return new MultiLevelCacheService(memoryCacheService, logger, config, l2Cache);
        });

        // Add advanced services
        services.TryAddSingleton<MetricsService>();
        services.TryAddSingleton<CircuitBreakerService>();
        services.TryAddScoped<QueryOptimizationService>();
        services.TryAddScoped<EnhancedBatchOperationsService>();

        // Add health checks
        services.AddDynamoDbFusionHealthChecks();

        return services;
    }

    /// <summary>
    /// Adds Redis L2 cache to DynamoDB Fusion
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="connectionString">Redis connection string</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddDynamoDbFusionRedisCache(
        this IServiceCollection services,
        string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new ArgumentNullException(nameof(connectionString));
        }

        // Register Redis connection
        services.TryAddSingleton<IConnectionMultiplexer>(provider =>
        {
            var config = provider.GetRequiredService<IOptions<CacheConfiguration>>();
            var connectionOptions = ConfigurationOptions.Parse(connectionString);
            connectionOptions.ConnectTimeout = (int)config.Value.L2.ConnectionTimeout.TotalMilliseconds;
            connectionOptions.SyncTimeout = (int)config.Value.L2.OperationTimeout.TotalMilliseconds;
            
            return ConnectionMultiplexer.Connect(connectionOptions);
        });

        // Register Redis cache service
        services.TryAddSingleton<RedisCacheService>();

        return services;
    }
} 