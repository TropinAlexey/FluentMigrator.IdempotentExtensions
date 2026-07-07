using System;
using System.IO;
using FluentMigrator;
using FluentMigrator.IdempotentExtensions;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;

namespace FluentMigrator.IdempotentExtensions.Tests;

/// <summary>
/// Integration tests using SQLite to verify idempotency of all extension methods.
/// Each test fixture gets its own SQLite file; each migration is run twice to confirm no exception.
/// Note: SQLite does not support schemas — pass <c>schemaName: ""</c> in test migrations.
/// </summary>
public sealed class IdempotentExtensionsTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly IServiceProvider _services;

    public IdempotentExtensionsTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"fm_test_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath}";

        _services = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddSQLite()
                .WithGlobalConnectionString(_connectionString)
                .ScanIn(typeof(IdempotentExtensionsTests).Assembly).For.Migrations())
            .AddLogging()
            .BuildServiceProvider(false);
    }

    private void Run(Migration migration)
    {
        using var scope = _services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
        runner.Up(migration);
    }

    // ---- tests ----

    [Fact]
    public void CreateTableIfNotExists_IsIdempotent()
    {
        Run(new CreateTableMigration());
        var ex = Record.Exception(() => Run(new CreateTableMigration()));
        Assert.Null(ex);
    }

    [Fact]
    public void CreateColumnIfNotExists_IsIdempotent()
    {
        Run(new CreateTableMigration());
        Run(new AddColumnMigration());
        var ex = Record.Exception(() => Run(new AddColumnMigration()));
        Assert.Null(ex);
    }

    [Fact]
    public void CreateIndexIfNotExists_IsIdempotent()
    {
        Run(new CreateTableMigration());
        Run(new AddColumnMigration());
        Run(new AddIndexMigration());
        var ex = Record.Exception(() => Run(new AddIndexMigration()));
        Assert.Null(ex);
    }

    [Fact]
    public void CreateCompositeIndexIfNotExists_IsIdempotent()
    {
        Run(new CreateTableMigration());
        Run(new AddColumnMigration());
        Run(new AddCompositeIndexMigration());
        var ex = Record.Exception(() => Run(new AddCompositeIndexMigration()));
        Assert.Null(ex);
    }

    [Fact]
    public void CreateLogTableIfNotExists_IsIdempotent()
    {
        Run(new CreateTableMigration());
        Run(new CreateLogTableMigration());
        var ex = Record.Exception(() => Run(new CreateLogTableMigration()));
        Assert.Null(ex);
    }

    [Fact]
    public void DeleteColumnIfExists_IsIdempotent()
    {
        Run(new CreateTableMigration());
        Run(new AddColumnMigration());
        Run(new DeleteColumnMigration());
        var ex = Record.Exception(() => Run(new DeleteColumnMigration()));
        Assert.Null(ex);
    }

    [Fact]
    public void CreateColumnIfNotExists_ReturnsNullWhenTableMissing()
    {
        // Should not throw even though table doesn't exist
        var ex = Record.Exception(() => Run(new AddColumnMigration()));
        Assert.Null(ex);
    }

    [Fact]
    public void DropConstraintIfExists_DoesNotThrowWhenConstraintMissing()
    {
        Run(new CreateTableMigration());
        var ex = Record.Exception(() => Run(new DropConstraintMigration()));
        Assert.Null(ex);
    }

    [Fact]
    public void DropTableIfExists_IsIdempotent()
    {
        Run(new CreateTableMigration());
        Run(new DropTableMigration());
        var ex = Record.Exception(() => Run(new DropTableMigration()));
        Assert.Null(ex);
    }

    [Fact]
    public void DropTableIfExists_DoesNotThrowWhenTableMissing()
    {
        var ex = Record.Exception(() => Run(new DropTableMigration()));
        Assert.Null(ex);
    }

    [Fact]
    public void RenameColumnIfExists_DoesNotThrowWhenColumnMissing()
    {
        Run(new CreateTableMigration());
        var ex = Record.Exception(() => Run(new RenameNonExistentColumnMigration()));
        Assert.Null(ex);
    }

    [Fact]
    public void CreateUniqueConstraintIfNotExists_IsIdempotent()
    {
        Run(new CreateTableMigration());
        Run(new AddUniqueConstraintMigration());
        var ex = Record.Exception(() => Run(new AddUniqueConstraintMigration()));
        Assert.Null(ex);
    }

    [Fact]
    public void CreateTableIfNotExists_WithoutSchemaName_AutoDetectsSqliteEmptySchema_IsIdempotent()
    {
        // No schemaName passed here — must auto-resolve to "" on SQLite (see ResolveDefaultSchema).
        // A wrong non-empty default would make Schema.Schema(x).Table(...).Exists() always report
        // false against SQLite, so the second run would throw "table already exists".
        Run(new CreateTableAutoSchemaMigration());
        var ex = Record.Exception(() => Run(new CreateTableAutoSchemaMigration()));
        Assert.Null(ex);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}

