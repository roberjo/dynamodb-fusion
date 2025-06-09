using DynamoDbFusion.Core.Extensions;
using DynamoDbFusion.Core.Interfaces;
using DynamoDbFusion.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace DynamoDbFusion.WebApi.Controllers;

/// <summary>
/// Sample controller demonstrating DynamoDB Fusion usage
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IDynamoDbQueryService _queryService;
    private readonly ILogger<ProductsController> _logger;

    public ProductsController(
        IDynamoDbQueryService queryService,
        ILogger<ProductsController> logger)
    {
        _queryService = queryService;
        _logger = logger;
    }

    /// <summary>
    /// Gets products with optional filtering and pagination
    /// </summary>
    /// <param name="category">Filter by category</param>
    /// <param name="name__contains">Filter products where name contains this value</param>
    /// <param name="price__gte">Filter products with price greater than or equal to this value</param>
    /// <param name="price__lte">Filter products with price less than or equal to this value</param>
    /// <param name="inStock">Filter by stock availability</param>
    /// <param name="pageSize">Number of items per page (default: 20, max: 100)</param>
    /// <param name="nextToken">Pagination token for next page</param>
    /// <returns>Paginated list of products</returns>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<Product>>>> GetProducts(
        [FromQuery] string? category = null,
        [FromQuery(Name = "name__contains")] string? nameContains = null,
        [FromQuery(Name = "price__gte")] decimal? priceGte = null,
        [FromQuery(Name = "price__lte")] decimal? priceLte = null,
        [FromQuery] bool? inStock = null,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? nextToken = null)
    {
        try
        {
            // Convert query parameters to DynamoDB request
            var request = HttpContext.Request.Query.ToDynamoDbQueryRequest("Products");
            
            // Override table-specific settings
            request.Pagination.PageSize = Math.Min(pageSize, 100); // Cap at 100
            request.Pagination.NextToken = nextToken;

            var result = await _queryService.QueryAsync<Product>(request);
            
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving products");
            return StatusCode(500, ApiResponse<PagedResult<Product>>.CreateFailure("An error occurred while retrieving products"));
        }
    }

    /// <summary>
    /// Gets products by category (uses Query operation for better performance)
    /// </summary>
    /// <param name="category">Product category</param>
    /// <param name="pageSize">Number of items per page</param>
    /// <param name="nextToken">Pagination token</param>
    /// <returns>Products in the specified category</returns>
    [HttpGet("by-category/{category}")]
    public async Task<ActionResult<ApiResponse<PagedResult<Product>>>> GetProductsByCategory(
        string category,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? nextToken = null)
    {
        try
        {
            var request = new DynamoDbQueryRequest
            {
                TableName = "Products",
                PartitionKey = "Category",
                PartitionKeyValue = category,
                Pagination = new PaginationRequest
                {
                    PageSize = Math.Min(pageSize, 100),
                    NextToken = nextToken
                },
                Strategy = QueryStrategy.ForceQuery // Force Query operation for better performance
            };

            // Add any additional filters from query string
            var additionalFilters = HttpContext.Request.Query.ToDynamoFilters();
            foreach (var filter in additionalFilters)
            {
                if (!filter.Key.Equals("Category", StringComparison.OrdinalIgnoreCase))
                {
                    request.Filters[filter.Key] = filter.Value;
                }
            }

            var result = await _queryService.QueryAsync<Product>(request);
            
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving products by category {Category}", category);
            return StatusCode(500, ApiResponse<PagedResult<Product>>.CreateFailure("An error occurred while retrieving products"));
        }
    }

    /// <summary>
    /// Streams all products (useful for large datasets)
    /// </summary>
    /// <param name="category">Optional category filter</param>
    /// <returns>Stream of products</returns>
    [HttpGet("stream")]
    public async IAsyncEnumerable<Product> StreamProducts([FromQuery] string? category = null)
    {
        var request = new DynamoDbQueryRequest
        {
            TableName = "Products",
            Pagination = new PaginationRequest { PageSize = 50 } // Smaller page size for streaming
        };

        if (!string.IsNullOrEmpty(category))
        {
            request.PartitionKey = "Category";
            request.PartitionKeyValue = category;
        }

        await foreach (var product in _queryService.StreamAsync<Product>(request))
        {
            yield return product;
        }
    }

    /// <summary>
    /// Performs batch queries across multiple categories
    /// </summary>
    /// <param name="categories">List of categories to query</param>
    /// <returns>Batch results</returns>
    [HttpPost("batch")]
    public async Task<ActionResult<ApiResponse<BatchResult<Product>>>> BatchQuery(
        [FromBody] List<string> categories)
    {
        try
        {
            var requests = categories.Select(category => new DynamoDbQueryRequest
            {
                TableName = "Products",
                PartitionKey = "Category",
                PartitionKeyValue = category,
                Pagination = new PaginationRequest { PageSize = 50 }
            });

            var result = await _queryService.BatchQueryAsync<Product>(requests);
            
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing batch query");
            return StatusCode(500, ApiResponse<BatchResult<Product>>.CreateFailure("An error occurred during batch query"));
        }
    }
}

/// <summary>
/// Sample Product DTO for demonstration
/// </summary>
public class Product
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool InStock { get; set; }
    public int Quantity { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<string> Tags { get; set; } = new();
} 