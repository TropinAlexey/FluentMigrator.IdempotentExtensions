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
/// Constraints and keys.
/// </summary>
public static partial class IdempotentExtensions
{
    public static void DropConstraintIfExists(
        this Migration self,
        string tableName,
        string constraintName,
        string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();

        if (self.Schema.Schema(schemaName).Table(tableName).Constraint(constraintName).Exists())
            self.Delete.UniqueConstraint(constraintName).FromTable(tableName).InSchema(schemaName);
    }

    /// <summary>
    /// Drops the primary key or unique constraint named <paramref name="keyName"/> if it exists.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Target table name.</param>
    /// <param name="keyName">Name of the constraint to drop.</param>
    /// <param name="configureDelete">Callback to configure additional delete options.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database
    /// provider (<c>dbo</c> for SQL Server, <c>public</c> for PostgreSQL, empty string for MySQL/SQLite).
    /// Pass an explicit value to target a specific schema (e.g. multi-tenant setups).</param>
    public static IFluentSyntax? DropPrimaryKeyIfExists(
        this Migration self,
        string tableName,
        string keyName,
        Func<IDeleteConstraintInSchemaOptionsSyntax, IFluentSyntax> configureDelete,
        string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();

        return self.Schema.Schema(schemaName).Table(tableName).Constraint(keyName).Exists()
            ? configureDelete(self.Delete.PrimaryKey(keyName).FromTable(tableName).InSchema(schemaName))
            : null;
    }

    /// <summary>
    /// Creates a named PRIMARY KEY constraint on <paramref name="columns"/> if it does not already exist.
    /// Useful when the primary key needs to be added after the table already exists (e.g. legacy tables).
    /// Not supported on SQLite (FluentMigrator's SQLite generator does not implement this DDL).
    /// </summary>
    /// <remarks>
    /// On MySQL, primary key constraints are always physically named <c>PRIMARY</c> regardless of the name
    /// requested at creation time, so the existence check looks for that fixed name instead of
    /// <paramref name="keyName"/> — checking by <paramref name="keyName"/> would never match an existing
    /// key and the second run would fail with "Multiple primary key defined".
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Target table name.</param>
    /// <param name="keyName">Name of the primary key constraint to create.</param>
    /// <param name="columns">Columns included in the primary key.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database
    /// provider (<c>dbo</c> for SQL Server, <c>public</c> for PostgreSQL, empty string for MySQL/SQLite).
    /// Pass an explicit value to target a specific schema (e.g. multi-tenant setups).</param>
    public static void CreatePrimaryKeyIfNotExists(
        this Migration self,
        string tableName,
        string keyName,
        string[] columns,
        string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();

        var existingKeyName = self.GetProvider() is DatabaseProvider.MySql or DatabaseProvider.MariaDb
            ? "PRIMARY"
            : keyName;

        if (!self.Schema.Schema(schemaName).Table(tableName).Constraint(existingKeyName).Exists())
            self.Create.PrimaryKey(keyName)
                .OnTable(tableName)
                .WithSchema(schemaName)
                .Columns(columns);
    }

    /// <summary>
    /// Drops <paramref name="tableName"/> if it exists.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Name of the table to drop.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database
    /// provider (<c>dbo</c> for SQL Server, <c>public</c> for PostgreSQL, empty string for MySQL/SQLite).
    /// Pass an explicit value to target a specific schema (e.g. multi-tenant setups).</param>
    public static void CreateUniqueConstraintIfNotExists(
        this Migration self,
        string tableName,
        string constraintName,
        string[] columns,
        string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();

        if (!self.Schema.Schema(schemaName).Table(tableName).Constraint(constraintName).Exists())
            self.Create.UniqueConstraint(constraintName)
                .OnTable(tableName)
                .WithSchema(schemaName)
                .Columns(columns);
    }

    /// <summary>
    /// Creates a foreign key from <paramref name="tableName"/> to <paramref name="primaryTableName"/> if it does
    /// not already exist. Not supported on SQLite.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Table the foreign key is defined on (the referencing/child table).</param>
    /// <param name="foreignKeyName">Name of the foreign key constraint to create.</param>
    /// <param name="foreignColumns">Columns on <paramref name="tableName"/> that reference the primary table.</param>
    /// <param name="primaryTableName">Referenced/parent table name.</param>
    /// <param name="primaryColumns">Columns on <paramref name="primaryTableName"/> being referenced.</param>
    /// <param name="schemaName">Schema of <paramref name="tableName"/>. If <c>null</c>, auto-detected from the
    /// database provider.</param>
    /// <param name="primarySchemaName">Schema of <paramref name="primaryTableName"/>. Defaults to <paramref name="schemaName"/> when omitted.</param>
    public static void CreateForeignKeyIfNotExists(
        this Migration self,
        string tableName,
        string foreignKeyName,
        string[] foreignColumns,
        string primaryTableName,
        string[] primaryColumns,
        string? schemaName = null,
        string? primarySchemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();
        primarySchemaName ??= schemaName;

        if (self.Schema.Schema(schemaName).Table(tableName).Constraint(foreignKeyName).Exists())
            return;

        self.Create.ForeignKey(foreignKeyName)
            .FromTable(tableName).InSchema(schemaName).ForeignColumns(foreignColumns)
            .ToTable(primaryTableName).InSchema(primarySchemaName).PrimaryColumns(primaryColumns);
    }

