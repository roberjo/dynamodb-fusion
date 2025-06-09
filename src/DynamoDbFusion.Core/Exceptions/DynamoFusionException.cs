using DynamoDbFusion.Core.Models;

namespace DynamoDbFusion.Core.Exceptions;

/// <summary>
/// Base exception for all DynamoDB Fusion specific exceptions
/// </summary>
public abstract class DynamoFusionException : Exception
{
    /// <summary>
    /// Unique error code for this exception type
    /// </summary>
    public abstract string ErrorCode { get; }

    /// <summary>
    /// Indicates if this exception represents a retryable error
    /// </summary>
    public abstract bool IsRetryable { get; }

    /// <summary>
    /// Additional metadata about the error
    /// </summary>
    public Dictionary<string, object> Metadata { get; } = new();

    protected DynamoFusionException(string message) : base(message)
    {
    }

    protected DynamoFusionException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets the HTTP status code that should be returned for this exception
    /// </summary>
    public virtual int GetHttpStatusCode()
    {
        return this switch
        {
            ValidationException => 400,
            UnauthorizedException => 401,
            ForbiddenException => 403,
            ResourceNotFoundException => 404,
            ConflictException => 409,
            ThrottlingException => 429,
            ServiceUnavailableException => 503,
            _ => 500
        };
    }
}

/// <summary>
/// Exception thrown when request validation fails
/// </summary>
public class ValidationException : DynamoFusionException
{
    public override string ErrorCode => "VALIDATION_ERROR";
    public override bool IsRetryable => false;

    public List<ValidationError> ValidationErrors { get; }

    public ValidationException(string message) : base(message)
    {
        ValidationErrors = new List<ValidationError>();
    }

    public ValidationException(string message, IEnumerable<ValidationError> errors) : base(message)
    {
        ValidationErrors = errors?.ToList() ?? new List<ValidationError>();
    }

    public ValidationException(string field, string message, string? errorCode = null) : base("Validation failed")
    {
        ValidationErrors = new List<ValidationError>
        {
            new() { Field = field, Message = message, ErrorCode = errorCode ?? "VALIDATION_ERROR" }
        };
    }
}

/// <summary>
/// Exception thrown when a resource is not found
/// </summary>
public class ResourceNotFoundException : DynamoFusionException
{
    public override string ErrorCode => "RESOURCE_NOT_FOUND";
    public override bool IsRetryable => false;

    public string ResourceType { get; }
    public string ResourceId { get; }

    public ResourceNotFoundException(string resourceType, string resourceId) 
        : base($"{resourceType} '{resourceId}' not found")
    {
        ResourceType = resourceType;
        ResourceId = resourceId;
        Metadata["resourceType"] = resourceType;
        Metadata["resourceId"] = resourceId;
    }

    public ResourceNotFoundException(string message) : base(message)
    {
        ResourceType = "Unknown";
        ResourceId = "Unknown";
    }
}

/// <summary>
/// Exception thrown when authentication is required
/// </summary>
public class UnauthorizedException : DynamoFusionException
{
    public override string ErrorCode => "UNAUTHORIZED";
    public override bool IsRetryable => false;

    public UnauthorizedException(string message = "Authentication required") : base(message)
    {
    }
}

/// <summary>
/// Exception thrown when access is forbidden
/// </summary>
public class ForbiddenException : DynamoFusionException
{
    public override string ErrorCode => "FORBIDDEN";
    public override bool IsRetryable => false;

    public string? RequiredPermission { get; }

    public ForbiddenException(string message) : base(message)
    {
    }

    public ForbiddenException(string message, string requiredPermission) : base(message)
    {
        RequiredPermission = requiredPermission;
        Metadata["requiredPermission"] = requiredPermission;
    }
}

/// <summary>
/// Exception thrown when a resource conflict occurs
/// </summary>
public class ConflictException : DynamoFusionException
{
    public override string ErrorCode => "CONFLICT";
    public override bool IsRetryable => false;

    public ConflictException(string message) : base(message)
    {
    }
}

