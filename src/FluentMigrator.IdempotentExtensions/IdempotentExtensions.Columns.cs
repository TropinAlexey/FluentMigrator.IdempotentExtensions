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
/// Columns and defaults.
/// </summary>
public static partial class IdempotentExtensions
{
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
    public static IFluentSyntax? CreateColumnIfNotExists(
        this Migration self,
        string tableName,
        string colName,
        Func<IAlterTableColumnAsTypeSyntax, IFluentSyntax> constructCol,
        string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();

        if (!self.TableExists(tableName, schemaName))
            return null;
        if (self.ColumnExists(tableName, colName, schemaName))
            return null;

        return constructCol(self.Alter.Table(tableName).InSchema(schemaName).AddColumn(colName));
    }

    /// <summary>
    /// Alters an existing column on <paramref name="tableName"/> only if the column already exists.
    /// Returns <c>null</c> if the table or column does not exist.
    /// </summary>
    /// <remarks>
    /// This only guards against a missing table/column — unlike <see cref="CreateColumnIfNotExists"/>, it does not
    /// compare the current column definition to the target one, so it applies <paramref name="constructCol"/>
    /// unconditionally whenever the column is present. Not supported on SQLite (no native ALTER COLUMN).
    /// On Oracle specifically, a <paramref name="constructCol"/> that calls <c>.Nullable()</c> on a column
    /// that's already nullable throws ORA-01451 (<c>MODIFY col ... NULL</c> is rejected when nullability
    /// doesn't change) — this can fail on the very first call, not just a rerun, and is an inherent Oracle
    /// restriction this method can't suppress. Only affects calls that leave nullability unchanged; a genuine
    /// nullability flip (<c>NotNullable()</c> &lt;-&gt; <c>Nullable()</c>) works normally.
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Target table name.</param>
    /// <param name="colName">Name of the column to alter.</param>
    /// <param name="constructCol">Fluent builder callback that redefines the column type and constraints.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database
    /// provider (<c>dbo</c> for SQL Server, <c>public</c> for PostgreSQL, empty string for MySQL/SQLite).
    /// Pass an explicit value to target a specific schema (e.g. multi-tenant setups).</param>
    /// <returns>The fluent syntax result, or <c>null</c> if the table or column does not exist.</returns>
    public static IFluentSyntax? AlterColumnIfExists(
        this Migration self,
        string tableName,
        string colName,
        Func<IAlterTableColumnAsTypeSyntax, IFluentSyntax> constructCol,
        string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();

        if (!self.ColumnExists(tableName, colName, schemaName))
            return null;

        return constructCol(self.Alter.Table(tableName).InSchema(schemaName).AlterColumn(colName));
    }

    /// <summary>
    /// Removes a column from <paramref name="tableName"/> if it exists.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Target table name.</param>
    /// <param name="colName">Name of the column to remove.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database
    /// provider (<c>dbo</c> for SQL Server, <c>public</c> for PostgreSQL, empty string for MySQL/SQLite).
    /// Pass an explicit value to target a specific schema (e.g. multi-tenant setups).</param>
    public static void DeleteColumnIfExists(
        this Migration self,
        string tableName,
        string colName,
        string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();

        if (self.Schema.Schema(schemaName).Table(tableName).Column(colName).Exists())
            self.Delete.Column(colName).FromTable(tableName).InSchema(schemaName);
    }

    /// <summary>
    /// Drops the default value on <paramref name="columnName"/> if one is set — safe to call on any provider.
    /// </summary>
    /// <remarks>
    /// On SQL Server, DEFAULT constraints are auto-named objects, so this locates the actual constraint via
    /// <c>sys.default_constraints</c> and drops it with a single conditional T-SQL block. On PostgreSQL and MySQL,
    /// <c>ALTER COLUMN ... DROP DEFAULT</c> is itself a no-op when the column has no default, so it is executed
    /// directly. Not supported on SQLite (throws — no default-constraint concept separate from the column).
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Target table name.</param>
    /// <param name="columnName">Column whose default value should be removed.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database
    /// provider (<c>dbo</c> for SQL Server, <c>public</c> for PostgreSQL, empty string for MySQL/SQLite).
    /// Pass an explicit value to target a specific schema (e.g. multi-tenant setups).</param>
    public static void DropColumnDefaultIfExists(
        this Migration self,
        string tableName,
        string columnName,
        string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();
        var provider = self.GetProvider();

        if (provider == DatabaseProvider.SqlServer)
        {
            var escSchema = EscapeBracket(schemaName);
            var escTable = EscapeBracket(tableName);
            var escColumn = EscapeLiteral(columnName);
            self.Execute.Sql($@"
IF EXISTS (
    SELECT 1
    FROM sys.default_constraints dc
    JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'[{escSchema}].[{escTable}]')
      AND c.name = N'{escColumn}'
)
BEGIN
    DECLARE @constraintName SYSNAME;
    DECLARE @sql NVARCHAR(MAX);

    DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
        SELECT dc.name
        FROM sys.default_constraints dc
        JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
        WHERE dc.parent_object_id = OBJECT_ID(N'[{escSchema}].[{escTable}]')
          AND c.name = N'{escColumn}';

    OPEN cur;
    FETCH NEXT FROM cur INTO @constraintName;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @sql = N'ALTER TABLE [{escSchema}].[{escTable}] DROP CONSTRAINT ' + QUOTENAME(@constraintName);
        EXEC sp_executesql @sql;
        FETCH NEXT FROM cur INTO @constraintName;
    END;
    CLOSE cur;
    DEALLOCATE cur;
END");
            return;
        }

        if (provider == DatabaseProvider.Oracle)
        {
            // No trailing semicolon: Oracle does not allow it on direct DDL (ORA-00911).
            self.Execute.Sql($"ALTER TABLE {QualifyTable(provider, schemaName, tableName)} MODIFY {QuoteIdent(provider, columnName)} DEFAULT NULL");
            return;
        }

        // PostgreSQL/MySQL: dropping a default that isn't set is a harmless no-op.
        // SQLite: throws FluentMigrator's own "not supported" error, same as other SQLite-unsupported methods.
        self.Delete.DefaultConstraint().OnTable(tableName).InSchema(schemaName).OnColumn(columnName);
    }

