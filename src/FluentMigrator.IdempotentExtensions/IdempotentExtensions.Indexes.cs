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
/// Indexes.
/// </summary>
public static partial class IdempotentExtensions
{
    /// <summary>
    /// Creates an index on <paramref name="columnName"/> if it does not already exist.
    /// The default index name is <c>index_{columnName}</c>; supply <paramref name="indexName"/> to override.
    /// </summary>
    /// <remarks>
    /// The default name does not include the table, so indexing the same column name on two tables
    /// with defaults collides (and PostgreSQL requires index names to be unique per schema, so the
    /// second <c>CREATE INDEX</c> would fail outright) — pass an explicit <paramref name="indexName"/>
    /// in that case. The default is kept for backward compatibility.
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Target table name.</param>
    /// <param name="columnName">Column to index.</param>
    /// <param name="configureIndex">Callback to configure ascending/descending and uniqueness.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database
    /// provider (<c>dbo</c> for SQL Server, <c>public</c> for PostgreSQL, empty string for MySQL/SQLite).
    /// Pass an explicit value to target a specific schema (e.g. multi-tenant setups).</param>
    /// <param name="indexName">Explicit index name. Defaults to <c>index_{columnName}</c> if omitted.</param>
    public static IFluentSyntax? CreateIndexIfNotExists(
        this MigrationBase self,
        string tableName,
        string columnName,
        Func<ICreateIndexColumnOptionsSyntax, IFluentSyntax> configureIndex,
        string? schemaName = null,
        string? indexName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();
        indexName ??= $"index_{columnName}";

        return !self.Schema.Schema(schemaName).Table(tableName).Index(indexName).Exists()
            ? configureIndex(self.Create.Index(indexName).OnTable(tableName).InSchema(schemaName).OnColumn(columnName))
            : null;
    }

    /// <summary>
    /// Creates a composite index on <paramref name="columns"/> if it does not already exist.
    /// The default index name is <c>index_{col1}_{col2}_…</c>; supply <paramref name="indexName"/> to override.
    /// </summary>
    /// <remarks>
    /// The <paramref name="columns"/> are pre-applied <c>Ascending()</c> before
    /// <paramref name="configureIndex"/> runs (kept for backward compatibility — existing callers
    /// like <c>idx =&gt; idx.WithOptions().Unique()</c> rely on it), so per-column sort direction
    /// cannot be changed through this method. For mixed ASC/DESC indexes use raw SQL or an
    /// explicit <c>Create.Index(...)</c> guarded by an existence check.
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Target table name.</param>
    /// <param name="columns">Columns to include in the composite index (in order).</param>
    /// <param name="configureIndex">Callback to configure uniqueness, clustering, etc.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database
    /// provider (<c>dbo</c> for SQL Server, <c>public</c> for PostgreSQL, empty string for MySQL/SQLite).
    /// Pass an explicit value to target a specific schema (e.g. multi-tenant setups).</param>
    /// <param name="indexName">Explicit index name. Auto-generated from <paramref name="columns"/> if omitted.</param>
    public static IFluentSyntax? CreateCompositeIndexIfNotExists(
        this MigrationBase self,
        string tableName,
        string[] columns,
        Func<ICreateIndexOnColumnSyntax, IFluentSyntax> configureIndex,
        string? schemaName = null,
        string? indexName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();
        indexName ??= $"index_{string.Join("_", columns)}";

        if (self.Schema.Schema(schemaName).Table(tableName).Index(indexName).Exists())
            return null;

        var index = self.Create.Index(indexName)
            .OnTable(tableName)
            .InSchema(schemaName);

        foreach (var col in columns)
            index.OnColumn(col).Ascending();

        return configureIndex(index);
    }

    /// <summary>
    /// Drops the named index on <paramref name="columnName"/> if it exists.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Target table name.</param>
    /// <param name="columnName">Column the index is defined on.</param>
    /// <param name="indexName">Name of the index to drop.</param>
    /// <param name="configureDelete">Callback to configure additional delete options.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database
    /// provider (<c>dbo</c> for SQL Server, <c>public</c> for PostgreSQL, empty string for MySQL/SQLite).
    /// Pass an explicit value to target a specific schema (e.g. multi-tenant setups).</param>
    public static IFluentSyntax? DropIndexIfExists(
        this Migration self,
        string tableName,
        string columnName,
        string indexName,
        Func<IDeleteIndexOptionsSyntax, IFluentSyntax> configureDelete,
        string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();

        return self.Schema.Schema(schemaName).Table(tableName).Index(indexName).Exists()
            ? configureDelete(self.Delete.Index(indexName).OnTable(tableName).InSchema(schemaName).OnColumn(columnName))
            : null;
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
    public static void RenameIndexIfExists(
        this Migration self,
        string tableName,
        string oldName,
        string newName,
        string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();

        if (!self.Schema.Schema(schemaName).Table(tableName).Index(oldName).Exists())
            return;

        var provider = self.GetProvider();

        if (provider == DatabaseProvider.SqlServer)
        {
            self.Execute.Sql($"EXEC sp_rename N'{EscapeLiteral(schemaName)}.{EscapeLiteral(tableName)}.{EscapeLiteral(oldName)}', N'{EscapeLiteral(newName)}', N'INDEX';");
            return;
        }

        if (provider == DatabaseProvider.Postgres)
        {
            self.Execute.Sql($"ALTER INDEX {QualifyTable(provider, schemaName, oldName)} RENAME TO {QuoteIdent(provider, newName)};");
            return;
        }

        if (provider is DatabaseProvider.MySql or DatabaseProvider.MariaDb)
        {
            self.Execute.Sql($"ALTER TABLE {QualifyTable(provider, schemaName, tableName)} RENAME INDEX {QuoteIdent(provider, oldName)} TO {QuoteIdent(provider, newName)};");
            return;
        }

        if (provider == DatabaseProvider.Oracle)
        {
            // Oracle index names are unique per schema, so no table qualifier is needed — but the
            // schema itself still must be, otherwise this targets the connected user's default schema.
            // No trailing semicolon on Oracle direct DDL (ORA-00911).
            self.Execute.Sql($"ALTER INDEX {QualifyTable(provider, schemaName, oldName)} RENAME TO {QuoteIdent(provider, newName)}");
            return;
        }

        throw new NotSupportedException("RenameIndexIfExists is not supported on SQLite.");
    }
}
