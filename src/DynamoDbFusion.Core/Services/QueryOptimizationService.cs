using DynamoDbFusion.Core.Models;
using DynamoDbFusion.Core.Exceptions;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace DynamoDbFusion.Core.Services;

/// <summary>
/// Service for analyzing and optimizing DynamoDB queries
/// </summary>
public class QueryOptimizationService
{
    private readonly ILogger<QueryOptimizationService> _logger;
    private readonly ConcurrentDictionary<string, TableSchema> _tableSchemas;
    private readonly ConcurrentDictionary<string, QueryPatternAnalysis> _queryPatterns;

    public QueryOptimizationService(ILogger<QueryOptimizationService> logger)
    {
        _logger = logger;
        _tableSchemas = new ConcurrentDictionary<string, TableSchema>();
        _queryPatterns = new ConcurrentDictionary<string, QueryPatternAnalysis>();
    }

    /// <summary>
    /// Optimizes a DynamoDB query request for better performance
    /// </summary>
    /// <param name="request">Original query request</param>
    /// <returns>Optimized query request with recommendations</returns>
    public QueryOptimizationResult OptimizeQuery(DynamoDbQueryRequest request)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            var result = new QueryOptimizationResult
            {
                OriginalRequest = request,
                OptimizedRequest = new DynamoDbQueryRequest(),
                Recommendations = new List<OptimizationRecommendation>(),
                EstimatedCostReduction = 0,
                EstimatedPerformanceGain = 0
            };

            // Copy original request
            CopyRequest(request, result.OptimizedRequest);

            // Analyze and optimize different aspects
            OptimizeQueryStrategy(result);
            OptimizeFilterExpression(result);
            OptimizeProjection(result);
            OptimizePagination(result);
            OptimizeIndexUsage(result);
            
            // Track query patterns for future optimizations
            TrackQueryPattern(request);

            stopwatch.Stop();
            result.OptimizationTimeMs = stopwatch.ElapsedMilliseconds;

