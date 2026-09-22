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
using System.Reflection;

/// <summary>
/// Idempotent extension methods for FluentMigrator migrations.
/// All methods check existence before applying DDL — safe to run multiple times.
/// </summary>
public static class IdempotentExtensions
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
        var databaseType = self.GetDatabaseType();

        if (databaseType.IndexOf("SqlServer", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var escSchema = EscapeBracket(schemaName);
            var escTable = EscapeBracket(tableName);
            var escColumn = columnName.Replace("'", "''");
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

        if (databaseType.IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            // No trailing semicolon: Oracle does not allow it on direct DDL (ORA-00911).
            self.Execute.Sql($"ALTER TABLE {QualifyTable(databaseType, schemaName, tableName)} MODIFY {QuoteIdent(databaseType, columnName)} DEFAULT NULL");
            return;
        }

        // PostgreSQL/MySQL: dropping a default that isn't set is a harmless no-op.
        // SQLite: throws FluentMigrator's own "not supported" error, same as other SQLite-unsupported methods.
        self.Delete.DefaultConstraint().OnTable(tableName).InSchema(schemaName).OnColumn(columnName);
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
    /// <param name="indexName">Explicit index name. Defaults to <c>index_{tableName}_{columnName}</c> if omitted.</param>
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
    /// Drops a named UNIQUE or CHECK constraint from <paramref name="tableName"/> if it exists.
    /// Works on all databases supported by FluentMigrator.
    /// For default constraints use <see cref="DropColumnDefaultIfExists"/>.
    /// </summary>
    /// <remarks>
    /// This issues a generic <c>DROP CONSTRAINT</c>, which covers UNIQUE and CHECK constraints on
    /// every provider. For foreign keys prefer <see cref="DropForeignKeyIfExists"/>, for primary
    /// keys prefer <see cref="DropPrimaryKeyIfExists"/>.
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Target table name.</param>
    /// <param name="constraintName">Name of the constraint to drop.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database
    /// provider (<c>dbo</c> for SQL Server, <c>public</c> for PostgreSQL, empty string for MySQL/SQLite).
    /// Pass an explicit value to target a specific schema (e.g. multi-tenant setups).</param>
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

        var existingKeyName = self.GetDatabaseType().IndexOf("MySql", StringComparison.OrdinalIgnoreCase) >= 0
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
    /// Creates <paramref name="schemaName"/> if it does not already exist.
    /// Not supported on SQLite.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="schemaName">Schema to create.</param>
    public static void CreateSchemaIfNotExists(this Migration self, string schemaName)
    {
        if (!self.Schema.Schema(schemaName).Exists())
            self.Create.Schema(schemaName);
    }

    /// <summary>
    /// Drops <paramref name="schemaName"/> if it exists.
    /// Not supported on SQLite.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="schemaName">Schema to drop.</param>
    public static void DropSchemaIfExists(this Migration self, string schemaName)
    {
        if (self.Schema.Schema(schemaName).Exists())
            self.Delete.Schema(schemaName);
    }

    /// <summary>
    /// Creates <paramref name="sequenceName"/> if it does not already exist.
    /// Not supported on MySQL/MariaDB or SQLite.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="sequenceName">Name of the sequence to create.</param>
    /// <param name="configureSequence">Optional callback to configure increment, min/max, start value, caching, cycling.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
    public static void CreateSequenceIfNotExists(
        this Migration self,
        string sequenceName,
        Action<ICreateSequenceSyntax>? configureSequence = null,
        string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();

        if (self.Schema.Schema(schemaName).Sequence(sequenceName).Exists())
            return;

        var sequence = self.Create.Sequence(sequenceName).InSchema(schemaName);
        configureSequence?.Invoke(sequence);
    }

    /// <summary>
    /// Drops <paramref name="sequenceName"/> if it exists.
    /// Not supported on MySQL/MariaDB or SQLite.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="sequenceName">Name of the sequence to drop.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
    public static void DropSequenceIfExists(
        this Migration self,
        string sequenceName,
        string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();

        if (self.Schema.Schema(schemaName).Sequence(sequenceName).Exists())
            self.Delete.Sequence(sequenceName).InSchema(schemaName);
    }

    /// <summary>
    /// Adds a named CHECK constraint on <paramref name="tableName"/> if it does not already exist.
    /// Not supported on SQLite (its <c>ALTER TABLE</c> cannot add constraints to an existing table).
    /// </summary>
    /// <remarks>
    /// To drop a check constraint, reuse <see cref="DropConstraintIfExists"/> — it issues a generic
    /// <c>DROP CONSTRAINT</c>, which SQL Server, PostgreSQL, and MySQL (8.0.19+) all accept for CHECK constraints.
    /// The <paramref name="checkSql"/> expression is executed verbatim — trusted developer input only,
    /// never end-user input.
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Target table name.</param>
    /// <param name="constraintName">Name of the CHECK constraint to create.</param>
    /// <param name="checkSql">The boolean SQL expression to check, without the surrounding parentheses
    /// (e.g. <c>"age &gt;= 0"</c>).</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
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

        var databaseType = self.GetDatabaseType();
        // No trailing semicolon on Oracle direct DDL (ORA-00911).
        var terminator = databaseType.IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0 ? "" : ";";
        self.Execute.Sql($"ALTER TABLE {QualifyTable(databaseType, schemaName, tableName)} ADD CONSTRAINT {QuoteIdent(databaseType, constraintName)} CHECK ({checkSql}){terminator}");
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

        var databaseType = self.GetDatabaseType();
        var formattedValue = FormatSqlValue(defaultValue, databaseType);

        if (databaseType.IndexOf("SqlServer", StringComparison.OrdinalIgnoreCase) >= 0)
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
      AND c.name = N'{columnName.Replace("'", "''")}'
)
    ALTER TABLE [{escSchema}].[{escTable}] ADD CONSTRAINT [{escConstraint}] DEFAULT {formattedValue} FOR [{escColumn}];");
            return;
        }

        if (databaseType.IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($"ALTER TABLE {QualifyTable(databaseType, schemaName, tableName)} MODIFY {QuoteIdent(databaseType, columnName)} DEFAULT {formattedValue}");
            return;
        }

        self.Execute.Sql($"ALTER TABLE {QualifyTable(databaseType, schemaName, tableName)} ALTER COLUMN {QuoteIdent(databaseType, columnName)} SET DEFAULT {formattedValue};");
    }

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
        var databaseType = self.GetDatabaseType();

        if (databaseType.IndexOf("SqlServer", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var escSchema = EscapeBracket(schemaName);
            var escView = EscapeBracket(viewName);
            self.Execute.Sql($@"
IF NOT EXISTS (SELECT 1 FROM sys.views WHERE object_id = OBJECT_ID(N'[{escSchema}].[{escView}]'))
    EXEC sp_executesql N'CREATE VIEW [{escSchema}].[{escView}] AS {selectSql.Replace("'", "''")}';");
            return;
        }

        if (databaseType.IndexOf("SQLite", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (!string.IsNullOrEmpty(schemaName))
                throw new ArgumentException("SQLite has no schema support — schemaName must be empty.", nameof(schemaName));
            self.Execute.Sql($"CREATE VIEW IF NOT EXISTS {QuoteIdent(databaseType, viewName)} AS {selectSql};");
            return;
        }

        self.Execute.Sql($"CREATE OR REPLACE VIEW {QualifyTable(databaseType, schemaName, viewName)} AS {selectSql};");
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
        var databaseType = self.GetDatabaseType();

        if (databaseType.IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0)
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

        self.Execute.Sql($"DROP VIEW IF EXISTS {QualifyTable(databaseType, schemaName, viewName)};");
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
    public static void DropTriggerIfExists(this Migration self, string triggerName, string tableName, string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();
        var databaseType = self.GetDatabaseType();

        if (databaseType.IndexOf("Postgres", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($"DROP TRIGGER IF EXISTS {QuoteIdent(databaseType, triggerName)} ON {QualifyTable(databaseType, schemaName, tableName)};");
            return;
        }

        if (databaseType.IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0)
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

        self.Execute.Sql($"DROP TRIGGER IF EXISTS {QuoteIdent(databaseType, triggerName)};");
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
        var databaseType = self.GetDatabaseType();

        if (databaseType.IndexOf("SqlServer", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($"DROP FUNCTION IF EXISTS [{EscapeBracket(schemaName)}].[{EscapeBracket(functionName)}];");
            return;
        }

        if (databaseType.IndexOf("Postgres", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($"DROP FUNCTION IF EXISTS {QualifyTable(databaseType, schemaName, functionName)}();");
            return;
        }

        if (databaseType.IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0)
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

        var databaseType = self.GetDatabaseType();

        if (databaseType.IndexOf("SqlServer", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($"EXEC sp_rename N'{EscapeLiteral(schemaName)}.{EscapeLiteral(tableName)}.{EscapeLiteral(oldName)}', N'{EscapeLiteral(newName)}', N'INDEX';");
            return;
        }

        if (databaseType.IndexOf("Postgres", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($"ALTER INDEX {QualifyTable(databaseType, schemaName, oldName)} RENAME TO {QuoteIdent(databaseType, newName)};");
            return;
        }

        if (databaseType.IndexOf("MySql", StringComparison.OrdinalIgnoreCase) >= 0 ||
            databaseType.IndexOf("MariaDb", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($"ALTER TABLE {QualifyTable(databaseType, schemaName, tableName)} RENAME INDEX {QuoteIdent(databaseType, oldName)} TO {QuoteIdent(databaseType, newName)};");
            return;
        }

        if (databaseType.IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            // Oracle index names are unique per schema, so no table qualifier is needed — but the
            // schema itself still must be, otherwise this targets the connected user's default schema.
            // No trailing semicolon on Oracle direct DDL (ORA-00911).
            self.Execute.Sql($"ALTER INDEX {QualifyTable(databaseType, schemaName, oldName)} RENAME TO {QuoteIdent(databaseType, newName)}");
            return;
        }

        throw new NotSupportedException("RenameIndexIfExists is not supported on SQLite.");
    }

    /// <summary>
    /// Renames the constraint <paramref name="oldName"/> to <paramref name="newName"/> on
    /// <paramref name="tableName"/> if it exists. Only supported on SQL Server, PostgreSQL and Oracle —
    /// MySQL/MariaDB has no general-purpose constraint rename (only <c>RENAME INDEX</c>, see
    /// <see cref="RenameIndexIfExists"/>), and SQLite has no rename-constraint DDL at all.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Table the constraint is defined on.</param>
    /// <param name="oldName">Current constraint name.</param>
    /// <param name="newName">New constraint name.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
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

        var databaseType = self.GetDatabaseType();

        if (databaseType.IndexOf("SqlServer", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            // No @objtype: UNIQUE/PRIMARY KEY constraints are backed by an index (needs 'INDEX'), while
            // CHECK/DEFAULT/FOREIGN KEY constraints are true objects (needs 'OBJECT' or nothing) — since this
            // method doesn't know which kind of constraint it's renaming, claiming the wrong type makes
            // sp_rename fail with "the claimed @objtype is wrong". Omitting it lets SQL Server resolve it itself.
            self.Execute.Sql($"EXEC sp_rename N'{EscapeLiteral(schemaName)}.{EscapeLiteral(tableName)}.{EscapeLiteral(oldName)}', N'{EscapeLiteral(newName)}';");
            return;
        }

        if (databaseType.IndexOf("Postgres", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($"ALTER TABLE {QualifyTable(databaseType, schemaName, tableName)} RENAME CONSTRAINT {QuoteIdent(databaseType, oldName)} TO {QuoteIdent(databaseType, newName)};");
            return;
        }

        if (databaseType.IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            // No trailing semicolon on Oracle direct DDL (ORA-00911).
            self.Execute.Sql($"ALTER TABLE {QualifyTable(databaseType, schemaName, tableName)} RENAME CONSTRAINT {QuoteIdent(databaseType, oldName)} TO {QuoteIdent(databaseType, newName)}");
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
    public static void UpdateDataIfExists(
        this Migration self,
        string tableName,
        IReadOnlyDictionary<string, object?> keyValues,
        IReadOnlyDictionary<string, object?> setValues,
        string? schemaName = null)
    {
        if (keyValues.Count == 0)
            throw new ArgumentException("At least one key column is required.", nameof(keyValues));
        if (setValues.Count == 0)
            throw new ArgumentException("At least one column to set is required.", nameof(setValues));

        schemaName ??= self.ResolveDefaultSchema();
        var databaseType = self.GetDatabaseType();

        var setClause = string.Join(", ", setValues.Select(kv => $"{QuoteIdent(databaseType, kv.Key)} = {FormatSqlValue(kv.Value, databaseType)}"));
        var whereClause = string.Join(" AND ", keyValues.Select(kv => FormatSqlPredicate(databaseType, kv.Key, kv.Value)));

        self.Execute.Sql($"UPDATE {QualifyTable(databaseType, schemaName, tableName)} SET {setClause} WHERE {whereClause};");
    }

    /// <summary>
    /// Deletes rows from <paramref name="tableName"/> matching <paramref name="keyValues"/>. Naturally
    /// idempotent — a <c>DELETE</c> that matches zero rows (because they were already deleted, or never
    /// existed) is a safe no-op on every provider, so no existence guard is needed.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Target table name.</param>
    /// <param name="keyValues">Column/value pairs identifying which rows to delete. Must contain at least one entry.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
    public static void DeleteDataIfExists(
        this Migration self,
        string tableName,
        IReadOnlyDictionary<string, object?> keyValues,
        string? schemaName = null)
    {
        if (keyValues.Count == 0)
            throw new ArgumentException("At least one key column is required.", nameof(keyValues));

        schemaName ??= self.ResolveDefaultSchema();
        var databaseType = self.GetDatabaseType();

        var whereClause = string.Join(" AND ", keyValues.Select(kv => FormatSqlPredicate(databaseType, kv.Key, kv.Value)));

        self.Execute.Sql($"DELETE FROM {QualifyTable(databaseType, schemaName, tableName)} WHERE {whereClause};");
    }

    /// <summary>
    /// Inserts a row into <paramref name="tableName"/> only if no row matching <paramref name="keyValues"/>
    /// already exists. Intended for idempotently seeding small reference/lookup tables.
    /// </summary>
    /// <remarks>
    /// Uses a portable <c>INSERT ... SELECT ... WHERE NOT EXISTS (...)</c> statement that runs unmodified on
    /// SQL Server, PostgreSQL, MySQL and SQLite — no per-provider branching needed. Values are formatted as SQL
    /// literals (strings are quote-escaped; <c>null</c> key values use <c>IS NULL</c> so they still match;
    /// booleans become <c>TRUE</c>/<c>FALSE</c> on PostgreSQL and <c>1</c>/<c>0</c> elsewhere;
    /// <see cref="Guid"/> is quoted; enums use their underlying numeric value).
    /// Only whitelisted value types are accepted — anything else throws instead of being embedded blindly.
    /// Note: concurrent writers can both pass the <c>NOT EXISTS</c> check and collide on a unique
    /// constraint — like every check-then-act in this library, this is idempotent under retries,
    /// not under racing transactions.
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Target table name.</param>
    /// <param name="keyValues">Column/value pairs that uniquely identify the row — used for the existence check.
    /// Must contain at least one entry.</param>
    /// <param name="additionalValues">Extra column/value pairs to insert alongside <paramref name="keyValues"/>. Not part of the existence check.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
    public static void InsertDataIfNotExists(
        this Migration self,
        string tableName,
        IReadOnlyDictionary<string, object?> keyValues,
        IReadOnlyDictionary<string, object?>? additionalValues = null,
        string? schemaName = null)
    {
        if (keyValues.Count == 0)
            throw new ArgumentException("At least one key column is required.", nameof(keyValues));

        schemaName ??= self.ResolveDefaultSchema();
        var databaseType = self.GetDatabaseType();

        if (additionalValues is not null)
        {
            var overlap = additionalValues.Keys.Intersect(keyValues.Keys, StringComparer.OrdinalIgnoreCase).ToList();
            if (overlap.Count > 0)
                throw new ArgumentException(
                    $"additionalValues must not redefine key columns: {string.Join(", ", overlap)}.",
                    nameof(additionalValues));
        }

        var qualifiedTable = QualifyTable(databaseType, schemaName, tableName);

        var values = new Dictionary<string, object?>();
        foreach (var kv in keyValues)
            values[kv.Key] = kv.Value;
        if (additionalValues is not null)
            foreach (var kv in additionalValues)
                values[kv.Key] = kv.Value;

        var columns = values.Keys.ToList();
        var columnList = string.Join(", ", columns.Select(c => QuoteIdent(databaseType, c)));
        var selectList = string.Join(", ", columns.Select(c => FormatSqlValue(values[c], databaseType)));
        var whereClause = string.Join(" AND ", keyValues.Select(kv => FormatSqlPredicate(databaseType, kv.Key, kv.Value)));

        // Oracle has no FROM-less SELECT — every SELECT needs a source, hence FROM DUAL.
        var fromDual = databaseType.IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0
            ? " FROM DUAL"
            : "";

        self.Execute.Sql($@"INSERT INTO {qualifiedTable} ({columnList})
SELECT {selectList}{fromDual}
WHERE NOT EXISTS (SELECT 1 FROM {qualifiedTable} WHERE {whereClause});");
    }

    /// <summary>
    /// Alters <paramref name="sequenceName"/> if it already exists; no-op otherwise.
    /// Not supported on MySQL/MariaDB or SQLite.
    /// </summary>
    /// <remarks>
    /// FluentMigrator has no <c>Alter.Sequence</c> API, so this builds and executes a raw
    /// <c>ALTER SEQUENCE</c> statement from the supplied parameters. At least one parameter
    /// must be non-null. Provider differences are handled internally — e.g. Oracle uses
    /// <c>START WITH</c> instead of <c>RESTART WITH</c>, and <c>NOCYCLE</c>/<c>NOCACHE</c>
    /// instead of <c>NO CYCLE</c>/<c>NO CACHE</c>.
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="sequenceName">Name of the sequence to alter.</param>
    /// <param name="incrementBy">New increment value.</param>
    /// <param name="minValue">New minimum value.</param>
    /// <param name="maxValue">New maximum value.</param>
    /// <param name="restartWith">Restart the sequence at this value. Not supported on Oracle (use <paramref name="startWith"/> instead).</param>
    /// <param name="startWith">Set the start value. On Oracle, also restarts the sequence.</param>
    /// <param name="cache">Number of values to cache. Pass <c>0</c> to disable caching.</param>
    /// <param name="cycle">Whether the sequence should cycle when it reaches its limit.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
    public static void AlterSequenceIfExists(
        this Migration self,
        string sequenceName,
        long? incrementBy = null,
        long? minValue = null,
        long? maxValue = null,
        long? restartWith = null,
        long? startWith = null,
        long? cache = null,
        bool? cycle = null,
        string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();

        if (!self.Schema.Schema(schemaName).Sequence(sequenceName).Exists())
            return;

        var databaseType = self.GetDatabaseType();
        var isOracle = databaseType.IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0;

        if (isOracle && restartWith.HasValue)
            throw new ArgumentException(
                "restartWith is not supported on Oracle — pass startWith instead (it also restarts the sequence there).",
                nameof(restartWith));

        var clauses = new List<string>();
        if (incrementBy.HasValue) clauses.Add($"INCREMENT BY {incrementBy.Value}");
        if (minValue.HasValue) clauses.Add($"MINVALUE {minValue.Value}");
        if (maxValue.HasValue) clauses.Add($"MAXVALUE {maxValue.Value}");
        if (startWith.HasValue) clauses.Add($"START WITH {startWith.Value}");
        if (restartWith.HasValue) clauses.Add($"RESTART WITH {restartWith.Value}");
        if (cache.HasValue)
            clauses.Add(cache.Value > 0
                ? $"CACHE {cache.Value}"
                : isOracle ? "NOCACHE" : "NO CACHE");
        if (cycle.HasValue)
            clauses.Add(cycle.Value
                ? "CYCLE"
                : isOracle ? "NOCYCLE" : "NO CYCLE");

        if (clauses.Count == 0)
            return;

        // No trailing semicolon on Oracle direct DDL (ORA-00911).
        var terminator = isOracle ? "" : ";";
        self.Execute.Sql($"ALTER SEQUENCE {QualifyTable(databaseType, schemaName, sequenceName)} {string.Join(" ", clauses)}{terminator}");
    }

    /// <summary>
    /// Drops and recreates <paramref name="viewName"/> in a single call — equivalent to
    /// <see cref="DropViewIfExists"/> followed by <see cref="CreateViewIfNotExists"/>.
    /// If the view does not exist, it is simply created.
    /// </summary>
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
    public static void UpsertData(
        this Migration self,
        string tableName,
        IReadOnlyDictionary<string, object> keyValues,
        IReadOnlyDictionary<string, object?>? additionalValues = null,
        string? schemaName = null)
    {
        if (keyValues.Count == 0)
            throw new ArgumentException("At least one key column is required.", nameof(keyValues));
        if (keyValues.Any(kv => kv.Value is null))
            throw new ArgumentException(
                "Upsert key values must be non-null: NULL never matches in a conflict/ON clause, " +
                "so a null key would re-insert instead of updating.",
                nameof(keyValues));

        schemaName ??= self.ResolveDefaultSchema();

        var allValues = new Dictionary<string, object?>();
        foreach (var kv in keyValues)
            allValues[kv.Key] = kv.Value;
        if (additionalValues is not null)
            foreach (var kv in additionalValues)
                allValues[kv.Key] = kv.Value;

        var databaseType = self.GetDatabaseType();
        var qualifiedTable = QualifyTable(databaseType, schemaName, tableName);
        var columns = allValues.Keys.ToList();
        var columnList = string.Join(", ", columns.Select(c => QuoteIdent(databaseType, c)));
        var valueList = string.Join(", ", columns.Select(c => FormatSqlValue(allValues[c], databaseType)));
        var hasUpdates = additionalValues is not null && additionalValues.Count > 0;

        if (databaseType.IndexOf("SqlServer", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            // HOLDLOCK: without it two concurrent runners can both pass the match check
            // and hit a duplicate-key error (the well-known MERGE race).
            var onClause = string.Join(" AND ", keyValues.Select(kv => $"target.{QuoteIdent(databaseType, kv.Key)} = source.{QuoteIdent(databaseType, kv.Key)}"));
            // A bare NULL has no type in a derived table ("type cannot be determined") — cast it.
            var sourceColumns = string.Join(", ", columns.Select(c => allValues[c] is null
                ? $"CAST(NULL AS NVARCHAR(MAX)) AS {QuoteIdent(databaseType, c)}"
                : $"{FormatSqlValue(allValues[c], databaseType)} AS {QuoteIdent(databaseType, c)}"));
            var matchedClause = hasUpdates
                ? $"WHEN MATCHED THEN UPDATE SET {string.Join(", ", additionalValues!.Select(kv => $"target.{QuoteIdent(databaseType, kv.Key)} = source.{QuoteIdent(databaseType, kv.Key)}"))}\n"
                : "";
            var insertColumns = string.Join(", ", columns.Select(c => QuoteIdent(databaseType, c)));
            var insertValues = string.Join(", ", columns.Select(c => $"source.{QuoteIdent(databaseType, c)}"));

            self.Execute.Sql($@"MERGE {qualifiedTable} WITH (HOLDLOCK) AS target
USING (SELECT {sourceColumns}) AS source
ON ({onClause})
{matchedClause}WHEN NOT MATCHED THEN INSERT ({insertColumns}) VALUES ({insertValues});");
            return;
        }

        if (databaseType.IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var onClause = string.Join(" AND ", keyValues.Select(kv => $"target.{QuoteIdent(databaseType, kv.Key)} = source.{QuoteIdent(databaseType, kv.Key)}"));
            var sourceColumns = string.Join(", ", columns.Select(c => allValues[c] is null
                ? $"CAST(NULL AS VARCHAR2(4000)) AS {QuoteIdent(databaseType, c)}"
                : $"{FormatSqlValue(allValues[c], databaseType)} AS {QuoteIdent(databaseType, c)}"));
            var matchedClause = hasUpdates
                ? $"WHEN MATCHED THEN UPDATE SET {string.Join(", ", additionalValues!.Select(kv => $"target.{QuoteIdent(databaseType, kv.Key)} = source.{QuoteIdent(databaseType, kv.Key)}"))}\n"
                : "";
            // No trailing semicolon: this statement is sent as direct SQL (ORA-00911),
            // unlike the PL/SQL blocks elsewhere which require their own terminators.
            var insertColumns = string.Join(", ", columns.Select(c => $"target.{QuoteIdent(databaseType, c)}"));
            var insertValues = string.Join(", ", columns.Select(c => $"source.{QuoteIdent(databaseType, c)}"));

            self.Execute.Sql($@"MERGE INTO {qualifiedTable} target
USING (SELECT {sourceColumns} FROM DUAL) source
ON ({onClause})
{matchedClause}WHEN NOT MATCHED THEN INSERT ({insertColumns}) VALUES ({insertValues})");
            return;
        }

        if (databaseType.IndexOf("MySql", StringComparison.OrdinalIgnoreCase) >= 0 ||
            databaseType.IndexOf("MariaDb", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var isMariaDb = databaseType.IndexOf("MariaDb", StringComparison.OrdinalIgnoreCase) >= 0;
            string updateSet;
            string prefix;
            if (hasUpdates)
            {
                // VALUES(col) is deprecated since MySQL 8.0.20 — the row-alias form needs 8.0.19+.
                // MariaDB never deprecated VALUES(), so it stays there for maximum server compatibility.
                updateSet = isMariaDb
                    ? string.Join(", ", additionalValues!.Select(kv => $"{QuoteIdent(databaseType, kv.Key)} = VALUES({QuoteIdent(databaseType, kv.Key)})"))
                    : string.Join(", ", additionalValues!.Select(kv => $"{QuoteIdent(databaseType, kv.Key)} = new.{QuoteIdent(databaseType, kv.Key)}"));
                prefix = isMariaDb ? "" : "AS new ";
            }
            else
            {
                // ON DUPLICATE KEY UPDATE requires at least one assignment — self-assign the keys (a no-op).
                updateSet = isMariaDb
                    ? string.Join(", ", keyValues.Select(kv => $"{QuoteIdent(databaseType, kv.Key)} = VALUES({QuoteIdent(databaseType, kv.Key)})"))
                    : string.Join(", ", keyValues.Select(kv => $"{QuoteIdent(databaseType, kv.Key)} = new.{QuoteIdent(databaseType, kv.Key)}"));
                prefix = isMariaDb ? "" : "AS new ";
            }

            self.Execute.Sql($@"INSERT INTO {qualifiedTable} ({columnList}) VALUES ({valueList})
{prefix}ON DUPLICATE KEY UPDATE {updateSet};");
            return;
        }

        // PostgreSQL / SQLite: ON CONFLICT ... DO UPDATE (or DO NOTHING for key-only upserts).
        var keyColumnList = string.Join(", ", keyValues.Keys.Select(k => QuoteIdent(databaseType, k)));
        var conflictClause = hasUpdates
            ? $"DO UPDATE SET {string.Join(", ", additionalValues!.Select(kv => $"{QuoteIdent(databaseType, kv.Key)} = EXCLUDED.{QuoteIdent(databaseType, kv.Key)}"))}"
            : "DO NOTHING";

        self.Execute.Sql($@"INSERT INTO {qualifiedTable} ({columnList}) VALUES ({valueList})
ON CONFLICT ({keyColumnList}) {conflictClause};");
    }

    /// <summary>
    /// Executes <paramref name="executeSql"/> only if <paramref name="conditionSql"/> returns at least one row.
    /// An escape hatch for idempotent operations not covered by the specialized methods.
    /// </summary>
    /// <remarks>
    /// Supported on SQL Server (<c>IF EXISTS ... EXEC sp_executesql</c>), PostgreSQL (<c>DO $fm_idempotent$ ... END</c>),
    /// and Oracle (<c>DECLARE ... EXECUTE IMMEDIATE</c>). Not supported on MySQL or SQLite.
    /// Both statements are executed verbatim — trusted developer input only, never end-user input.
    /// <paramref name="conditionSql"/> must be a single <c>SELECT</c> without a trailing semicolon
    /// (multi-statement input is rejected); <paramref name="executeSql"/> must not contain the
    /// <c>$fm_idempotent$</c> dollar-quote tag.
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="conditionSql">A <c>SELECT</c> statement; if it returns any rows, <paramref name="executeSql"/> runs.</param>
    /// <param name="executeSql">The SQL statement to execute when the condition is met.</param>
    public static void ExecuteSqlIfExists(
        this Migration self,
        string conditionSql,
        string executeSql)
    {
        const string pgTag = "$fm_idempotent$";
        var databaseType = self.GetDatabaseType();

        if (conditionSql.Contains(';'))
            throw new ArgumentException("conditionSql must be a single SELECT statement without semicolons.", nameof(conditionSql));
        if (databaseType.IndexOf("Postgres", StringComparison.OrdinalIgnoreCase) >= 0 && executeSql.Contains(pgTag))
            throw new ArgumentException($"executeSql must not contain the '{pgTag}' dollar-quote tag.", nameof(executeSql));

        if (databaseType.IndexOf("SqlServer", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($@"IF EXISTS ({conditionSql})
    EXEC sp_executesql N'{executeSql.Replace("'", "''")}';");
            return;
        }

        if (databaseType.IndexOf("Postgres", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($@"DO {pgTag}
BEGIN
    IF EXISTS ({conditionSql}) THEN
        EXECUTE '{executeSql.Replace("'", "''")}';
    END IF;
END {pgTag};");
            return;
        }

        if (databaseType.IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($@"
DECLARE
    v_cnt NUMBER;
BEGIN
    SELECT COUNT(*) INTO v_cnt FROM ({conditionSql}) WHERE ROWNUM = 1;
    IF v_cnt > 0 THEN
        EXECUTE IMMEDIATE '{executeSql.Replace("'", "''")}';
    END IF;
END;");
            return;
        }

        throw new NotSupportedException($"ExecuteSqlIfExists is not supported on {databaseType}.");
    }

    /// <summary>
    /// Executes <paramref name="executeSql"/> only if <paramref name="conditionSql"/> returns no rows.
    /// An escape hatch for idempotent operations not covered by the specialized methods.
    /// </summary>
    /// <remarks>
    /// Same provider support and trusted-input contract as <see cref="ExecuteSqlIfExists"/>.
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="conditionSql">A <c>SELECT</c> statement; if it returns no rows, <paramref name="executeSql"/> runs.</param>
    /// <param name="executeSql">The SQL statement to execute when the condition is not met.</param>
    public static void ExecuteSqlIfNotExists(
        this Migration self,
        string conditionSql,
        string executeSql)
    {
        const string pgTag = "$fm_idempotent$";
        var databaseType = self.GetDatabaseType();

        if (conditionSql.Contains(';'))
            throw new ArgumentException("conditionSql must be a single SELECT statement without semicolons.", nameof(conditionSql));
        if (databaseType.IndexOf("Postgres", StringComparison.OrdinalIgnoreCase) >= 0 && executeSql.Contains(pgTag))
            throw new ArgumentException($"executeSql must not contain the '{pgTag}' dollar-quote tag.", nameof(executeSql));

        if (databaseType.IndexOf("SqlServer", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($@"IF NOT EXISTS ({conditionSql})
    EXEC sp_executesql N'{executeSql.Replace("'", "''")}';");
            return;
        }

        if (databaseType.IndexOf("Postgres", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($@"DO {pgTag}
BEGIN
    IF NOT EXISTS ({conditionSql}) THEN
        EXECUTE '{executeSql.Replace("'", "''")}';
    END IF;
END {pgTag};");
            return;
        }

        if (databaseType.IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($@"
DECLARE
    v_cnt NUMBER;
BEGIN
    SELECT COUNT(*) INTO v_cnt FROM ({conditionSql}) WHERE ROWNUM = 1;
    IF v_cnt = 0 THEN
        EXECUTE IMMEDIATE '{executeSql.Replace("'", "''")}';
    END IF;
END;");
            return;
        }

        throw new NotSupportedException($"ExecuteSqlIfNotExists is not supported on {databaseType}.");
    }

    /// <summary>
    /// Reorganizes/defragments all indexes on <paramref name="tableName"/>. Pure maintenance — no
    /// schema changes — so it is safe to re-run on every deploy.
    /// </summary>
    /// <remarks>
    /// Provider mapping: SQL Server runs <c>ALTER INDEX ALL ... REORGANIZE</c>; PostgreSQL runs
    /// <c>REINDEX TABLE</c>; MySQL/MariaDB runs <c>OPTIMIZE TABLE</c> (which also refreshes index
    /// statistics); SQLite runs <c>REINDEX table</c>; Oracle rebuilds each of the table's indexes via
    /// a PL/SQL loop (<c>ALTER INDEX ... REBUILD</c>, plain rebuild so it works on every edition).
    /// The table is passed as a parameter — nothing is hardcoded. If the table does not exist the
    /// statement fails server-side (no silent skip).
    /// On PostgreSQL this takes an <c>ACCESS EXCLUSIVE</c> lock — do NOT run it on every deploy
    /// against a busy table; schedule it in a maintenance window or pass <c>concurrently: true</c>
    /// (PostgreSQL 12+, slower but lock-friendly).
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Table whose indexes should be reorganized.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
    /// <param name="concurrently">PostgreSQL only: use <c>REINDEX TABLE CONCURRENTLY</c>. Ignored elsewhere.</param>
    public static void ReorganizeIndexes(
        this Migration self,
        string tableName,
        string? schemaName = null,
        bool concurrently = false)
    {
        schemaName ??= self.ResolveDefaultSchema();
        var databaseType = self.GetDatabaseType();

        if (databaseType.IndexOf("SqlServer", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($"ALTER INDEX ALL ON [{EscapeBracket(schemaName)}].[{EscapeBracket(tableName)}] REORGANIZE;");
            return;
        }

        if (databaseType.IndexOf("Postgres", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var keyword = concurrently ? "REINDEX TABLE CONCURRENTLY" : "REINDEX TABLE";
            self.Execute.Sql($"{keyword} {QualifyTable(databaseType, schemaName, tableName)};");
            return;
        }

        if (databaseType.IndexOf("MySql", StringComparison.OrdinalIgnoreCase) >= 0 ||
            databaseType.IndexOf("MariaDb", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($"OPTIMIZE TABLE {QualifyTable(databaseType, schemaName, tableName)};");
            return;
        }

        if (databaseType.IndexOf("SQLite", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($"REINDEX {QuoteIdent(databaseType, tableName)};");
            return;
        }

        if (databaseType.IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            // Oracle has no "reorganize all indexes on a table" statement: loop over the table's
            // indexes and rebuild each one. Unquoted identifiers are stored uppercase, but quoted
            // (case-sensitive) names are stored as-is — match either.
            var escapedTable = tableName.Replace("'", "''");
            var escapedSchema = schemaName.Replace("'", "''");

            var indexCursor = string.IsNullOrEmpty(schemaName)
                ? $"SELECT index_name FROM user_indexes WHERE table_name IN ('{escapedTable}', UPPER('{escapedTable}'))"
                : $"SELECT owner, index_name FROM all_indexes WHERE table_name IN ('{escapedTable}', UPPER('{escapedTable}')) AND owner IN ('{escapedSchema}', UPPER('{escapedSchema}'))";

            var rebuildSql = string.IsNullOrEmpty(schemaName)
                ? "'ALTER INDEX \"' || idx.index_name || '\" REBUILD'"
                : "'ALTER INDEX \"' || idx.owner || '\".\"' || idx.index_name || '\" REBUILD'";

            self.Execute.Sql($@"
BEGIN
    FOR idx IN ({indexCursor}) LOOP
        EXECUTE IMMEDIATE {rebuildSql};
    END LOOP;
END;");
            return;
        }

        throw new NotSupportedException($"ReorganizeIndexes is not supported on {databaseType}.");
    }

    /// <summary>
    /// Refreshes the optimizer statistics for <paramref name="tableName"/>. Pure maintenance — no
    /// schema changes — so it is safe to re-run on every deploy.
    /// </summary>
    /// <remarks>
    /// Provider mapping: SQL Server runs <c>UPDATE STATISTICS ... WITH SAMPLE n PERCENT</c> (or without
    /// a sampling clause when <paramref name="samplePercent"/> is <c>null</c>); PostgreSQL runs
    /// <c>ANALYZE</c>; MySQL/MariaDB runs <c>ANALYZE TABLE</c>; SQLite runs <c>ANALYZE table</c>;
    /// Oracle calls <c>DBMS_STATS.GATHER_TABLE_STATS</c> with <c>estimate_percent</c>
    /// (<c>DBMS_STATS.AUTO_SAMPLE_SIZE</c> when <paramref name="samplePercent"/> is <c>null</c>).
    /// Sampling is only honored on SQL Server and Oracle — the other providers have no per-table
    /// sampling clause, so <paramref name="samplePercent"/> is ignored there.
    /// The table is passed as a parameter — nothing is hardcoded. If the table does not exist the
    /// statement fails server-side (no silent skip).
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Table whose statistics should be refreshed.</param>
    /// <param name="samplePercent">Sampling percent, 1–100. Honored on SQL Server and Oracle only.
    /// <c>null</c> means server default (no sampling clause / auto sample size). Defaults to 30.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
    public static void UpdateStatistics(
        this Migration self,
        string tableName,
        int? samplePercent = 30,
        string? schemaName = null)
    {
        if (samplePercent.HasValue && (samplePercent.Value < 1 || samplePercent.Value > 100))
            throw new ArgumentOutOfRangeException(nameof(samplePercent), "Sample percent must be between 1 and 100.");

        schemaName ??= self.ResolveDefaultSchema();
        var databaseType = self.GetDatabaseType();

        if (databaseType.IndexOf("SqlServer", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var sampleClause = samplePercent.HasValue ? $" WITH SAMPLE {samplePercent.Value} PERCENT" : "";
            self.Execute.Sql($"UPDATE STATISTICS [{EscapeBracket(schemaName)}].[{EscapeBracket(tableName)}]{sampleClause};");
            return;
        }

        if (databaseType.IndexOf("Postgres", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($"ANALYZE {QualifyTable(databaseType, schemaName, tableName)};");
            return;
        }

        if (databaseType.IndexOf("MySql", StringComparison.OrdinalIgnoreCase) >= 0 ||
            databaseType.IndexOf("MariaDb", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($"ANALYZE TABLE {QualifyTable(databaseType, schemaName, tableName)};");
            return;
        }

        if (databaseType.IndexOf("SQLite", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($"ANALYZE {QuoteIdent(databaseType, tableName)};");
            return;
        }

        if (databaseType.IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var estimate = samplePercent.HasValue
                ? samplePercent.Value.ToString(CultureInfo.InvariantCulture)
                : "DBMS_STATS.AUTO_SAMPLE_SIZE";
            var escapedTable = tableName.Replace("'", "''");

            // Resolve the real stored names first: unquoted identifiers live uppercase in the
            // dictionary, quoted (case-sensitive) ones as-is. GATHER_TABLE_STATS needs exact names.
            string statsSql;
            if (string.IsNullOrEmpty(schemaName))
            {
                statsSql = $@"
DECLARE
    v_tab VARCHAR2(128);
BEGIN
    BEGIN
        SELECT table_name INTO v_tab FROM (
            SELECT table_name FROM user_tables WHERE table_name IN ('{escapedTable}', UPPER('{escapedTable}'))
            UNION ALL
            SELECT table_name FROM all_tables WHERE table_name IN ('{escapedTable}', UPPER('{escapedTable}')) AND owner = USER AND ROWNUM = 1)
        WHERE ROWNUM = 1;
    EXCEPTION
        WHEN NO_DATA_FOUND THEN
            RAISE_APPLICATION_ERROR(-20001, 'UpdateStatistics: table ''{escapedTable}'' not found');
    END;
    DBMS_STATS.GATHER_TABLE_STATS(ownname => USER, tabname => v_tab, estimate_percent => {estimate});
END;";
            }
            else
            {
                var escapedSchema = schemaName.Replace("'", "''");
                statsSql = $@"
DECLARE
    v_owner VARCHAR2(128);
    v_tab VARCHAR2(128);
BEGIN
    BEGIN
        SELECT username INTO v_owner FROM all_users WHERE username IN ('{escapedSchema}', UPPER('{escapedSchema}')) AND ROWNUM = 1;
    EXCEPTION
        WHEN NO_DATA_FOUND THEN
            RAISE_APPLICATION_ERROR(-20001, 'UpdateStatistics: schema ''{escapedSchema}'' not found');
    END;
    BEGIN
        SELECT table_name INTO v_tab FROM (
            SELECT table_name FROM all_tables WHERE table_name IN ('{escapedTable}', UPPER('{escapedTable}')) AND owner = v_owner AND ROWNUM = 1)
        WHERE ROWNUM = 1;
    EXCEPTION
        WHEN NO_DATA_FOUND THEN
            RAISE_APPLICATION_ERROR(-20001, 'UpdateStatistics: table ''{escapedSchema}.{escapedTable}'' not found');
    END;
    DBMS_STATS.GATHER_TABLE_STATS(ownname => v_owner, tabname => v_tab, estimate_percent => {estimate});
END;";
            }

            self.Execute.Sql(statsSql);
            return;
        }

        throw new NotSupportedException($"UpdateStatistics is not supported on {databaseType}.");
    }

    private static string EscapeLiteral(string value) => value.Replace("'", "''");

    private static string EscapeBracket(string value) => value.Replace("]", "]]");

    private static string EscapeDoubleQuote(string value) => value.Replace("\"", "\"\"");

    /// <summary>
    /// Quotes a single identifier for the current provider: <c>[x]</c> on SQL Server
    /// (with <c>]</c> escaped as <c>]]</c>), backticks on MySQL/MariaDB, double quotes
    /// everywhere else (PostgreSQL, SQLite, Oracle).
    /// </summary>
    /// <remarks>
    /// Quoted identifiers are case-sensitive, while FluentMigrator itself emits them unquoted —
    /// so on engines that fold unquoted names the value is folded first to resolve exactly like
    /// the old unquoted SQL did: <c>UPPER</c> on Oracle (which stores everything uppercase),
    /// <c>lower</c> on PostgreSQL (which stores everything lowercase). SQL Server, MySQL and
    /// SQLite compare case-insensitively (or store as-given on both sides), so no folding there.
    /// </remarks>
    private static string QuoteIdent(string databaseType, string name)
    {
        if (databaseType.IndexOf("SqlServer", StringComparison.OrdinalIgnoreCase) >= 0)
            return $"[{EscapeBracket(name)}]";
        if (databaseType.IndexOf("MySql", StringComparison.OrdinalIgnoreCase) >= 0 ||
            databaseType.IndexOf("MariaDb", StringComparison.OrdinalIgnoreCase) >= 0)
            return $"`{name.Replace("`", "``")}`";
        if (databaseType.IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0)
            return $"\"{EscapeDoubleQuote(name.ToUpperInvariant())}\"";
        if (databaseType.IndexOf("Postgres", StringComparison.OrdinalIgnoreCase) >= 0)
            return $"\"{EscapeDoubleQuote(name.ToLowerInvariant())}\"";
        return $"\"{EscapeDoubleQuote(name)}\"";
    }

    private static string QualifyTable(string databaseType, string schemaName, string tableName)
        => string.IsNullOrEmpty(schemaName)
            ? QuoteIdent(databaseType, tableName)
            : $"{QuoteIdent(databaseType, schemaName)}.{QuoteIdent(databaseType, tableName)}";

    private static string FormatSqlPredicate(string databaseType, string column, object? value)
        => value is null
            ? $"{QuoteIdent(databaseType, column)} IS NULL"
            : $"{QuoteIdent(databaseType, column)} = {FormatSqlValue(value, databaseType)}";

    /// <summary>
    /// Formats a value as a SQL literal. Only whitelisted CLR types are supported — anything else
    /// throws <see cref="NotSupportedException"/> instead of silently embedding
    /// <c>ToString()</c> output (which would allow arbitrary text, e.g. from a custom type,
    /// to flow unquoted into SQL).
    /// </summary>
    private static string FormatSqlValue(object? value, string databaseType)
    {
        switch (value)
        {
            case null:
                return "NULL";
            case string s:
                return $"'{EscapeLiteral(s)}'";
            case char ch:
                return $"'{EscapeLiteral(ch.ToString())}'";
            case bool b:
                // PostgreSQL has a real boolean type — 1/0 is a syntax error there.
                if (databaseType.IndexOf("Postgres", StringComparison.OrdinalIgnoreCase) >= 0)
                    return b ? "TRUE" : "FALSE";
                return b ? "1" : "0";
            case DateTime dt:
                return $"'{dt:yyyy-MM-dd HH:mm:ss.fff}'";
            case DateTimeOffset dto:
                return $"'{dto:yyyy-MM-dd HH:mm:ss.fff zzz}'";
            case Guid g:
                return $"'{g}'";
            case Enum e:
                return Convert.ToInt64(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            case byte _:
            case sbyte _:
            case short _:
            case ushort _:
            case int _:
            case uint _:
            case long _:
            case ulong _:
            case float _:
            case double _:
            case decimal _:
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "NULL";
            case byte[] bytes:
                var hex = BitConverter.ToString(bytes).Replace("-", string.Empty);
                if (databaseType.IndexOf("Postgres", StringComparison.OrdinalIgnoreCase) >= 0)
                    return $"'\\x{hex}'";
                if (databaseType.IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0)
                    return $"HEXTORAW('{hex}')";
                return $"0x{hex}";
            default:
                throw new NotSupportedException(
                    $"Values of type '{value.GetType().FullName}' are not supported as SQL literals. " +
                    "Supported types: string, char, bool, DateTime, DateTimeOffset, Guid, enums, " +
                    "numeric types and byte[].");
        }
    }

    /// <summary>
    /// Resolves the default schema name for the current migration's database provider, used whenever
    /// a caller omits <c>schemaName</c>. Detected via <see cref="GetDatabaseType"/>.
    /// </summary>
    private static string ResolveDefaultSchema(this MigrationBase self)
    {
        var databaseType = self.GetDatabaseType();

        if (databaseType.IndexOf("SqlServer", StringComparison.OrdinalIgnoreCase) >= 0)
            return "dbo";
        if (databaseType.IndexOf("Postgres", StringComparison.OrdinalIgnoreCase) >= 0)
            return "public";
        if (databaseType.IndexOf("MySql", StringComparison.OrdinalIgnoreCase) >= 0 ||
            databaseType.IndexOf("MariaDb", StringComparison.OrdinalIgnoreCase) >= 0)
            return "";
        if (databaseType.IndexOf("SQLite", StringComparison.OrdinalIgnoreCase) >= 0)
            return "";
        if (databaseType.IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0)
            return "";

        return "dbo";
    }

    /// <summary>
    /// Synchronously reports the live processor's database type via <see cref="MigrationBase.IfDatabase(Predicate{string})"/>.
    /// The predicate's return value is always <c>false</c>, so no DDL is ever emitted — this only reads the type.
    /// </summary>
    private static string GetDatabaseType(this MigrationBase self)
    {
        string? databaseType = null;
        self.IfDatabase(dt =>
        {
            databaseType = dt;
            return false;
        });

        return databaseType ?? string.Empty;
    }

    private static bool TableExists(this Migration self, string tableName, string schemaName)
        => self.Schema.Schema(schemaName).Table(tableName).Exists();

    private static bool ColumnExists(this Migration self, string tableName, string colName, string schemaName)
        => self.Schema.Schema(schemaName).Table(tableName).Column(colName).Exists();

    private static Dictionary<string, object?> ObjectToDictionary(object obj)
        => obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
              .Where(p => p.GetIndexParameters().Length == 0)
              .ToDictionary<PropertyInfo, string, object?>(p => p.Name, p => p.GetValue(obj));

    private static Dictionary<string, object> ObjectToNonNullDictionary(object obj)
    {
        var result = new Dictionary<string, object>();
        foreach (var prop in obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetIndexParameters().Length > 0)
                continue;
            var value = prop.GetValue(obj);
            if (value is null)
                throw new ArgumentException(
                    $"Property '{prop.Name}' is null, but upsert key values must be non-null.",
                    nameof(obj));
            result[prop.Name] = value;
        }

        return result;
    }

    /// <inheritdoc cref="InsertDataIfNotExists(Migration, string, IReadOnlyDictionary{string, object?}, IReadOnlyDictionary{string, object?}?, string?)"/>
    public static void InsertDataIfNotExists(
        this Migration self,
        string tableName,
        object keyValues,
        object? additionalValues = null,
        string? schemaName = null)
        => InsertDataIfNotExists(self, tableName, ObjectToDictionary(keyValues),
            additionalValues is not null ? ObjectToDictionary(additionalValues) : null, schemaName);

    /// <inheritdoc cref="UpdateDataIfExists(Migration, string, IReadOnlyDictionary{string, object?}, IReadOnlyDictionary{string, object?}, string?)"/>
    public static void UpdateDataIfExists(
        this Migration self,
        string tableName,
        object keyValues,
        object setValues,
        string? schemaName = null)
        => UpdateDataIfExists(self, tableName, ObjectToDictionary(keyValues), ObjectToDictionary(setValues), schemaName);

    /// <inheritdoc cref="DeleteDataIfExists(Migration, string, IReadOnlyDictionary{string, object?}, string?)"/>
    public static void DeleteDataIfExists(
        this Migration self,
        string tableName,
        object keyValues,
        string? schemaName = null)
        => DeleteDataIfExists(self, tableName, ObjectToDictionary(keyValues), schemaName);

    /// <inheritdoc cref="UpsertData(Migration, string, IReadOnlyDictionary{string, object}, IReadOnlyDictionary{string, object?}?, string?)"/>
    public static void UpsertData(
        this Migration self,
        string tableName,
        object keyValues,
        object? additionalValues = null,
        string? schemaName = null)
        => UpsertData(self, tableName, ObjectToNonNullDictionary(keyValues),
            additionalValues is not null ? ObjectToDictionary(additionalValues) : null, schemaName);
}
