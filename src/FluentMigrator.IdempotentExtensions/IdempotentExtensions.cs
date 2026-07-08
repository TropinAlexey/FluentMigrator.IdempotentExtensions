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

        return !self.TableExists(tableName, schemaName)
            ? constructTable(self.Create.Table(tableName))
            : null;
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

        if (self.GetDatabaseType().IndexOf("SqlServer", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($@"
IF EXISTS (
    SELECT 1
    FROM sys.default_constraints dc
    JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'[{schemaName}].[{tableName}]')
      AND c.name = N'{columnName}'
)
BEGIN
    DECLARE @constraintName SYSNAME;
    DECLARE @sql NVARCHAR(500);

    DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
        SELECT dc.name
        FROM sys.default_constraints dc
        JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
        WHERE dc.parent_object_id = OBJECT_ID(N'[{schemaName}].[{tableName}]')
          AND c.name = N'{columnName}';

    OPEN cur;
    FETCH NEXT FROM cur INTO @constraintName;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @sql = N'ALTER TABLE [{schemaName}].[{tableName}] DROP CONSTRAINT [' + @constraintName + N']';
        EXEC sp_executesql @sql;
        FETCH NEXT FROM cur INTO @constraintName;
    END;
    CLOSE cur;
    DEALLOCATE cur;
END");
            return;
        }

        // PostgreSQL/MySQL: dropping a default that isn't set is a harmless no-op.
        // SQLite: throws FluentMigrator's own "not supported" error, same as other SQLite-unsupported methods.
        self.Delete.DefaultConstraint().OnTable(tableName).InSchema(schemaName).OnColumn(columnName);
    }

    /// <summary>
    /// Creates a <c>{tableName}_log</c> audit log table if it does not already exist.
    /// The table includes: <c>id</c>, <c>timestamp</c>, <c>username</c>, <c>action</c>, <c>record_id</c>.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Base table name; the log table will be named <c>{tableName}_log</c>.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database
    /// provider (<c>dbo</c> for SQL Server, <c>public</c> for PostgreSQL, empty string for MySQL/SQLite).
    /// Pass an explicit value to target a specific schema (e.g. multi-tenant setups).</param>
    public static void CreateLogTableIfNotExists(
        this Migration self,
        string tableName,
        string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();

        if (self.TableExists($"{tableName}_log", schemaName))
            return;

        self.Create.Table($"{tableName}_log").InSchema(schemaName)
            .WithIdColumn()
            .WithColumn("timestamp").AsDateTime().Nullable()
            .WithColumn("username").AsAnsiString(500)
            .WithColumn("action").AsAnsiString(50)
            .WithColumn("record_id").AsInt32().NotNullable();
    }

    /// <summary>
    /// Creates an index named <c>index_{columnName}</c> on <paramref name="columnName"/> if it does not already exist.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Target table name.</param>
    /// <param name="columnName">Column to index.</param>
    /// <param name="configureIndex">Callback to configure ascending/descending and uniqueness.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database
    /// provider (<c>dbo</c> for SQL Server, <c>public</c> for PostgreSQL, empty string for MySQL/SQLite).
    /// Pass an explicit value to target a specific schema (e.g. multi-tenant setups).</param>
    public static IFluentSyntax? CreateIndexIfNotExists(
        this MigrationBase self,
        string tableName,
        string columnName,
        Func<ICreateIndexColumnOptionsSyntax, IFluentSyntax> configureIndex,
        string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();

        var indexName = $"index_{columnName}";
        return !self.Schema.Schema(schemaName).Table(tableName).Index(indexName).Exists()
            ? configureIndex(self.Create.Index(indexName).OnTable(tableName).InSchema(schemaName).OnColumn(columnName))
            : null;
    }

    /// <summary>
    /// Creates a composite index on <paramref name="columns"/> if it does not already exist.
    /// The default index name is <c>index_{col1}_{col2}_…</c>; supply <paramref name="indexName"/> to override.
    /// </summary>
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
            ? configureDelete(self.Delete.UniqueConstraint(keyName).FromTable(tableName).InSchema(schemaName))
            : null;
    }

    /// <summary>
    /// Creates a named PRIMARY KEY constraint on <paramref name="columns"/> if it does not already exist.
    /// Useful when the primary key needs to be added after the table already exists (e.g. legacy tables).
    /// Not supported on SQLite (FluentMigrator's SQLite generator does not implement this DDL).
    /// </summary>
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

        if (!self.Schema.Schema(schemaName).Table(tableName).Constraint(keyName).Exists())
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
    /// Inserts a row into <paramref name="tableName"/> only if no row matching <paramref name="keyValues"/>
    /// already exists. Intended for idempotently seeding small reference/lookup tables.
    /// </summary>
    /// <remarks>
    /// Uses a portable <c>INSERT ... SELECT ... WHERE NOT EXISTS (...)</c> statement that runs unmodified on
    /// SQL Server, PostgreSQL, MySQL and SQLite — no per-provider branching needed. Values are formatted as SQL
    /// literals (strings are quote-escaped; <c>null</c> key values use <c>IS NULL</c> so they still match;
    /// booleans become <c>1</c>/<c>0</c>; <see cref="Guid"/> is quoted; enums use their underlying numeric value);
    /// table and column identifiers are not quoted, so avoid reserved words.
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

        var qualifiedTable = string.IsNullOrEmpty(schemaName) ? tableName : $"{schemaName}.{tableName}";

        var values = new Dictionary<string, object?>();
        foreach (var kv in keyValues)
            values[kv.Key] = kv.Value;
        if (additionalValues is not null)
            foreach (var kv in additionalValues)
                values[kv.Key] = kv.Value;

        var columns = values.Keys.ToList();
        var selectList = string.Join(", ", columns.Select(c => FormatSqlValue(values[c])));
        var whereClause = string.Join(" AND ", keyValues.Select(kv => FormatSqlPredicate(kv.Key, kv.Value)));

        self.Execute.Sql($@"INSERT INTO {qualifiedTable} ({string.Join(", ", columns)})
SELECT {selectList}
WHERE NOT EXISTS (SELECT 1 FROM {qualifiedTable} WHERE {whereClause});");
    }

    /// <summary>
    /// Formats a <c>column = value</c> predicate, using <c>IS NULL</c> instead of <c>= NULL</c> — in SQL,
    /// <c>x = NULL</c> is never true (three-valued logic), so a literal <c>=</c> would silently defeat the
    /// existence check for nullable key columns.
    /// </summary>
    private static string FormatSqlPredicate(string column, object? value)
        => value is null ? $"{column} IS NULL" : $"{column} = {FormatSqlValue(value)}";

    private static string FormatSqlValue(object? value)
    {
        return value switch
        {
            null => "NULL",
            string s => $"'{s.Replace("'", "''")}'",
            bool b => b ? "1" : "0",
            DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss}'",
            Guid g => $"'{g}'",
            Enum e => Convert.ToInt64(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "NULL"
        };
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
}
