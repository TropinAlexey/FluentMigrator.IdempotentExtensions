namespace FluentMigrator.IdempotentExtensions;

using System;
using System.Collections.Generic;

/// <summary>
/// Describes a related table used by <see cref="IdempotentExtensions.UpdateDataIfRelatedExists(Migration, string, IReadOnlyDictionary{string, object?}, RelatedRows, IReadOnlyDictionary{string, object?}?, IReadOnlyCollection{string}?, string?)"/>
/// and its siblings to correlate rows through a portable <c>EXISTS</c> subquery.
/// </summary>
/// <remarks>
/// The related table is matched with full qualified names (<c>schema.table.column</c>) and no aliases.
/// This form works on SQL Server, PostgreSQL, MySQL/MariaDB, SQLite and Oracle without per-provider branching.
/// Self-references (related table is the same as the target table) are not supported because MySQL rejects
/// referencing the modified table in a subquery (error 1093).
/// </remarks>
public sealed class RelatedRows
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyWhere =
        new Dictionary<string, object?>();

    private static readonly IReadOnlyCollection<string> EmptyNotNullColumns =
        Array.Empty<string>();

    /// <summary>
    /// Creates a description of the related rows to match.
    /// </summary>
    /// <param name="tableName">Related table name.</param>
    /// <param name="joinColumns">Mapping from a column in the related table to the matching column in the target table. Must contain at least one pair.</param>
    /// <param name="schemaName">Schema of the related table. If <c>null</c>, the target migration's default schema is used.</param>
    /// <exception cref="ArgumentException"><paramref name="tableName"/> is empty, or <paramref name="joinColumns"/> is empty.</exception>
    public RelatedRows(string tableName, IReadOnlyDictionary<string, string> joinColumns, string? schemaName = null)
    {
        if (string.IsNullOrEmpty(tableName))
            throw new ArgumentException("Related table name is required.", nameof(tableName));
        if (joinColumns.Count == 0)
            throw new ArgumentException("At least one join column pair is required.", nameof(joinColumns));

        TableName = tableName;
        JoinColumns = joinColumns;
        SchemaName = schemaName;
    }

    /// <summary>
    /// Related table name.
    /// </summary>
    public string TableName { get; }

    /// <summary>
    /// Schema of the related table. If <c>null</c>, the target migration's default schema is used.
    /// </summary>
    public string? SchemaName { get; }

    /// <summary>
    /// Mapping from a column in the related table to the matching column in the target table.
    /// Each pair generates <c>&lt;related&gt;.&lt;relatedColumn&gt; = &lt;target&gt;.&lt;targetColumn&gt;</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> JoinColumns { get; }

    /// <summary>
    /// Optional equality / <c>IS NULL</c> filters on the related table, using the same value formatting
    /// as <see cref="IdempotentExtensions.UpdateDataIfExists(Migration, string, IReadOnlyDictionary{string, object?}, IReadOnlyDictionary{string, object?}, string?)"/>.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Where
    {
        get;
        init => field = value ?? EmptyWhere;
    } = EmptyWhere;

    /// <summary>
    /// Columns of the related table that must be <c>IS NOT NULL</c>.
    /// </summary>
    public IReadOnlyCollection<string> NotNullColumns
    {
        get;
        init => field = value ?? EmptyNotNullColumns;
    } = EmptyNotNullColumns;
}
