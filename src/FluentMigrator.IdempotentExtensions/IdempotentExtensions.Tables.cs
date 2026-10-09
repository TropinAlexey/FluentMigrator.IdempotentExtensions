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
    /// <summary>
    /// Creates a table only if it does not already exist in the specified schema.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Name of the table to create.</param>
    /// <param name="constructTable">Fluent builder delegate that defines columns and constraints.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database
    /// provider (<c>dbo</c> for SQL Server, <c>public</c> for PostgreSQL, empty string for MySQL/SQLite).
    /// Pass an explicit value to target a specific schema (e.g. multi-tenant setups).</param>
    /// <returns>The fluent syntax result, or <c>null</c> if the table already exists.</returns>
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
    /// Creates an audit log table if it does not already exist.
    /// The table includes: <c>id</c>, <c>timestamp</c>, <c>username</c>, <c>action</c>, <c>record_id</c>.
    /// The default log table name is <c>{tableName}_log</c>; supply <paramref name="logTableName"/> to override.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Base table name; the log table will be named <c>{tableName}_log</c> unless overridden.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database
    /// provider (<c>dbo</c> for SQL Server, <c>public</c> for PostgreSQL, empty string for MySQL/SQLite).
    /// Pass an explicit value to target a specific schema (e.g. multi-tenant setups).</param>
    /// <param name="logTableName">Explicit log table name. Defaults to <c>{tableName}_log</c> if omitted.</param>
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
    /// Drops <paramref name="tableName"/> if it exists.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Name of the table to drop.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database
    /// provider (<c>dbo</c> for SQL Server, <c>public</c> for PostgreSQL, empty string for MySQL/SQLite).
    /// Pass an explicit value to target a specific schema (e.g. multi-tenant setups).</param>
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
}
