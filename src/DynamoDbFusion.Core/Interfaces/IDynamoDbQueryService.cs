using DynamoDbFusion.Core.Models;

namespace DynamoDbFusion.Core.Interfaces;

/// <summary>
/// Main service interface for querying DynamoDB tables with automatic DTO mapping
/// </summary>
public interface IDynamoDbQueryService
{
    /// <summary>
    /// Executes a query against a DynamoDB table with automatic DTO mapping
    /// </summary>
    /// <typeparam name="TDto">The DTO type to map results to</typeparam>
    /// <param name="request">The query request parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated results wrapped in an API response</returns>
    Task<ApiResponse<PagedResult<TDto>>> QueryAsync<TDto>(
        DynamoDbQueryRequest request,
        CancellationToken cancellationToken = default
    ) where TDto : class, new();

    /// <summary>
    /// Streams query results for large datasets
    /// </summary>
    /// <typeparam name="TDto">The DTO type to map results to</typeparam>
    /// <param name="request">The query request parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Async enumerable of DTOs</returns>
    IAsyncEnumerable<TDto> StreamAsync<TDto>(
        DynamoDbQueryRequest request,
        CancellationToken cancellationToken = default
    ) where TDto : class, new();

    /// <summary>
    /// Executes multiple queries in batch
    /// </summary>
    /// <typeparam name="TDto">The DTO type to map results to</typeparam>
    /// <param name="requests">Collection of query requests</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Batch results</returns>
    Task<ApiResponse<BatchResult<TDto>>> BatchQueryAsync<TDto>(
        IEnumerable<DynamoDbQueryRequest> requests,
        CancellationToken cancellationToken = default
    ) where TDto : class, new();
} 