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
}
