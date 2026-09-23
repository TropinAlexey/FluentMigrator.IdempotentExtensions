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
/// Tables.
/// </summary>
public static partial class IdempotentExtensions
{
    public static IFluentSyntax? CreateTableIfNotExists(
        this Migration self,
        string tableName,
        Func<ICreateTableWithColumnOrSchemaOrDescriptionSyntax, IFluentSyntax> constructTable,
        string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();

        if (!self.TableExists(tableName, schemaName))
        {
            // InSchema mutates the shared CreateTableExpression in place (and returns the same
            // builder narrowed), so call it for the side effect and keep passing the wide
            // interface the constructTable delegate expects.
            var table = self.Create.Table(tableName);
            table.InSchema(schemaName);
            return constructTable(table);
        }

        return null;
    }

    /// <summary>
    /// Adds a column to <paramref name="tableName"/> only if it does not already exist.
    /// Returns <c>null</c> if the column already exists or the table does not exist.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Target table name.</param>
    /// <param name="colName">Name of the column to add.</param>
    /// <param name="constructCol">Fluent builder callback that defines the column type and constraints.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database
    /// provider (<c>dbo</c> for SQL Server, <c>public</c> for PostgreSQL, empty string for MySQL/SQLite).
    /// Pass an explicit value to target a specific schema (e.g. multi-tenant setups).</param>
    /// <returns>The fluent syntax result, or <c>null</c> if the column already existed or the table does not exist.</returns>
    public static void CreateLogTableIfNotExists(
        this Migration self,
        string tableName,
        string? schemaName = null,
        string? logTableName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();
        logTableName ??= $"{tableName}_log";

        if (self.TableExists(logTableName, schemaName))
            return;

        self.Create.Table(logTableName).InSchema(schemaName)
            .WithIdColumn()
            .WithColumn("timestamp").AsDateTime().Nullable()
            .WithColumn("username").AsAnsiString(500)
            .WithColumn("action").AsAnsiString(50)
            .WithColumn("record_id").AsInt32().NotNullable();
    }

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
    public static void DropTableIfExists(
        this Migration self,
        string tableName,
        string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();

        if (self.Schema.Schema(schemaName).Table(tableName).Exists())
            self.Delete.Table(tableName).InSchema(schemaName);
    }

    /// <summary>
    /// Renames <paramref name="oldName"/> table to <paramref name="newName"/> if the source table exists.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="oldName">Current table name.</param>
    /// <param name="newName">New table name.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database
    /// provider (<c>dbo</c> for SQL Server, <c>public</c> for PostgreSQL, empty string for MySQL/SQLite).
    /// Pass an explicit value to target a specific schema (e.g. multi-tenant setups).</param>
    public static void RenameTableIfExists(
        this Migration self,
        string oldName,
        string newName,
        string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();

        if (self.TableExists(oldName, schemaName))
            self.Rename.Table(oldName).InSchema(schemaName).To(newName);
    }

    /// <summary>
    /// Creates a named UNIQUE constraint on <paramref name="columns"/> if it does not already exist.
    /// Works on all databases supported by FluentMigrator.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Target table name.</param>
    /// <param name="constraintName">Name of the unique constraint to create.</param>
    /// <param name="columns">Columns included in the constraint.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database
    /// provider (<c>dbo</c> for SQL Server, <c>public</c> for PostgreSQL, empty string for MySQL/SQLite).
    /// Pass an explicit value to target a specific schema (e.g. multi-tenant setups).</param>
}
