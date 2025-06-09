# ADR-003: Security Model

## Status
**Accepted** - 2024-01-15

## Context

The DynamoDB Fusion service needs a comprehensive security model to protect against:
- **Injection Attacks**: SQL-like injection through query parameters
- **Unauthorized Access**: Access to tables/data without proper permissions
- **Data Exposure**: Sensitive information leakage through error messages
- **Resource Abuse**: DoS attacks through expensive queries
- **Configuration Tampering**: Malicious modification of service behavior

The service operates in environments with varying security requirements:
- **Internal APIs**: Corporate networks with trusted users
- **Public APIs**: Internet-facing applications with untrusted input
- **Multi-tenant**: Shared service instances with tenant isolation needs

## Decision

We will implement a **defense-in-depth security model** with the following layers:

1. **Input Validation & Sanitization**: Comprehensive validation of all inputs
2. **Authorization Framework**: Pluggable authorization with role-based access
3. **Rate Limiting**: Request throttling to prevent abuse
4. **Audit Logging**: Complete audit trail of all operations
5. **Error Handling**: Secure error responses that don't leak information
6. **Configuration Security**: Secure handling of secrets and configurations

## Rationale

### Multi-Layer Security Approach:
Each layer provides independent protection, ensuring that if one layer fails, others maintain security posture.

### Input Validation Strategy:
```csharp
public class InputValidationOptions
{
    public int MaxTableNameLength { get; set; } = 255;
    public int MaxFilterValueLength { get; set; } = 1000;
    public int MaxFiltersPerRequest { get; set; } = 10;
    public List<string> AllowedTableNameChars { get; set; } = ["a-zA-Z0-9_-"];
    public List<string> ForbiddenPatterns { get; set; } = ["<script", "javascript:", "data:"];
    public bool EnableSqlInjectionDetection { get; set; } = true;
}
```

### Authorization Framework:
- **Interface-based**: `IAuthorizationHandler` for pluggable authorization
- **Attribute-based**: Table and operation-level access control
- **Context-aware**: User, tenant, and resource-specific decisions

## Implementation Details

### 1. Input Validation & Sanitization

```csharp
public interface IInputValidator
{
    ValidationResult ValidateTableName(string tableName);
    ValidationResult ValidateFilterValue(string key, object value);
    ValidationResult ValidateQueryRequest(DynamoDbQueryRequest request);
}

public class DefaultInputValidator : IInputValidator
{
    public ValidationResult ValidateTableName(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            return ValidationResult.Error("Table name is required");
            
        if (tableName.Length > _options.MaxTableNameLength)
            return ValidationResult.Error($"Table name exceeds maximum length of {_options.MaxTableNameLength}");
            
        if (!Regex.IsMatch(tableName, @"^[a-zA-Z0-9_-]+$"))
            return ValidationResult.Error("Table name contains invalid characters");
            
        // Check against known malicious patterns
        foreach (var pattern in _options.ForbiddenPatterns)
        {
            if (tableName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return ValidationResult.Error("Table name contains forbidden pattern");
        }
        
        return ValidationResult.Success();
    }
}
```

### 2. Authorization Framework

```csharp
public interface IAuthorizationHandler
{
    Task<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user, 
        string tableName, 
        string operation, 
        DynamoDbQueryRequest request);
}

public class RoleBasedAuthorizationHandler : IAuthorizationHandler
{
    public async Task<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user, 
        string tableName, 
        string operation, 
        DynamoDbQueryRequest request)
    {
        // Check if user has required role for table access
        var requiredRole = _tablePermissions.GetRequiredRole(tableName, operation);
        if (!user.IsInRole(requiredRole))
        {
            _logger.LogWarning("Authorization failed: User {UserId} lacks role {Role} for table {Table}", 
                user.GetUserId(), requiredRole, tableName);
            return AuthorizationResult.Forbidden();
        }
        
        // Check for row-level security if configured
        if (_tablePermissions.HasRowLevelSecurity(tableName))
        {
            return await ApplyRowLevelSecurityAsync(user, tableName, request);
        }
        
        return AuthorizationResult.Success();
    }
}
```

### 3. Rate Limiting

```csharp
public class RateLimitingOptions
{
    public int RequestsPerMinute { get; set; } = 60;
    public int RequestsPerHour { get; set; } = 1000;
    public Dictionary<string, int> TableSpecificLimits { get; set; } = new();
    public Dictionary<string, int> UserSpecificLimits { get; set; } = new();
    public bool EnableDistributedRateLimiting { get; set; } = true;
}

public interface IRateLimitingService
{
    Task<RateLimitResult> CheckLimitAsync(string identifier, string resource);
}
```

