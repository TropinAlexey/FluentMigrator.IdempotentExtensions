using System;
using System.Collections.Generic;
using System.Data;
using FluentMigrator;
using FluentMigrator.Expressions;
using FluentMigrator.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace FluentMigrator.IdempotentExtensions.Tests;

/// <summary>
/// Captures the SQL statement produced by a migration expression without hitting a real database.
/// </summary>
internal static class SqlCapture
{
    public static string For(string databaseType, Action<Migration> act)
    {
        var processor = new FakeMigrationProcessor(databaseType);
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var context = new MigrationContext(processor, serviceProvider, string.Empty);
        var migration = new CapturingMigration(act);

        migration.GetUpExpressions(context);

        var expression = (ExecuteSqlStatementExpression)context.Expressions.Single();
        return expression.SqlStatement;
    }
}

internal sealed class CapturingMigration : Migration
{
    private readonly Action<Migration> _act;

    public CapturingMigration(Action<Migration> act) => _act = act;

    public override void Up() => _act(this);
    public override void Down() { }
}

/// <summary>
/// Minimal fake processor that only supplies the database type and schema queries.
/// <see cref="IfDatabaseExpressionRoot"/> requires an <see cref="IMigrationProcessor"/>
/// to resolve the database type, not just an <see cref="IQuerySchema"/>.
/// </summary>
internal sealed class FakeMigrationProcessor : IMigrationProcessor
{
    public FakeMigrationProcessor(string databaseType)
    {
        DatabaseType = databaseType;
        DatabaseTypeAliases = new List<string>();
    }

    public string DatabaseType { get; }
    public IList<string> DatabaseTypeAliases { get; }

    public bool SchemaExists(string schemaName) => false;
    public bool TableExists(string schemaName, string tableName) => false;
    public bool ColumnExists(string schemaName, string tableName, string columnName) => false;
    public bool ConstraintExists(string schemaName, string tableName, string constraintName) => false;
    public bool DefaultValueExists(string schemaName, string tableName, string columnName, object defaultValue) => false;
    public bool IndexExists(string schemaName, string tableName, string indexName) => false;
    public bool SequenceExists(string schemaName, string sequenceName) => false;

    public void Execute(string sql) => throw new NotSupportedException();
    public void Execute(string template, params object[] args) => throw new NotSupportedException();
    public DataSet ReadTableData(string schemaName, string tableName) => throw new NotSupportedException();
    public DataSet Read(string template, params object[] args) => throw new NotSupportedException();
    public bool Exists(string template, params object[] args) => throw new NotSupportedException();
    public void BeginTransaction() => throw new NotSupportedException();
    public void CommitTransaction() => throw new NotSupportedException();
    public void RollbackTransaction() => throw new NotSupportedException();
    public void Process(CreateSchemaExpression expression) => throw new NotSupportedException();
    public void Process(DeleteSchemaExpression expression) => throw new NotSupportedException();
    public void Process(AlterTableExpression expression) => throw new NotSupportedException();
    public void Process(AlterColumnExpression expression) => throw new NotSupportedException();
    public void Process(CreateTableExpression expression) => throw new NotSupportedException();
    public void Process(CreateColumnExpression expression) => throw new NotSupportedException();
    public void Process(DeleteTableExpression expression) => throw new NotSupportedException();
    public void Process(DeleteColumnExpression expression) => throw new NotSupportedException();
    public void Process(CreateForeignKeyExpression expression) => throw new NotSupportedException();
    public void Process(DeleteForeignKeyExpression expression) => throw new NotSupportedException();
    public void Process(CreateIndexExpression expression) => throw new NotSupportedException();
    public void Process(DeleteIndexExpression expression) => throw new NotSupportedException();
    public void Process(RenameTableExpression expression) => throw new NotSupportedException();
    public void Process(RenameColumnExpression expression) => throw new NotSupportedException();
    public void Process(InsertDataExpression expression) => throw new NotSupportedException();
    public void Process(AlterDefaultConstraintExpression expression) => throw new NotSupportedException();
    public void Process(PerformDBOperationExpression expression) => throw new NotSupportedException();
    public void Process(DeleteDataExpression expression) => throw new NotSupportedException();
    public void Process(UpdateDataExpression expression) => throw new NotSupportedException();
    public void Process(AlterSchemaExpression expression) => throw new NotSupportedException();
    public void Process(CreateSequenceExpression expression) => throw new NotSupportedException();
    public void Process(DeleteSequenceExpression expression) => throw new NotSupportedException();
    public void Process(CreateConstraintExpression expression) => throw new NotSupportedException();
    public void Process(DeleteConstraintExpression expression) => throw new NotSupportedException();
    public void Process(DeleteDefaultConstraintExpression expression) => throw new NotSupportedException();

    public void Dispose() { }
}
