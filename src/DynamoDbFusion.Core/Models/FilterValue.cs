namespace DynamoDbFusion.Core.Models;

/// <summary>
/// Represents a filter condition with operator and value
/// </summary>
public class FilterValue
{
    /// <summary>
    /// The filter value
    /// </summary>
    public object Value { get; set; } = null!;

    /// <summary>
    /// The filter operator
    /// </summary>
    public FilterOperator Operator { get; set; } = FilterOperator.Equals;

    /// <summary>
    /// Additional values for operators like Between, In
    /// </summary>
    public List<object> AdditionalValues { get; set; } = new();

    /// <summary>
    /// Creates a simple equals filter
    /// </summary>
    /// <param name="value">The value to filter by</param>
    /// <returns>FilterValue with Equals operator</returns>
    public static FilterValue Equal(object value) => new()
    {
        Value = value,
        Operator = FilterOperator.Equals
    };

    /// <summary>
    /// Creates a contains filter
    /// </summary>
    /// <param name="value">The value that should be contained</param>
    /// <returns>FilterValue with Contains operator</returns>
    public static FilterValue Contains(object value) => new()
    {
        Value = value,
        Operator = FilterOperator.Contains
    };

    /// <summary>
    /// Creates a between filter
    /// </summary>
    /// <param name="minValue">The minimum value</param>
    /// <param name="maxValue">The maximum value</param>
    /// <returns>FilterValue with Between operator</returns>
    public static FilterValue Between(object minValue, object maxValue) => new()
    {
        Value = minValue,
        Operator = FilterOperator.Between,
        AdditionalValues = new List<object> { maxValue }
    };

    /// <summary>
    /// Creates an "in" filter for multiple values
    /// </summary>
    /// <param name="values">The values to match against</param>
    /// <returns>FilterValue with In operator</returns>
    public static FilterValue In(params object[] values) => new()
    {
        Value = values.FirstOrDefault() ?? string.Empty,
        Operator = FilterOperator.In,
        AdditionalValues = values.Skip(1).ToList()
    };

    /// <summary>
    /// Creates a greater than or equal filter
    /// </summary>
    /// <param name="value">The minimum value</param>
    /// <returns>FilterValue with GreaterThanOrEqual operator</returns>
    public static FilterValue GreaterThanOrEqual(object value) => new()
    {
        Value = value,
        Operator = FilterOperator.GreaterThanOrEqual
    };

    /// <summary>
    /// Creates a less than or equal filter
    /// </summary>
    /// <param name="value">The maximum value</param>
    /// <returns>FilterValue with LessThanOrEqual operator</returns>
    public static FilterValue LessThanOrEqual(object value) => new()
    {
        Value = value,
        Operator = FilterOperator.LessThanOrEqual
    };
}

/// <summary>
/// Available filter operators for DynamoDB queries
/// </summary>
public enum FilterOperator
{
    /// <summary>
    /// Equals comparison (=)
    /// </summary>
    Equals,

    /// <summary>
    /// Not equals comparison (<>)
    /// </summary>
    NotEquals,

    /// <summary>
    /// Less than comparison (<)
    /// </summary>
    LessThan,

    /// <summary>
    /// Less than or equal comparison (<=)
    /// </summary>
    LessThanOrEqual,

    /// <summary>
    /// Greater than comparison (>)
    /// </summary>
    GreaterThan,

    /// <summary>
    /// Greater than or equal comparison (>=)
    /// </summary>
    GreaterThanOrEqual,

    /// <summary>
    /// Between two values (BETWEEN)
    /// </summary>
    Between,

    /// <summary>
    /// Value is in a list of values (IN)
    /// </summary>
    In,

    /// <summary>
    /// Value is not in a list of values (NOT IN)
    /// </summary>
    NotIn,

    /// <summary>
    /// String contains substring (contains)
    /// </summary>
    Contains,

    /// <summary>
    /// String begins with prefix (begins_with)
    /// </summary>
    BeginsWith,

    /// <summary>
    /// Attribute exists (attribute_exists)
    /// </summary>
    Exists,

    /// <summary>
    /// Attribute does not exist (attribute_not_exists)
    /// </summary>
    NotExists
} 