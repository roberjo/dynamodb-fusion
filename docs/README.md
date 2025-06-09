# DynamoDB Fusion Documentation

A comprehensive ASP.NET Core 8 service for querying DynamoDB tables with filtering, pagination, and automatic DTO mapping from HTTP GET request query strings.

## 📚 Documentation Index

### Core Documentation
- [Requirements Document](./dynamodb-fusion-reqs.md) - Complete functional and technical requirements
- [API Reference](./api-reference.md) - Complete API documentation with examples
- [Architecture Overview](./architecture.md) - System design and architectural decisions

### Development & Deployment
- [Developer Guide](./developer-guide.md) - Setup, contributing, and development workflow
- [Deployment Guide](./deployment-guide.md) - Production deployment and configuration
- [Performance Guide](./performance-guide.md) - Optimization and tuning recommendations

### Architecture Decision Records (ADRs)
- [ADR-001: Framework Selection](./adrs/001-framework-selection.md)
- [ADR-002: Cache Strategy](./adrs/002-cache-strategy.md)
- [ADR-003: Security Model](./adrs/003-security-model.md)
- [ADR-004: Query vs Scan Strategy](./adrs/004-query-scan-strategy.md)

### Examples & Guides
- [Usage Examples](./examples/) - Working code samples
- [Migration Guide](./migration-guide.md) - Migrating from direct DynamoDB SDK
- [Troubleshooting](./troubleshooting.md) - Common issues and solutions

## 🚀 Quick Start

```csharp
// Add to Program.cs
builder.Services.AddDynamoDbQueryService(options =>
{
    options.DefaultPageSize = 20;
    options.MaxPageSize = 100;
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

// Use in controller
[HttpGet]
public async Task<ActionResult<ApiResponse<PagedResult<UserDto>>>> GetUsers()
{
    var request = new DynamoDbQueryRequest
    {
        TableName = "Users",
        Filters = HttpContext.Request.Query.ToDynamoFilters(),
        Pagination = HttpContext.Request.Query.ToPaginationRequest()
    };

    var result = await _queryService.QueryAsync<UserDto>(request);
    return Ok(result);
}
```

## 📋 Project Status

- ✅ Requirements Analysis Complete
- 🔄 Core Implementation (In Progress)
- ⏳ Testing Suite (Planned)
- ⏳ Documentation Complete (In Progress)
- ⏳ Performance Benchmarking (Planned)

## 🤝 Contributing

Please read our [Developer Guide](./developer-guide.md) for information on how to contribute to this project.

## 📄 License

This project is licensed under the MIT License - see the LICENSE file for details. 