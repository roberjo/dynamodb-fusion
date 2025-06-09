# Basic Usage Examples

This document provides practical examples of using DynamoDB Fusion in various scenarios.

## Table of Contents

1. [Setup and Configuration](#setup-and-configuration)
2. [Basic Queries](#basic-queries)
3. [HTTP Query String Integration](#http-query-string-integration)
4. [Working with Different Data Types](#working-with-different-data-types)
5. [Pagination Examples](#pagination-examples)
6. [Error Handling](#error-handling)

## Setup and Configuration

### 1. Basic Setup

```csharp
// Program.cs
using DynamoDbFusion;

var builder = WebApplication.CreateBuilder(args);

// Add DynamoDB Fusion services
builder.Services.AddDynamoDbQueryService(options =>
{
    options.DefaultPageSize = 20;
    options.MaxPageSize = 100;
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

// Add other services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
```

### 2. Configuration with Custom Options

```csharp
// appsettings.json
{
  "DynamoDbFusion": {
    "DynamoDb": {
      "Region": "us-east-1",
      "ServiceUrl": null // Use AWS DynamoDB service
    },
    "Cache": {
      "Provider": "Redis",
      "ConnectionString": "localhost:6379",
      "EnableL1Cache": true,
      "EnableL2Cache": true,
      "DefaultTtl": "00:05:00"
    },
    "Security": {
      "EnableAuthentication": false,
      "RateLimiting": {
        "RequestsPerMinute": 100
      }
    }
  }
}

// Program.cs with configuration
builder.Services.AddDynamoDbQueryService(options =>
{
    builder.Configuration.GetSection("DynamoDbFusion").Bind(options);
});
```

## Basic Queries

### 1. Simple Service Implementation

```csharp
// DTOs
public class UserDto
{
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
    public int Age { get; set; }
    public List<string> Tags { get; set; } = new();
}

// Service
public class UserService
{
    private readonly IDynamoDbQueryService _queryService;
    private readonly ILogger<UserService> _logger;

    public UserService(IDynamoDbQueryService queryService, ILogger<UserService> logger)
    {
        _queryService = queryService;
        _logger = logger;
    }

    // Get user by ID (using partition key)
    public async Task<UserDto?> GetUserByIdAsync(string userId)
    {
        var request = new DynamoDbQueryRequest
        {
            TableName = "Users",
            PartitionKey = "UserId",
            PartitionKeyValue = userId
        };

        var result = await _queryService.QueryAsync<UserDto>(request);
        
        if (!result.Success)
        {
            _logger.LogError("Failed to get user {UserId}: {Error}", userId, result.Message);
            return null;
        }

        return result.Data?.Items.FirstOrDefault();
    }

    // Get users by status (filter query)
    public async Task<List<UserDto>> GetUsersByStatusAsync(string status)
    {
        var request = new DynamoDbQueryRequest
        {
            TableName = "Users",
            Filters = new Dictionary<string, FilterValue>
            {
                ["Status"] = new FilterValue 
                { 
                    Value = status, 
                    Operator = FilterOperator.Equals 
                }
            }
        };

        var result = await _queryService.QueryAsync<UserDto>(request);
        return result.Success ? result.Data?.Items.ToList() ?? new List<UserDto>() : new List<UserDto>();
    }

    // Complex filtering
    public async Task<List<UserDto>> GetActiveUsersInAgeRangeAsync(int minAge, int maxAge)
    {
        var request = new DynamoDbQueryRequest
        {
            TableName = "Users",
            Filters = new Dictionary<string, FilterValue>
            {
                ["Status"] = new FilterValue 
                { 
                    Value = "active", 
                    Operator = FilterOperator.Equals 
                },
                ["Age"] = new FilterValue
                {
                    Value = minAge,
                    Operator = FilterOperator.Between,
                    AdditionalValues = new List<object> { maxAge }
                }
            }
        };

        var result = await _queryService.QueryAsync<UserDto>(request);
        return result.Success ? result.Data?.Items.ToList() ?? new List<UserDto>() : new List<UserDto>();
    }
}
```

### 2. Controller Implementation

```csharp
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly UserService _userService;

    public UsersController(UserService userService)
    {
        _userService = userService;
    }

    [HttpGet("{userId}")]
    public async Task<ActionResult<UserDto>> GetUser(string userId)
    {
        var user = await _userService.GetUserByIdAsync(userId);
        
        if (user == null)
            return NotFound();
            
        return Ok(user);
    }

    [HttpGet]
    public async Task<ActionResult<List<UserDto>>> GetUsers([FromQuery] string? status = null)
    {
        if (!string.IsNullOrEmpty(status))
        {
            var users = await _userService.GetUsersByStatusAsync(status);
            return Ok(users);
        }

        // If no filters, return error or default behavior
        return BadRequest("At least one filter parameter is required");
    }

    [HttpGet("age-range")]
    public async Task<ActionResult<List<UserDto>>> GetUsersByAgeRange(
        [FromQuery] int minAge = 18, 
        [FromQuery] int maxAge = 65)
    {
        var users = await _userService.GetActiveUsersInAgeRangeAsync(minAge, maxAge);
        return Ok(users);
    }
}
```

## HTTP Query String Integration

### 1. Automatic Query String Processing

```csharp
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IDynamoDbQueryService _queryService;

    public ProductsController(IDynamoDbQueryService queryService)
    {
        _queryService = queryService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<ProductDto>>>> GetProducts()
    {
        var request = new DynamoDbQueryRequest
        {
            TableName = "Products",
            Filters = HttpContext.Request.Query.ToDynamoFilters(),
            Pagination = HttpContext.Request.Query.ToPaginationRequest()
        };

        var result = await _queryService.QueryAsync<ProductDto>(request);
        return Ok(result);
    }
}

// Usage examples:
// GET /api/products?category=Electronics&price__gte=100&price__lte=500
// GET /api/products?name__contains=iPhone&inStock=true&pageSize=25
// GET /api/products?tags__contains=smartphone&brand=Apple
```

### 2. Custom Query String Processing

```csharp
public static class QueryStringExtensions
{
    public static DynamoDbQueryRequest ToProductQueryRequest(this IQueryCollection query, string tableName)
    {
        var request = new DynamoDbQueryRequest
        {
            TableName = tableName,
            Filters = new Dictionary<string, FilterValue>(),
            Pagination = query.ToPaginationRequest()
        };

        // Handle specific product query parameters
        if (query.TryGetValue("category", out var category))
        {
            request.Filters["Category"] = new FilterValue 
            { 
                Value = category.ToString(), 
                Operator = FilterOperator.Equals 
            };
        }

        if (query.TryGetValue("minPrice", out var minPrice) && 
            query.TryGetValue("maxPrice", out var maxPrice))
        {
            if (decimal.TryParse(minPrice, out var min) && decimal.TryParse(maxPrice, out var max))
            {
                request.Filters["Price"] = new FilterValue
                {
                    Value = min,
                    Operator = FilterOperator.Between,
                    AdditionalValues = new List<object> { max }
                };
            }
        }
        else if (query.TryGetValue("minPrice", out minPrice) && decimal.TryParse(minPrice, out var minVal))
        {
            request.Filters["Price"] = new FilterValue
            {
                Value = minVal,
                Operator = FilterOperator.GreaterThanOrEqual
            };
        }

        if (query.TryGetValue("inStock", out var inStock) && bool.TryParse(inStock, out var inStockVal))
        {
            request.Filters["InStock"] = new FilterValue
            {
                Value = inStockVal,
                Operator = FilterOperator.Equals
            };
        }

        return request;
    }
}

// Usage in controller
[HttpGet("custom")]
public async Task<ActionResult<ApiResponse<PagedResult<ProductDto>>>> GetProductsCustom()
{
    var request = HttpContext.Request.Query.ToProductQueryRequest("Products");
    var result = await _queryService.QueryAsync<ProductDto>(request);
    return Ok(result);
}
```

## Working with Different Data Types

### 1. Complex DTOs

```csharp
public class OrderDto
{
    public string OrderId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
    public DateTime? ShippedDate { get; set; }
    public AddressDto ShippingAddress { get; set; } = new();
    public List<OrderItemDto> Items { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public class AddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
}

public class OrderItemDto
{
    public string ProductId { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
```

### 2. Querying Complex Data

```csharp
public class OrderService
{
    private readonly IDynamoDbQueryService _queryService;

    public async Task<List<OrderDto>> GetOrdersByUserAndDateRangeAsync(
        string userId, 
        DateTime fromDate, 
        DateTime toDate)
    {
        var request = new DynamoDbQueryRequest
        {
            TableName = "Orders",
            PartitionKey = "UserId",
            PartitionKeyValue = userId,
            Filters = new Dictionary<string, FilterValue>
            {
                ["CreatedDate"] = new FilterValue
                {
                    Value = fromDate.ToString("yyyy-MM-dd"),
                    Operator = FilterOperator.Between,
                    AdditionalValues = new List<object> { toDate.ToString("yyyy-MM-dd") }
                }
            }
        };

        var result = await _queryService.QueryAsync<OrderDto>(request);
        return result.Success ? result.Data?.Items.ToList() ?? new List<OrderDto>() : new List<OrderDto>();
    }

    public async Task<List<OrderDto>> GetOrdersByStatusAsync(List<string> statuses)
    {
        var request = new DynamoDbQueryRequest
        {
            TableName = "Orders",
            Filters = new Dictionary<string, FilterValue>
            {
                ["Status"] = new FilterValue
                {
                    Value = statuses.First(),
                    Operator = FilterOperator.In,
                    AdditionalValues = statuses.Skip(1).Cast<object>().ToList()
                }
            }
        };

        var result = await _queryService.QueryAsync<OrderDto>(request);
        return result.Success ? result.Data?.Items.ToList() ?? new List<OrderDto>() : new List<OrderDto>();
    }
}
```

## Pagination Examples

### 1. Basic Pagination

```csharp
[HttpGet("paginated")]
public async Task<ActionResult<ApiResponse<PagedResult<UserDto>>>> GetUsersPaginated(
    [FromQuery] int pageSize = 20,
    [FromQuery] string? nextToken = null)
{
    var request = new DynamoDbQueryRequest
    {
        TableName = "Users",
        Pagination = new PaginationRequest
        {
            PageSize = Math.Min(pageSize, 100), // Enforce max page size
            NextToken = nextToken
        },
        Filters = HttpContext.Request.Query.ToDynamoFilters()
    };

    var result = await _queryService.QueryAsync<UserDto>(request);
    return Ok(result);
}
```

### 2. Streaming Large Results

```csharp
[HttpGet("export")]
public async Task<IActionResult> ExportUsers()
{
    var request = new DynamoDbQueryRequest
    {
        TableName = "Users",
        Filters = new Dictionary<string, FilterValue>
        {
            ["Status"] = new FilterValue { Value = "active", Operator = FilterOperator.Equals }
        }
    };

    Response.ContentType = "application/json";
    Response.Headers.Add("Content-Disposition", "attachment; filename=users.jsonl");

    await foreach (var user in _queryService.StreamAsync<UserDto>(request))
    {
        var json = JsonSerializer.Serialize(user);
        await Response.WriteAsync(json + Environment.NewLine);
        await Response.Body.FlushAsync();
    }

    return new EmptyResult();
}
```

### 3. Manual Pagination Control

```csharp
public class PaginatedUsersService
{
    private readonly IDynamoDbQueryService _queryService;

    public async Task<PaginatedResult<UserDto>> GetAllActiveUsersAsync(int pageSize = 50)
    {
        var allUsers = new List<UserDto>();
        string? nextToken = null;
        var pageCount = 0;

        do
        {
            var request = new DynamoDbQueryRequest
            {
                TableName = "Users",
                Filters = new Dictionary<string, FilterValue>
                {
                    ["Status"] = new FilterValue { Value = "active", Operator = FilterOperator.Equals }
                },
                Pagination = new PaginationRequest
                {
                    PageSize = pageSize,
                    NextToken = nextToken
                }
            };

            var result = await _queryService.QueryAsync<UserDto>(request);
            
            if (!result.Success || result.Data?.Items == null)
                break;

            allUsers.AddRange(result.Data.Items);
            nextToken = result.Data.Pagination.NextPageToken;
            pageCount++;

            // Safety limit to prevent infinite loops
            if (pageCount > 100)
            {
                throw new InvalidOperationException("Too many pages - consider using streaming instead");
            }

        } while (!string.IsNullOrEmpty(nextToken));

        return new PaginatedResult<UserDto>
        {
            Items = allUsers,
            TotalPages = pageCount,
            TotalItems = allUsers.Count
        };
    }
}
```

## Error Handling

### 1. Service-Level Error Handling

```csharp
public class RobustUserService
{
    private readonly IDynamoDbQueryService _queryService;
    private readonly ILogger<RobustUserService> _logger;

    public async Task<ServiceResult<UserDto>> GetUserByIdAsync(string userId)
    {
        try
        {
            var request = new DynamoDbQueryRequest
            {
                TableName = "Users",
                PartitionKey = "UserId",
                PartitionKeyValue = userId
            };

            var result = await _queryService.QueryAsync<UserDto>(request);

            if (!result.Success)
            {
                _logger.LogError("Query failed for user {UserId}: {Error}", userId, result.Message);
                return ServiceResult<UserDto>.Failure(result.Message ?? "Query failed");
            }

            var user = result.Data?.Items.FirstOrDefault();
            if (user == null)
            {
                return ServiceResult<UserDto>.NotFound($"User {userId} not found");
            }

            return ServiceResult<UserDto>.Success(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting user {UserId}", userId);
            return ServiceResult<UserDto>.Error("An unexpected error occurred");
        }
    }
}

public class ServiceResult<T>
{
    public bool IsSuccess { get; set; }
    public T? Data { get; set; }
    public string? Error { get; set; }
    public ServiceResultType Type { get; set; }

    public static ServiceResult<T> Success(T data) => new()
    {
        IsSuccess = true,
        Data = data,
        Type = ServiceResultType.Success
    };

    public static ServiceResult<T> NotFound(string message) => new()
    {
        IsSuccess = false,
        Error = message,
        Type = ServiceResultType.NotFound
    };

    public static ServiceResult<T> Failure(string error) => new()
    {
        IsSuccess = false,
        Error = error,
        Type = ServiceResultType.Failure
    };

    public static ServiceResult<T> Error(string error) => new()
    {
        IsSuccess = false,
        Error = error,
        Type = ServiceResultType.Error
    };
}

public enum ServiceResultType
{
    Success,
    NotFound,
    Failure,
    Error
}
```

### 2. Controller-Level Error Handling

```csharp
[ApiController]
[Route("api/[controller]")]
public class RobustUsersController : ControllerBase
{
    private readonly RobustUserService _userService;

    [HttpGet("{userId}")]
    public async Task<ActionResult<UserDto>> GetUser(string userId)
    {
        var result = await _userService.GetUserByIdAsync(userId);

        return result.Type switch
        {
            ServiceResultType.Success => Ok(result.Data),
            ServiceResultType.NotFound => NotFound(new { message = result.Error }),
            ServiceResultType.Failure => BadRequest(new { message = result.Error }),
            ServiceResultType.Error => StatusCode(500, new { message = "Internal server error" }),
            _ => StatusCode(500, new { message = "Unknown error" })
        };
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<UserDto>>>> GetUsers()
    {
        try
        {
            var request = new DynamoDbQueryRequest
            {
                TableName = "Users",
                Filters = HttpContext.Request.Query.ToDynamoFilters(),
                Pagination = HttpContext.Request.Query.ToPaginationRequest()
            };

            var result = await _queryService.QueryAsync<UserDto>(request);

            if (!result.Success)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetUsers");
            return StatusCode(500, new { message = "Internal server error" });
        }
    }
}
```

These examples provide a solid foundation for implementing DynamoDB Fusion in your applications. Start with the basic setup and gradually add more complex features as needed. 