            _logger.LogDebug("Query optimization completed for table {TableName} in {Duration}ms with {RecommendationCount} recommendations",
                request.TableName, stopwatch.ElapsedMilliseconds, result.Recommendations.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error optimizing query for table {TableName}", request.TableName);
            throw new QueryOptimizationException(request.TableName, "general", "Failed to optimize query: " + ex.Message);
        }
    }

    /// <summary>
    /// Analyzes query patterns and provides recommendations
    /// </summary>
    /// <param name="tableName">Table name to analyze</param>
    /// <returns>Query pattern analysis results</returns>
    public QueryPatternAnalysis AnalyzeQueryPatterns(string tableName)
    {
        if (_queryPatterns.TryGetValue(tableName, out var analysis))
        {
            return analysis;
        }

        return new QueryPatternAnalysis
        {
            TableName = tableName,
            TotalQueries = 0,
            CommonFilters = new Dictionary<string, int>(),
            RecommendedIndices = new List<IndexRecommendation>(),
            PerformanceIssues = new List<string>()
        };
    }

    /// <summary>
    /// Registers table schema information for optimization
    /// </summary>
    /// <param name="schema">Table schema</param>
    public void RegisterTableSchema(TableSchema schema)
    {
        _tableSchemas.AddOrUpdate(schema.TableName, schema, (key, existing) => schema);
        _logger.LogDebug("Registered schema for table {TableName} with {AttributeCount} attributes", 
            schema.TableName, schema.Attributes.Count);
    }

    private void OptimizeQueryStrategy(QueryOptimizationResult result)
    {
        var request = result.OptimizedRequest;

        // Force Query over Scan when partition key is available
        if (!string.IsNullOrEmpty(request.PartitionKeyValue) && request.Strategy == QueryStrategy.AutoDetect)
        {
            request.Strategy = QueryStrategy.ForceQuery;
            result.Recommendations.Add(new OptimizationRecommendation
            {
                Type = OptimizationType.QueryStrategy,
                Message = "Using Query operation instead of Scan for better performance",
                Impact = "High",
                EstimatedCostReduction = 70
            });
            result.EstimatedCostReduction += 70;
            result.EstimatedPerformanceGain += 80;
        }

        // Warn about inefficient scans
        if (string.IsNullOrEmpty(request.PartitionKeyValue) && request.Filters.Count > 0)
        {
            result.Recommendations.Add(new OptimizationRecommendation
            {
                Type = OptimizationType.QueryStrategy,
                Message = "Consider using a Global Secondary Index (GSI) to avoid full table scan",
                Impact = "High",
                EstimatedCostReduction = 0 // This requires schema changes
            });
        }
    }

    private void OptimizeFilterExpression(QueryOptimizationResult result)
    {
        var filters = result.OptimizedRequest.Filters.ToList();
        
        // Reorder filters by selectivity (most selective first)
        var reorderedFilters = filters
            .OrderBy(f => GetFilterSelectivity(f.Value))
            .ToList();

        if (!filters.SequenceEqual(reorderedFilters))
        {
            result.OptimizedRequest.Filters.Clear();
            foreach (var filter in reorderedFilters)
            {
                result.OptimizedRequest.Filters[filter.Key] = filter.Value;
            }

            result.Recommendations.Add(new OptimizationRecommendation
            {
                Type = OptimizationType.FilterOptimization,
                Message = "Reordered filters by selectivity for better performance",
                Impact = "Medium",
                EstimatedCostReduction = 10
            });
            result.EstimatedPerformanceGain += 15;
        }

        // Suggest using begins_with instead of contains for strings when possible
        foreach (var filter in filters.Where(f => f.Value.Operator == FilterOperator.Contains))
        {
            if (filter.Value.Value is string stringValue && stringValue.EndsWith("%"))
            {
                result.Recommendations.Add(new OptimizationRecommendation
                {
                    Type = OptimizationType.FilterOptimization,
                    Message = $"Consider using 'begins_with' instead of 'contains' for filter on '{filter.Key}' for better performance",
                    Impact = "Medium",
                    EstimatedCostReduction = 5
                });
            }
        }
    }

    private void OptimizeProjection(QueryOptimizationResult result)
    {
        var request = result.OptimizedRequest;

        // Suggest projection if no attributes are specified
        if (request.ProjectionAttributes.Count == 0 && request.Filters.Count > 0)
        {
            // Suggest projecting only the keys and filtered attributes
            var suggestedAttributes = new List<string>();
            
            if (!string.IsNullOrEmpty(request.PartitionKey))
                suggestedAttributes.Add(request.PartitionKey);
            
            if (!string.IsNullOrEmpty(request.SortKey))
                suggestedAttributes.Add(request.SortKey);
            
            suggestedAttributes.AddRange(request.Filters.Keys);

            if (suggestedAttributes.Count < 10) // Only suggest if reasonable number of attributes
            {
                result.Recommendations.Add(new OptimizationRecommendation
                {
                    Type = OptimizationType.Projection,
                    Message = $"Consider projecting only needed attributes: {string.Join(", ", suggestedAttributes)}",
                    Impact = "Medium",
                    EstimatedCostReduction = 20,
                    SuggestedAttributes = suggestedAttributes
                });
            }
        }
    }

    private void OptimizePagination(QueryOptimizationResult result)
    {
        var request = result.OptimizedRequest;

        // Suggest optimal page size
        if (request.Pagination.PageSize > 1000)
        {
            request.Pagination.PageSize = 1000;
            result.Recommendations.Add(new OptimizationRecommendation
            {
                Type = OptimizationType.Pagination,
                Message = "Reduced page size to 1000 (DynamoDB maximum) for better performance",
                Impact = "Low",
                EstimatedCostReduction = 0
            });
        }
        else if (request.Pagination.PageSize < 10)
        {
            result.Recommendations.Add(new OptimizationRecommendation
            {
                Type = OptimizationType.Pagination,
                Message = "Consider increasing page size to reduce round trips",
                Impact = "Low",
                EstimatedCostReduction = 0
            });
        }
    }

    private void OptimizeIndexUsage(QueryOptimizationResult result)
    {
        var request = result.OptimizedRequest;
        
        if (!_tableSchemas.TryGetValue(request.TableName, out var schema))
        {
            return; // Can't optimize without schema information
        }

        // Check if current query could benefit from an existing GSI
        foreach (var gsi in schema.GlobalSecondaryIndexes)
        {
            if (string.IsNullOrEmpty(request.IndexName) && 
                request.Filters.ContainsKey(gsi.PartitionKey))
            {
                request.IndexName = gsi.IndexName;
                request.PartitionKey = gsi.PartitionKey;
                request.PartitionKeyValue = request.Filters[gsi.PartitionKey].Value?.ToString();

                result.Recommendations.Add(new OptimizationRecommendation
                {
                    Type = OptimizationType.IndexUsage,
                    Message = $"Using GSI '{gsi.IndexName}' for better query performance",
                    Impact = "High",
                    EstimatedCostReduction = 60
                });
                result.EstimatedCostReduction += 60;
                result.EstimatedPerformanceGain += 70;
                break;
            }
        }
    }

    private void TrackQueryPattern(DynamoDbQueryRequest request)
    {
        var pattern = _queryPatterns.AddOrUpdate(request.TableName,
            new QueryPatternAnalysis
            {
                TableName = request.TableName,
                TotalQueries = 1,
                CommonFilters = request.Filters.Keys.ToDictionary(k => k, _ => 1),
                LastAnalyzed = DateTime.UtcNow
            },
            (key, existing) =>
            {
                existing.TotalQueries++;
                foreach (var filterKey in request.Filters.Keys)
                {
                    existing.CommonFilters.TryGetValue(filterKey, out var count);
                    existing.CommonFilters[filterKey] = count + 1;
                }
                existing.LastAnalyzed = DateTime.UtcNow;
                return existing;
            });

        // Generate index recommendations if we have enough data
        if (pattern.TotalQueries > 100)
        {
            GenerateIndexRecommendations(pattern);
        }
    }

    private void GenerateIndexRecommendations(QueryPatternAnalysis pattern)
    {
        var topFilters = pattern.CommonFilters
            .Where(f => f.Value > pattern.TotalQueries * 0.1) // Used in >10% of queries
            .OrderByDescending(f => f.Value)
            .Take(5)
            .ToList();

        foreach (var filter in topFilters)
        {
            if (!pattern.RecommendedIndices.Any(i => i.PartitionKey == filter.Key))
            {
                pattern.RecommendedIndices.Add(new IndexRecommendation
                {
                    IndexName = $"GSI-{filter.Key}",
                    PartitionKey = filter.Key,
                    Usage = filter.Value,
                    EstimatedCostReduction = CalculateCostReduction(filter.Value, pattern.TotalQueries)
                });
            }
        }
    }

    private static int GetFilterSelectivity(FilterValue filter)
    {
        // Return a selectivity score (lower = more selective)
        return filter.Operator switch
        {
            FilterOperator.Equals => 1,
            FilterOperator.BeginsWith => 2,
            FilterOperator.Between => 3,
            FilterOperator.GreaterThan => 4,
            FilterOperator.GreaterThanOrEqual => 4,
            FilterOperator.LessThan => 4,
            FilterOperator.LessThanOrEqual => 4,
            FilterOperator.Contains => 5,
            FilterOperator.NotEquals => 6,
            FilterOperator.In => filter.AdditionalValues?.Count ?? 3,
            _ => 7
        };
    }

    private static decimal CalculateCostReduction(int usage, long totalQueries)
    {
        var usagePercentage = (decimal)usage / totalQueries;
        return usagePercentage * 80; // Up to 80% cost reduction for frequently used filters
    }

    private static void CopyRequest(DynamoDbQueryRequest source, DynamoDbQueryRequest destination)
    {
        destination.TableName = source.TableName;
        destination.PartitionKey = source.PartitionKey;
        destination.PartitionKeyValue = source.PartitionKeyValue;
        destination.SortKey = source.SortKey;
        destination.SortKeyValue = source.SortKeyValue;
        destination.IndexName = source.IndexName;
        destination.ConsistentRead = source.ConsistentRead;
        destination.Strategy = source.Strategy;
        
        destination.Filters = new Dictionary<string, FilterValue>(source.Filters);
        destination.ProjectionAttributes = new List<string>(source.ProjectionAttributes);
        destination.OrderBy = new Dictionary<string, SortDirection>(source.OrderBy);
        
        destination.Pagination = new PaginationRequest
        {
            PageSize = source.Pagination.PageSize,
            NextToken = source.Pagination.NextToken
        };
    }
}

