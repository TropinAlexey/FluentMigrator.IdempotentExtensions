namespace FluentMigrator.IdempotentExtensions;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FluentMigrator.Builders.Alter.Table;
using FluentMigrator.Builders.Create.Index;
using FluentMigrator.Builders.Create.Sequence;
using FluentMigrator.Builders.Create.Table;
using FluentMigrator.Builders.Delete.Constraint;
using FluentMigrator.Builders.Delete.Index;
using FluentMigrator.Infrastructure;
using System.Reflection;

/// <summary>
/// Internal plumbing: provider detection, SQL formatting.
/// </summary>
public static partial class IdempotentExtensions
{
    private static string EscapeLiteral(string value) => value.Replace("'", "''");

    /// <summary>
    /// Escapes a value for embedding inside a single-quoted SQL string literal.
    /// On MySQL/MariaDB the backslash is additionally doubled: it is an escape character
    /// inside string literals there (unless NO_BACKSLASH_ESCAPES), so quote-doubling alone
    /// is not enough — a value like <c>\'; DROP TABLE t; --</c> would otherwise break out
    /// of the literal. Other providers treat backslash literally.
    /// </summary>
    private static string EscapeLiteral(string value, DatabaseProvider provider)
        => provider is DatabaseProvider.MySql or DatabaseProvider.MariaDb
            ? value.Replace("\\", "\\\\").Replace("'", "''")
            : EscapeLiteral(value);

    private static string EscapeBracket(string value) => value.Replace("]", "]]" );

    private static string EscapeDoubleQuote(string value) => value.Replace("\"", "\"\"");

