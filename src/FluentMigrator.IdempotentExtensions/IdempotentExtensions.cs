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
/// Idempotent extension methods for FluentMigrator migrations.
/// All methods check existence before applying DDL — safe to run multiple times.
/// </summary>
public static partial class IdempotentExtensions
{
    /// <summary>
    /// Adds a standard <c>id INT NOT NULL PRIMARY KEY IDENTITY</c> column.
    /// </summary>
    public static ICreateTableColumnOptionOrWithColumnSyntax WithIdColumn(
        this ICreateTableWithColumnSyntax tableWithColumnSyntax)
    {
        return tableWithColumnSyntax
            .WithColumn("id").AsInt32().NotNullable().PrimaryKey().Identity();
    }

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
}
