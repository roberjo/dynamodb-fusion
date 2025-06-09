# DynamoDB Fusion - Phase 1: Core Foundation

DynamoDB Fusion is a generic ASP.NET Core 8 service for querying DynamoDB tables with automatic filtering, pagination, and DTO mapping from HTTP query strings.

## Phase 1 Implementation Status ✅

This phase implements the core foundation of DynamoDB Fusion:

### ✅ Completed Features

- **Core Interface**: `IDynamoDbQueryService` with basic query operations
- **Request/Response Models**: Complete model structure for queries and responses
- **Query String Processing**: Automatic conversion from HTTP query parameters to DynamoDB filters
- **Basic DynamoDB Operations**: Query and Scan operations with automatic strategy selection
- **DTO Mapping**: Automatic mapping from DynamoDB items to strongly-typed DTOs
- **Pagination**: Built-in pagination with continuation tokens
- **Dependency Injection**: Easy service registration with ASP.NET Core DI
- **Sample Web API**: Working example demonstrating all features

### 🔧 Core Components

#### 1. IDynamoDbQueryService Interface
```csharp
public interface IDynamoDbQueryService
{
    Task<ApiResponse<PagedResult<TDto>>> QueryAsync<TDto>(DynamoDbQueryRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<TDto> StreamAsync<TDto>(DynamoDbQueryRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<BatchResult<TDto>>> BatchQueryAsync<TDto>(IEnumerable<DynamoDbQueryRequest> requests, CancellationToken cancellationToken = default);
}
```

#### 2. Query String Extensions
Automatic conversion from HTTP query strings to DynamoDB filters:
```csharp
// GET /api/products?category=Electronics&price__gte=100&name__contains=phone
var request = HttpContext.Request.Query.ToDynamoDbQueryRequest("Products");
```

#### 3. Filter Operators
Supports comprehensive filtering with operators:
- `eq` (equals) - default
- `ne` (not equals)
- `lt`, `lte` (less than, less than or equal)
- `gt`, `gte` (greater than, greater than or equal)
- `between` (between two values)
- `in`, `not_in` (in/not in list)
- `contains` (string contains)
- `begins_with` (string starts with)
- `exists`, `not_exists` (attribute exists)

## 🚀 Quick Start

### 1. Install Dependencies
```bash
dotnet add package AWSSDK.DynamoDBv2
```

### 2. Register Services
```csharp
// Program.cs
builder.Services.AddDynamoDbFusion();
```

### 3. Use in Controllers
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
    public async Task<ActionResult<ApiResponse<PagedResult<Product>>>> GetProducts()
    {
        var request = HttpContext.Request.Query.ToDynamoDbQueryRequest("Products");
        var result = await _queryService.QueryAsync<Product>(request);
        return Ok(result);
    }
}
```

### 4. Query Examples

**Basic Query:**
```
GET /api/products?category=Electronics&pageSize=10
```

**Advanced Filtering:**
```
GET /api/products?price__gte=100&price__lte=500&name__contains=phone&inStock=true
```

**Pagination:**
```
GET /api/products?pageSize=20&nextToken=eyJJZCI6eyJTIjoiMTIzIn19
```

## 📁 Project Structure

```
DynamoDbFusion/
├── src/
│   └── DynamoDbFusion.Core/
│       ├── Interfaces/
│       │   └── IDynamoDbQueryService.cs
│       ├── Models/
│       │   ├── DynamoDbQueryRequest.cs
│       │   ├── FilterValue.cs
│       │   ├── PaginationRequest.cs
│       │   ├── ApiResponse.cs
│       │   └── PagedResult.cs
│       ├── Services/
│       │   └── DynamoDbQueryService.cs
│       └── Extensions/
│           ├── QueryStringExtensions.cs
│           └── ServiceCollectionExtensions.cs
├── samples/
│   └── DynamoDbFusion.WebApi/
│       ├── Controllers/
│       │   └── ProductsController.cs
│       └── Program.cs
└── docs/
    └── [existing documentation]
```

## 🧪 Testing the Sample

1. **Build the solution:**
   ```bash
   dotnet build
   ```

2. **Run the sample Web API:**
   ```bash
   cd samples/DynamoDbFusion.WebApi
   dotnet run
   ```

3. **Access Swagger UI:**
   ```
   https://localhost:7000/swagger
   ```

4. **Test endpoints:**
   - `GET /api/products` - Basic product listing with filtering
   - `GET /api/products/by-category/{category}` - Query by category
   - `GET /api/products/stream` - Stream large datasets
   - `POST /api/products/batch` - Batch queries

## 🔧 Configuration

### AWS Configuration
Ensure your AWS credentials are configured:
```bash
# Via AWS CLI
aws configure

# Or via environment variables
export AWS_ACCESS_KEY_ID=your_access_key
export AWS_SECRET_ACCESS_KEY=your_secret_key
export AWS_DEFAULT_REGION=us-east-1
```

### Custom DynamoDB Client
```csharp
// Custom client configuration
builder.Services.AddDynamoDbFusion(provider =>
{
    var config = new AmazonDynamoDBConfig
    {
        RegionEndpoint = RegionEndpoint.USEast1,
        ServiceURL = "http://localhost:8000" // For local DynamoDB
    };
    return new AmazonDynamoDBClient(config);
});
```

## 📋 Next Steps (Future Phases)

- **Phase 2**: Enhanced Features (caching, validation, security)
- **Phase 3**: Advanced Operations (batch operations, transactions)
- **Phase 4**: Production Features (monitoring, health checks, performance optimization)

## 🤝 Contributing

This is Phase 1 of the implementation. The core foundation is now complete and ready for testing and feedback.

## 📄 License

MIT License - see the existing documentation for details.
