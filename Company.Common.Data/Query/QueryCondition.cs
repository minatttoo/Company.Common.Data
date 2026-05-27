namespace Company.Common.Data.Query;

/// <summary>
/// Represents a single condition in a dynamic query builder.
/// </summary>
internal sealed class QueryCondition
{
    /// <summary>
    /// Initializes a new instance of the <see cref="QueryCondition"/> class.
    /// </summary>
    /// <param name="sql">The SQL fragment.</param>
    /// <param name="parameterNames">The parameter names associated with this condition.</param>
    public QueryCondition(string sql, params string[] parameterNames)
    {
        Sql = sql;
        ParameterNames = parameterNames ?? Array.Empty<string>();
    }

    /// <summary>
    /// Gets the SQL fragment.
    /// </summary>
    public string Sql { get; }

    /// <summary>
    /// Gets the parameter names associated with this condition.
    /// </summary>
    public string[] ParameterNames { get; }
}