/// <summary>
/// Query optimization result
/// </summary>
public class QueryOptimizationResult
{
    public DynamoDbQueryRequest OriginalRequest { get; set; } = new();
    public DynamoDbQueryRequest OptimizedRequest { get; set; } = new();
    public List<OptimizationRecommendation> Recommendations { get; set; } = new();
    public decimal EstimatedCostReduction { get; set; }
    public decimal EstimatedPerformanceGain { get; set; }
    public long OptimizationTimeMs { get; set; }
}

/// <summary>
/// Optimization recommendation
/// </summary>
public class OptimizationRecommendation
{
    public OptimizationType Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Impact { get; set; } = string.Empty; // Low, Medium, High
    public decimal EstimatedCostReduction { get; set; }
    public List<string>? SuggestedAttributes { get; set; }
}

/// <summary>
/// Types of optimizations
/// </summary>
public enum OptimizationType
{
    QueryStrategy,
    FilterOptimization,
    Projection,
    Pagination,
    IndexUsage,
    Caching
}

/// <summary>
/// Query pattern analysis
/// </summary>
public class QueryPatternAnalysis
{
    public string TableName { get; set; } = string.Empty;
    public long TotalQueries { get; set; }
    public Dictionary<string, int> CommonFilters { get; set; } = new();
    public List<IndexRecommendation> RecommendedIndices { get; set; } = new();
    public List<string> PerformanceIssues { get; set; } = new();
    public DateTime LastAnalyzed { get; set; }
}

