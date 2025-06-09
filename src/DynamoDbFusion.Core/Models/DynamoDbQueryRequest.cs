namespace DynamoDbFusion.Core.Models;

/// <summary>
/// Primary request model for all DynamoDB query operations
/// </summary>
public class DynamoDbQueryRequest
{
    /// <summary>
    /// Name of the DynamoDB table to query
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Partition key attribute name
    /// </summary>
    public string? PartitionKey { get; set; }

    /// <summary>
    /// Partition key value for Query operations
    /// </summary>
    public string? PartitionKeyValue { get; set; }

    /// <summary>
    /// Sort key attribute name
    /// </summary>
    public string? SortKey { get; set; }

    /// <summary>
    /// Sort key value for Query operations
    /// </summary>
    public string? SortKeyValue { get; set; }

    /// <summary>
    /// Additional filters to apply
    /// </summary>
    public Dictionary<string, FilterValue> Filters { get; set; } = new();

    /// <summary>
    /// Pagination parameters
    /// </summary>
    public PaginationRequest Pagination { get; set; } = new();

    /// <summary>
    /// Query strategy override
    /// </summary>
    public QueryStrategy Strategy { get; set; } = QueryStrategy.AutoDetect;

    /// <summary>
    /// Global or Local Secondary Index name
    /// </summary>
    public string? IndexName { get; set; }

    /// <summary>
    /// Attributes to include in projection (empty means all attributes)
    /// </summary>
    public List<string> ProjectionAttributes { get; set; } = new();

    /// <summary>
    /// Enable consistent read (Query operations only)
    /// </summary>
    public bool ConsistentRead { get; set; } = false;

    /// <summary>
    /// Sorting configuration
    /// </summary>
    public Dictionary<string, SortDirection> OrderBy { get; set; } = new();
}

/// <summary>
/// Query strategy options
/// </summary>
public enum QueryStrategy
{
    /// <summary>
    /// Automatically detect the best strategy (Query vs Scan)
    /// </summary>
    AutoDetect,
    
    /// <summary>
    /// Force Query operation (will fail if no partition key)
    /// </summary>
    ForceQuery,
    
    /// <summary>
    /// Force Scan operation
    /// </summary>
    ForceScan,
    
    /// <summary>
    /// Always choose the least expensive option
    /// </summary>
    CostOptimized
}

/// <summary>
/// Sort direction for ordering results
/// </summary>
public enum SortDirection
{
    /// <summary>
    /// Ascending order
    /// </summary>
    Ascending,
    
    /// <summary>
    /// Descending order
    /// </summary>
    Descending
} 