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
}
