using DynamoDbFusion.Core.Interfaces;
using DynamoDbFusion.Core.Models;
using DynamoDbFusion.Core.Exceptions;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace DynamoDbFusion.Core.Services;

/// <summary>
/// Enhanced batch operations service with intelligent batching and optimization
/// </summary>
public class EnhancedBatchOperationsService
{
    private readonly IDynamoDbQueryService _queryService;
    private readonly CircuitBreakerService _circuitBreakerService;
    private readonly MetricsService _metricsService;
    private readonly ILogger<EnhancedBatchOperationsService> _logger;
    private readonly SemaphoreSlim _concurrencyLimiter;

    public EnhancedBatchOperationsService(
        IDynamoDbQueryService queryService,
        CircuitBreakerService circuitBreakerService,
        MetricsService metricsService,
        ILogger<EnhancedBatchOperationsService> logger)
    {
        _queryService = queryService;
        _circuitBreakerService = circuitBreakerService;
        _metricsService = metricsService;
        _logger = logger;
        _concurrencyLimiter = new SemaphoreSlim(10, 10); // Limit concurrent batches
    }

    /// <summary>
    /// Executes batch queries with intelligent optimization and retry logic
    /// </summary>
    /// <typeparam name="TDto">The DTO type to map results to</typeparam>
    /// <param name="requests">Collection of query requests</param>
    /// <param name="options">Batch execution options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Enhanced batch results</returns>
    public async Task<EnhancedBatchResult<TDto>> ExecuteBatchAsync<TDto>(
        IEnumerable<DynamoDbQueryRequest> requests,
        BatchExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
        where TDto : class, new()
    {
        options ??= BatchExecutionOptions.Default;
        var stopwatch = Stopwatch.StartNew();
        var requestList = requests.ToList();

        _logger.LogInformation("Starting enhanced batch execution with {RequestCount} requests", requestList.Count);

        try
        {
            // Validate and prepare requests
            var validationResult = ValidateBatchRequests(requestList, options);
            if (!validationResult.IsValid)
            {
                throw new ValidationException("Batch validation failed", validationResult.Errors);
            }

            // Optimize batch execution
            var optimizedBatches = OptimizeBatches(requestList, options);

            // Execute batches with circuit breaker protection
            var batchResults = await ExecuteOptimizedBatchesAsync<TDto>(optimizedBatches, options, cancellationToken);

            // Combine and analyze results
            var enhancedResult = CombineBatchResults<TDto>(batchResults, stopwatch.Elapsed);

            stopwatch.Stop();
            enhancedResult.TotalExecutionTimeMs = stopwatch.ElapsedMilliseconds;

            _logger.LogInformation("Enhanced batch execution completed in {Duration}ms with {SuccessfulBatches}/{TotalBatches} successful batches",
                stopwatch.ElapsedMilliseconds, enhancedResult.SuccessfulBatches, enhancedResult.TotalBatches);

            // Record metrics
            _metricsService.RecordRequest($"batch-{typeof(TDto).Name}", "POST", 200, stopwatch.ElapsedMilliseconds);

            return enhancedResult;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Enhanced batch execution failed after {Duration}ms", stopwatch.ElapsedMilliseconds);
            
            _metricsService.RecordError("BatchExecutionError", "ExecuteBatch", typeof(TDto).Name, ex);
            
            throw;
        }
    }

    /// <summary>
    /// Streams batch results for large datasets
    /// </summary>
    /// <typeparam name="TDto">The DTO type to map results to</typeparam>
    /// <param name="requests">Collection of query requests</param>
    /// <param name="options">Batch execution options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Async enumerable of batch results</returns>
    public async IAsyncEnumerable<BatchChunk<TDto>> StreamBatchAsync<TDto>(
        IEnumerable<DynamoDbQueryRequest> requests,
        BatchExecutionOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TDto : class, new()
    {
        options ??= BatchExecutionOptions.Default;
        var requestList = requests.ToList();
        var optimizedBatches = OptimizeBatches(requestList, options);

        foreach (var batch in optimizedBatches)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            var chunkResult = await ExecuteSingleBatchAsync<TDto>(batch, options, cancellationToken);
            
            yield return new BatchChunk<TDto>
            {
                ChunkId = batch.BatchId,
                Items = chunkResult.AllItems.ToList(),
                RequestCount = batch.Requests.Count,
                ExecutionTimeMs = chunkResult.TotalExecutionTimeMs,
                SuccessfulRequests = chunkResult.SuccessfulQueries,
                FailedRequests = chunkResult.FailedQueries
            };
        }
    }

    /// <summary>
    /// Executes parallel batch queries with automatic retry and fallback
    /// </summary>
    /// <typeparam name="TDto">The DTO type to map results to</typeparam>
    /// <param name="requests">Collection of query requests</param>
    /// <param name="maxConcurrency">Maximum concurrent operations</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Parallel batch results</returns>
    public async Task<ParallelBatchResult<TDto>> ExecuteParallelBatchAsync<TDto>(
        IEnumerable<DynamoDbQueryRequest> requests,
        int maxConcurrency = 5,
        CancellationToken cancellationToken = default)
        where TDto : class, new()
    {
        var requestList = requests.ToList();
        var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var results = new ConcurrentBag<ApiResponse<PagedResult<TDto>>>();
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Starting parallel batch execution with {RequestCount} requests and {MaxConcurrency} max concurrency",
            requestList.Count, maxConcurrency);

        try
        {
            var tasks = requestList.Select(async request =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var result = await _circuitBreakerService.ExecuteAsync(
                        $"query-{request.TableName}",
                        () => _queryService.QueryAsync<TDto>(request, cancellationToken),
                        () => CreateFallbackResult<TDto>(request)
                    );
                    
                    results.Add(result);
                    return result;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            stopwatch.Stop();

            var allResults = results.ToList();
            var successfulResults = allResults.Where(r => r.Success).ToList();
            var failedResults = allResults.Where(r => !r.Success).ToList();

            var parallelResult = new ParallelBatchResult<TDto>
            {
                TotalRequests = requestList.Count,
                SuccessfulRequests = successfulResults.Count,
                FailedRequests = failedResults.Count,
                TotalExecutionTimeMs = stopwatch.ElapsedMilliseconds,
                Items = successfulResults.SelectMany(r => r.Data?.Items ?? Enumerable.Empty<TDto>()).ToList(),
                Errors = failedResults.Select(r => r.Message ?? "Unknown error").ToList(),
                AverageRequestTime = successfulResults.Count > 0 
                    ? successfulResults.Average(r => r.Metadata?.ContainsKey("ExecutionTimeMs") == true 
                        ? Convert.ToDouble(r.Metadata["ExecutionTimeMs"]) : 0) 
                    : 0
            };

            _logger.LogInformation("Parallel batch execution completed: {Successful}/{Total} successful in {Duration}ms",
                parallelResult.SuccessfulRequests, parallelResult.TotalRequests, stopwatch.ElapsedMilliseconds);

            return parallelResult;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Parallel batch execution failed after {Duration}ms", stopwatch.ElapsedMilliseconds);
            throw;
        }
        finally
        {
            semaphore.Dispose();
        }
    }

    private BatchValidationResult ValidateBatchRequests(List<DynamoDbQueryRequest> requests, BatchExecutionOptions options)
    {
        var result = new BatchValidationResult { IsValid = true, Errors = new List<ValidationError>() };

        if (requests.Count == 0)
        {
            result.IsValid = false;
            result.Errors.Add(new ValidationError { Field = "requests", Message = "Batch cannot be empty" });
            return result;
        }

        if (requests.Count > options.MaxBatchSize)
        {
            result.IsValid = false;
            result.Errors.Add(new ValidationError 
            { 
                Field = "requests", 
                Message = $"Batch size {requests.Count} exceeds maximum {options.MaxBatchSize}" 
            });
        }

        // Validate individual requests
        for (int i = 0; i < requests.Count; i++)
        {
            var request = requests[i];
            if (string.IsNullOrWhiteSpace(request.TableName))
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError 
                { 
                    Field = $"requests[{i}].TableName", 
                    Message = "Table name is required" 
                });
            }
        }

