namespace DynamoDbFusion.Core.Models;

/// <summary>
/// Paginated result set for query operations
/// </summary>
/// <typeparam name="T">The type of items in the result</typeparam>
public class PagedResult<T>
{
    /// <summary>
    /// The result items
    /// </summary>
    public IEnumerable<T> Items { get; set; } = new List<T>();

    /// <summary>
    /// Pagination metadata
    /// </summary>
    public PaginationMetadata Pagination { get; set; } = new();

    /// <summary>
    /// Query execution metadata
    /// </summary>
    public QueryMetadata Query { get; set; } = new();

    /// <summary>
    /// Creates a paged result with items and pagination info
    /// </summary>
    /// <param name="items">The result items</param>
    /// <param name="pagination">Pagination metadata</param>
    /// <param name="query">Query execution metadata</param>
    /// <returns>PagedResult instance</returns>
    public static PagedResult<T> Create(
        IEnumerable<T> items, 
        PaginationMetadata pagination, 
        QueryMetadata query) => new()
    {
        Items = items,
        Pagination = pagination,
        Query = query
    };

    /// <summary>
    /// Creates an empty paged result
    /// </summary>
    /// <returns>Empty PagedResult</returns>
    public static PagedResult<T> Empty() => new()
    {
        Items = new List<T>(),
        Pagination = new PaginationMetadata { HasNextPage = false },
        Query = new QueryMetadata()
    };
}

/// <summary>
/// Metadata about pagination state
/// </summary>
public class PaginationMetadata
{
    /// <summary>
    /// Current page size
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Token for retrieving the next page
    /// </summary>
    public string? NextPageToken { get; set; }

    /// <summary>
    /// Indicates if more pages are available
    /// </summary>
    public bool HasNextPage { get; set; }

    /// <summary>
    /// Estimated total count (may be null for large datasets)
    /// </summary>
    public int? TotalCount { get; set; }

    /// <summary>
    /// Current page number (estimated, 1-based)
    /// </summary>
    public int? CurrentPage { get; set; }

    /// <summary>
    /// Number of items in the current page
    /// </summary>
    public int ItemCount { get; set; }
}

/// <summary>
/// Metadata about query execution
/// </summary>
public class QueryMetadata
{
    /// <summary>
    /// Type of operation executed (Query or Scan)
    /// </summary>
    public string OperationType { get; set; } = string.Empty;

    /// <summary>
    /// Index used (if any)
    /// </summary>
    public string? IndexUsed { get; set; }

    /// <summary>
    /// Estimated cost of the operation in USD
    /// </summary>
    public decimal EstimatedCost { get; set; }

    /// <summary>
    /// Execution time in milliseconds
    /// </summary>
    public long ExecutionTimeMs { get; set; }

    /// <summary>
    /// Number of items examined by DynamoDB
    /// </summary>
    public int ItemsExamined { get; set; }

    /// <summary>
    /// Number of items returned
    /// </summary>
    public int ItemsReturned { get; set; }

    /// <summary>
    /// Cache status for this query
    /// </summary>
    public CacheStatus CacheStatus { get; set; }

    /// <summary>
    /// Cost or performance warnings
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// Consumed capacity units (if available)
    /// </summary>
    public double? ConsumedCapacity { get; set; }
}

/// <summary>
/// Cache status for query results
/// </summary>
public enum CacheStatus
{
    /// <summary>
    /// Cache miss - data retrieved from DynamoDB
    /// </summary>
    Miss,

    /// <summary>
    /// Cache hit - data retrieved from cache
    /// </summary>
    Hit,

    /// <summary>
    /// Cache refresh - data updated in cache
    /// </summary>
    Refresh,

    /// <summary>
    /// Caching is disabled
    /// </summary>
    Disabled
}

/// <summary>
/// Batch result for multiple query operations
/// </summary>
/// <typeparam name="T">The type of items in the results</typeparam>
public class BatchResult<T>
{
    /// <summary>
    /// Individual query results
    /// </summary>
    public List<PagedResult<T>> Results { get; set; } = new();

    /// <summary>
    /// Overall batch execution metadata
    /// </summary>
    public BatchMetadata Batch { get; set; } = new();

    /// <summary>
    /// Gets all items from all results
    /// </summary>
    public IEnumerable<T> AllItems => Results.SelectMany(r => r.Items);

    /// <summary>
    /// Total number of items across all results
    /// </summary>
    public int TotalItemCount => Results.Sum(r => r.Pagination.ItemCount);
}

/// <summary>
/// Metadata about batch execution
/// </summary>
public class BatchMetadata
{
    /// <summary>
    /// Number of individual queries in the batch
    /// </summary>
    public int QueryCount { get; set; }

    /// <summary>
    /// Total execution time for the batch in milliseconds
    /// </summary>
    public long TotalExecutionTimeMs { get; set; }

    /// <summary>
    /// Number of successful queries
    /// </summary>
    public int SuccessfulQueries { get; set; }

    /// <summary>
    /// Number of failed queries
    /// </summary>
    public int FailedQueries { get; set; }

    /// <summary>
    /// Total estimated cost for the batch
    /// </summary>
    public decimal TotalEstimatedCost { get; set; }

    /// <summary>
    /// Batch-level warnings
    /// </summary>
    public List<string> Warnings { get; set; } = new();
} 