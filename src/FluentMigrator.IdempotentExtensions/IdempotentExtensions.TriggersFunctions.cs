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
/// Triggers and functions.
/// </summary>
public static partial class IdempotentExtensions
{
    public static void DropTriggerIfExists(this Migration self, string triggerName, string tableName, string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();
        var provider = self.GetProvider();

        if (provider == DatabaseProvider.Postgres)
        {
            self.Execute.Sql($"DROP TRIGGER IF EXISTS {QuoteIdent(provider, triggerName)} ON {QualifyTable(provider, schemaName, tableName)};");
            return;
        }

        if (provider == DatabaseProvider.Oracle)
        {
            // Oracle has no DROP TRIGGER IF EXISTS: swallow ORA-04080 ("trigger does not exist").
            var qualified = string.IsNullOrEmpty(schemaName) ? triggerName : $"{schemaName}.{triggerName}";
            self.Execute.Sql($@"
BEGIN
    EXECUTE IMMEDIATE 'DROP TRIGGER {EscapeLiteral(qualified)}';
EXCEPTION
    WHEN OTHERS THEN
        IF SQLCODE != -4080 THEN
            RAISE;
        END IF;
END;");
            return;
        }

        self.Execute.Sql($"DROP TRIGGER IF EXISTS {QuoteIdent(provider, triggerName)};");
    }

    /// <summary>
    /// Creates <paramref name="triggerName"/> by first dropping any existing trigger of the same name
    /// (via <see cref="DropTriggerIfExists"/>), then executing <paramref name="createTriggerSql"/> verbatim.
    /// </summary>
    /// <remarks>
    /// Trigger bodies are inherently provider-specific (T-SQL vs PL/pgSQL vs MySQL trigger syntax) and there
    /// is no portable <c>CREATE TRIGGER IF NOT EXISTS</c>/<c>OR REPLACE</c> across SQL Server, PostgreSQL and
    /// MySQL — so the caller supplies the full <c>CREATE TRIGGER</c> statement for their target provider, and
    /// this only makes re-running that statement safe by dropping the old trigger first.
    /// <paramref name="createTriggerSql"/> is executed verbatim — trusted developer input only.
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="triggerName">Name of the trigger being created.</param>
    /// <param name="tableName">Table the trigger is defined on (only used for the PostgreSQL drop syntax).</param>
    /// <param name="createTriggerSql">The full, provider-specific <c>CREATE TRIGGER ...</c> statement.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
    public static void CreateTriggerIfNotExists(
        this Migration self,
        string triggerName,
        string tableName,
        string createTriggerSql,
        string? schemaName = null)
    {
        self.DropTriggerIfExists(triggerName, tableName, schemaName);
        self.Execute.Sql(createTriggerSql);
    }

    /// <summary>
    /// Drops <paramref name="functionName"/> if it exists. Only supported on SQL Server, PostgreSQL and
    /// Oracle — MySQL/MariaDB and SQLite have no comparable general-purpose SQL function feature.
    /// </summary>
    /// <remarks>
    /// On PostgreSQL the zero-argument form is used (<c>DROP FUNCTION ... ();</c>). Overloaded
    /// functions (same name, different signatures) cannot be addressed without their full argument
    /// list — pass the name of a non-overloaded function, or manage overloads with raw SQL.
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="functionName">Name of the function to drop.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
    public static void DropFunctionIfExists(this Migration self, string functionName, string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();
        var provider = self.GetProvider();

        if (provider == DatabaseProvider.SqlServer)
        {
            self.Execute.Sql($"DROP FUNCTION IF EXISTS [{EscapeBracket(schemaName)}].[{EscapeBracket(functionName)}];");
            return;
        }

        if (provider == DatabaseProvider.Postgres)
        {
            self.Execute.Sql($"DROP FUNCTION IF EXISTS {QualifyTable(provider, schemaName, functionName)}();");
            return;
        }

        if (provider == DatabaseProvider.Oracle)
        {
            // Oracle has no DROP FUNCTION IF EXISTS: swallow ORA-04043 ("object does not exist").
            var qualified = string.IsNullOrEmpty(schemaName) ? functionName : $"{schemaName}.{functionName}";
            self.Execute.Sql($@"
BEGIN
    EXECUTE IMMEDIATE 'DROP FUNCTION {EscapeLiteral(qualified)}';
EXCEPTION
    WHEN OTHERS THEN
        IF SQLCODE != -4043 THEN
            RAISE;
        END IF;
END;");
            return;
        }

        throw new NotSupportedException("DropFunctionIfExists is only supported on SQL Server, PostgreSQL and Oracle.");
    }

    /// <summary>
    /// Creates <paramref name="functionName"/> by first dropping any existing function of the same name
    /// (via <see cref="DropFunctionIfExists"/>), then executing <paramref name="createFunctionSql"/> verbatim.
    /// Only supported on SQL Server and PostgreSQL. Mainly useful for PostgreSQL trigger functions, which
    /// must exist before a trigger created via <see cref="CreateTriggerIfNotExists"/> can reference them.
    /// <paramref name="createFunctionSql"/> is executed verbatim — trusted developer input only.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="functionName">Name of the function being created.</param>
    /// <param name="createFunctionSql">The full, provider-specific <c>CREATE FUNCTION ...</c> statement.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
    public static void CreateFunctionIfNotExists(
        this Migration self,
        string functionName,
        string createFunctionSql,
        string? schemaName = null)
    {
        self.DropFunctionIfExists(functionName, schemaName);
        self.Execute.Sql(createFunctionSql);
    }

    /// <summary>
    /// Renames the index <paramref name="oldName"/> to <paramref name="newName"/> on <paramref name="tableName"/>
    /// if it exists. Not supported on SQLite (no rename-index DDL; the index would need to be dropped and
    /// recreated from its original definition, which this method does not have).
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Table the index is defined on.</param>
    /// <param name="oldName">Current index name.</param>
    /// <param name="newName">New index name.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
}