    /// <summary>
    /// Quotes a single identifier for the current provider: <c>[x]</c> on SQL Server
    /// (with <c>]</c> escaped as <c>]]</c>), backticks on MySQL/MariaDB, double quotes
    /// everywhere else (PostgreSQL, SQLite, Oracle).
    /// </summary>
    /// <remarks>
    /// Quoted identifiers are case-sensitive, while FluentMigrator itself emits them unquoted —
    /// so on engines that fold unquoted names the value is folded first to resolve exactly like
    /// the old unquoted SQL did: <c>UPPER</c> on Oracle (which stores everything uppercase),
    /// <c>lower</c> on PostgreSQL (which stores everything lowercase). SQL Server, MySQL and
    /// SQLite compare case-insensitively (or store as-given on both sides), so no folding there.
    /// </remarks>
    private static string QuoteIdent(DatabaseProvider provider, string name)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer => $"[{EscapeBracket(name)}]",
            DatabaseProvider.MySql or DatabaseProvider.MariaDb => $"`{name.Replace("`", "``")}`",
            DatabaseProvider.Oracle => $"\"{EscapeDoubleQuote(name.ToUpperInvariant())}\"",
            DatabaseProvider.Postgres => $"\"{EscapeDoubleQuote(name.ToLowerInvariant())}\"",
            _ => $"\"{EscapeDoubleQuote(name)}\"",
        };
    }

    private static string QualifyTable(DatabaseProvider provider, string schemaName, string tableName)
        => string.IsNullOrEmpty(schemaName)
            ? QuoteIdent(provider, tableName)
            : $"{QuoteIdent(provider, schemaName)}.{QuoteIdent(provider, tableName)}";

    private static string BuildSetClause(DatabaseProvider provider, IReadOnlyDictionary<string, object?> setValues)
        => string.Join(", ", setValues.Select(kv => $"{QuoteIdent(provider, kv.Key)} = {FormatSqlValue(kv.Value, provider)}"));

    private static string FormatSqlPredicate(DatabaseProvider provider, string column, object? value, string? tableQualifier = null)
    {
        var qualifiedColumn = tableQualifier is null
            ? QuoteIdent(provider, column)
            : $"{tableQualifier}.{QuoteIdent(provider, column)}";
        return value is null
            ? $"{qualifiedColumn} IS NULL"
            : $"{qualifiedColumn} = {FormatSqlValue(value, provider)}";
    }

    /// <summary>
    /// Formats a value as a SQL literal. Only whitelisted CLR types are supported — anything else
    /// throws <see cref="NotSupportedException"/> instead of silently embedding
    /// <c>ToString()</c> output (which would allow arbitrary text, e.g. from a custom type,
    /// to flow unquoted into SQL). Non-finite floats (<see cref="float.NaN"/>, infinities) have
    /// no SQL literal representation and throw <see cref="ArgumentException"/> immediately
    /// instead of emitting invalid SQL.
    /// </summary>
    private static string FormatSqlValue(object? value, DatabaseProvider provider)
    {
        switch (value)
        {
            case null:
                return "NULL";
            case string s:
                return $"'{EscapeLiteral(s, provider)}'";
            case char ch:
                return $"'{EscapeLiteral(ch.ToString(), provider)}'";
            case bool b:
                // PostgreSQL has a real boolean type — 1/0 is a syntax error there.
                if (provider == DatabaseProvider.Postgres)
                    return b ? "TRUE" : "FALSE";
                return b ? "1" : "0";
            case DateTime dt:
                return $"'{dt:yyyy-MM-dd HH:mm:ss.fff}'";
            case DateTimeOffset dto:
                return $"'{dto:yyyy-MM-dd HH:mm:ss.fff zzz}'";
#if NET8_0_OR_GREATER
            case DateOnly d:
                return $"'{d:yyyy-MM-dd}'";
            case TimeOnly t:
                return $"'{t:HH:mm:ss.FFFFFFF}'";
#endif
            case Guid g:
                return $"'{g}'";
            case Enum e:
                return Convert.ToInt64(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            case float f when float.IsNaN(f) || float.IsInfinity(f):
            case double d when double.IsNaN(d) || double.IsInfinity(d):
                throw new ArgumentException(
                    $"Value '{value}' of type '{value.GetType().FullName}' has no SQL literal representation.",
                    nameof(value));
            case byte _:
            case sbyte _:
            case short _:
            case ushort _:
            case int _:
            case uint _:
            case long _:
            case ulong _:
            case float _:
            case double _:
            case decimal _:
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "NULL";
            case byte[] bytes:
                var hex = BitConverter.ToString(bytes).Replace("-", string.Empty);
                if (provider == DatabaseProvider.Postgres)
                    return $"'\\x{hex}'";
                if (provider == DatabaseProvider.Oracle)
                    return $"HEXTORAW('{hex}')";
                return $"0x{hex}";
            default:
                const string supportedTypes = "string, char, bool, DateTime, DateTimeOffset, "
#if NET8_0_OR_GREATER
                    + "DateOnly, TimeOnly, "
#endif
                    + "Guid, enums, numeric types and byte[].";
                throw new NotSupportedException(
                    $"Values of type '{value.GetType().FullName}' are not supported as SQL literals. " +
                    "Supported types: " + supportedTypes + " " +
                    "TimeSpan has no portable literal (Oracle needs INTERVAL syntax) — pass a " +
                    "provider-specific string instead.");
        }
    }

    /// <summary>
    /// Resolves the default schema name for the current migration's database provider, used whenever
    /// a caller omits <c>schemaName</c>. Detected via <see cref="GetProvider"/>.
    /// </summary>
    private static string ResolveDefaultSchema(this MigrationBase self) => ResolveDefaultSchema(self.GetProvider());

    private static string ResolveDefaultSchema(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer => "dbo",
            DatabaseProvider.Postgres => "public",
            DatabaseProvider.MySql or DatabaseProvider.MariaDb => "",
            DatabaseProvider.SQLite => "",
            DatabaseProvider.Oracle => "",
            _ => "dbo",
        };
    }

    /// <summary>
    /// Database providers supported by the idempotent extensions. Parsed once per call from
    /// FluentMigrator's provider string (see <see cref="GetProvider"/>), so all branching is a
    /// single centralized match instead of scattered substring checks.
    /// </summary>
    private enum DatabaseProvider
    {
        Unknown,
        SqlServer,
        Postgres,
        MySql,
        MariaDb,
        SQLite,
        Oracle,
    }

    private static DatabaseProvider ParseDatabaseProvider(string databaseType)
    {
        // NOTE: stock FluentMigrator ships no separate MariaDB processor — MariaDB runs through
        // the MySQL processor and reports "MySql". The MariaDb member exists for forward
        // compatibility, and both members always share the same branches below.
        if (databaseType.IndexOf("SqlServer", StringComparison.OrdinalIgnoreCase) >= 0)
            return DatabaseProvider.SqlServer;
        if (databaseType.IndexOf("Postgres", StringComparison.OrdinalIgnoreCase) >= 0)
            return DatabaseProvider.Postgres;
        if (databaseType.IndexOf("MariaDb", StringComparison.OrdinalIgnoreCase) >= 0)
            return DatabaseProvider.MariaDb;
        if (databaseType.IndexOf("MySql", StringComparison.OrdinalIgnoreCase) >= 0)
            return DatabaseProvider.MySql;
        if (databaseType.IndexOf("SQLite", StringComparison.OrdinalIgnoreCase) >= 0)
            return DatabaseProvider.SQLite;
        if (databaseType.IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0)
            return DatabaseProvider.Oracle;
        return DatabaseProvider.Unknown;
    }

    private static DatabaseProvider GetProvider(this MigrationBase self)
        => ParseDatabaseProvider(self.GetDatabaseTypeName());

    /// <summary>
    /// The raw FluentMigrator provider string (e.g. "SqlServer"). Used only for display in
    /// exception messages — never for branching (use <see cref="GetProvider"/> instead), so
    /// message texts stay byte-identical to previous releases.
    /// </summary>
    private static string GetDatabaseTypeName(this MigrationBase self)
    {
        string? databaseType = null;
        self.IfDatabase(dt =>
        {
            databaseType = dt;
            return false;
        });

        return databaseType ?? string.Empty;
    }

    private static bool TableExists(this Migration self, string tableName, string schemaName)
        => self.Schema.Schema(schemaName).Table(tableName).Exists();

    private static bool ColumnExists(this Migration self, string tableName, string colName, string schemaName)
        => self.Schema.Schema(schemaName).Table(tableName).Column(colName).Exists();

    private static Dictionary<string, object?> ObjectToDictionary(object obj)
        => obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
              .Where(p => p.GetIndexParameters().Length == 0)
              .ToDictionary<PropertyInfo, string, object?>(p => p.Name, p => p.GetValue(obj));

    private static Dictionary<string, object> ObjectToNonNullDictionary(object obj)
    {
        var result = new Dictionary<string, object>();
        foreach (var prop in obj.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(prop => prop.GetIndexParameters().Length == 0))
        {
            var value = prop.GetValue(obj);
            if (value is null)
                throw new ArgumentException(
                    $"Property '{prop.Name}' is null, but upsert key values must be non-null.",
                    nameof(obj));
            result[prop.Name] = value;
        }

        return result;
    }

    private static void ValidateRelatedRows(RelatedRows related, string tableName, string schemaName, DatabaseProvider provider)
    {
        if (related.JoinColumns.Count == 0)
            throw new ArgumentException("At least one join column pair is required.", nameof(related));

        var overlap = related.Where.Keys
            .Intersect(related.NotNullColumns, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (overlap.Count > 0)
        {
            throw new ArgumentException(
                $"A column cannot appear in both {nameof(RelatedRows.Where)} and {nameof(RelatedRows.NotNullColumns)}: {string.Join(", ", overlap)}.",
                nameof(related));
        }

        var relatedSchemaName = related.SchemaName ?? ResolveDefaultSchema(provider);
        if (string.Equals(related.TableName, tableName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(relatedSchemaName, schemaName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Related table cannot be the same as the target table. Self-reference is not supported because MySQL rejects referencing the modified table in a subquery (error 1093).",
                nameof(related));
        }
    }

    private static (DatabaseProvider Provider, string ResolvedSchemaName, string WhereClause) PrepareRelatedOperation(
        Migration self,
        string tableName,
        RelatedRows related,
        IReadOnlyDictionary<string, object?>? keyValues,
        IReadOnlyCollection<string>? notNullColumns,
        string? schemaName,
        bool exists)
    {
        schemaName ??= self.ResolveDefaultSchema();
        var provider = self.GetProvider();
        ValidateRelatedRows(related, tableName, schemaName, provider);
        var whereClause = BuildRelatedWhereClause(provider, schemaName, tableName, related, keyValues, notNullColumns, exists);
        return (provider, schemaName, whereClause);
    }

    private static string BuildRelatedWhereClause(
        DatabaseProvider provider,
        string schemaName,
        string tableName,
        RelatedRows related,
        IReadOnlyDictionary<string, object?>? keyValues,
        IReadOnlyCollection<string>? notNullColumns,
        bool exists)
    {
        var targetTable = QualifyTable(provider, schemaName, tableName);
        var relatedSchemaName = related.SchemaName ?? ResolveDefaultSchema(provider);
        var relatedTable = QualifyTable(provider, relatedSchemaName, related.TableName);

        var conditions = new List<string>();

        if (keyValues is { Count: > 0 })
        {
            foreach (var kv in keyValues)
                conditions.Add(FormatSqlPredicate(provider, kv.Key, kv.Value));
        }

        if (notNullColumns is { Count: > 0 })
        {
            foreach (var col in notNullColumns)
                conditions.Add($"{QuoteIdent(provider, col)} IS NOT NULL");
        }

        var relatedConditions = new List<string>();
        foreach (var jc in related.JoinColumns)
        {
            relatedConditions.Add(
                $"{relatedTable}.{QuoteIdent(provider, jc.Key)} = {targetTable}.{QuoteIdent(provider, jc.Value)}");
        }

        if (related.Where is { Count: > 0 })
        {
            foreach (var kv in related.Where)
                relatedConditions.Add(FormatSqlPredicate(provider, kv.Key, kv.Value, relatedTable));
        }

        if (related.NotNullColumns is { Count: > 0 })
        {
            foreach (var col in related.NotNullColumns)
                relatedConditions.Add($"{relatedTable}.{QuoteIdent(provider, col)} IS NOT NULL");
        }

        var existsKeyword = exists ? "EXISTS" : "NOT EXISTS";
        conditions.Add($"{existsKeyword} (SELECT 1 FROM {relatedTable} WHERE {string.Join(" AND ", relatedConditions)})");

        return string.Join(" AND ", conditions);
    }
}
