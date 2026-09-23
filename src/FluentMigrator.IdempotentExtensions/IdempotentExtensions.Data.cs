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

/// <summary>
/// Seed data (insert/update/delete/upsert).
/// </summary>
public static partial class IdempotentExtensions
{
    public static void UpdateDataIfExists(
        this Migration self,
        string tableName,
        IReadOnlyDictionary<string, object?> keyValues,
        IReadOnlyDictionary<string, object?> setValues,
        string? schemaName = null)
    {
        if (keyValues.Count == 0)
            throw new ArgumentException("At least one key column is required.", nameof(keyValues));
        if (setValues.Count == 0)
            throw new ArgumentException("At least one column to set is required.", nameof(setValues));

        schemaName ??= self.ResolveDefaultSchema();
        var provider = self.GetProvider();

        var setClause = string.Join(", ", setValues.Select(kv => $"{QuoteIdent(provider, kv.Key)} = {FormatSqlValue(kv.Value, provider)}"));
        var whereClause = string.Join(" AND ", keyValues.Select(kv => FormatSqlPredicate(provider, kv.Key, kv.Value)));

        self.Execute.Sql($"UPDATE {QualifyTable(provider, schemaName, tableName)} SET {setClause} WHERE {whereClause};");
    }

    /// <summary>
    /// Deletes rows from <paramref name="tableName"/> matching <paramref name="keyValues"/>. Naturally
    /// idempotent — a <c>DELETE</c> that matches zero rows (because they were already deleted, or never
    /// existed) is a safe no-op on every provider, so no existence guard is needed.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Target table name.</param>
    /// <param name="keyValues">Column/value pairs identifying which rows to delete. Must contain at least one entry.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
    public static void DeleteDataIfExists(
        this Migration self,
        string tableName,
        IReadOnlyDictionary<string, object?> keyValues,
        string? schemaName = null)
    {
        if (keyValues.Count == 0)
            throw new ArgumentException("At least one key column is required.", nameof(keyValues));

        schemaName ??= self.ResolveDefaultSchema();
        var provider = self.GetProvider();

        var whereClause = string.Join(" AND ", keyValues.Select(kv => FormatSqlPredicate(provider, kv.Key, kv.Value)));

        self.Execute.Sql($"DELETE FROM {QualifyTable(provider, schemaName, tableName)} WHERE {whereClause};");
    }

