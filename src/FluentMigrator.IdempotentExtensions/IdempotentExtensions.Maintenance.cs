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
/// Index maintenance and statistics.
/// </summary>
public static partial class IdempotentExtensions
{
    public static void ReorganizeIndexes(
        this Migration self,
        string tableName,
        string? schemaName = null,
        bool concurrently = false)
    {
        schemaName ??= self.ResolveDefaultSchema();
        var provider = self.GetProvider();
        var databaseTypeName = self.GetDatabaseTypeName();

        if (provider == DatabaseProvider.SqlServer)
        {
            self.Execute.Sql($"ALTER INDEX ALL ON [{EscapeBracket(schemaName)}].[{EscapeBracket(tableName)}] REORGANIZE;");
            return;
        }

        if (provider == DatabaseProvider.Postgres)
        {
            var keyword = concurrently ? "REINDEX TABLE CONCURRENTLY" : "REINDEX TABLE";
            self.Execute.Sql($"{keyword} {QualifyTable(provider, schemaName, tableName)};");
            return;
        }

        if (provider is DatabaseProvider.MySql or DatabaseProvider.MariaDb)
        {
            self.Execute.Sql($"OPTIMIZE TABLE {QualifyTable(provider, schemaName, tableName)};");
            return;
        }

        if (provider == DatabaseProvider.SQLite)
        {
            self.Execute.Sql($"REINDEX {QuoteIdent(provider, tableName)};");
            return;
        }

        if (provider == DatabaseProvider.Oracle)
        {
            // Oracle has no "reorganize all indexes on a table" statement: loop over the table's
            // indexes and rebuild each one. Unquoted identifiers are stored uppercase, but quoted
            // (case-sensitive) names are stored as-is — match either.
            var escapedTable = EscapeLiteral(tableName);
            var escapedSchema = EscapeLiteral(schemaName);

            var indexCursor = string.IsNullOrEmpty(schemaName)
                ? $"SELECT index_name FROM user_indexes WHERE table_name IN ('{escapedTable}', UPPER('{escapedTable}'))"
                : $"SELECT owner, index_name FROM all_indexes WHERE table_name IN ('{escapedTable}', UPPER('{escapedTable}')) AND owner IN ('{escapedSchema}', UPPER('{escapedSchema}'))";

            var rebuildSql = string.IsNullOrEmpty(schemaName)
                ? "'ALTER INDEX \"' || idx.index_name || '\" REBUILD'"
                : "'ALTER INDEX \"' || idx.owner || '\".\"' || idx.index_name || '\" REBUILD'";

            self.Execute.Sql($@"
BEGIN
    FOR idx IN ({indexCursor}) LOOP
        EXECUTE IMMEDIATE {rebuildSql};
    END LOOP;
END;");
            return;
        }

        throw new NotSupportedException($"ReorganizeIndexes is not supported on {databaseTypeName}.");
    }

    /// <summary>
    /// Refreshes the optimizer statistics for <paramref name="tableName"/>. Pure maintenance — no
    /// schema changes — so re-applying the migration that contains it is always safe.
    /// </summary>
    /// <remarks>
    /// Note that FluentMigrator runs each migration version once: the statement below executes
    /// when the migration is applied, not on every deploy (see <see cref="ReorganizeIndexes"/>).
    /// Provider mapping: SQL Server runs <c>UPDATE STATISTICS ... WITH SAMPLE n PERCENT</c> (or without
    /// a sampling clause when <paramref name="samplePercent"/> is <c>null</c>); PostgreSQL runs
    /// <c>ANALYZE</c>; MySQL/MariaDB runs <c>ANALYZE TABLE</c>; SQLite runs <c>ANALYZE table</c>;
    /// Oracle calls <c>DBMS_STATS.GATHER_TABLE_STATS</c> with <c>estimate_percent</c>
    /// (<c>DBMS_STATS.AUTO_SAMPLE_SIZE</c> when <paramref name="samplePercent"/> is <c>null</c>).
    /// Sampling is only honored on SQL Server and Oracle — the other providers have no per-table
    /// sampling clause, so <paramref name="samplePercent"/> is ignored there.
    /// The table is passed as a parameter — nothing is hardcoded. If the table does not exist the
    /// statement fails server-side (no silent skip).
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Table whose statistics should be refreshed.</param>
    /// <param name="samplePercent">Sampling percent, 1–100. Honored on SQL Server and Oracle only.
    /// <c>null</c> means server default (no sampling clause / auto sample size). Defaults to 30.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
    public static void UpdateStatistics(
        this Migration self,
        string tableName,
        int? samplePercent = 30,
        string? schemaName = null)
    {
        if (samplePercent.HasValue && (samplePercent.Value < 1 || samplePercent.Value > 100))
            throw new ArgumentOutOfRangeException(nameof(samplePercent), "Sample percent must be between 1 and 100.");

        schemaName ??= self.ResolveDefaultSchema();
        var provider = self.GetProvider();
        var databaseTypeName = self.GetDatabaseTypeName();

        if (provider == DatabaseProvider.SqlServer)
        {
            var sampleClause = samplePercent.HasValue ? $" WITH SAMPLE {samplePercent.Value} PERCENT" : "";
            self.Execute.Sql($"UPDATE STATISTICS [{EscapeBracket(schemaName)}].[{EscapeBracket(tableName)}]{sampleClause};");
            return;
        }

        if (provider == DatabaseProvider.Postgres)
        {
            self.Execute.Sql($"ANALYZE {QualifyTable(provider, schemaName, tableName)};");
            return;
        }

        if (provider is DatabaseProvider.MySql or DatabaseProvider.MariaDb)
        {
            self.Execute.Sql($"ANALYZE TABLE {QualifyTable(provider, schemaName, tableName)};");
            return;
        }

        if (provider == DatabaseProvider.SQLite)
        {
            self.Execute.Sql($"ANALYZE {QuoteIdent(provider, tableName)};");
            return;
        }

        if (provider == DatabaseProvider.Oracle)
        {
            var estimate = samplePercent.HasValue
                ? samplePercent.Value.ToString(CultureInfo.InvariantCulture)
                : "DBMS_STATS.AUTO_SAMPLE_SIZE";
            var escapedTable = EscapeLiteral(tableName);

            // Resolve the real stored names first: unquoted identifiers live uppercase in the
            // dictionary, quoted (case-sensitive) ones as-is. GATHER_TABLE_STATS needs exact names.
            string statsSql;
            if (string.IsNullOrEmpty(schemaName))
            {
                statsSql = $@"
DECLARE
    v_tab VARCHAR2(128);
BEGIN
    BEGIN
        SELECT table_name INTO v_tab FROM (
            SELECT table_name FROM user_tables WHERE table_name IN ('{escapedTable}', UPPER('{escapedTable}'))
            UNION ALL
            SELECT table_name FROM all_tables WHERE table_name IN ('{escapedTable}', UPPER('{escapedTable}')) AND owner = USER AND ROWNUM = 1)
        WHERE ROWNUM = 1;
    EXCEPTION
        WHEN NO_DATA_FOUND THEN
            RAISE_APPLICATION_ERROR(-20001, 'UpdateStatistics: table ''{escapedTable}'' not found');
    END;
    DBMS_STATS.GATHER_TABLE_STATS(ownname => USER, tabname => v_tab, estimate_percent => {estimate});
END;";
            }
            else
            {
                var escapedSchema = EscapeLiteral(schemaName);
                statsSql = $@"
DECLARE
    v_owner VARCHAR2(128);
    v_tab VARCHAR2(128);
BEGIN
    BEGIN
        SELECT username INTO v_owner FROM all_users WHERE username IN ('{escapedSchema}', UPPER('{escapedSchema}')) AND ROWNUM = 1;
    EXCEPTION
        WHEN NO_DATA_FOUND THEN
            RAISE_APPLICATION_ERROR(-20001, 'UpdateStatistics: schema ''{escapedSchema}'' not found');
    END;
    BEGIN
        SELECT table_name INTO v_tab FROM (
            SELECT table_name FROM all_tables WHERE table_name IN ('{escapedTable}', UPPER('{escapedTable}')) AND owner = v_owner AND ROWNUM = 1)
        WHERE ROWNUM = 1;
    EXCEPTION
        WHEN NO_DATA_FOUND THEN
            RAISE_APPLICATION_ERROR(-20001, 'UpdateStatistics: table ''{escapedSchema}.{escapedTable}'' not found');
    END;
    DBMS_STATS.GATHER_TABLE_STATS(ownname => v_owner, tabname => v_tab, estimate_percent => {estimate});
END;";
            }

            self.Execute.Sql(statsSql);
            return;
        }

        throw new NotSupportedException($"UpdateStatistics is not supported on {databaseTypeName}.");
    }

}
