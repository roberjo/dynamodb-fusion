using DynamoDbFusion.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace DynamoDbFusion.Core.Extensions;

/// <summary>
/// Extension methods for converting HTTP query strings to DynamoDB requests
/// </summary>
public static class QueryStringExtensions
{
    private static readonly HashSet<string> ReservedParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "pageSize", "nextToken", "page", "limit", "offset"
    };

    /// <summary>
    /// Converts IQueryCollection to DynamoDB filters
    /// </summary>
    /// <param name="query">HTTP query collection</param>
    /// <returns>Dictionary of filters</returns>
    public static Dictionary<string, FilterValue> ToDynamoFilters(this IQueryCollection query)
    {
        var filters = new Dictionary<string, FilterValue>();

        foreach (var kvp in query)
        {
            if (IsReservedParameter(kvp.Key))
                continue;

            var (fieldName, operatorName) = ParseFieldName(kvp.Key);
            var filterOperator = ParseOperator(operatorName);
            var values = kvp.Value.Where(v => !string.IsNullOrEmpty(v)).ToList();

            if (values.Count == 0)
                continue;

            filters[fieldName] = CreateFilterValue(filterOperator, values);
        }

        return filters;
    }

    /// <summary>
    /// Converts IQueryCollection to pagination request
    /// </summary>
    /// <param name="query">HTTP query collection</param>
    /// <returns>Pagination request</returns>
    public static PaginationRequest ToPaginationRequest(this IQueryCollection query)
    {
        var pagination = new PaginationRequest();

        if (query.TryGetValue("pageSize", out var pageSize) && int.TryParse(pageSize, out var size))
        {
            pagination.PageSize = Math.Max(1, size); // Ensure positive page size
        }

        if (query.TryGetValue("nextToken", out var token) && !StringValues.IsNullOrEmpty(token))
        {
            pagination.NextToken = token.ToString();
        }

        return pagination;
    }

    /// <summary>
    /// Converts IQueryCollection to a complete DynamoDB query request
    /// </summary>
    /// <param name="query">HTTP query collection</param>
    /// <param name="tableName">DynamoDB table name</param>
    /// <returns>Complete DynamoDB query request</returns>
    public static DynamoDbQueryRequest ToDynamoDbQueryRequest(this IQueryCollection query, string tableName)
    {
        return new DynamoDbQueryRequest
        {
            TableName = tableName,
            Filters = query.ToDynamoFilters(),
            Pagination = query.ToPaginationRequest()
        };
    }

    /// <summary>
    /// Converts IQueryCollection to a DynamoDB query request with partition key
    /// </summary>
    /// <param name="query">HTTP query collection</param>
    /// <param name="tableName">DynamoDB table name</param>
    /// <param name="partitionKey">Partition key attribute name</param>
    /// <param name="partitionKeyValue">Partition key value</param>
    /// <returns>DynamoDB query request with partition key</returns>
    public static DynamoDbQueryRequest ToDynamoDbQueryRequest(
        this IQueryCollection query, 
        string tableName, 
        string partitionKey, 
        string partitionKeyValue)
    {
        var request = query.ToDynamoDbQueryRequest(tableName);
        request.PartitionKey = partitionKey;
        request.PartitionKeyValue = partitionKeyValue;
        return request;
    }

    private static bool IsReservedParameter(string parameterName)
    {
        return ReservedParameters.Contains(parameterName);
    }

    private static (string fieldName, string? operatorName) ParseFieldName(string queryKey)
    {
        var parts = queryKey.Split("__", 2, StringSplitOptions.RemoveEmptyEntries);
        
        if (parts.Length == 2)
        {
            return (parts[0], parts[1]);
        }

        return (queryKey, null);
    }

    private static FilterOperator ParseOperator(string? operatorName)
    {
        return operatorName?.ToLowerInvariant() switch
        {
            null or "eq" => FilterOperator.Equals,
            "ne" => FilterOperator.NotEquals,
            "lt" => FilterOperator.LessThan,
            "lte" => FilterOperator.LessThanOrEqual,
            "gt" => FilterOperator.GreaterThan,
            "gte" => FilterOperator.GreaterThanOrEqual,
            "between" => FilterOperator.Between,
            "in" => FilterOperator.In,
            "not_in" or "notin" => FilterOperator.NotIn,
            "contains" => FilterOperator.Contains,
            "begins_with" or "beginswith" => FilterOperator.BeginsWith,
            "exists" => FilterOperator.Exists,
            "not_exists" or "notexists" => FilterOperator.NotExists,
            _ => FilterOperator.Equals
        };
    }

    private static FilterValue CreateFilterValue(FilterOperator filterOperator, List<string> values)
    {
        var primaryValue = values.First();

        return filterOperator switch
        {
            FilterOperator.Between when values.Count >= 2 => new FilterValue
            {
                Value = ConvertValue(primaryValue),
                Operator = filterOperator,
                AdditionalValues = new List<object> { ConvertValue(values[1]) }
            },
            FilterOperator.In or FilterOperator.NotIn => new FilterValue
            {
                Value = ConvertValue(primaryValue),
                Operator = filterOperator,
                AdditionalValues = values.Skip(1).Select(ConvertValue).ToList()
            },
            FilterOperator.Exists or FilterOperator.NotExists => new FilterValue
            {
                Value = ConvertBooleanValue(primaryValue),
                Operator = filterOperator
            },
            _ => new FilterValue
            {
                Value = ConvertValue(primaryValue),
                Operator = filterOperator
            }
        };
    }

    private static object ConvertValue(string value)
    {
        // Try to convert to appropriate types
        if (bool.TryParse(value, out var boolValue))
            return boolValue;

        if (int.TryParse(value, out var intValue))
            return intValue;

        if (long.TryParse(value, out var longValue))
            return longValue;

        if (decimal.TryParse(value, out var decimalValue))
            return decimalValue;

        if (double.TryParse(value, out var doubleValue))
            return doubleValue;

        if (DateTime.TryParse(value, out var dateValue))
            return dateValue;

        if (Guid.TryParse(value, out var guidValue))
            return guidValue;

        // Return as string if no conversion possible
        return value;
    }

    private static object ConvertBooleanValue(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "on" => true,
            "false" or "0" or "no" or "off" => false,
            _ => bool.TryParse(value, out var result) ? result : true
        };
    }
}

