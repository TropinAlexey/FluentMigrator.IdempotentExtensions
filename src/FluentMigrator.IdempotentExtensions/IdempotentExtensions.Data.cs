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
    /// <summary>
    /// Updates rows in <paramref name="tableName"/> matching <paramref name="keyValues"/> with
    /// <paramref name="setValues"/>. Naturally idempotent — an <c>UPDATE</c> that matches zero rows (because
    /// they were already updated, or don't exist) is a safe no-op on every provider, so no existence guard
    /// is needed.
    /// </summary>
    /// <remarks>
    /// Uses the same portable value formatting as <see cref="InsertDataIfNotExists(Migration, string, IReadOnlyDictionary{string, object?}, IReadOnlyDictionary{string, object?}?, string?)"/> (strings quote-escaped,
    /// <c>null</c> compared with <c>IS NULL</c>, <see cref="Guid"/> quoted, enums as their numeric value).
    /// Only whitelisted value types are accepted — anything else throws instead of being embedded blindly.
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Target table name.</param>
    /// <param name="keyValues">Column/value pairs identifying which rows to update. Must contain at least one entry.</param>
    /// <param name="setValues">Column/value pairs to set on the matched rows. Must contain at least one entry.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
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

        var setClause = BuildSetClause(provider, setValues);
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
    /// Inserts a row if no row matching <paramref name="keyValues"/> exists; otherwise updates the matching
    /// row with <paramref name="additionalValues"/>. Uses provider-specific MERGE/upsert syntax:
    /// <c>MERGE</c> on SQL Server and Oracle, <c>INSERT ... ON CONFLICT ... DO UPDATE</c> on PostgreSQL,
    /// <c>INSERT ... ON DUPLICATE KEY UPDATE</c> on MySQL, and <c>INSERT ... ON CONFLICT ... DO UPDATE</c>
    /// on SQLite.
    /// </summary>
    /// <remarks>
    /// PostgreSQL, MySQL, and SQLite require a UNIQUE constraint (or PRIMARY KEY) on the key columns for
    /// conflict detection to work. If no such constraint exists, the statement will fail — ensure a unique
    /// index or constraint covers the key columns before calling this method.
    /// Key values must be non-null (null never matches in a conflict check). On MySQL the row-alias
    /// form is used (<c>AS new ... = new.col</c>), which requires MySQL 8.0.19+; MariaDB keeps the
    /// legacy <c>VALUES(col)</c> form. On SQL Server the target is taken <c>WITH (HOLDLOCK)</c> to
    /// close the check-then-act race between concurrent runners.
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Target table name.</param>
    /// <param name="keyValues">Column/value pairs that uniquely identify the row. Must have at least one entry.
    /// Values are non-nullable — upsert conflict detection requires non-null keys on all providers.</param>
    /// <param name="additionalValues">Column/value pairs to insert alongside keys / update on conflict. May be empty or null for key-only rows.</param>
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

    /// <inheritdoc cref="InsertDataIfNotExists(Migration, string, IReadOnlyDictionary{string, object?}, IReadOnlyDictionary{string, object?}?, string?)"/>
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

    /// <summary>
    /// Updates rows in <paramref name="tableName"/> when at least one matching row exists in the related
    /// table described by <paramref name="related"/>. Uses a portable correlated <c>EXISTS</c> subquery
    /// instead of provider-specific <c>UPDATE ... FROM</c>/<c>JOIN</c> syntax.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The generated SQL has the form:
    /// <c>UPDATE &lt;target&gt; SET ... WHERE &lt;target-filter&gt; AND EXISTS (SELECT 1 FROM &lt;related&gt; WHERE &lt;related&gt;.&lt;col&gt; = &lt;target&gt;.&lt;col&gt; ...)</c>.
    /// Tables are referenced by fully qualified names (<c>schema.table.column</c>) with no aliases so the
    /// statement runs unmodified on SQL Server, PostgreSQL, MySQL/MariaDB, SQLite and Oracle.
    /// </para>
    /// <para>
    /// Idempotency is opt-in, just like <see cref="UpdateDataIfExists(Migration, string, IReadOnlyDictionary{string, object?}, IReadOnlyDictionary{string, object?}, string?)"/>:
    /// include a <paramref name="keyValues"/> filter that excludes already-updated rows (for example,
    /// the old value you are changing from). Without such a filter the <c>UPDATE</c> will rewrite the same
    /// rows with the same values on every run — safe, but it may fire triggers each time.
    /// </para>
    /// <para>
    /// This method does not guard the existence of either table. Wrap it in <c>ExecuteSqlIfExists</c> or
    /// check <c>Schema.Table(...).Exists()</c> first if the table may not exist.
    /// Self-references (related table is the same as the target table) are not supported because MySQL
    /// rejects referencing the modified table in a subquery (error 1093).
    /// </para>
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Target table name.</param>
    /// <param name="setValues">Column/value pairs to set on the matched rows. Must contain at least one entry.</param>
    /// <param name="related">Description of the related table and how it joins to the target.</param>
    /// <param name="keyValues">Optional equality / <c>IS NULL</c> filters on the target table.</param>
    /// <param name="notNullColumns">Optional target columns that must be <c>IS NOT NULL</c>.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
    /// <exception cref="ArgumentException">Validation rules are violated (empty <paramref name="setValues"/>, empty <see cref="RelatedRows.JoinColumns"/>, overlapping <see cref="RelatedRows.Where"/> and <see cref="RelatedRows.NotNullColumns"/>, or related table equals target table).</exception>
    public static void UpdateDataIfRelatedExists(
        this Migration self,
        string tableName,
        IReadOnlyDictionary<string, object?> setValues,
        RelatedRows related,
        IReadOnlyDictionary<string, object?>? keyValues = null,
        IReadOnlyCollection<string>? notNullColumns = null,
        string? schemaName = null)
    {
        if (setValues.Count == 0)
            throw new ArgumentException("At least one column to set is required.", nameof(setValues));

        var (provider, resolvedSchemaName, whereClause) = PrepareRelatedOperation(
            self, tableName, related, keyValues, notNullColumns, schemaName, exists: true);
        var setClause = BuildSetClause(provider, setValues);

        self.Execute.Sql($"UPDATE {QualifyTable(provider, resolvedSchemaName, tableName)} SET {setClause} WHERE {whereClause};");
    }

    /// <summary>
    /// Updates rows in <paramref name="tableName"/> when no matching row exists in the related table.
    /// See <see cref="UpdateDataIfRelatedExists(Migration, string, IReadOnlyDictionary{string, object?}, RelatedRows, IReadOnlyDictionary{string, object?}?, IReadOnlyCollection{string}?, string?)"/>
    /// for details on portability, idempotency and limitations.
    /// </summary>
    public static void UpdateDataIfRelatedNotExists(
        this Migration self,
        string tableName,
        IReadOnlyDictionary<string, object?> setValues,
        RelatedRows related,
        IReadOnlyDictionary<string, object?>? keyValues = null,
        IReadOnlyCollection<string>? notNullColumns = null,
        string? schemaName = null)
    {
        if (setValues.Count == 0)
            throw new ArgumentException("At least one column to set is required.", nameof(setValues));

        var (provider, resolvedSchemaName, whereClause) = PrepareRelatedOperation(
            self, tableName, related, keyValues, notNullColumns, schemaName, exists: false);
        var setClause = BuildSetClause(provider, setValues);

        self.Execute.Sql($"UPDATE {QualifyTable(provider, resolvedSchemaName, tableName)} SET {setClause} WHERE {whereClause};");
    }

    /// <summary>
    /// Deletes rows from <paramref name="tableName"/> when at least one matching row exists in the related
    /// table described by <paramref name="related"/>. Uses a portable correlated <c>EXISTS</c> subquery.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The generated SQL has the form:
    /// <c>DELETE FROM &lt;target&gt; WHERE &lt;target-filter&gt; AND EXISTS (SELECT 1 FROM &lt;related&gt; WHERE ...)</c>.
    /// See <see cref="UpdateDataIfRelatedExists(Migration, string, IReadOnlyDictionary{string, object?}, RelatedRows, IReadOnlyDictionary{string, object?}?, IReadOnlyCollection{string}?, string?)"/>
    /// for portability, idempotency and validation notes.
    /// </para>
    /// <para>
    /// <paramref name="keyValues"/> may be null or empty, in which case the <c>WHERE</c> clause contains only
    /// the <c>EXISTS</c> condition. This deletes every target row that has a related match — use carefully.
    /// </para>
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Target table name.</param>
    /// <param name="related">Description of the related table and how it joins to the target.</param>
    /// <param name="keyValues">Optional equality / <c>IS NULL</c> filters on the target table.</param>
    /// <param name="notNullColumns">Optional target columns that must be <c>IS NOT NULL</c>.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
    /// <exception cref="ArgumentException">Validation rules are violated (empty <see cref="RelatedRows.JoinColumns"/>, overlapping <see cref="RelatedRows.Where"/> and <see cref="RelatedRows.NotNullColumns"/>, or related table equals target table).</exception>
    public static void DeleteDataIfRelatedExists(
        this Migration self,
        string tableName,
        RelatedRows related,
        IReadOnlyDictionary<string, object?>? keyValues = null,
        IReadOnlyCollection<string>? notNullColumns = null,
        string? schemaName = null)
    {
        var (provider, resolvedSchemaName, whereClause) = PrepareRelatedOperation(
            self, tableName, related, keyValues, notNullColumns, schemaName, exists: true);

        self.Execute.Sql($"DELETE FROM {QualifyTable(provider, resolvedSchemaName, tableName)} WHERE {whereClause};");
    }

    /// <summary>
    /// Deletes rows from <paramref name="tableName"/> when no matching row exists in the related table.
    /// See <see cref="DeleteDataIfRelatedExists(Migration, string, RelatedRows, IReadOnlyDictionary{string, object?}?, IReadOnlyCollection{string}?, string?)"/>
    /// for details on portability, idempotency and limitations.
    /// </summary>
    public static void DeleteDataIfRelatedNotExists(
        this Migration self,
        string tableName,
        RelatedRows related,
        IReadOnlyDictionary<string, object?>? keyValues = null,
        IReadOnlyCollection<string>? notNullColumns = null,
        string? schemaName = null)
    {
        var (provider, resolvedSchemaName, whereClause) = PrepareRelatedOperation(
            self, tableName, related, keyValues, notNullColumns, schemaName, exists: false);

        self.Execute.Sql($"DELETE FROM {QualifyTable(provider, resolvedSchemaName, tableName)} WHERE {whereClause};");
    }

    /// <inheritdoc cref="UpdateDataIfRelatedExists(Migration, string, IReadOnlyDictionary{string, object?}, RelatedRows, IReadOnlyDictionary{string, object?}?, IReadOnlyCollection{string}?, string?)"/>
    public static void UpdateDataIfRelatedExists(
        this Migration self,
        string tableName,
        object setValues,
        RelatedRows related,
        object? keyValues = null,
        IReadOnlyCollection<string>? notNullColumns = null,
        string? schemaName = null)
        => UpdateDataIfRelatedExists(
            self, tableName,
            ObjectToDictionary(setValues),
            related,
            keyValues is not null ? ObjectToDictionary(keyValues) : null,
            notNullColumns,
            schemaName);

    /// <inheritdoc cref="UpdateDataIfRelatedNotExists(Migration, string, IReadOnlyDictionary{string, object?}, RelatedRows, IReadOnlyDictionary{string, object?}?, IReadOnlyCollection{string}?, string?)"/>
    public static void UpdateDataIfRelatedNotExists(
        this Migration self,
        string tableName,
        object setValues,
        RelatedRows related,
        object? keyValues = null,
        IReadOnlyCollection<string>? notNullColumns = null,
        string? schemaName = null)
        => UpdateDataIfRelatedNotExists(
            self, tableName,
            ObjectToDictionary(setValues),
            related,
            keyValues is not null ? ObjectToDictionary(keyValues) : null,
            notNullColumns,
            schemaName);

    /// <inheritdoc cref="DeleteDataIfRelatedExists(Migration, string, RelatedRows, IReadOnlyDictionary{string, object?}?, IReadOnlyCollection{string}?, string?)"/>
    public static void DeleteDataIfRelatedExists(
        this Migration self,
        string tableName,
        RelatedRows related,
        object? keyValues = null,
        IReadOnlyCollection<string>? notNullColumns = null,
        string? schemaName = null)
        => DeleteDataIfRelatedExists(
            self, tableName,
            related,
            keyValues is not null ? ObjectToDictionary(keyValues) : null,
            notNullColumns,
            schemaName);

    /// <inheritdoc cref="DeleteDataIfRelatedNotExists(Migration, string, RelatedRows, IReadOnlyDictionary{string, object?}?, IReadOnlyCollection{string}?, string?)"/>
    public static void DeleteDataIfRelatedNotExists(
        this Migration self,
        string tableName,
        RelatedRows related,
        object? keyValues = null,
        IReadOnlyCollection<string>? notNullColumns = null,
        string? schemaName = null)
        => DeleteDataIfRelatedNotExists(
            self, tableName,
            related,
            keyValues is not null ? ObjectToDictionary(keyValues) : null,
            notNullColumns,
            schemaName);

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
