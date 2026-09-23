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
/// Views.
/// </summary>
public static partial class IdempotentExtensions
{
    public static void CreateViewIfNotExists(
        this Migration self,
        string viewName,
        string selectSql,
        string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();
        var provider = self.GetProvider();

        if (provider == DatabaseProvider.SqlServer)
        {
            var escSchema = EscapeBracket(schemaName);
            var escView = EscapeBracket(viewName);
            self.Execute.Sql($@"
IF NOT EXISTS (SELECT 1 FROM sys.views WHERE object_id = OBJECT_ID(N'[{escSchema}].[{escView}]'))
    EXEC sp_executesql N'CREATE VIEW [{escSchema}].[{escView}] AS {selectSql.Replace("'", "''")}';");
            return;
        }

        if (provider == DatabaseProvider.SQLite)
        {
            if (!string.IsNullOrEmpty(schemaName))
                throw new ArgumentException("SQLite has no schema support — schemaName must be empty.", nameof(schemaName));
            self.Execute.Sql($"CREATE VIEW IF NOT EXISTS {QuoteIdent(provider, viewName)} AS {selectSql};");
            return;
        }

        self.Execute.Sql($"CREATE OR REPLACE VIEW {QualifyTable(provider, schemaName, viewName)} AS {selectSql};");
    }

    /// <summary>
    /// Drops <paramref name="viewName"/> if it exists, via the native <c>DROP VIEW IF EXISTS</c> — supported
    /// by SQL Server (2016+), PostgreSQL, MySQL, and SQLite alike. On Oracle (which has no
    /// <c>DROP VIEW IF EXISTS</c>) the ORA-00942 "table or view does not exist" error is swallowed instead.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="viewName">Name of the view to drop.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
    public static void DropViewIfExists(this Migration self, string viewName, string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();
        var provider = self.GetProvider();

        if (provider == DatabaseProvider.Oracle)
        {
            var qualified = string.IsNullOrEmpty(schemaName) ? viewName : $"{schemaName}.{viewName}";
            self.Execute.Sql($@"
BEGIN
    EXECUTE IMMEDIATE 'DROP VIEW {EscapeLiteral(qualified)}';
EXCEPTION
    WHEN OTHERS THEN
        IF SQLCODE != -942 THEN
            RAISE;
        END IF;
END;");
            return;
        }

        self.Execute.Sql($"DROP VIEW IF EXISTS {QualifyTable(provider, schemaName, viewName)};");
    }

    /// <summary>
    /// Drops <paramref name="triggerName"/> if it exists, via the native <c>DROP TRIGGER IF EXISTS</c> —
    /// supported by SQL Server (2016+), PostgreSQL, MySQL, and SQLite alike. PostgreSQL additionally requires
    /// the owning table, via <c>ON {tableName}</c>.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="triggerName">Name of the trigger to drop.</param>
    /// <param name="tableName">Table the trigger is defined on (only used for the PostgreSQL syntax).</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
    public static void CreateOrReplaceView(
        this Migration self,
        string viewName,
        string selectSql,
        string? schemaName = null)
    {
        self.DropViewIfExists(viewName, schemaName);
        self.CreateViewIfNotExists(viewName, selectSql, schemaName);
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
}