    /// <summary>
    /// Inserts a row into <paramref name="tableName"/> only if no row matching <paramref name="keyValues"/>
    /// already exists. Intended for idempotently seeding small reference/lookup tables.
    /// </summary>
    /// <remarks>
    /// Uses a portable <c>INSERT ... SELECT ... WHERE NOT EXISTS (...)</c> statement that runs unmodified on
    /// SQL Server, PostgreSQL, MySQL and SQLite — no per-provider branching needed. Values are formatted as SQL
    /// literals (strings are quote-escaped; <c>null</c> key values use <c>IS NULL</c> so they still match;
    /// booleans become <c>TRUE</c>/<c>FALSE</c> on PostgreSQL and <c>1</c>/<c>0</c> elsewhere;
    /// <see cref="Guid"/> is quoted; enums use their underlying numeric value).
    /// Only whitelisted value types are accepted — anything else throws instead of being embedded blindly.
    /// Note: concurrent writers can both pass the <c>NOT EXISTS</c> check and collide on a unique
    /// constraint — like every check-then-act in this library, this is idempotent under retries,
    /// not under racing transactions.
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Target table name.</param>
    /// <param name="keyValues">Column/value pairs that uniquely identify the row — used for the existence check.
    /// Must contain at least one entry.</param>
    /// <param name="additionalValues">Extra column/value pairs to insert alongside <paramref name="keyValues"/>. Not part of the existence check.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
    public static void InsertDataIfNotExists(
        this Migration self,
        string tableName,
        IReadOnlyDictionary<string, object?> keyValues,
        IReadOnlyDictionary<string, object?>? additionalValues = null,
        string? schemaName = null)
    {
        if (keyValues.Count == 0)
            throw new ArgumentException("At least one key column is required.", nameof(keyValues));

        schemaName ??= self.ResolveDefaultSchema();
        var provider = self.GetProvider();

        if (additionalValues is not null)
        {
            var overlap = additionalValues.Keys.Intersect(keyValues.Keys, StringComparer.OrdinalIgnoreCase).ToList();
            if (overlap.Count > 0)
                throw new ArgumentException(
                    $"additionalValues must not redefine key columns: {string.Join(", ", overlap)}.",
                    nameof(additionalValues));
        }

        var qualifiedTable = QualifyTable(provider, schemaName, tableName);

        var values = new Dictionary<string, object?>();
        foreach (var kv in keyValues)
            values[kv.Key] = kv.Value;
        if (additionalValues is not null)
            foreach (var kv in additionalValues)
                values[kv.Key] = kv.Value;

        var columns = values.Keys.ToList();
        var columnList = string.Join(", ", columns.Select(c => QuoteIdent(provider, c)));
        var selectList = string.Join(", ", columns.Select(c => FormatSqlValue(values[c], provider)));
        var whereClause = string.Join(" AND ", keyValues.Select(kv => FormatSqlPredicate(provider, kv.Key, kv.Value)));

        // Oracle has no FROM-less SELECT — every SELECT needs a source, hence FROM DUAL.
        var fromDual = provider == DatabaseProvider.Oracle
            ? " FROM DUAL"
            : "";

        self.Execute.Sql($@"INSERT INTO {qualifiedTable} ({columnList})
SELECT {selectList}{fromDual}
WHERE NOT EXISTS (SELECT 1 FROM {qualifiedTable} WHERE {whereClause});");
    }

    /// <summary>
    /// Alters <paramref name="sequenceName"/> if it already exists; no-op otherwise.
    /// Not supported on MySQL/MariaDB or SQLite.
    /// </summary>
    /// <remarks>
    /// FluentMigrator has no <c>Alter.Sequence</c> API, so this builds and executes a raw
    /// <c>ALTER SEQUENCE</c> statement from the supplied parameters. At least one parameter
    /// must be non-null. Provider differences are handled internally — e.g. Oracle uses
    /// <c>START WITH</c> instead of <c>RESTART WITH</c>, and <c>NOCYCLE</c>/<c>NOCACHE</c>
    /// instead of <c>NO CYCLE</c>/<c>NO CACHE</c>.
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="sequenceName">Name of the sequence to alter.</param>
    /// <param name="incrementBy">New increment value.</param>
    /// <param name="minValue">New minimum value.</param>
    /// <param name="maxValue">New maximum value.</param>
    /// <param name="restartWith">Restart the sequence at this value. Not supported on Oracle (use <paramref name="startWith"/> instead).</param>
    /// <param name="startWith">Set the start value. On Oracle, also restarts the sequence.</param>
    /// <param name="cache">Number of values to cache. Pass <c>0</c> to disable caching.</param>
    /// <param name="cycle">Whether the sequence should cycle when it reaches its limit.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
    public static void UpsertData(
        this Migration self,
        string tableName,
        IReadOnlyDictionary<string, object> keyValues,
        IReadOnlyDictionary<string, object?>? additionalValues = null,
        string? schemaName = null)
    {
        if (keyValues.Count == 0)
            throw new ArgumentException("At least one key column is required.", nameof(keyValues));
        if (keyValues.Any(kv => kv.Value is null))
            throw new ArgumentException(
                "Upsert key values must be non-null: NULL never matches in a conflict/ON clause, " +
                "so a null key would re-insert instead of updating.",
                nameof(keyValues));

        schemaName ??= self.ResolveDefaultSchema();

        var allValues = new Dictionary<string, object?>();
        foreach (var kv in keyValues)
            allValues[kv.Key] = kv.Value;
        if (additionalValues is not null)
            foreach (var kv in additionalValues)
                allValues[kv.Key] = kv.Value;

        var provider = self.GetProvider();
        var qualifiedTable = QualifyTable(provider, schemaName, tableName);
        var columns = allValues.Keys.ToList();
        var columnList = string.Join(", ", columns.Select(c => QuoteIdent(provider, c)));
        var valueList = string.Join(", ", columns.Select(c => FormatSqlValue(allValues[c], provider)));
        var hasUpdates = additionalValues is not null && additionalValues.Count > 0;

        if (provider == DatabaseProvider.SqlServer)
        {
            // HOLDLOCK: without it two concurrent runners can both pass the match check
            // and hit a duplicate-key error (the well-known MERGE race).
            var onClause = string.Join(" AND ", keyValues.Select(kv => $"target.{QuoteIdent(provider, kv.Key)} = source.{QuoteIdent(provider, kv.Key)}"));
            // A bare NULL has no type in a derived table ("type cannot be determined") — cast it.
            var sourceColumns = string.Join(", ", columns.Select(c => allValues[c] is null
                ? $"CAST(NULL AS NVARCHAR(MAX)) AS {QuoteIdent(provider, c)}"
                : $"{FormatSqlValue(allValues[c], provider)} AS {QuoteIdent(provider, c)}"));
            var matchedClause = hasUpdates
                ? $"WHEN MATCHED THEN UPDATE SET {string.Join(", ", additionalValues!.Select(kv => $"target.{QuoteIdent(provider, kv.Key)} = source.{QuoteIdent(provider, kv.Key)}"))}\n"
                : "";
            var insertColumns = string.Join(", ", columns.Select(c => QuoteIdent(provider, c)));
            var insertValues = string.Join(", ", columns.Select(c => $"source.{QuoteIdent(provider, c)}"));

            self.Execute.Sql($@"MERGE {qualifiedTable} WITH (HOLDLOCK) AS target
USING (SELECT {sourceColumns}) AS source
ON ({onClause})
{matchedClause}WHEN NOT MATCHED THEN INSERT ({insertColumns}) VALUES ({insertValues});");
            return;
        }

        if (provider == DatabaseProvider.Oracle)
        {
            var onClause = string.Join(" AND ", keyValues.Select(kv => $"target.{QuoteIdent(provider, kv.Key)} = source.{QuoteIdent(provider, kv.Key)}"));
            var sourceColumns = string.Join(", ", columns.Select(c => allValues[c] is null
                ? $"CAST(NULL AS VARCHAR2(4000)) AS {QuoteIdent(provider, c)}"
                : $"{FormatSqlValue(allValues[c], provider)} AS {QuoteIdent(provider, c)}"));
            var matchedClause = hasUpdates
                ? $"WHEN MATCHED THEN UPDATE SET {string.Join(", ", additionalValues!.Select(kv => $"target.{QuoteIdent(provider, kv.Key)} = source.{QuoteIdent(provider, kv.Key)}"))}\n"
                : "";
            // No trailing semicolon: this statement is sent as direct SQL (ORA-00911),
            // unlike the PL/SQL blocks elsewhere which require their own terminators.
            var insertColumns = string.Join(", ", columns.Select(c => $"target.{QuoteIdent(provider, c)}"));
            var insertValues = string.Join(", ", columns.Select(c => $"source.{QuoteIdent(provider, c)}"));

            self.Execute.Sql($@"MERGE INTO {qualifiedTable} target
USING (SELECT {sourceColumns} FROM DUAL) source
ON ({onClause})
{matchedClause}WHEN NOT MATCHED THEN INSERT ({insertColumns}) VALUES ({insertValues})");
            return;
        }

        if (provider is DatabaseProvider.MySql or DatabaseProvider.MariaDb)
        {
            var isMariaDb = provider == DatabaseProvider.MariaDb;
            string updateSet;
            string prefix;
            if (hasUpdates)
            {
                // VALUES(col) is deprecated since MySQL 8.0.20 — the row-alias form needs 8.0.19+.
                // MariaDB never deprecated VALUES(), so it stays there for maximum server compatibility.
                updateSet = isMariaDb
                    ? string.Join(", ", additionalValues!.Select(kv => $"{QuoteIdent(provider, kv.Key)} = VALUES({QuoteIdent(provider, kv.Key)})"))
                    : string.Join(", ", additionalValues!.Select(kv => $"{QuoteIdent(provider, kv.Key)} = new.{QuoteIdent(provider, kv.Key)}"));
                prefix = isMariaDb ? "" : "AS new ";
            }
            else
            {
                // ON DUPLICATE KEY UPDATE requires at least one assignment — self-assign the keys (a no-op).
                updateSet = isMariaDb
                    ? string.Join(", ", keyValues.Select(kv => $"{QuoteIdent(provider, kv.Key)} = VALUES({QuoteIdent(provider, kv.Key)})"))
                    : string.Join(", ", keyValues.Select(kv => $"{QuoteIdent(provider, kv.Key)} = new.{QuoteIdent(provider, kv.Key)}"));
                prefix = isMariaDb ? "" : "AS new ";
            }

            self.Execute.Sql($@"INSERT INTO {qualifiedTable} ({columnList}) VALUES ({valueList})
{prefix}ON DUPLICATE KEY UPDATE {updateSet};");
            return;
        }

        // PostgreSQL / SQLite: ON CONFLICT ... DO UPDATE (or DO NOTHING for key-only upserts).
        var keyColumnList = string.Join(", ", keyValues.Keys.Select(k => QuoteIdent(provider, k)));
        var conflictClause = hasUpdates
            ? $"DO UPDATE SET {string.Join(", ", additionalValues!.Select(kv => $"{QuoteIdent(provider, kv.Key)} = EXCLUDED.{QuoteIdent(provider, kv.Key)}"))}"
            : "DO NOTHING";

        self.Execute.Sql($@"INSERT INTO {qualifiedTable} ({columnList}) VALUES ({valueList})
ON CONFLICT ({keyColumnList}) {conflictClause};");
    }

    /// <summary>
    /// Executes <paramref name="executeSql"/> only if <paramref name="conditionSql"/> returns at least one row.
    /// An escape hatch for idempotent operations not covered by the specialized methods.
    /// </summary>
    /// <remarks>
    /// Supported on SQL Server (<c>IF EXISTS ... EXEC sp_executesql</c>), PostgreSQL (<c>DO $fm_idempotent$ ... END</c>),
    /// and Oracle (<c>DECLARE ... EXECUTE IMMEDIATE</c>). Not supported on MySQL or SQLite.
    /// Both statements are executed verbatim — trusted developer input only, never end-user input.
    /// The single-statement check on <paramref name="conditionSql"/> is a guard rail, not a security
    /// boundary (<paramref name="executeSql"/> is not validated at all) — treat both as code.
    /// <paramref name="conditionSql"/> must be a single <c>SELECT</c> without a trailing semicolon
    /// (multi-statement input is rejected); <paramref name="executeSql"/> must not contain the
    /// <c>$fm_idempotent$</c> dollar-quote tag.
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="conditionSql">A <c>SELECT</c> statement; if it returns any rows, <paramref name="executeSql"/> runs.</param>
    /// <param name="executeSql">The SQL statement to execute when the condition is met.</param>
    public static void InsertDataIfNotExists(
        this Migration self,
        string tableName,
        object keyValues,
        object? additionalValues = null,
        string? schemaName = null)
        => InsertDataIfNotExists(self, tableName, ObjectToDictionary(keyValues),
            additionalValues is not null ? ObjectToDictionary(additionalValues) : null, schemaName);

    /// <inheritdoc cref="UpdateDataIfExists(Migration, string, IReadOnlyDictionary{string, object?}, IReadOnlyDictionary{string, object?}, string?)"/>
    public static void UpdateDataIfExists(
        this Migration self,
        string tableName,
        object keyValues,
        object setValues,
        string? schemaName = null)
        => UpdateDataIfExists(self, tableName, ObjectToDictionary(keyValues), ObjectToDictionary(setValues), schemaName);

    /// <inheritdoc cref="DeleteDataIfExists(Migration, string, IReadOnlyDictionary{string, object?}, string?)"/>
    public static void DeleteDataIfExists(
        this Migration self,
        string tableName,
        object keyValues,
        string? schemaName = null)
        => DeleteDataIfExists(self, tableName, ObjectToDictionary(keyValues), schemaName);

    /// <inheritdoc cref="UpsertData(Migration, string, IReadOnlyDictionary{string, object}, IReadOnlyDictionary{string, object?}?, string?)"/>
    public static void UpsertData(
        this Migration self,
        string tableName,
        object keyValues,
        object? additionalValues = null,
        string? schemaName = null)
        => UpsertData(self, tableName, ObjectToNonNullDictionary(keyValues),
            additionalValues is not null ? ObjectToDictionary(additionalValues) : null, schemaName);
}