        return result;
    }

    private List<OptimizedBatch> OptimizeBatches(List<DynamoDbQueryRequest> requests, BatchExecutionOptions options)
    {
        var batches = new List<OptimizedBatch>();
        
        // Group requests by table for better connection reuse
        var groupedByTable = requests.GroupBy(r => r.TableName).ToList();

        foreach (var tableGroup in groupedByTable)
        {
            var tableRequests = tableGroup.ToList();
            
            // Split into optimally sized batches
            for (int i = 0; i < tableRequests.Count; i += options.OptimalBatchSize)
            {
                var batchRequests = tableRequests.Skip(i).Take(options.OptimalBatchSize).ToList();
                
                batches.Add(new OptimizedBatch
                {
                    BatchId = Guid.NewGuid().ToString(),
                    TableName = tableGroup.Key,
                    Requests = batchRequests,
                    Priority = CalculateBatchPriority(batchRequests),
                    EstimatedExecutionTime = EstimateBatchExecutionTime(batchRequests)
                });
            }
        }

        // Sort batches by priority (higher priority first)
        return batches.OrderByDescending(b => b.Priority).ToList();
    }

    private async Task<List<ApiResponse<BatchResult<TDto>>>> ExecuteOptimizedBatchesAsync<TDto>(
        List<OptimizedBatch> batches,
        BatchExecutionOptions options,
        CancellationToken cancellationToken)
        where TDto : class, new()
    {
        var results = new List<ApiResponse<BatchResult<TDto>>>();

        foreach (var batch in batches)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await _concurrencyLimiter.WaitAsync(cancellationToken);
            try
            {
                var batchResult = await ExecuteSingleBatchAsync<TDto>(batch, options, cancellationToken);
                results.Add(ApiResponse<BatchResult<TDto>>.CreateSuccess(batchResult));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Batch {BatchId} execution failed", batch.BatchId);
                results.Add(ApiResponse<BatchResult<TDto>>.CreateFailure($"Batch {batch.BatchId} failed: {ex.Message}"));
            }
            finally
            {
                _concurrencyLimiter.Release();
            }
        }

        return results;
    }

    private async Task<BatchResult<TDto>> ExecuteSingleBatchAsync<TDto>(
        OptimizedBatch batch,
        BatchExecutionOptions options,
        CancellationToken cancellationToken)
        where TDto : class, new()
    {
        return await _circuitBreakerService.ExecuteAsync(
            $"batch-{batch.TableName}",
            async () =>
            {
                var result = await _queryService.BatchQueryAsync<TDto>(batch.Requests, cancellationToken);
                return result.Data ?? new BatchResult<TDto>();
            },
            () => Task.FromResult(new BatchResult<TDto>()) // Empty fallback
        );
    }

    private EnhancedBatchResult<TDto> CombineBatchResults<TDto>(
        List<ApiResponse<BatchResult<TDto>>> batchResults,
        TimeSpan totalExecutionTime)
        where TDto : class, new()
    {
        var successfulResults = batchResults.Where(r => r.Success && r.Data != null).Select(r => r.Data!).ToList();
        var failedResults = batchResults.Where(r => !r.Success).ToList();

        return new EnhancedBatchResult<TDto>
        {
            TotalBatches = batchResults.Count,
            SuccessfulBatches = successfulResults.Count,
            FailedBatches = failedResults.Count,
            TotalItems = successfulResults.Sum(r => r.TotalItemCount),
            AllItems = successfulResults.SelectMany(r => r.AllItems).ToList(),
            TotalEstimatedCost = successfulResults.Sum(r => r.Batch.TotalEstimatedCost),
            SuccessfulQueries = successfulResults.Sum(r => r.Batch.SuccessfulQueries),
            FailedQueries = successfulResults.Sum(r => r.Batch.FailedQueries),
            BatchExecutionSummary = successfulResults.Select(r => new BatchExecutionSummary
            {
                QueryCount = r.Batch.QueryCount,
                ExecutionTimeMs = r.Batch.TotalExecutionTimeMs,
                EstimatedCost = r.Batch.TotalEstimatedCost
            }).ToList(),
            Errors = failedResults.Select(r => r.Message ?? "Unknown error").ToList()
        };
    }

    private static async Task<ApiResponse<PagedResult<TDto>>> CreateFallbackResult<TDto>(DynamoDbQueryRequest request)
        where TDto : class, new()
    {
        await Task.Delay(1); // Simulate minimal work
        return ApiResponse<PagedResult<TDto>>.CreateFailure($"Fallback triggered for table {request.TableName}");
    }

    private static int CalculateBatchPriority(List<DynamoDbQueryRequest> requests)
    {
        // Higher priority for queries with partition keys (more efficient)
        var withPartitionKey = requests.Count(r => !string.IsNullOrEmpty(r.PartitionKeyValue));
        return withPartitionKey * 10 + (100 - requests.Count); // Smaller batches get slight priority
    }

    private static TimeSpan EstimateBatchExecutionTime(List<DynamoDbQueryRequest> requests)
    {
        // Estimate based on request complexity
        var baseTimePerRequest = TimeSpan.FromMilliseconds(50);
        var complexityMultiplier = 1.0;

        foreach (var request in requests)
        {
            if (string.IsNullOrEmpty(request.PartitionKeyValue))
                complexityMultiplier += 0.5; // Scans are slower
            
            if (request.Filters.Count > 3)
                complexityMultiplier += 0.2; // Complex filters
        }

        return TimeSpan.FromTicks((long)(baseTimePerRequest.Ticks * requests.Count * complexityMultiplier));
    }
}

