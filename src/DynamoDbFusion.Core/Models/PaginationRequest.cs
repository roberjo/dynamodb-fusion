namespace DynamoDbFusion.Core.Models;

/// <summary>
/// Pagination parameters for query requests
/// </summary>
public class PaginationRequest
{
    /// <summary>
    /// Number of items per page (will be capped by MaxPageSize configuration)
    /// </summary>
    public int PageSize { get; set; } = 20;

    /// <summary>
    /// Base64 encoded pagination token from previous response
    /// </summary>
    public string? NextToken { get; set; }

    /// <summary>
    /// Creates a default pagination request
    /// </summary>
    /// <returns>PaginationRequest with default page size</returns>
    public static PaginationRequest Default() => new();

    /// <summary>
    /// Creates a pagination request with specified page size
    /// </summary>
    /// <param name="pageSize">Number of items per page</param>
    /// <returns>PaginationRequest with specified page size</returns>
    public static PaginationRequest WithPageSize(int pageSize) => new()
    {
        PageSize = pageSize
    };

    /// <summary>
    /// Creates a pagination request for the next page
    /// </summary>
    /// <param name="nextToken">Token from previous response</param>
    /// <param name="pageSize">Number of items per page</param>
    /// <returns>PaginationRequest for next page</returns>
    public static PaginationRequest NextPage(string nextToken, int pageSize = 20) => new()
    {
        PageSize = pageSize,
        NextToken = nextToken
    };
} 