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
/// Conditional raw SQL.
/// </summary>
public static partial class IdempotentExtensions
{
    public static void ExecuteSqlIfExists(
        this Migration self,
        string conditionSql,
        string executeSql)
    {
        const string pgTag = "$fm_idempotent$";
        var provider = self.GetProvider();
        var databaseTypeName = self.GetDatabaseTypeName();

        if (conditionSql.Contains(';'))
            throw new ArgumentException("conditionSql must be a single SELECT statement without semicolons.", nameof(conditionSql));
        if (provider == DatabaseProvider.Postgres && executeSql.Contains(pgTag))
            throw new ArgumentException($"executeSql must not contain the '{pgTag}' dollar-quote tag.", nameof(executeSql));

        if (provider == DatabaseProvider.SqlServer)
        {
            self.Execute.Sql($@"IF EXISTS ({conditionSql})
    EXEC sp_executesql N'{executeSql.Replace("'", "''")}';");
            return;
        }

        if (provider == DatabaseProvider.Postgres)
        {
            self.Execute.Sql($@"DO {pgTag}
BEGIN
    IF EXISTS ({conditionSql}) THEN
        EXECUTE '{executeSql.Replace("'", "''")}';
    END IF;
END {pgTag};");
            return;
        }

        if (provider == DatabaseProvider.Oracle)
        {
            self.Execute.Sql($@"
DECLARE
    v_cnt NUMBER;
BEGIN
    SELECT COUNT(*) INTO v_cnt FROM ({conditionSql}) WHERE ROWNUM = 1;
    IF v_cnt > 0 THEN
        EXECUTE IMMEDIATE '{executeSql.Replace("'", "''")}';
    END IF;
END;");
            return;
        }

        throw new NotSupportedException($"ExecuteSqlIfExists is not supported on {databaseTypeName}.");
    }

    /// <summary>
    /// Executes <paramref name="executeSql"/> only if <paramref name="conditionSql"/> returns no rows.
    /// An escape hatch for idempotent operations not covered by the specialized methods.
    /// </summary>
    /// <remarks>
    /// Same provider support and trusted-input contract as <see cref="ExecuteSqlIfExists"/>.
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="conditionSql">A <c>SELECT</c> statement; if it returns no rows, <paramref name="executeSql"/> runs.</param>
    /// <param name="executeSql">The SQL statement to execute when the condition is not met.</param>
    public static void ExecuteSqlIfNotExists(
        this Migration self,
        string conditionSql,
        string executeSql)
    {
        const string pgTag = "$fm_idempotent$";
        var provider = self.GetProvider();
        var databaseTypeName = self.GetDatabaseTypeName();

        if (conditionSql.Contains(';'))
            throw new ArgumentException("conditionSql must be a single SELECT statement without semicolons.", nameof(conditionSql));
        if (provider == DatabaseProvider.Postgres && executeSql.Contains(pgTag))
            throw new ArgumentException($"executeSql must not contain the '{pgTag}' dollar-quote tag.", nameof(executeSql));

        if (provider == DatabaseProvider.SqlServer)
        {
            self.Execute.Sql($@"IF NOT EXISTS ({conditionSql})
    EXEC sp_executesql N'{executeSql.Replace("'", "''")}';");
            return;
        }

        if (provider == DatabaseProvider.Postgres)
        {
            self.Execute.Sql($@"DO {pgTag}
BEGIN
    IF NOT EXISTS ({conditionSql}) THEN
        EXECUTE '{executeSql.Replace("'", "''")}';
    END IF;
END {pgTag};");
            return;
        }

        if (provider == DatabaseProvider.Oracle)
        {
            self.Execute.Sql($@"
DECLARE
    v_cnt NUMBER;
BEGIN
    SELECT COUNT(*) INTO v_cnt FROM ({conditionSql}) WHERE ROWNUM = 1;
    IF v_cnt = 0 THEN
        EXECUTE IMMEDIATE '{executeSql.Replace("'", "''")}';
    END IF;
END;");
            return;
        }

        throw new NotSupportedException($"ExecuteSqlIfNotExists is not supported on {databaseTypeName}.");
    }

    /// <summary>
    /// Reorganizes/defragments all indexes on <paramref name="tableName"/>. Pure maintenance — no
    /// schema changes — so re-applying the migration that contains it is always safe.
    /// </summary>
    /// <remarks>
    /// Note that FluentMigrator runs each migration version once: the statement below executes
    /// when the migration is applied, not on every deploy. To run maintenance periodically,
    /// create a new migration version each time (or invoke these helpers outside migrations).
    /// Provider mapping: SQL Server runs <c>ALTER INDEX ALL ... REORGANIZE</c>; PostgreSQL runs
    /// <c>REINDEX TABLE</c>; MySQL/MariaDB runs <c>OPTIMIZE TABLE</c> (which also refreshes index
    /// statistics); SQLite runs <c>REINDEX table</c>; Oracle rebuilds each of the table's indexes via
    /// a PL/SQL loop (<c>ALTER INDEX ... REBUILD</c>, plain rebuild so it works on every edition).
    /// The table is passed as a parameter — nothing is hardcoded. If the table does not exist the
    /// statement fails server-side (no silent skip).
    /// On PostgreSQL this takes an <c>ACCESS EXCLUSIVE</c> lock — do NOT run it on every deploy
    /// against a busy table; schedule it in a maintenance window or pass <c>concurrently: true</c>
    /// (PostgreSQL 12+, slower but lock-friendly).
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Table whose indexes should be reorganized.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
    /// <param name="concurrently">PostgreSQL only: use <c>REINDEX TABLE CONCURRENTLY</c>. Ignored elsewhere.</param>
}