/// <summary>
/// Extension methods for working with query string values
/// </summary>
public static class QueryValueExtensions
{
    /// <summary>
    /// Safely gets a string value from query parameters
    /// </summary>
    /// <param name="query">Query collection</param>
    /// <param name="key">Parameter key</param>
    /// <param name="defaultValue">Default value if not found</param>
    /// <returns>String value or default</returns>
    public static string GetString(this IQueryCollection query, string key, string defaultValue = "")
    {
        return query.TryGetValue(key, out var value) && !StringValues.IsNullOrEmpty(value)
            ? value.ToString()
            : defaultValue;
    }

    /// <summary>
    /// Safely gets an integer value from query parameters
    /// </summary>
    /// <param name="query">Query collection</param>
    /// <param name="key">Parameter key</param>
    /// <param name="defaultValue">Default value if not found or invalid</param>
    /// <returns>Integer value or default</returns>
    public static int GetInt(this IQueryCollection query, string key, int defaultValue = 0)
    {
        return query.TryGetValue(key, out var value) && int.TryParse(value, out var result)
            ? result
            : defaultValue;
    }

    /// <summary>
    /// Safely gets a boolean value from query parameters
    /// </summary>
    /// <param name="query">Query collection</param>
    /// <param name="key">Parameter key</param>
    /// <param name="defaultValue">Default value if not found or invalid</param>
    /// <returns>Boolean value or default</returns>
    public static bool GetBool(this IQueryCollection query, string key, bool defaultValue = false)
    {
        if (!query.TryGetValue(key, out var value) || StringValues.IsNullOrEmpty(value))
            return defaultValue;

        var stringValue = value.ToString().ToLowerInvariant();
        return stringValue switch
        {
            "true" or "1" or "yes" or "on" => true,
            "false" or "0" or "no" or "off" => false,
            _ => bool.TryParse(stringValue, out var result) ? result : defaultValue
        };
    }

    /// <summary>
    /// Gets multiple values for a parameter as a list
    /// </summary>
    /// <param name="query">Query collection</param>
    /// <param name="key">Parameter key</param>
    /// <returns>List of values</returns>
    public static List<string> GetList(this IQueryCollection query, string key)
    {
        if (!query.TryGetValue(key, out var values))
            return new List<string>();

        return values.Where(v => !string.IsNullOrEmpty(v)).ToList()!;
    }
} 