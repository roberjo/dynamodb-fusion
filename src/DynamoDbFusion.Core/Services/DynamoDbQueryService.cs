using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using DynamoDbFusion.Core.Interfaces;
using DynamoDbFusion.Core.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Amazon;

namespace DynamoDbFusion.Core.Services;

/// <summary>
/// Main implementation of the DynamoDB query service
/// </summary>
public class DynamoDbQueryService : IDynamoDbQueryService
{
    private readonly IAmazonDynamoDB _dynamoDbClient;
    private readonly ILogger<DynamoDbQueryService> _logger;

    public DynamoDbQueryService(
        IAmazonDynamoDB dynamoDbClient,
        ILogger<DynamoDbQueryService> logger)
    {
        _dynamoDbClient = dynamoDbClient ?? throw new ArgumentNullException(nameof(dynamoDbClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ApiResponse<PagedResult<TDto>>> QueryAsync<TDto>(
        DynamoDbQueryRequest request,
        CancellationToken cancellationToken = default)
        where TDto : class, new()
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogDebug("Executing query for table {TableName} with {FilterCount} filters",
                request.TableName, request.Filters.Count);

            // Validate request
            var validationResult = ValidateRequest<TDto>(request);
            if (!validationResult.Success)
            {
                return validationResult;
            }

            // Determine operation strategy (Query vs Scan)
            var useQuery = ShouldUseQuery(request);
            
            PagedResult<TDto> result;
            if (useQuery)
            {
                result = await ExecuteQueryAsync<TDto>(request, cancellationToken);
            }
            else
            {
                result = await ExecuteScanAsync<TDto>(request, cancellationToken);
            }

            stopwatch.Stop();
            result.Query.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;

            _logger.LogInformation("Query completed for table {TableName} in {Duration}ms, returned {ItemCount} items",
                request.TableName, stopwatch.ElapsedMilliseconds, result.Pagination.ItemCount);

            var metadata = new Dictionary<string, object>
            {
                ["OperationType"] = result.Query.OperationType,
                ["ExecutionTimeMs"] = result.Query.ExecutionTimeMs,
                ["ItemsReturned"] = result.Query.ItemsReturned
            };

            return ApiResponse<PagedResult<TDto>>.CreateSuccessWithMetadata(result, metadata);
        }
        catch (ResourceNotFoundException ex)
        {
            _logger.LogError(ex, "Table {TableName} not found", request.TableName);
            return ApiResponse<PagedResult<TDto>>.CreateNotFound($"Table '{request.TableName}' not found");
        }
        catch (AmazonDynamoDBException ex) when (ex.Message.Contains("ValidationException"))
        {
            _logger.LogError(ex, "Validation error for table {TableName}: {Message}", request.TableName, ex.Message);
            return ApiResponse<PagedResult<TDto>>.CreateSingleValidationError("Request", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error querying table {TableName}", request.TableName);
            return ApiResponse<PagedResult<TDto>>.CreateFailure("An error occurred while executing the query");
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TDto> StreamAsync<TDto>(
        DynamoDbQueryRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TDto : class, new()
    {
        string? nextToken = null;
        var originalPageSize = request.Pagination.PageSize;

        do
        {
            request.Pagination.NextToken = nextToken;
            request.Pagination.PageSize = Math.Min(originalPageSize, 100); // Limit streaming page size

            var result = await QueryAsync<TDto>(request, cancellationToken);
            
            if (!result.Success || result.Data?.Items == null)
                yield break;

            foreach (var item in result.Data.Items)
            {
                yield return item;
            }

            nextToken = result.Data.Pagination.NextPageToken;

        } while (!string.IsNullOrEmpty(nextToken) && !cancellationToken.IsCancellationRequested);
    }

    /// <inheritdoc />
    public async Task<ApiResponse<BatchResult<TDto>>> BatchQueryAsync<TDto>(
        IEnumerable<DynamoDbQueryRequest> requests,
        CancellationToken cancellationToken = default)
        where TDto : class, new()
    {
        var stopwatch = Stopwatch.StartNew();
        var requestList = requests.ToList();

        try
        {
            _logger.LogDebug("Executing batch query with {RequestCount} requests", requestList.Count);

            var tasks = requestList.Select(request => QueryAsync<TDto>(request, cancellationToken));
            var results = await Task.WhenAll(tasks);

            var batchResult = new BatchResult<TDto>
            {
                Results = results.Where(r => r.Success).Select(r => r.Data!).ToList(),
                Batch = new BatchMetadata
                {
                    QueryCount = requestList.Count,
                    TotalExecutionTimeMs = stopwatch.ElapsedMilliseconds,
                    SuccessfulQueries = results.Count(r => r.Success),
                    FailedQueries = results.Count(r => !r.Success),
                    TotalEstimatedCost = results.Where(r => r.Success).Sum(r => r.Data?.Query.EstimatedCost ?? 0)
                }
            };

            stopwatch.Stop();

            _logger.LogInformation("Batch query completed with {SuccessfulQueries}/{TotalQueries} successful queries in {Duration}ms",
                batchResult.Batch.SuccessfulQueries, batchResult.Batch.QueryCount, stopwatch.ElapsedMilliseconds);

            return ApiResponse<BatchResult<TDto>>.CreateSuccess(batchResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing batch query");
            return ApiResponse<BatchResult<TDto>>.CreateFailure("An error occurred while executing the batch query");
        }
    }

    private ApiResponse<PagedResult<TDto>> ValidateRequest<TDto>(DynamoDbQueryRequest request)
        where TDto : class, new()
    {
        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(request.TableName))
        {
            errors.Add(new ValidationError
            {
                Field = nameof(request.TableName),
                Message = "Table name is required",
                ErrorCode = "REQUIRED_FIELD"
            });
        }

        if (request.Pagination.PageSize <= 0)
        {
            errors.Add(new ValidationError
            {
                Field = nameof(request.Pagination.PageSize),
                Message = "Page size must be greater than 0",
                ErrorCode = "INVALID_VALUE"
            });
        }

        if (request.Pagination.PageSize > 1000)
        {
            errors.Add(new ValidationError
            {
                Field = nameof(request.Pagination.PageSize),
                Message = "Page size cannot exceed 1000",
                ErrorCode = "INVALID_VALUE"
            });
        }

        if (errors.Any())
        {
            return ApiResponse<PagedResult<TDto>>.CreateValidationError(errors);
        }

        return ApiResponse<PagedResult<TDto>>.CreateSuccess(PagedResult<TDto>.Empty());
    }

    private bool ShouldUseQuery(DynamoDbQueryRequest request)
    {
        return request.Strategy switch
        {
            QueryStrategy.ForceQuery => true,
            QueryStrategy.ForceScan => false,
            QueryStrategy.AutoDetect or QueryStrategy.CostOptimized => 
                !string.IsNullOrEmpty(request.PartitionKeyValue),
            _ => !string.IsNullOrEmpty(request.PartitionKeyValue)
        };
    }

    private async Task<PagedResult<TDto>> ExecuteQueryAsync<TDto>(
        DynamoDbQueryRequest request,
        CancellationToken cancellationToken)
        where TDto : class, new()
    {
        var queryRequest = new QueryRequest
        {
            TableName = request.TableName,
            Limit = request.Pagination.PageSize,
            ConsistentRead = request.ConsistentRead
        };

        // Build key condition expression
        if (!string.IsNullOrEmpty(request.PartitionKey) && !string.IsNullOrEmpty(request.PartitionKeyValue))
        {
            queryRequest.KeyConditionExpression = "#pk = :pkval";
            queryRequest.ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#pk"] = request.PartitionKey
            };
            queryRequest.ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pkval"] = new AttributeValue { S = request.PartitionKeyValue }
            };
        }

        // Add sort key condition if provided
        if (!string.IsNullOrEmpty(request.SortKey) && !string.IsNullOrEmpty(request.SortKeyValue))
        {
            queryRequest.KeyConditionExpression += " AND #sk = :skval";
            queryRequest.ExpressionAttributeNames["#sk"] = request.SortKey;
            queryRequest.ExpressionAttributeValues[":skval"] = new AttributeValue { S = request.SortKeyValue };
        }

        // Add filter expression for additional filters
        if (request.Filters.Any())
        {
            var (filterExpression, filterNames, filterValues) = BuildFilterExpression(request.Filters);
            queryRequest.FilterExpression = filterExpression;
            
            // Merge with existing attribute names and values
            foreach (var kvp in filterNames)
            {
                queryRequest.ExpressionAttributeNames[kvp.Key] = kvp.Value;
            }
            foreach (var kvp in filterValues)
            {
                queryRequest.ExpressionAttributeValues[kvp.Key] = kvp.Value;
            }
        }

        // Handle pagination
        if (!string.IsNullOrEmpty(request.Pagination.NextToken))
        {
            queryRequest.ExclusiveStartKey = DecodeNextToken(request.Pagination.NextToken);
        }

        // Add index name if specified
        if (!string.IsNullOrEmpty(request.IndexName))
        {
            queryRequest.IndexName = request.IndexName;
        }

        var response = await _dynamoDbClient.QueryAsync(queryRequest, cancellationToken);

        var items = response.Items.Select(MapFromDynamoDb<TDto>).ToList();
        var nextToken = response.LastEvaluatedKey?.Any() == true 
            ? EncodeNextToken(response.LastEvaluatedKey) 
            : null;

        return PagedResult<TDto>.Create(
            items,
            new PaginationMetadata
            {
                PageSize = request.Pagination.PageSize,
                NextPageToken = nextToken,
                HasNextPage = !string.IsNullOrEmpty(nextToken),
                ItemCount = items.Count
            },
            new QueryMetadata
            {
                OperationType = "Query",
                IndexUsed = request.IndexName,
                ItemsExamined = response.ScannedCount,
                ItemsReturned = response.Count,
                ConsumedCapacity = response.ConsumedCapacity?.CapacityUnits,
                CacheStatus = CacheStatus.Miss
            }
        );
    }

    private async Task<PagedResult<TDto>> ExecuteScanAsync<TDto>(
        DynamoDbQueryRequest request,
        CancellationToken cancellationToken)
        where TDto : class, new()
    {
        var scanRequest = new ScanRequest
        {
            TableName = request.TableName,
            Limit = request.Pagination.PageSize,
            ConsistentRead = request.ConsistentRead
        };

        // Add filter expression
        if (request.Filters.Any())
        {
            var (filterExpression, filterNames, filterValues) = BuildFilterExpression(request.Filters);
            scanRequest.FilterExpression = filterExpression;
            scanRequest.ExpressionAttributeNames = filterNames;
            scanRequest.ExpressionAttributeValues = filterValues;
        }

        // Handle pagination
        if (!string.IsNullOrEmpty(request.Pagination.NextToken))
        {
            scanRequest.ExclusiveStartKey = DecodeNextToken(request.Pagination.NextToken);
        }

        // Add index name if specified
        if (!string.IsNullOrEmpty(request.IndexName))
        {
            scanRequest.IndexName = request.IndexName;
        }

        var response = await _dynamoDbClient.ScanAsync(scanRequest, cancellationToken);

        var items = response.Items.Select(MapFromDynamoDb<TDto>).ToList();
        var nextToken = response.LastEvaluatedKey?.Any() == true 
            ? EncodeNextToken(response.LastEvaluatedKey) 
            : null;

        return PagedResult<TDto>.Create(
            items,
            new PaginationMetadata
            {
                PageSize = request.Pagination.PageSize,
                NextPageToken = nextToken,
                HasNextPage = !string.IsNullOrEmpty(nextToken),
                ItemCount = items.Count
            },
            new QueryMetadata
            {
                OperationType = "Scan",
                IndexUsed = request.IndexName,
                ItemsExamined = response.ScannedCount,
                ItemsReturned = response.Count,
                ConsumedCapacity = response.ConsumedCapacity?.CapacityUnits,
                CacheStatus = CacheStatus.Miss,
                Warnings = response.ScannedCount > 1000 
                    ? new List<string> { "Large scan operation detected - consider using Query with partition key" }
                    : new List<string>()
            }
        );
    }

    private (string filterExpression, Dictionary<string, string> attributeNames, Dictionary<string, AttributeValue> attributeValues) 
        BuildFilterExpression(Dictionary<string, FilterValue> filters)
    {
        var expressions = new List<string>();
        var attributeNames = new Dictionary<string, string>();
        var attributeValues = new Dictionary<string, AttributeValue>();
        var valueCounter = 0;

        foreach (var filter in filters)
        {
            var attributeName = $"#attr{valueCounter}";
            var valueName = $":val{valueCounter}";
            
            attributeNames[attributeName] = filter.Key;
            
            var expression = filter.Value.Operator switch
            {
                FilterOperator.Equals => $"{attributeName} = {valueName}",
                FilterOperator.NotEquals => $"{attributeName} <> {valueName}",
                FilterOperator.LessThan => $"{attributeName} < {valueName}",
                FilterOperator.LessThanOrEqual => $"{attributeName} <= {valueName}",
                FilterOperator.GreaterThan => $"{attributeName} > {valueName}",
                FilterOperator.GreaterThanOrEqual => $"{attributeName} >= {valueName}",
                FilterOperator.Between => $"{attributeName} BETWEEN {valueName} AND :val{valueCounter + 1}",
                FilterOperator.Contains => $"contains({attributeName}, {valueName})",
                FilterOperator.BeginsWith => $"begins_with({attributeName}, {valueName})",
                FilterOperator.Exists => $"attribute_exists({attributeName})",
                FilterOperator.NotExists => $"attribute_not_exists({attributeName})",
                _ => $"{attributeName} = {valueName}"
            };

            expressions.Add(expression);
            
            // Add primary value
            if (filter.Value.Operator != FilterOperator.Exists && filter.Value.Operator != FilterOperator.NotExists)
            {
                attributeValues[valueName] = ConvertToAttributeValue(filter.Value.Value);
            }

            // Add additional values for Between operator
            if (filter.Value.Operator == FilterOperator.Between && filter.Value.AdditionalValues.Any())
            {
                attributeValues[$":val{valueCounter + 1}"] = ConvertToAttributeValue(filter.Value.AdditionalValues.First());
                valueCounter++;
            }

            valueCounter++;
        }

        return (string.Join(" AND ", expressions), attributeNames, attributeValues);
    }

    private AttributeValue ConvertToAttributeValue(object value)
    {
        return value switch
        {
            string s => new AttributeValue { S = s },
            int i => new AttributeValue { N = i.ToString() },
            long l => new AttributeValue { N = l.ToString() },
            decimal d => new AttributeValue { N = d.ToString() },
            double d => new AttributeValue { N = d.ToString() },
            float f => new AttributeValue { N = f.ToString() },
            bool b => new AttributeValue { BOOL = b },
            DateTime dt => new AttributeValue { S = dt.ToString("O") },
            Guid g => new AttributeValue { S = g.ToString() },
            _ => new AttributeValue { S = value.ToString() ?? string.Empty }
        };
    }

    private TDto MapFromDynamoDb<TDto>(Dictionary<string, AttributeValue> item)
        where TDto : class, new()
    {
        var dto = new TDto();
        var properties = typeof(TDto).GetProperties();

        foreach (var property in properties)
        {
            if (item.TryGetValue(property.Name, out var attributeValue))
            {
                var value = ConvertFromAttributeValue(attributeValue, property.PropertyType);
                if (value != null)
                {
                    property.SetValue(dto, value);
                }
            }
        }

        return dto;
    }

    private object? ConvertFromAttributeValue(AttributeValue attributeValue, Type targetType)
    {
        if (attributeValue.NULL)
            return null;

        if (!string.IsNullOrEmpty(attributeValue.S))
        {
            if (targetType == typeof(string))
                return attributeValue.S;
            
            if (targetType == typeof(DateTime) || targetType == typeof(DateTime?))
                return DateTime.TryParse(attributeValue.S, out var dt) ? dt : null;
            
            if (targetType == typeof(Guid) || targetType == typeof(Guid?))
                return Guid.TryParse(attributeValue.S, out var g) ? g : null;
        }

        if (!string.IsNullOrEmpty(attributeValue.N))
        {
            if (targetType == typeof(int) || targetType == typeof(int?))
                return int.TryParse(attributeValue.N, out var i) ? i : null;
            
            if (targetType == typeof(long) || targetType == typeof(long?))
                return long.TryParse(attributeValue.N, out var l) ? l : null;
            
            if (targetType == typeof(decimal) || targetType == typeof(decimal?))
                return decimal.TryParse(attributeValue.N, out var d) ? d : null;
            
            if (targetType == typeof(double) || targetType == typeof(double?))
                return double.TryParse(attributeValue.N, out var db) ? db : null;
        }

        if (attributeValue.IsBOOLSet)
        {
            if (targetType == typeof(bool) || targetType == typeof(bool?))
                return attributeValue.BOOL;
        }

        return null;
    }

    private string EncodeNextToken(Dictionary<string, AttributeValue> lastEvaluatedKey)
    {
        var json = JsonSerializer.Serialize(lastEvaluatedKey);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(bytes);
    }

    private Dictionary<string, AttributeValue> DecodeNextToken(string nextToken)
    {
        try
        {
            var bytes = Convert.FromBase64String(nextToken);
            var json = System.Text.Encoding.UTF8.GetString(bytes);
            return JsonSerializer.Deserialize<Dictionary<string, AttributeValue>>(json) 
                   ?? new Dictionary<string, AttributeValue>();
        }
        catch
        {
            return new Dictionary<string, AttributeValue>();
        }
    }
} 