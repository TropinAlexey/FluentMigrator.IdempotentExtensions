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

        if (self.GetDatabaseType().IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($"ALTER TABLE {QualifyTable(schemaName, tableName)} MODIFY {columnName} DEFAULT NULL;");
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

        self.Execute.Sql($"ALTER TABLE {QualifyTable(schemaName, tableName)} ADD CONSTRAINT {constraintName} CHECK ({checkSql});");
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
    public static void AddColumnDefaultIfExists(
        this Migration self,
        string tableName,
        string columnName,
        object? defaultValue,
        string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();

        if (!self.ColumnExists(tableName, columnName, schemaName))
            return;

        var formattedValue = FormatSqlValue(defaultValue);

        if (self.GetDatabaseType().IndexOf("SqlServer", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($@"
IF NOT EXISTS (
    SELECT 1
    FROM sys.default_constraints dc
    JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'[{schemaName}].[{tableName}]')
      AND c.name = N'{columnName}'
)
    ALTER TABLE [{schemaName}].[{tableName}] ADD CONSTRAINT [DF_{tableName}_{columnName}] DEFAULT {formattedValue} FOR [{columnName}];");
            return;
        }

        if (self.GetDatabaseType().IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($"ALTER TABLE {QualifyTable(schemaName, tableName)} MODIFY {columnName} DEFAULT {formattedValue};");
            return;
        }

        self.Execute.Sql($"ALTER TABLE {QualifyTable(schemaName, tableName)} ALTER COLUMN {columnName} SET DEFAULT {formattedValue};");
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
    /// <param name="selectSql">The view's <c>SELECT</c> statement, without the <c>CREATE VIEW ... AS</c> prefix.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
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
            self.Execute.Sql($@"
IF NOT EXISTS (SELECT 1 FROM sys.views WHERE object_id = OBJECT_ID(N'[{schemaName}].[{viewName}]'))
    EXEC('CREATE VIEW [{schemaName}].[{viewName}] AS {selectSql.Replace("'", "''")}');");
            return;
        }

        if (databaseType.IndexOf("SQLite", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($"CREATE VIEW IF NOT EXISTS {viewName} AS {selectSql};");
            return;
        }

        self.Execute.Sql($"CREATE OR REPLACE VIEW {QualifyTable(schemaName, viewName)} AS {selectSql};");
    }

    /// <summary>
    /// Drops <paramref name="viewName"/> if it exists, via the native <c>DROP VIEW IF EXISTS</c> — supported
    /// by SQL Server (2016+), PostgreSQL, MySQL, and SQLite alike.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="viewName">Name of the view to drop.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
    public static void DropViewIfExists(this Migration self, string viewName, string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();
        self.Execute.Sql($"DROP VIEW IF EXISTS {QualifyTable(schemaName, viewName)};");
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

        if (self.GetDatabaseType().IndexOf("Postgres", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($"DROP TRIGGER IF EXISTS {triggerName} ON {QualifyTable(schemaName, tableName)};");
            return;
        }

        if (self.GetDatabaseType().IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            // Oracle has no DROP TRIGGER IF EXISTS: swallow ORA-04080 ("trigger does not exist").
            self.Execute.Sql($@"
BEGIN
    EXECUTE IMMEDIATE 'DROP TRIGGER {triggerName}';
EXCEPTION
    WHEN OTHERS THEN
        IF SQLCODE != -4080 THEN
            RAISE;
        END IF;
END;");
            return;
        }

        self.Execute.Sql($"DROP TRIGGER IF EXISTS {triggerName};");
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
    /// <param name="self">The migration instance.</param>
    /// <param name="functionName">Name of the function to drop.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
    public static void DropFunctionIfExists(this Migration self, string functionName, string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();
        var databaseType = self.GetDatabaseType();

        if (databaseType.IndexOf("SqlServer", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($"DROP FUNCTION IF EXISTS [{schemaName}].[{functionName}];");
            return;
        }

        if (databaseType.IndexOf("Postgres", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($"DROP FUNCTION IF EXISTS {QualifyTable(schemaName, functionName)};");
            return;
        }

        if (databaseType.IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            // Oracle has no DROP FUNCTION IF EXISTS: swallow ORA-04043 ("object does not exist").
            self.Execute.Sql($@"
BEGIN
    EXECUTE IMMEDIATE 'DROP FUNCTION {functionName}';
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
            self.Execute.Sql($"EXEC sp_rename N'{schemaName}.{tableName}.{oldName}', N'{newName}', N'INDEX';");
            return;
        }

        if (databaseType.IndexOf("Postgres", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($"ALTER INDEX {QualifyTable(schemaName, oldName)} RENAME TO {newName};");
            return;
        }

        if (databaseType.IndexOf("MySql", StringComparison.OrdinalIgnoreCase) >= 0 ||
            databaseType.IndexOf("MariaDb", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($"ALTER TABLE {QualifyTable(schemaName, tableName)} RENAME INDEX {oldName} TO {newName};");
            return;
        }

        if (databaseType.IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            // Oracle index names are unique per schema, so no table qualifier is needed — but the
            // schema itself still must be, otherwise this targets the connected user's default schema.
            self.Execute.Sql($"ALTER INDEX {QualifyTable(schemaName, oldName)} RENAME TO {newName};");
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
            self.Execute.Sql($"EXEC sp_rename N'{schemaName}.{tableName}.{oldName}', N'{newName}';");
            return;
        }

        if (databaseType.IndexOf("Postgres", StringComparison.OrdinalIgnoreCase) >= 0 ||
            databaseType.IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            self.Execute.Sql($"ALTER TABLE {QualifyTable(schemaName, tableName)} RENAME CONSTRAINT {oldName} TO {newName};");
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
    /// <c>null</c> compared with <c>IS NULL</c>, <see cref="Guid"/> quoted, enums as their numeric value);
    /// table and column identifiers are not quoted, so avoid reserved words.
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

        var setClause = string.Join(", ", setValues.Select(kv => $"{kv.Key} = {FormatSqlValue(kv.Value)}"));
        var whereClause = string.Join(" AND ", keyValues.Select(kv => FormatSqlPredicate(kv.Key, kv.Value)));

        self.Execute.Sql($"UPDATE {QualifyTable(schemaName, tableName)} SET {setClause} WHERE {whereClause};");
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

        var whereClause = string.Join(" AND ", keyValues.Select(kv => FormatSqlPredicate(kv.Key, kv.Value)));

        self.Execute.Sql($"DELETE FROM {QualifyTable(schemaName, tableName)} WHERE {whereClause};");
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

        var qualifiedTable = QualifyTable(schemaName, tableName);

        var values = new Dictionary<string, object?>();
        foreach (var kv in keyValues)
            values[kv.Key] = kv.Value;
        if (additionalValues is not null)
            foreach (var kv in additionalValues)
                values[kv.Key] = kv.Value;

        var columns = values.Keys.ToList();
        var selectList = string.Join(", ", columns.Select(c => FormatSqlValue(values[c])));
        var whereClause = string.Join(" AND ", keyValues.Select(kv => FormatSqlPredicate(kv.Key, kv.Value)));

        // Oracle has no FROM-less SELECT — every SELECT needs a source, hence FROM DUAL.
        var fromDual = self.GetDatabaseType().IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0
            ? " FROM DUAL"
            : "";

        self.Execute.Sql($@"INSERT INTO {qualifiedTable} ({string.Join(", ", columns)})
SELECT {selectList}{fromDual}
WHERE NOT EXISTS (SELECT 1 FROM {qualifiedTable} WHERE {whereClause});");
    }

    /// <summary>
    /// Formats a <c>column = value</c> predicate, using <c>IS NULL</c> instead of <c>= NULL</c> — in SQL,
    /// <c>x = NULL</c> is never true (three-valued logic), so a literal <c>=</c> would silently defeat the
    /// existence check for nullable key columns.
    /// </summary>
    private static string QualifyTable(string schemaName, string tableName)
        => string.IsNullOrEmpty(schemaName) ? tableName : $"{schemaName}.{tableName}";

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
}
