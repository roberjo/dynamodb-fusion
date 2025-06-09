namespace DynamoDbFusion.Core.Models;

/// <summary>
/// Standard response wrapper for all API operations
/// </summary>
/// <typeparam name="T">The type of data being returned</typeparam>
public class ApiResponse<T>
{
    /// <summary>
    /// Indicates if the operation was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Human-readable message describing the result
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// The response data
    /// </summary>
    public T? Data { get; set; }

    /// <summary>
    /// Validation errors (if any)
    /// </summary>
    public List<ValidationError> Errors { get; set; } = new();

    /// <summary>
    /// Additional metadata about the operation
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }

    /// <summary>
    /// Creates a successful response with data
    /// </summary>
    public static ApiResponse<T> CreateSuccess(T data, string? message = null)
    {
        return new ApiResponse<T>
        {
            Success = true,
            Data = data,
            Message = message
        };
    }

    /// <summary>
    /// Creates a successful response with data and metadata
    /// </summary>
    public static ApiResponse<T> CreateSuccessWithMetadata(T data, Dictionary<string, object> metadata, string? message = null)
    {
        return new ApiResponse<T>
        {
            Success = true,
            Data = data,
            Message = message,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Creates a failed response with error message
    /// </summary>
    public static ApiResponse<T> CreateFailure(string message)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Message = message
        };
    }

    /// <summary>
    /// Creates a not found response
    /// </summary>
    public static ApiResponse<T> CreateNotFound(string? message = null)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Message = message ?? "Resource not found"
        };
    }

    /// <summary>
    /// Creates a failed response with validation errors
    /// </summary>
    public static ApiResponse<T> CreateValidationError(IEnumerable<ValidationError> errors, string? message = null)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Message = message ?? "Validation failed",
            Errors = errors.ToList()
        };
    }

    /// <summary>
    /// Creates a failed response with a single validation error
    /// </summary>
    public static ApiResponse<T> CreateSingleValidationError(string field, string message, string? errorCode = null)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Message = "Validation failed",
            Errors = new List<ValidationError>
            {
                new() { Field = field, Message = message, ErrorCode = errorCode ?? "VALIDATION_ERROR" }
            }
        };
    }
}

/// <summary>
/// Represents a validation error
/// </summary>
public class ValidationError
{
    /// <summary>
    /// The field that failed validation
    /// </summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>
    /// The validation error message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// The error code for programmatic handling
    /// </summary>
    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>
    /// Additional context about the error
    /// </summary>
    public Dictionary<string, object>? Context { get; set; }
} 