### 4. Audit Logging

```csharp
public class AuditEvent
{
    public string EventId { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string UserId { get; set; }
    public string TableName { get; set; }
    public string Operation { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string ClientIpAddress { get; set; }
    public string UserAgent { get; set; }
}

public interface IAuditLogger
{
    Task LogAsync(AuditEvent auditEvent);
    Task LogSecurityEventAsync(string eventType, string details, string userId = null);
}
```

### 5. Error Handling

```csharp
public class SecureErrorHandler
{
    public ApiResponse<T> HandleError<T>(Exception exception, bool isDevelopment)
    {
        // Log detailed error internally
        _logger.LogError(exception, "Query execution failed");
        
        // Return sanitized error to client
        return new ApiResponse<T>
        {
            Success = false,
            Message = GetSafeErrorMessage(exception, isDevelopment),
            Data = default(T)
        };
    }
    
    private string GetSafeErrorMessage(Exception exception, bool isDevelopment)
    {
        return exception switch
        {
            ValidationException => exception.Message, // Safe to expose validation errors
            UnauthorizedException => "Access denied",
            ResourceNotFoundException => "Resource not found",
            ThrottlingException => "Request rate exceeded",
            _ => isDevelopment ? exception.Message : "An error occurred processing your request"
        };
    }
}
```

## Alternatives Considered

### Basic Authentication Only
- **Pros**: Simple implementation, minimal overhead
- **Cons**: Insufficient for production security requirements
- **Verdict**: Rejected - Not suitable for production environments

### Custom Security Framework
- **Pros**: Perfect fit for specific requirements
- **Cons**: High development cost, maintenance burden, security review complexity
- **Verdict**: Rejected - Prefer established patterns and frameworks

### OAuth2/OpenID Connect Integration
- **Pros**: Industry standard, excellent tooling support
- **Cons**: Additional complexity for simple use cases
- **Verdict**: Accepted as optional integration - Support through standard ASP.NET Core authentication

## Security Configuration

### Development Environment:
```json
{
  "DynamoDbSecurity": {
    "EnableDetailedErrors": true,
    "EnableAuditLogging": false,
    "RateLimiting": {
      "RequestsPerMinute": 1000
    },
    "Authorization": {
      "EnableAuthentication": false
    }
  }
}
```

### Production Environment:
```json
{
  "DynamoDbSecurity": {
    "EnableDetailedErrors": false,
    "EnableAuditLogging": true,
    "RateLimiting": {
      "RequestsPerMinute": 60,
      "RequestsPerHour": 1000
    },
    "Authorization": {
      "EnableAuthentication": true,
      "RequireHttps": true
    },
    "AuditLogging": {
      "Provider": "ElasticSearch",
      "RetentionDays": 365
    }
  }
}
```

## Consequences

### Positive:
- **Comprehensive Protection**: Multiple security layers provide robust defense
- **Compliance Ready**: Audit logging supports regulatory compliance requirements
- **Configurable**: Security levels can be adjusted per environment
- **Observable**: Security events are logged and monitorable

### Negative:
- **Performance Impact**: Validation and authorization add processing overhead
- **Complexity**: More moving parts to configure and maintain
- **Development Overhead**: Additional testing required for security scenarios

### Neutral:
- **Standard Practices**: Uses well-established security patterns
- **Integration Requirements**: May require additional infrastructure (Redis for rate limiting, log aggregation)

## Security Testing Requirements

### 1. Input Validation Testing
- Malicious input patterns (XSS, injection attempts)
- Boundary value testing (length limits, special characters)
- Unicode and encoding edge cases

### 2. Authorization Testing
- Role-based access scenarios
- Privilege escalation attempts
- Cross-tenant data access attempts

### 3. Rate Limiting Testing
- Burst traffic scenarios
- Distributed rate limiting accuracy
- Rate limit bypass attempts

### 4. Penetration Testing
- OWASP Top 10 vulnerability assessment
- API security best practices compliance
- Infrastructure security review

## Monitoring & Alerting

Critical security metrics to monitor:
- Failed authentication attempts
- Authorization failures
- Rate limit violations
- Suspicious query patterns
- Error rates and types

## Review Date
This decision should be reviewed quarterly or after any security incident to ensure continued effectiveness against evolving threats. 