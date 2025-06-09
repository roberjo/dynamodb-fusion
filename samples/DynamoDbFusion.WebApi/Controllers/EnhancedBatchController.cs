using DynamoDbFusion.Core.Models;
using DynamoDbFusion.Core.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace DynamoDbFusion.WebApi.Controllers;

/// <summary>
/// Enhanced batch operations controller demonstrating advanced features
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class EnhancedBatchController : ControllerBase
{
    private readonly EnhancedBatchOperationsService _batchService;
    private readonly QueryOptimizationService _optimizationService;
    private readonly ILogger<EnhancedBatchController> _logger;

    public EnhancedBatchController(
        EnhancedBatchOperationsService batchService,
        QueryOptimizationService optimizationService,
        ILogger<EnhancedBatchController> logger)
    {
        _batchService = batchService;
        _optimizationService = optimizationService;
        _logger = logger;
    }

    /// <summary>
    /// Executes an enhanced batch query with intelligent optimization
    /// </summary>
    /// <param name="requests">Collection of query requests</param>
    /// <param name="options">Batch execution options</param>
    /// <returns>Enhanced batch results</returns>
    [HttpPost("execute")]
    public async Task<ActionResult<ApiResponse<EnhancedBatchResult<Product>>>> ExecuteEnhancedBatch(
        [FromBody] List<DynamoDbQueryRequest> requests,
        [FromQuery] string? options = null)
    {
        try
        {
            if (requests?.Count == 0)
            {
                return BadRequest(ApiResponse<EnhancedBatchResult<Product>>.CreateSingleValidationError("requests", "Batch requests cannot be empty"));
            }

            var batchOptions = ParseBatchOptions(options);
            var result = await _batchService.ExecuteBatchAsync<Product>(requests!, batchOptions);
            
            _logger.LogInformation("Enhanced batch executed with {TotalBatches} batches, {SuccessfulBatches} successful, {TotalItems} items returned",
                result.TotalBatches, result.SuccessfulBatches, result.TotalItems);

            return Ok(ApiResponse<EnhancedBatchResult<Product>>.CreateSuccess(result, "Enhanced batch executed successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing enhanced batch");
            return StatusCode(500, ApiResponse<EnhancedBatchResult<Product>>.CreateFailure("An error occurred during enhanced batch execution"));
        }
    }

    /// <summary>
    /// Streams batch results for large datasets
    /// </summary>
    /// <param name="requests">Collection of query requests</param>
    /// <param name="options">Batch execution options</param>
    /// <returns>Stream of batch chunks</returns>
    [HttpPost("stream")]
    public async Task StreamBatch(
        [FromBody] List<DynamoDbQueryRequest> requests,
        [FromQuery] string? options = null)
    {
        try
        {
            if (requests?.Count == 0)
            {
                Response.StatusCode = 400;
                await Response.WriteAsync(JsonSerializer.Serialize(
                    ApiResponse<object>.CreateSingleValidationError("requests", "Batch requests cannot be empty")));
                return;
            }

            Response.ContentType = "application/json";
            Response.Headers.Add("Transfer-Encoding", "chunked");

            var batchOptions = ParseBatchOptions(options);
            
            await foreach (var chunk in _batchService.StreamBatchAsync<Product>(requests!, batchOptions, HttpContext.RequestAborted))
            {
                var chunkJson = JsonSerializer.Serialize(chunk, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                await Response.WriteAsync(chunkJson + "\n");
                await Response.Body.FlushAsync();
            }

            _logger.LogInformation("Batch streaming completed for {RequestCount} requests", requests!.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during batch streaming");
            
            if (!Response.HasStarted)
            {
                Response.StatusCode = 500;
                await Response.WriteAsync(JsonSerializer.Serialize(
                    ApiResponse<object>.CreateFailure("An error occurred during batch streaming")));
            }
        }
    }

    /// <summary>
    /// Executes parallel batch queries with automatic retry
    /// </summary>
    /// <param name="requests">Collection of query requests</param>
    /// <param name="maxConcurrency">Maximum concurrent operations</param>
    /// <returns>Parallel batch results</returns>
    [HttpPost("parallel")]
    public async Task<ActionResult<ApiResponse<ParallelBatchResult<Product>>>> ExecuteParallelBatch(
        [FromBody] List<DynamoDbQueryRequest> requests,
        [FromQuery] int maxConcurrency = 5)
    {
        try
        {
            if (requests?.Count == 0)
            {
                return BadRequest(ApiResponse<ParallelBatchResult<Product>>.CreateSingleValidationError("requests", "Batch requests cannot be empty"));
            }

            if (maxConcurrency < 1 || maxConcurrency > 20)
            {
                return BadRequest(ApiResponse<ParallelBatchResult<Product>>.CreateSingleValidationError("maxConcurrency", "Max concurrency must be between 1 and 20"));
            }

            var result = await _batchService.ExecuteParallelBatchAsync<Product>(requests!, maxConcurrency);
            
            _logger.LogInformation("Parallel batch executed with {TotalRequests} requests, {SuccessfulRequests} successful, concurrency: {MaxConcurrency}",
                result.TotalRequests, result.SuccessfulRequests, maxConcurrency);

            return Ok(ApiResponse<ParallelBatchResult<Product>>.CreateSuccess(result, "Parallel batch executed successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing parallel batch");
            return StatusCode(500, ApiResponse<ParallelBatchResult<Product>>.CreateFailure("An error occurred during parallel batch execution"));
        }
    }

    /// <summary>
    /// Optimizes a batch of queries and returns recommendations
    /// </summary>
    /// <param name="requests">Collection of query requests to optimize</param>
    /// <returns>Optimization results and recommendations</returns>
    [HttpPost("optimize")]
    public ActionResult<ApiResponse<List<QueryOptimizationResult>>> OptimizeBatch([FromBody] List<DynamoDbQueryRequest> requests)
    {
        try
        {
            if (requests?.Count == 0)
            {
                return BadRequest(ApiResponse<List<QueryOptimizationResult>>.CreateSingleValidationError("requests", "Requests cannot be empty"));
            }

            var optimizationResults = new List<QueryOptimizationResult>();
            
            foreach (var request in requests!)
            {
                var result = _optimizationService.OptimizeQuery(request);
                optimizationResults.Add(result);
            }

            var totalRecommendations = optimizationResults.Sum(r => r.Recommendations.Count);
            var totalCostReduction = optimizationResults.Sum(r => r.EstimatedCostReduction);
            var totalPerformanceGain = optimizationResults.Sum(r => r.EstimatedPerformanceGain);

            var metadata = new Dictionary<string, object>
            {
                ["totalQueries"] = requests.Count,
                ["totalRecommendations"] = totalRecommendations,
                ["totalEstimatedCostReduction"] = totalCostReduction,
                ["totalEstimatedPerformanceGain"] = totalPerformanceGain
            };

            _logger.LogInformation("Batch optimization completed for {QueryCount} queries with {RecommendationCount} recommendations",
                requests.Count, totalRecommendations);

            return Ok(ApiResponse<List<QueryOptimizationResult>>.CreateSuccessWithMetadata(
                optimizationResults, metadata, "Batch optimization completed successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error optimizing batch queries");
            return StatusCode(500, ApiResponse<List<QueryOptimizationResult>>.CreateFailure("An error occurred during batch optimization"));
        }
    }

    /// <summary>
    /// Creates sample batch requests for testing
    /// </summary>
    /// <param name="count">Number of sample requests to create</param>
    /// <param name="tableName">Table name to use (default: Products)</param>
    /// <returns>Sample batch requests</returns>
    [HttpGet("sample-requests")]
    public ActionResult<ApiResponse<List<DynamoDbQueryRequest>>> CreateSampleRequests(
        [FromQuery] int count = 5,
        [FromQuery] string tableName = "Products")
    {
        try
        {
            if (count < 1 || count > 100)
            {
                return BadRequest(ApiResponse<List<DynamoDbQueryRequest>>.CreateSingleValidationError("count", "Count must be between 1 and 100"));
            }

            var sampleRequests = new List<DynamoDbQueryRequest>();
            var categories = new[] { "Electronics", "Books", "Clothing", "Home", "Sports" };
            var random = new Random();

            for (int i = 0; i < count; i++)
            {
                var request = new DynamoDbQueryRequest
                {
                    TableName = tableName,
                    PartitionKey = "Category",
                    PartitionKeyValue = categories[random.Next(categories.Length)],
                    Filters = new Dictionary<string, FilterValue>
                    {
                        ["Price"] = new FilterValue
                        {
                            Value = random.Next(10, 500),
                            Operator = FilterOperator.GreaterThanOrEqual
                        }
                    },
                    Pagination = new PaginationRequest { PageSize = 50 }
                };

                // Randomly add additional filters
                if (random.NextDouble() > 0.5)
                {
                    request.Filters["InStock"] = new FilterValue
                    {
                        Value = true,
                        Operator = FilterOperator.Equals
                    };
                }

                sampleRequests.Add(request);
            }

            return Ok(ApiResponse<List<DynamoDbQueryRequest>>.CreateSuccess(sampleRequests, $"Generated {count} sample requests"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating sample requests");
            return StatusCode(500, ApiResponse<List<DynamoDbQueryRequest>>.CreateFailure("An error occurred while creating sample requests"));
        }
    }

    private static BatchExecutionOptions ParseBatchOptions(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson))
        {
            return BatchExecutionOptions.Default;
        }

        try
        {
            var optionsType = optionsJson.ToLowerInvariant() switch
            {
                "conservative" => BatchExecutionOptions.Conservative,
                "aggressive" => BatchExecutionOptions.Aggressive,
                _ => BatchExecutionOptions.Default
            };

            return optionsType;
        }
        catch
        {
            return BatchExecutionOptions.Default;
        }
    }
} 