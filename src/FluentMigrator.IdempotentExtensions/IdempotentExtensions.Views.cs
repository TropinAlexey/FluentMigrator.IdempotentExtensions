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
    /// <summary>
    /// Creates <paramref name="viewName"/> if it does not already exist.
    /// </summary>
    /// <remarks>
    /// On PostgreSQL and MySQL, uses <c>CREATE OR REPLACE VIEW</c>, so re-running with the same
    /// <paramref name="selectSql"/> is a harmless no-op (the view is simply redefined identically).
    /// On SQLite, uses the native <c>CREATE VIEW IF NOT EXISTS</c>. On SQL Server, which supports neither,
    /// the existence check is done via <c>sys.views</c> and the view is created via dynamic SQL.
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="viewName">Name of the view to create.</param>
    /// <param name="selectSql">The view's <c>SELECT</c> statement, without the <c>CREATE VIEW ... AS</c> prefix. Executed verbatim — trusted developer input only.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider. Ignored on SQLite (which has no schemas) — passing a non-empty value throws.</param>
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
    /// Drops and recreates <paramref name="viewName"/> in a single call — equivalent to
    /// <see cref="DropViewIfExists"/> followed by <see cref="CreateViewIfNotExists"/>.
    /// If the view does not exist, it is simply created.
    /// </summary>
    /// <remarks>
    /// On SQL Server and SQLite the replace is a DROP followed by CREATE, which discards
    /// permissions granted on the view — re-apply them afterwards if needed. On PostgreSQL
    /// and MySQL (<c>CREATE OR REPLACE</c>) existing grants are preserved.
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="viewName">Name of the view to create or replace.</param>
    /// <param name="selectSql">The view's <c>SELECT</c> statement, without the <c>CREATE VIEW ... AS</c> prefix.</param>
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
}
