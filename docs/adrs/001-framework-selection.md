# ADR-001: Framework Selection

## Status
**Accepted** - 2024-01-15

## Context

We need to select a web framework and runtime for building the DynamoDB Fusion query service. The service needs to:

- Provide HTTP endpoints for querying DynamoDB
- Support modern .NET features and performance optimizations
- Integrate well with dependency injection and configuration systems
- Support both synchronous and asynchronous operations
- Be suitable for containerization and cloud deployment

## Decision

We will use **ASP.NET Core 8.0** as the primary framework for the DynamoDB Fusion service.

## Rationale

### ASP.NET Core 8.0 Advantages:
1. **Performance**: Significant performance improvements over previous versions
2. **Native AOT Support**: Better startup time and memory usage for containerized deployments
3. **Built-in Features**: 
   - Dependency injection container
   - Configuration system
   - Logging framework
   - Health checks
   - OpenAPI/Swagger integration
4. **Long-term Support**: LTS release with 3-year support lifecycle
5. **Cloud-Native**: Excellent Docker and Kubernetes integration
6. **Ecosystem**: Rich NuGet package ecosystem for AWS SDK integration

### Alternatives Considered:

#### .NET 6 LTS
- **Pros**: Stable, widely adopted, LTS until November 2024
- **Cons**: Missing performance improvements and features from .NET 8
- **Verdict**: Rejected - .NET 8 provides significant benefits without compatibility issues

#### .NET Framework 4.8
- **Pros**: Maximum compatibility with legacy systems
- **Cons**: 
  - Windows-only deployment
  - Older HTTP pipeline
  - No built-in containerization support
  - Limited async/await optimization
- **Verdict**: Rejected - Not suitable for modern cloud deployments

#### Minimal APIs vs Controller-based APIs
- **Decision**: Support both patterns through flexible service design
- **Rationale**: Allow consumers to choose their preferred API style

## Consequences

### Positive:
- Access to latest .NET performance optimizations
- Native container and cloud deployment support
- Rich middleware ecosystem for cross-cutting concerns
- Strong typing and compile-time safety
- Excellent tooling and debugging support

### Negative:
- Requires .NET 8 runtime on target systems
- Some enterprises may prefer LTS versions for stability
- Learning curve for teams new to ASP.NET Core

### Neutral:
- Standard Microsoft technology stack reduces vendor lock-in concerns
- Good balance between innovation and enterprise acceptance

## Implementation Notes

1. **Target Framework**: `net8.0`
2. **Package Style**: Create as a NuGet library that can be added to existing ASP.NET Core applications
3. **Compatibility**: Maintain backward compatibility with .NET Standard 2.1 where possible for broader adoption
4. **Dependencies**: Minimize external dependencies to reduce conflicts in consuming applications

## Review Date
This decision should be reviewed by January 2027 when .NET 8 approaches end of support. 