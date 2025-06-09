using DynamoDbFusion.Core.Exceptions;
using DynamoDbFusion.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace DynamoDbFusion.Core.Middleware;

/// <summary>
/// Global exception middleware for handling all unhandled exceptions and converting them to structured API responses
/// </summary>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "An unhandled exception occurred while processing the request");
            await HandleExceptionAsync(context, exception);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var response = CreateErrorResponse(exception);
        
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = response.StatusCode;

        // Add custom headers for debugging
        if (_environment.IsDevelopment())
        {
            context.Response.Headers.Add("X-Error-Type", exception.GetType().Name);
            context.Response.Headers.Add("X-Error-Source", exception.Source ?? "Unknown");
        }

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = _environment.IsDevelopment()
        };

        var jsonResponse = JsonSerializer.Serialize(response.Body, jsonOptions);
        await context.Response.WriteAsync(jsonResponse);
    }

    private ErrorResponse CreateErrorResponse(Exception exception)
    {
        return exception switch
        {
            DynamoFusionException dynamoFusionEx => CreateDynamoFusionErrorResponse(dynamoFusionEx),
            Amazon.DynamoDBv2.AmazonDynamoDBException dynamoDbEx => CreateDynamoDbErrorResponse(dynamoDbEx),
            TaskCanceledException => CreateTimeoutErrorResponse(),
            OperationCanceledException => CreateCancellationErrorResponse(),
            ArgumentException argEx => CreateArgumentErrorResponse(argEx),
            UnauthorizedAccessException => CreateUnauthorizedErrorResponse(),
            NotImplementedException => CreateNotImplementedErrorResponse(),
            _ => CreateGenericErrorResponse(exception)
        };
    }

    private ErrorResponse CreateDynamoFusionErrorResponse(DynamoFusionException exception)
    {
        var apiResponse = exception switch
        {
            ValidationException validationEx => ApiResponse<object>.CreateValidationError(
                validationEx.ValidationErrors, validationEx.Message),
            
            ResourceNotFoundException notFoundEx => ApiResponse<object>.CreateNotFound(notFoundEx.Message),
            
            _ => ApiResponse<object>.CreateFailure(exception.Message)
        };

        // Add metadata from the exception
        if (exception.Metadata.Count > 0)
        {
            apiResponse.Metadata = exception.Metadata;
        }

        return new ErrorResponse
        {
            StatusCode = exception.GetHttpStatusCode(),
            Body = apiResponse
        };
    }

    private ErrorResponse CreateDynamoDbErrorResponse(Amazon.DynamoDBv2.AmazonDynamoDBException exception)
    {
        var (statusCode, errorCode, message) = exception.ErrorCode switch
        {
            "ResourceNotFoundException" => (404, "RESOURCE_NOT_FOUND", "The requested table or resource was not found"),
            "ValidationException" => (400, "VALIDATION_ERROR", exception.Message),
            "ConditionalCheckFailedException" => (409, "CONDITIONAL_CHECK_FAILED", "The conditional check failed"),
            "ProvisionedThroughputExceededException" => (429, "THROTTLING_ERROR", "Request rate exceeded for this table"),
            "ThrottlingException" => (429, "THROTTLING_ERROR", "Request was throttled"),
            "LimitExceededException" => (429, "LIMIT_EXCEEDED", "Request limit exceeded"),
            "ItemCollectionSizeLimitExceededException" => (413, "ITEM_TOO_LARGE", "Item collection size limit exceeded"),
            "RequestLimitExceeded" => (429, "REQUEST_LIMIT_EXCEEDED", "Too many requests"),
            "ServiceUnavailableException" => (503, "SERVICE_UNAVAILABLE", "DynamoDB service is temporarily unavailable"),
            "InternalServerError" => (500, "INTERNAL_ERROR", "An internal error occurred in DynamoDB"),
            _ => (500, "DYNAMODB_ERROR", exception.Message)
        };

        var apiResponse = ApiResponse<object>.CreateFailure(message);
        apiResponse.Metadata = new Dictionary<string, object>
        {
            ["dynamoDbErrorCode"] = exception.ErrorCode,
            ["dynamoDbErrorType"] = exception.ErrorType?.ToString() ?? "Unknown",
            ["requestId"] = exception.RequestId ?? "Unknown"
        };

        if (statusCode == 429)
        {
            apiResponse.Metadata["retryAfter"] = 60; // Suggest retry after 60 seconds
        }

        return new ErrorResponse
        {
            StatusCode = statusCode,
            Body = apiResponse
        };
    }

    private ErrorResponse CreateTimeoutErrorResponse()
    {
        var apiResponse = ApiResponse<object>.CreateFailure("The request timed out");
        apiResponse.Metadata = new Dictionary<string, object>
        {
            ["errorCode"] = "TIMEOUT_ERROR",
            ["retryable"] = true
        };

        return new ErrorResponse
        {
            StatusCode = 408,
            Body = apiResponse
        };
    }

    private ErrorResponse CreateCancellationErrorResponse()
    {
        var apiResponse = ApiResponse<object>.CreateFailure("The request was cancelled");
        apiResponse.Metadata = new Dictionary<string, object>
        {
            ["errorCode"] = "REQUEST_CANCELLED",
            ["retryable"] = false
        };

        return new ErrorResponse
        {
            StatusCode = 499, // Client Closed Request
            Body = apiResponse
        };
    }

    private ErrorResponse CreateArgumentErrorResponse(ArgumentException exception)
    {
        var apiResponse = ApiResponse<object>.CreateSingleValidationError(
            exception.ParamName ?? "Unknown", 
            exception.Message, 
            "INVALID_ARGUMENT");

        return new ErrorResponse
        {
            StatusCode = 400,
            Body = apiResponse
        };
    }

    private ErrorResponse CreateUnauthorizedErrorResponse()
    {
        var apiResponse = ApiResponse<object>.CreateFailure("Authentication is required");
        apiResponse.Metadata = new Dictionary<string, object>
        {
            ["errorCode"] = "UNAUTHORIZED",
            ["retryable"] = false
        };

        return new ErrorResponse
        {
            StatusCode = 401,
            Body = apiResponse
        };
    }

    private ErrorResponse CreateNotImplementedErrorResponse()
    {
        var apiResponse = ApiResponse<object>.CreateFailure("This feature is not yet implemented");
        apiResponse.Metadata = new Dictionary<string, object>
        {
            ["errorCode"] = "NOT_IMPLEMENTED",
            ["retryable"] = false
        };

        return new ErrorResponse
        {
            StatusCode = 501,
            Body = apiResponse
        };
    }

    private ErrorResponse CreateGenericErrorResponse(Exception exception)
    {
        var message = _environment.IsDevelopment() 
            ? exception.Message 
            : "An internal server error occurred";

        var apiResponse = ApiResponse<object>.CreateFailure(message);
        
        if (_environment.IsDevelopment())
        {
            apiResponse.Metadata = new Dictionary<string, object>
            {
                ["exceptionType"] = exception.GetType().Name,
                ["stackTrace"] = exception.StackTrace ?? "No stack trace available"
            };
        }
        else
        {
            apiResponse.Metadata = new Dictionary<string, object>
            {
                ["errorCode"] = "INTERNAL_ERROR",
                ["retryable"] = false
            };
        }

        return new ErrorResponse
        {
            StatusCode = 500,
            Body = apiResponse
        };
    }
}

/// <summary>
/// Internal class for structuring error responses
/// </summary>
internal class ErrorResponse
{
    public int StatusCode { get; set; }
    public ApiResponse<object> Body { get; set; } = new();
} 