/// <summary>
/// Exception thrown when rate limits are exceeded
/// </summary>
public class ThrottlingException : DynamoFusionException
{
    public override string ErrorCode => "THROTTLING_ERROR";
    public override bool IsRetryable => true;

    public TimeSpan RetryAfter { get; }
    public string LimitType { get; }

    public ThrottlingException(string message, TimeSpan retryAfter, string limitType = "requests_per_minute") 
        : base(message)
    {
        RetryAfter = retryAfter;
        LimitType = limitType;
        Metadata["retryAfter"] = retryAfter.TotalSeconds;
        Metadata["limitType"] = limitType;
    }
}

/// <summary>
/// Exception thrown when a service is temporarily unavailable
/// </summary>
public class ServiceUnavailableException : DynamoFusionException
{
    public override string ErrorCode => "SERVICE_UNAVAILABLE";
    public override bool IsRetryable => true;

    public string ServiceName { get; }
    public TimeSpan? EstimatedDowntime { get; }

    public ServiceUnavailableException(string serviceName, string message) : base(message)
    {
        ServiceName = serviceName;
        Metadata["serviceName"] = serviceName;
    }

    public ServiceUnavailableException(string serviceName, string message, TimeSpan estimatedDowntime) 
        : base(message)
    {
        ServiceName = serviceName;
        EstimatedDowntime = estimatedDowntime;
        Metadata["serviceName"] = serviceName;
        Metadata["estimatedDowntime"] = estimatedDowntime.TotalSeconds;
    }
}

/// <summary>
/// Exception thrown when a timeout occurs
/// </summary>
public class TimeoutException : DynamoFusionException
{
    public override string ErrorCode => "TIMEOUT_ERROR";
    public override bool IsRetryable => true;

    public TimeSpan Timeout { get; }
    public string Operation { get; }

    public TimeoutException(string operation, TimeSpan timeout) 
        : base($"Operation '{operation}' timed out after {timeout.TotalSeconds:F1} seconds")
    {
        Operation = operation;
        Timeout = timeout;
        Metadata["operation"] = operation;
        Metadata["timeout"] = timeout.TotalSeconds;
    }
}

/// <summary>
/// Exception thrown when cache operations fail
/// </summary>
public class CacheException : DynamoFusionException
{
    public override string ErrorCode => "CACHE_ERROR";
    public override bool IsRetryable => true;

    public string CacheLevel { get; }
    public string Operation { get; }

    public CacheException(string cacheLevel, string operation, string message) : base(message)
    {
        CacheLevel = cacheLevel;
        Operation = operation;
        Metadata["cacheLevel"] = cacheLevel;
        Metadata["operation"] = operation;
    }

    public CacheException(string cacheLevel, string operation, string message, Exception innerException) 
        : base(message, innerException)
    {
        CacheLevel = cacheLevel;
        Operation = operation;
        Metadata["cacheLevel"] = cacheLevel;
        Metadata["operation"] = operation;
    }
}

/// <summary>
/// Exception thrown when configuration is invalid
/// </summary>
public class ConfigurationException : DynamoFusionException
{
    public override string ErrorCode => "CONFIGURATION_ERROR";
    public override bool IsRetryable => false;

    public string ConfigurationSection { get; }

    public ConfigurationException(string configurationSection, string message) : base(message)
    {
        ConfigurationSection = configurationSection;
        Metadata["configurationSection"] = configurationSection;
    }
}

/// <summary>
/// Exception thrown when query optimization fails
/// </summary>
public class QueryOptimizationException : DynamoFusionException
{
    public override string ErrorCode => "QUERY_OPTIMIZATION_ERROR";
    public override bool IsRetryable => false;

    public string TableName { get; }
    public string OptimizationAttempted { get; }

    public QueryOptimizationException(string tableName, string optimizationAttempted, string message) 
        : base(message)
    {
        TableName = tableName;
        OptimizationAttempted = optimizationAttempted;
        Metadata["tableName"] = tableName;
        Metadata["optimizationAttempted"] = optimizationAttempted;
    }
} 