/// <summary>
/// Index recommendation
/// </summary>
public class IndexRecommendation
{
    public string IndexName { get; set; } = string.Empty;
    public string PartitionKey { get; set; } = string.Empty;
    public string? SortKey { get; set; }
    public int Usage { get; set; }
    public decimal EstimatedCostReduction { get; set; }
}

/// <summary>
/// Table schema information
/// </summary>
public class TableSchema
{
    public string TableName { get; set; } = string.Empty;
    public List<AttributeDefinition> Attributes { get; set; } = new();
    public List<GlobalSecondaryIndexDefinition> GlobalSecondaryIndexes { get; set; } = new();
    public List<LocalSecondaryIndexDefinition> LocalSecondaryIndexes { get; set; } = new();
}

/// <summary>
/// Attribute definition
/// </summary>
public class AttributeDefinition
{
    public string AttributeName { get; set; } = string.Empty;
    public string AttributeType { get; set; } = string.Empty; // S, N, B
}

/// <summary>
/// GSI definition
/// </summary>
public class GlobalSecondaryIndexDefinition
{
    public string IndexName { get; set; } = string.Empty;
    public string PartitionKey { get; set; } = string.Empty;
    public string? SortKey { get; set; }
}

/// <summary>
/// LSI definition
/// </summary>
public class LocalSecondaryIndexDefinition
{
    public string IndexName { get; set; } = string.Empty;
    public string SortKey { get; set; } = string.Empty;
} 