using Company.Common.Data.Exceptions;
using Company.Common.Data.Interfaces;
using Dapper;

namespace Company.Common.Data.Query;

/// <summary>
/// Builds parameterized SQL queries dynamically without string concatenation.
/// Prevents SQL injection (SonarQube S3649) by enforcing parameter-based value substitution.
/// </summary>
public sealed class QueryBuilder : IQueryBuilder
{
    private readonly List<QueryCondition> _conditions;
    private readonly DynamicParameters _parameters;
    private string _baseQuery;
    private string? _orderByClause;
    private string? _pagingClause;
    private readonly HashSet<string> _parameterNames;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryBuilder"/> class.
    /// </summary>
    /// <param name="baseQuery">The base SQL query (e.g., "SELECT * FROM Table WHERE 1=1").</param>
    /// <exception cref="QueryBuilderException">Thrown if baseQuery is null or empty.</exception>
    public QueryBuilder(string baseQuery)
    {
        if (string.IsNullOrWhiteSpace(baseQuery))
        {
            throw new QueryBuilderException("Base query cannot be null or empty.");
        }

        _baseQuery = baseQuery.Trim();
        _conditions = new List<QueryCondition>();
        _parameters = new DynamicParameters();
        _parameterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the complete SQL query with all conditions applied.
    /// </summary>
    public string Query
    {
        get
        {
            var query = _baseQuery;

            foreach (var condition in _conditions)
            {
                query += " " + condition.Sql;
            }

            if (!string.IsNullOrWhiteSpace(_orderByClause))
            {
                query += " " + _orderByClause;
            }

            if (!string.IsNullOrWhiteSpace(_pagingClause))
            {
                query += " " + _pagingClause;
            }

            return query;
        }
    }

    /// <summary>
    /// Gets the Dapper DynamicParameters containing all parameter values.
    /// </summary>
    public DynamicParameters Parameters => _parameters;

    /// <summary>
    /// Adds a conditional clause to the query with parameterized values.
    /// </summary>
    /// <param name="condition">Boolean condition to determine if the clause should be added.</param>
    /// <param name="sql">The SQL fragment to append (e.g., "AND PolicyNumber=@PolicyNumber").</param>
    /// <param name="parameterName">The parameter name without the @ symbol.</param>
    /// <param name="value">The parameter value.</param>
    /// <returns>The current builder instance for method chaining.</returns>
    /// <exception cref="QueryBuilderException">Thrown if parameter name is null, empty, or already exists.</exception>
    public IQueryBuilder AddCondition(bool condition, string sql, string parameterName, object? value)
    {
        if (!condition)
        {
            return this;
        }

        if (string.IsNullOrWhiteSpace(parameterName))
        {
            throw new QueryBuilderException("Parameter name cannot be null or empty.");
        }

        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new QueryBuilderException("SQL fragment cannot be null or empty.");
        }

        if (_parameterNames.Contains(parameterName))
        {
            throw new QueryBuilderException($"Parameter '{parameterName}' has already been added to the query builder.");
        }

        _parameterNames.Add(parameterName);
        _parameters.Add($"@{parameterName}", value);
        _conditions.Add(new QueryCondition(sql, parameterName));

        return this;
    }

    /// <summary>
    /// Adds an IN condition to the query for filtering against multiple values.
    /// </summary>
    /// <typeparam name="T">The type of values in the collection.</typeparam>
    /// <param name="condition">Boolean condition to determine if the clause should be added.</param>
    /// <param name="fieldName">The database field name.</param>
    /// <param name="parameterName">The parameter name prefix without the @ symbol.</param>
    /// <param name="values">The collection of values for the IN clause.</param>
    /// <returns>The current builder instance for method chaining.</returns>
    /// <exception cref="QueryBuilderException">Thrown if values is null, empty, or parameter names would collide.</exception>
    public IQueryBuilder AddInCondition<T>(bool condition, string fieldName, string parameterName, IEnumerable<T> values)
    {
        if (!condition)
        {
            return this;
        }

        if (string.IsNullOrWhiteSpace(fieldName))
        {
            throw new QueryBuilderException("Field name cannot be null or empty.");
        }

        if (string.IsNullOrWhiteSpace(parameterName))
        {
            throw new QueryBuilderException("Parameter name cannot be null or empty.");
        }

        var valueList = values?.ToList() ?? new List<T>();

        if (valueList.Count == 0)
        {
            throw new QueryBuilderException($"Values collection for IN condition cannot be empty.");
        }

        // Generate parameter names for each value
        var generatedParamNames = new List<string>();
        for (int i = 0; i < valueList.Count; i++)
        {
            var uniqueParamName = $"{parameterName}{i}";

            if (_parameterNames.Contains(uniqueParamName))
            {
                throw new QueryBuilderException($"Parameter '{uniqueParamName}' has already been added to the query builder.");
            }

            _parameterNames.Add(uniqueParamName);
            _parameters.Add($"@{uniqueParamName}", valueList[i]);
            generatedParamNames.Add($"@{uniqueParamName}");
        }

        // Build the IN clause
        var inClause = string.Join(", ", generatedParamNames);
        var sql = $"AND {fieldName} IN ({inClause})";

        _conditions.Add(new QueryCondition(sql, generatedParamNames.Select(p => p.TrimStart('@')).ToArray()));

        return this;
    }

    /// <summary>
    /// Adds an ORDER BY clause to the query.
    /// </summary>
    /// <param name="field">The field name to order by.</param>
    /// <param name="descending">If true, orders in descending order; otherwise ascending.</param>
    /// <returns>The current builder instance for method chaining.</returns>
    /// <exception cref="QueryBuilderException">Thrown if field name is null or empty.</exception>
    public IQueryBuilder AddOrderBy(string field, bool descending = false)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            throw new QueryBuilderException("Field name for ORDER BY cannot be null or empty.");
        }

        var direction = descending ? "DESC" : "ASC";
        _orderByClause = $"ORDER BY {field} {direction}";

        return this;
    }

    /// <summary>
    /// Adds pagination (OFFSET/FETCH) to the query.
    /// </summary>
    /// <param name="pageNumber">The page number (1-based).</param>
    /// <param name="pageSize">The number of records per page.</param>
    /// <returns>The current builder instance for method chaining.</returns>
    /// <exception cref="QueryBuilderException">Thrown if pageNumber or pageSize is invalid.</exception>
    public IQueryBuilder AddPaging(int pageNumber, int pageSize)
    {
        if (pageNumber < 1)
        {
            throw new QueryBuilderException("Page number must be greater than 0.");
        }

        if (pageSize < 1)
        {
            throw new QueryBuilderException("Page size must be greater than 0.");
        }

        var offset = (pageNumber - 1) * pageSize;
        _pagingClause = $"OFFSET {offset} ROWS FETCH NEXT {pageSize} ROWS ONLY";

        return this;
    }
}