# Developer Guide

Complete guide for developers working on the DynamoDB Fusion project, covering setup, development workflow, testing, and contribution guidelines.

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Development Environment Setup](#development-environment-setup)
3. [Project Structure](#project-structure)
4. [Development Workflow](#development-workflow)
5. [Testing Strategy](#testing-strategy)
6. [Code Standards](#code-standards)
7. [Contributing Guidelines](#contributing-guidelines)
8. [Debugging Guide](#debugging-guide)
9. [Performance Profiling](#performance-profiling)

## Prerequisites

### Required Software

- **.NET 8.0 SDK** or later
- **Docker Desktop** (for local DynamoDB and Redis)
- **Git** for version control
- **IDE**: Visual Studio 2022, VS Code, or JetBrains Rider

### Optional Tools

- **AWS CLI** for cloud deployment testing
- **DynamoDB Admin** for database visualization
- **Redis Insight** for cache monitoring
- **Postman** or **Insomnia** for API testing

### System Requirements

- **OS**: Windows 10+, macOS 10.15+, or Linux
- **RAM**: 8GB minimum, 16GB recommended
- **Storage**: 10GB free space for development dependencies

## Development Environment Setup

### 1. Clone the Repository

```powershell
git clone https://github.com/your-org/dynamodb-fusion.git
Set-Location dynamodb-fusion
```

### 2. Install Dependencies

```powershell
# Restore NuGet packages
dotnet restore

# Install global tools
dotnet tool install --global dotnet-ef
dotnet tool install --global dotnet-format
```

### 3. Start Local Services

```powershell
# Start DynamoDB Local and Redis using Docker Compose
docker-compose up -d

# Verify services are running
docker-compose ps
```

### 4. Configure Development Settings

Create `appsettings.Development.json`:

```json
{
  "DynamoDbFusion": {
    "DynamoDb": {
      "ServiceUrl": "http://localhost:8000",
      "Region": "us-east-1",
      "UseLocalDynamoDb": true
    },
    "Cache": {
      "Provider": "Redis",
      "ConnectionString": "localhost:6379",
      "EnableL1Cache": true,
      "EnableL2Cache": true
    },
    "Security": {
      "EnableAuthentication": false,
      "EnableDetailedErrors": true,
      "RateLimiting": {
        "RequestsPerMinute": 1000
      }
    },
    "Logging": {
      "LogLevel": {
        "DynamoDbFusion": "Debug",
        "Microsoft.AspNetCore": "Information"
      }
    }
  }
}
```

### 5. Initialize Test Data

```powershell
# Run the database initialization script
dotnet run --project src/DynamoDbFusion.TestData -- --create-tables --seed-data
```

### 6. Verify Setup

```powershell
# Build the solution
dotnet build

# Run unit tests
dotnet test

# Start the API
dotnet run --project src/DynamoDbFusion.Api
```

Visit `https://localhost:5001/swagger` to verify the API is running.

## Project Structure

```
dynamodb-fusion/
├── docs/                           # Documentation
│   ├── adrs/                      # Architecture Decision Records
│   ├── examples/                  # Usage examples
│   └── *.md                       # Various documentation files
├── src/                           # Source code
│   ├── DynamoDbFusion.Core/       # Core library
│   │   ├── Interfaces/            # Service interfaces
│   │   ├── Models/                # Request/Response models
│   │   ├── Services/              # Core services
│   │   └── Extensions/            # Extension methods
│   ├── DynamoDbFusion.Infrastructure/ # Infrastructure layer
│   │   ├── DynamoDb/              # DynamoDB implementations
│   │   ├── Caching/               # Cache implementations
│   │   └── Security/              # Security implementations
│   ├── DynamoDbFusion.Api/        # Web API project
│   │   ├── Controllers/           # API controllers
│   │   ├── Middleware/            # Custom middleware
│   │   └── Program.cs             # Application entry point
│   └── DynamoDbFusion.TestData/   # Test data utilities
├── tests/                         # Test projects
│   ├── DynamoDbFusion.Core.Tests/     # Unit tests
│   ├── DynamoDbFusion.Integration.Tests/ # Integration tests
│   └── DynamoDbFusion.Performance.Tests/ # Performance tests
├── scripts/                       # Build and deployment scripts
├── docker-compose.yml             # Local development services
├── DynamoDbFusion.sln            # Solution file
└── README.md                      # Project overview
```

## Development Workflow

### 1. Feature Development

#### Branch Strategy
- `main`: Production-ready code
- `develop`: Integration branch for features
- `feature/*`: Feature development branches
- `hotfix/*`: Critical bug fixes

#### Workflow Steps

```powershell
# 1. Create feature branch
git checkout develop
git pull origin develop
git checkout -b feature/add-sorting-support

# 2. Implement feature with tests
# ... development work ...

# 3. Run quality checks
dotnet format --verify-no-changes
dotnet test
dotnet build --configuration Release

# 4. Commit changes
git add .
git commit -m "feat: add sorting support for query results"

# 5. Push and create PR
git push origin feature/add-sorting-support
# Create PR via GitHub/GitLab interface
```

### 2. Commit Message Convention

Follow [Conventional Commits](https://www.conventionalcommits.org/):

```
<type>[optional scope]: <description>

[optional body]

[optional footer(s)]
```

**Types:**
- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation changes
- `style`: Code style changes
- `refactor`: Code refactoring
- `test`: Adding tests
- `chore`: Maintenance tasks

**Examples:**
```
feat(cache): add Redis distributed caching support
fix(query): resolve null reference in filter parsing
docs(api): update API reference documentation
test(integration): add DynamoDB integration tests
```

### 3. Code Review Process

#### PR Requirements
- [ ] All tests pass
- [ ] Code coverage >= 80%
- [ ] No linting errors
- [ ] Documentation updated
- [ ] Performance impact assessed
- [ ] Security considerations reviewed

#### Review Checklist
- **Functionality**: Does the code work as intended?
- **Architecture**: Does it follow project patterns?
- **Performance**: Any performance implications?
- **Security**: Are there security vulnerabilities?
- **Maintainability**: Is the code readable and maintainable?
- **Testing**: Are there adequate tests?

## Testing Strategy

### 1. Unit Tests

**Location**: `tests/DynamoDbFusion.Core.Tests/`

**Framework**: xUnit with FluentAssertions and Moq

**Example Test:**

```csharp
public class QueryRequestValidatorTests
{
    private readonly QueryRequestValidator _validator;
    private readonly Mock<IOptions<DynamoDbQueryOptions>> _options;

    public QueryRequestValidatorTests()
    {
        _options = new Mock<IOptions<DynamoDbQueryOptions>>();
        _options.Setup(x => x.Value).Returns(new DynamoDbQueryOptions());
        _validator = new QueryRequestValidator(_options.Object);
    }

    [Fact]
    public async Task ValidateAsync_WithValidRequest_ShouldReturnSuccess()
    {
        // Arrange
        var request = new DynamoDbQueryRequest
        {
            TableName = "Users",
            PartitionKey = "UserId",
            PartitionKeyValue = "123"
        };

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task ValidateAsync_WithInvalidTableName_ShouldReturnError(string tableName)
    {
        // Arrange
        var request = new DynamoDbQueryRequest { TableName = tableName };

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == nameof(DynamoDbQueryRequest.TableName));
    }
}
```

**Running Unit Tests:**
```powershell
# Run all unit tests
dotnet test tests/DynamoDbFusion.Core.Tests/

# Run with coverage
dotnet test tests/DynamoDbFusion.Core.Tests/ --collect:"XPlat Code Coverage"

# Run specific test
dotnet test --filter "QueryRequestValidatorTests"
```

### 2. Integration Tests

**Location**: `tests/DynamoDbFusion.Integration.Tests/`

**Setup**: Uses TestContainers for DynamoDB Local and Redis

**Example Test:**

```csharp
public class DynamoDbQueryServiceIntegrationTests : IClassFixture<IntegrationTestFixture>
{
    private readonly IntegrationTestFixture _fixture;
    private readonly IDynamoDbQueryService _queryService;

    public DynamoDbQueryServiceIntegrationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _queryService = _fixture.GetService<IDynamoDbQueryService>();
    }

    [Fact]
    public async Task QueryAsync_WithValidRequest_ShouldReturnResults()
    {
        // Arrange
        await _fixture.SeedTestDataAsync();
        var request = new DynamoDbQueryRequest
        {
            TableName = "TestUsers",
            PartitionKey = "UserId",
            PartitionKeyValue = "user1"
        };

        // Act
        var result = await _queryService.QueryAsync<UserDto>(request);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data.Items.Should().HaveCountGreaterThan(0);
    }
}
```

### 3. Performance Tests

**Location**: `tests/DynamoDbFusion.Performance.Tests/`

**Framework**: NBomber for load testing

**Example Test:**

```csharp
public class QueryPerformanceTests
{
    [Fact]
    public void QueryService_ShouldHandleHighLoad()
    {
        var scenario = Scenario.Create("query_load_test", async context =>
        {
            var request = new DynamoDbQueryRequest
            {
                TableName = "Users",
                PartitionKey = "UserId",
                PartitionKeyValue = $"user{Random.Shared.Next(1, 1000)}"
            };

            var result = await _queryService.QueryAsync<UserDto>(request);
            
            return result.Success ? Response.Ok() : Response.Fail();
        })
        .WithLoadSimulations(
            Simulation.InjectPerSec(rate: 100, during: TimeSpan.FromMinutes(5))
        );

        NBomberRunner
            .RegisterScenarios(scenario)
            .Run();
    }
}
```

## Code Standards

### 1. C# Coding Conventions

**Naming Conventions:**
- PascalCase for classes, methods, properties
- camelCase for fields, parameters, local variables
- UPPER_CASE for constants
- Prefix interfaces with 'I'

**Code Organization:**
```csharp
// File: UserService.cs
namespace DynamoDbFusion.Core.Services;

/// <summary>
/// Provides user-related query operations
/// </summary>
public class UserService : IUserService
{
    private readonly IDynamoDbQueryService _queryService;
    private readonly ILogger<UserService> _logger;

    public UserService(
        IDynamoDbQueryService queryService,
        ILogger<UserService> logger)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<UserDto?> GetUserByIdAsync(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        try
        {
            var request = new DynamoDbQueryRequest
            {
                TableName = "Users",
                PartitionKey = "UserId",
                PartitionKeyValue = userId
            };

            var result = await _queryService.QueryAsync<UserDto>(request);
            return result.Success ? result.Data?.Items.FirstOrDefault() : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user {UserId}", userId);
            throw;
        }
    }
}
```

### 2. Documentation Standards

**XML Documentation:**
- All public APIs must have XML documentation
- Include `<summary>`, `<param>`, `<returns>`, and `<exception>` tags
- Provide code examples for complex methods

**README Requirements:**
- Each major component should have a README
- Include usage examples and configuration options
- Document breaking changes

### 3. Error Handling Standards

```csharp
// Good: Specific exception types
public class ValidationException : DynamoFusionException
{
    public ValidationException(string message, IEnumerable<ValidationError> errors)
        : base(message)
    {
        Errors = errors?.ToList() ?? new List<ValidationError>();
    }

    public List<ValidationError> Errors { get; }
}

// Good: Proper exception handling
public async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation)
{
    var attempt = 0;
    var maxAttempts = 3;

    while (attempt < maxAttempts)
    {
        try
        {
            return await operation();
        }
        catch (ThrottlingException) when (attempt < maxAttempts - 1)
        {
            attempt++;
            var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
            await Task.Delay(delay);
        }
    }

    throw new MaxRetriesExceededException($"Operation failed after {maxAttempts} attempts");
}
```

## Contributing Guidelines

### 1. Issue Reporting

**Bug Reports:**
- Use the bug report template
- Include reproduction steps
- Provide environment details
- Attach relevant logs/screenshots

**Feature Requests:**
- Use the feature request template
- Explain the use case and benefits
- Provide implementation suggestions if possible

### 2. Pull Request Process

1. **Fork and Clone**: Fork the repository and clone your fork
2. **Branch**: Create a feature branch from `develop`
3. **Develop**: Implement your changes with comprehensive tests
4. **Test**: Ensure all tests pass and coverage is maintained
5. **Document**: Update documentation as needed
6. **PR**: Create a pull request with a clear description
7. **Review**: Address feedback from code review
8. **Merge**: Squash and merge after approval

### 3. Community Guidelines

- **Be Respectful**: Treat all community members with respect
- **Be Constructive**: Provide helpful feedback and suggestions
- **Be Patient**: Understand that reviews take time
- **Follow Standards**: Adhere to project coding and documentation standards

## Debugging Guide

### 1. Local Debugging Setup

**VS Code Configuration** (`.vscode/launch.json`):
```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "Launch API",
      "type": "coreclr",
      "request": "launch",
      "program": "${workspaceFolder}/src/DynamoDbFusion.Api/bin/Debug/net8.0/DynamoDbFusion.Api.dll",
      "args": [],
      "cwd": "${workspaceFolder}/src/DynamoDbFusion.Api",
      "env": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      },
      "sourceFileMap": {
        "/Views": "${workspaceFolder}/Views"
      }
    }
  ]
}
```

### 2. Logging Configuration

**Structured Logging Example:**
```csharp
_logger.LogInformation("Query executed for table {TableName} with {FilterCount} filters in {Duration}ms",
    request.TableName, 
    request.Filters.Count, 
    stopwatch.ElapsedMilliseconds);
```

**Log Levels:**
- `Debug`: Detailed diagnostic information
- `Information`: General application flow
- `Warning`: Unusual but not error conditions
- `Error`: Error conditions that don't stop the application
- `Critical`: Fatal errors that may cause the application to abort

### 3. Common Issues and Solutions

**Issue: DynamoDB Connection Timeout**
```
Solution: Check if DynamoDB Local is running and accessible
docker-compose ps
docker-compose logs dynamodb-local
```

**Issue: Redis Cache Errors**
```
Solution: Verify Redis connection and configuration
docker-compose logs redis
```

**Issue: High Memory Usage**
```
Solution: Profile memory usage and check for memory leaks
dotnet-dump collect -p <process-id>
dotnet-dump analyze <dump-file>
```

## Performance Profiling

### 1. Memory Profiling

```powershell
# Install profiling tools
dotnet tool install --global dotnet-dump
dotnet tool install --global dotnet-counters

# Monitor memory usage
dotnet-counters monitor --process-id <pid> --counters System.Runtime

# Capture memory dump
dotnet-dump collect --process-id <pid>
```

### 2. CPU Profiling

```powershell
# Install profiling tools
dotnet tool install --global dotnet-trace

# Capture CPU trace
dotnet-trace collect --process-id <pid> --duration 00:00:30

# Analyze trace file (use Visual Studio or PerfView)
```

### 3. Application Insights Integration

```csharp
// Add to Program.cs
builder.Services.AddApplicationInsightsTelemetry();

// Custom telemetry
public class QueryTelemetryService
{
    private readonly TelemetryClient _telemetryClient;

    public void TrackQueryExecution(string tableName, TimeSpan duration, bool cached)
    {
        _telemetryClient.TrackDependency("DynamoDB", tableName, 
            DateTime.UtcNow.Subtract(duration), duration, !cached);
        
        _telemetryClient.TrackMetric("QueryDuration", duration.TotalMilliseconds,
            new Dictionary<string, string> 
            {
                ["TableName"] = tableName,
                ["Cached"] = cached.ToString()
            });
    }
}
```

### 4. Benchmarking

```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class QueryServiceBenchmark
{
    private IDynamoDbQueryService _service;
    private DynamoDbQueryRequest _request;

    [GlobalSetup]
    public void Setup()
    {
        // Initialize service and request
    }

    [Benchmark]
    public async Task<ApiResponse<PagedResult<UserDto>>> QueryWithCache()
    {
        return await _service.QueryAsync<UserDto>(_request);
    }

    [Benchmark]
    public async Task<ApiResponse<PagedResult<UserDto>>> QueryWithoutCache()
    {
        // Disable cache for this benchmark
        return await _service.QueryAsync<UserDto>(_request);
    }
}
```

This developer guide provides a comprehensive foundation for working on the DynamoDB Fusion project. Keep it updated as the project evolves and new practices are established. 