// ---- helper migrations — SQLite: schemaName must be "" (SQLite has no schema support) ----

[Migration(1)]
internal sealed class CreateTableMigration : Migration
{
    public override void Up()
    {
        this.CreateTableIfNotExists("test_users", t => t
            .WithIdColumn()
            .WithColumn("name").AsString(200).NotNullable()
            .WithColumn("email").AsString(500).Nullable(),
            schemaName: "");
    }

    public override void Down() => Delete.Table("test_users");
}

[Migration(11)]
internal sealed class CreateTableAutoSchemaMigration : Migration
{
    public override void Up()
    {
        this.CreateTableIfNotExists("test_users_auto", t => t
            .WithIdColumn()
            .WithColumn("name").AsString(200).NotNullable());
    }

    public override void Down() => Delete.Table("test_users_auto");
}

[Migration(2)]
internal sealed class AddColumnMigration : Migration
{
    public override void Up()
    {
        this.CreateColumnIfNotExists("test_users", "age",
            c => c.AsInt32().Nullable(),
            schemaName: "");
    }

    public override void Down() { }
}

[Migration(3)]
internal sealed class AddIndexMigration : Migration
{
    public override void Up()
    {
        this.CreateIndexIfNotExists("test_users", "email",
            idx => idx.Ascending(),
            schemaName: "");
    }

    public override void Down() { }
}

[Migration(4)]
internal sealed class AddCompositeIndexMigration : Migration
{
    public override void Up()
    {
        this.CreateCompositeIndexIfNotExists("test_users", new[] { "name", "age" },
            idx => idx.WithOptions().NonClustered(),
            schemaName: "");
    }

    public override void Down() { }
}

[Migration(5)]
internal sealed class CreateLogTableMigration : Migration
{
    public override void Up()
    {
        this.CreateLogTableIfNotExists("test_users", schemaName: "");
    }

    public override void Down() { }
}

[Migration(6)]
internal sealed class DeleteColumnMigration : Migration
{
    public override void Up()
    {
        this.DeleteColumnIfExists("test_users", "age", schemaName: "");
    }

    public override void Down() { }
}

[Migration(7)]
internal sealed class DropConstraintMigration : Migration
{
    public override void Up()
    {
        this.DropConstraintIfExists("test_users", "nonexistent_constraint", schemaName: "");
    }

    public override void Down() { }
}

[Migration(8)]
internal sealed class DropTableMigration : Migration
{
    public override void Up()
    {
        this.DropTableIfExists("test_users", schemaName: "");
    }

    public override void Down() { }
}

[Migration(9)]
internal sealed class RenameNonExistentColumnMigration : Migration
{
    public override void Up()
    {
        this.RenameColumnIfExists("test_users", "nonexistent_col", "new_col", schemaName: "");
    }

    public override void Down() { }
}

[Migration(10)]
internal sealed class AddUniqueConstraintMigration : Migration
{
    public override void Up()
    {
        this.CreateUniqueConstraintIfNotExists("test_users", "uc_users_email", new[] { "email" }, schemaName: "");
    }

    public override void Down() { }
}