    /// <summary>
    /// Renames <paramref name="oldName"/> column to <paramref name="newName"/> if the source column exists.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Target table name.</param>
    /// <param name="oldName">Current column name.</param>
    /// <param name="newName">New column name.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database
    /// provider (<c>dbo</c> for SQL Server, <c>public</c> for PostgreSQL, empty string for MySQL/SQLite).
    /// Pass an explicit value to target a specific schema (e.g. multi-tenant setups).</param>
    public static void RenameColumnIfExists(
        this Migration self,
        string tableName,
        string oldName,
        string newName,
        string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();

        if (self.Schema.Schema(schemaName).Table(tableName).Column(oldName).Exists())
            self.Rename.Column(oldName).OnTable(tableName).InSchema(schemaName).To(newName);
    }

    /// <summary>
    /// Sets a default value on <paramref name="columnName"/> if the column exists; a no-op if it does not.
    /// Companion to <see cref="DropColumnDefaultIfExists"/>. Not supported on SQLite (no <c>ALTER COLUMN</c>).
    /// </summary>
    /// <remarks>
    /// On PostgreSQL and MySQL, <c>ALTER COLUMN ... SET DEFAULT</c> simply overwrites any existing default, so
    /// it is executed directly. On SQL Server, DEFAULT constraints must be explicitly named and cannot coexist
    /// with an existing one on the same column, so the existing default is checked for first via
    /// <c>sys.default_constraints</c>.
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Target table name.</param>
    /// <param name="columnName">Column to set the default value on.</param>
    /// <param name="defaultValue">The default value. Formatted as a SQL literal the same way as
    /// <see cref="InsertDataIfNotExists(Migration, string, IReadOnlyDictionary{string, object?}, IReadOnlyDictionary{string, object?}?, string?)"/> values.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
    /// <param name="constraintName">SQL Server only: explicit name for the created DEFAULT constraint.
    /// Defaults to <c>DF_{tableName}_{columnName}</c> if omitted. Ignored on other providers
    /// (their defaults are unnamed).</param>
    public static void AddColumnDefaultIfExists(
        this Migration self,
        string tableName,
        string columnName,
        object? defaultValue,
        string? schemaName = null,
        string? constraintName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();

        if (!self.ColumnExists(tableName, columnName, schemaName))
            return;

        var provider = self.GetProvider();
        var formattedValue = FormatSqlValue(defaultValue, provider);

        if (provider == DatabaseProvider.SqlServer)
        {
            var escSchema = EscapeBracket(schemaName);
            var escTable = EscapeBracket(tableName);
            var escColumn = EscapeBracket(columnName);
            var escConstraint = EscapeBracket(constraintName ?? $"DF_{tableName}_{columnName}");
            self.Execute.Sql($@"
IF NOT EXISTS (
    SELECT 1
    FROM sys.default_constraints dc
    JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'[{escSchema}].[{escTable}]')
      AND c.name = N'{EscapeLiteral(columnName)}'
)
    ALTER TABLE [{escSchema}].[{escTable}] ADD CONSTRAINT [{escConstraint}] DEFAULT {formattedValue} FOR [{escColumn}];");
            return;
        }

        if (provider == DatabaseProvider.Oracle)
        {
            self.Execute.Sql($"ALTER TABLE {QualifyTable(provider, schemaName, tableName)} MODIFY {QuoteIdent(provider, columnName)} DEFAULT {formattedValue}");
            return;
        }

        self.Execute.Sql($"ALTER TABLE {QualifyTable(provider, schemaName, tableName)} ALTER COLUMN {QuoteIdent(provider, columnName)} SET DEFAULT {formattedValue};");
    }
}