/// <summary>
/// Batch execution options
/// </summary>
public class BatchExecutionOptions
{
    public int MaxBatchSize { get; set; } = 1000;
    public int OptimalBatchSize { get; set; } = 25;
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    public bool EnableCircuitBreaker { get; set; } = true;
    public bool EnableMetrics { get; set; } = true;

    public static BatchExecutionOptions Default => new();
    
    public static BatchExecutionOptions Conservative => new()
    {
        MaxBatchSize = 500,
        OptimalBatchSize = 10,
        MaxRetryAttempts = 5,
        RetryDelay = TimeSpan.FromSeconds(2)
    };

    public static BatchExecutionOptions Aggressive => new()
    {
        MaxBatchSize = 2000,
        OptimalBatchSize = 50,
        MaxRetryAttempts = 2,
        RetryDelay = TimeSpan.FromMilliseconds(500)
    };
}

/// <summary>
/// Enhanced batch result with detailed metrics
/// </summary>
public class EnhancedBatchResult<T>
{
    public int TotalBatches { get; set; }
    public int SuccessfulBatches { get; set; }
    public int FailedBatches { get; set; }
    public int TotalItems { get; set; }
    public List<T> AllItems { get; set; } = new();
    public long TotalExecutionTimeMs { get; set; }
    public decimal TotalEstimatedCost { get; set; }
    public int SuccessfulQueries { get; set; }
    public int FailedQueries { get; set; }
    public List<BatchExecutionSummary> BatchExecutionSummary { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Parallel batch result
/// </summary>
public class ParallelBatchResult<T>
{
    public int TotalRequests { get; set; }
    public int SuccessfulRequests { get; set; }
    public int FailedRequests { get; set; }
    public long TotalExecutionTimeMs { get; set; }
    public double AverageRequestTime { get; set; }
    public List<T> Items { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Batch chunk for streaming
/// </summary>
public class BatchChunk<T>
{
    public string ChunkId { get; set; } = string.Empty;
    public List<T> Items { get; set; } = new();
    public int RequestCount { get; set; }
    public long ExecutionTimeMs { get; set; }
    public int SuccessfulRequests { get; set; }
    public int FailedRequests { get; set; }
}

/// <summary>
/// Batch execution summary
/// </summary>
public class BatchExecutionSummary
{
    public int QueryCount { get; set; }
    public long ExecutionTimeMs { get; set; }
    public decimal EstimatedCost { get; set; }
}

/// <summary>
/// Optimized batch for execution
/// </summary>
internal class OptimizedBatch
{
    public string BatchId { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public List<DynamoDbQueryRequest> Requests { get; set; } = new();
    public int Priority { get; set; }
    public TimeSpan EstimatedExecutionTime { get; set; }
}

/// <summary>
/// Batch validation result
/// </summary>
internal class BatchValidationResult
{
    public bool IsValid { get; set; }
    public List<ValidationError> Errors { get; set; } = new();
} 