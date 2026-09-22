namespace FluentMigrator.IdempotentExtensions.SqlServer;

/// <summary>
/// SQL Server-specific idempotent extension methods for FluentMigrator migrations.
/// These methods execute raw T-SQL and only work with SQL Server / Azure SQL.
/// </summary>
public static class SqlServerExtensions
{
    /// <summary>
    /// Drops the DEFAULT constraint on <paramref name="columnName"/> if one exists.
    /// Uses <c>sys.default_constraints</c> to locate the constraint by column — safe regardless of
    /// the constraint's auto-generated name.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Target table name.</param>
    /// <param name="columnName">Column whose DEFAULT constraint should be removed.</param>
    /// <param name="schemaName">Database schema. Defaults to <c>dbo</c>.</param>
    /// <remarks>
    /// SQL Server only. Uses a CURSOR + <c>sp_executesql</c> to handle any constraint name.
    /// Kept for backward compatibility — new code targeting multiple providers should use
    /// <c>DropColumnDefaultIfExists</c> from the core package instead, which covers SQL Server,
    /// PostgreSQL and MySQL with one call.
    /// </remarks>
    public static void DropDefaultConstraintIfExists(
        this Migration self,
        string tableName,
        string columnName,
        string schemaName = "dbo")
    {
        var escSchema = schemaName.Replace("]", "]]");
        var escTable = tableName.Replace("]", "]]");
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
    }
}