    /// <summary>
    /// Drops the named foreign key from <paramref name="tableName"/> if it exists. Not supported on SQLite.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Table the foreign key is defined on.</param>
    /// <param name="foreignKeyName">Name of the foreign key constraint to drop.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
    public static void DropForeignKeyIfExists(
        this Migration self,
        string tableName,
        string foreignKeyName,
        string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();

        if (self.Schema.Schema(schemaName).Table(tableName).Constraint(foreignKeyName).Exists())
            self.Delete.ForeignKey(foreignKeyName).OnTable(tableName).InSchema(schemaName);
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
    public static void CreateCheckConstraintIfNotExists(
        this Migration self,
        string tableName,
        string constraintName,
        string checkSql,
        string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();

        if (self.Schema.Schema(schemaName).Table(tableName).Constraint(constraintName).Exists())
            return;

        var provider = self.GetProvider();
        // No trailing semicolon on Oracle direct DDL (ORA-00911).
        var terminator = provider == DatabaseProvider.Oracle ? "" : ";";
        self.Execute.Sql($"ALTER TABLE {QualifyTable(provider, schemaName, tableName)} ADD CONSTRAINT {QuoteIdent(provider, constraintName)} CHECK ({checkSql}){terminator}");
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
    /// <see cref="InsertDataIfNotExists"/> values.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
    /// <param name="constraintName">SQL Server only: explicit name for the created DEFAULT constraint.
    /// Defaults to <c>DF_{tableName}_{columnName}</c> if omitted. Ignored on other providers
    /// (their defaults are unnamed).</param>
    public static void RenameConstraintIfExists(
        this Migration self,
        string tableName,
        string oldName,
        string newName,
        string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();

        if (!self.Schema.Schema(schemaName).Table(tableName).Constraint(oldName).Exists())
            return;

        var provider = self.GetProvider();

        if (provider == DatabaseProvider.SqlServer)
        {
            // No @objtype: UNIQUE/PRIMARY KEY constraints are backed by an index (needs 'INDEX'), while
            // CHECK/DEFAULT/FOREIGN KEY constraints are true objects (needs 'OBJECT' or nothing) — since this
            // method doesn't know which kind of constraint it's renaming, claiming the wrong type makes
            // sp_rename fail with "the claimed @objtype is wrong". Omitting it lets SQL Server resolve it itself.
            self.Execute.Sql($"EXEC sp_rename N'{EscapeLiteral(schemaName)}.{EscapeLiteral(tableName)}.{EscapeLiteral(oldName)}', N'{EscapeLiteral(newName)}';");
            return;
        }

        if (provider == DatabaseProvider.Postgres)
        {
            self.Execute.Sql($"ALTER TABLE {QualifyTable(provider, schemaName, tableName)} RENAME CONSTRAINT {QuoteIdent(provider, oldName)} TO {QuoteIdent(provider, newName)};");
            return;
        }

        if (provider == DatabaseProvider.Oracle)
        {
            // No trailing semicolon on Oracle direct DDL (ORA-00911).
            self.Execute.Sql($"ALTER TABLE {QualifyTable(provider, schemaName, tableName)} RENAME CONSTRAINT {QuoteIdent(provider, oldName)} TO {QuoteIdent(provider, newName)}");
            return;
        }

        throw new NotSupportedException("RenameConstraintIfExists is only supported on SQL Server, PostgreSQL and Oracle.");
    }

    /// <summary>
    /// Updates rows in <paramref name="tableName"/> matching <paramref name="keyValues"/> with
    /// <paramref name="setValues"/>. Naturally idempotent — an <c>UPDATE</c> that matches zero rows (because
    /// they were already updated, or don't exist) is a safe no-op on every provider, so no existence guard
    /// is needed.
    /// </summary>
    /// <remarks>
    /// Uses the same portable value formatting as <see cref="InsertDataIfNotExists"/> (strings quote-escaped,
    /// <c>null</c> compared with <c>IS NULL</c>, <see cref="Guid"/> quoted, enums as their numeric value).
    /// Only whitelisted value types are accepted — anything else throws instead of being embedded blindly.
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Target table name.</param>
    /// <param name="keyValues">Column/value pairs identifying which rows to update. Must contain at least one entry.</param>
    /// <param name="setValues">Column/value pairs to set on the matched rows. Must contain at least one entry.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
}
