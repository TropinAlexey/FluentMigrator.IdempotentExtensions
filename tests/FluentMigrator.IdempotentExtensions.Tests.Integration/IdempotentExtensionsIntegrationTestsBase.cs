using System.Data.Common;
using FluentMigrator;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;

namespace FluentMigrator.IdempotentExtensions.Tests.Integration;

/// <summary>
/// Shared idempotency test suite run against a real database container. Subclasses supply the
/// Testcontainers instance and FluentMigrator provider wiring; the migrations themselves
/// (<see cref="Migrations"/>) omit <c>schemaName</c> so each provider exercises its own
/// auto-detected default schema (dbo / public / "").
/// </summary>
public abstract class IdempotentExtensionsIntegrationTestsBase : IAsyncLifetime
{
    private IServiceProvider _services = null!;

    protected abstract Task<string> StartContainerAsync();

    protected abstract Task StopContainerAsync();

    protected abstract void ConfigureProvider(IMigrationRunnerBuilder builder, string connectionString);

    protected abstract DbConnection CreateConnection(string connectionString);

    /// <summary>MySQL/MariaDB does not support sequences — subclasses override to skip.</summary>
    protected virtual bool SupportsSequences => true;

    /// <summary>Provider-specific <c>CREATE TRIGGER</c> statement — trigger bodies aren't portable.</summary>
    protected abstract string BuildCreateTriggerSql(string triggerName, string tableName);

    /// <summary>Hook for provider-specific setup a trigger needs before it can be created (e.g. a PostgreSQL trigger function). No-op by default.</summary>
    protected virtual void EnsureTriggerPrerequisites()
    {
    }

    /// <summary>MySQL/MariaDB has no general-purpose SQL function feature — subclasses override to skip.</summary>
    protected virtual bool SupportsFunctions => true;

    /// <summary>Provider-specific <c>CREATE FUNCTION</c> statement — function bodies aren't portable. Unused when <see cref="SupportsFunctions"/> is false.</summary>
    protected virtual string BuildCreateFunctionSql(string functionName) => throw new NotSupportedException();

    /// <summary>MySQL/MariaDB has no general-purpose constraint rename — subclasses override to skip.</summary>
    protected virtual bool SupportsConstraintRename => true;

    /// <summary>
    /// Oracle rejects <c>MODIFY col ... NULL</c> with ORA-01451 whenever the column is already nullable —
    /// unlike every other provider, this fails even on the very first <c>AlterColumnIfExists</c> call if
    /// <c>constructCol</c> specifies <c>.Nullable()</c> on a column that's already nullable (test_users.email
    /// is created nullable, so <see cref="AlterColumnMigration"/>'s <c>.Nullable()</c> always triggers this).
    /// Subclasses override to skip the whole assertion.
    /// </summary>
    protected virtual bool SupportsAlterColumnKeepingNullable => true;

    private string _connectionString = null!;

    public async Task InitializeAsync()
    {
        _connectionString = await StartContainerAsync();

        var services = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(rb =>
            {
                ConfigureProvider(rb, _connectionString);
                rb.ScanIn(typeof(IdempotentExtensionsIntegrationTestsBase).Assembly).For.Migrations();
            })
            .AddLogging();

        _services = services.BuildServiceProvider(false);
    }

    public async Task DisposeAsync() => await StopContainerAsync();

    protected void Run(Migration migration)
    {
        using var scope = _services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
        runner.Up(migration);
    }

    private long CountRows(string tableName)
    {
        using var connection = CreateConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName}";
        return Convert.ToInt64(command.ExecuteScalar());
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
    public void RenameTableIfExists_IsIdempotent()
    {
        Run(new CreateTableMigration());
        Run(new RenameTableMigration());
        var ex = Record.Exception(() => Run(new RenameTableMigration()));
        Assert.Null(ex);
    }

    [Fact]
    public void RenameTableIfExists_DoesNotThrowWhenTableMissing()
    {
        var ex = Record.Exception(() => Run(new RenameNonExistentTableMigration()));
        Assert.Null(ex);
    }

    [Fact]
    public void InsertDataIfNotExists_IsIdempotent()
    {
        Run(new CreateTableMigration());
        Run(new InsertSeedDataMigration());
        Run(new InsertSeedDataMigration());

        Assert.Equal(1, CountRows("test_users"));
    }

    [Fact]
    public void InsertDataIfNotExists_WithNullKeyValue_IsIdempotent()
    {
        Run(new CreateTableMigration());
        Run(new InsertSeedDataWithNullKeyMigration());
        Run(new InsertSeedDataWithNullKeyMigration());

        Assert.Equal(1, CountRows("test_users"));
    }

    // ---- provider-only DDL, not exercised by the SQLite suite ----

    [Fact]
    public void AlterColumnIfExists_AltersExistingColumn_IsIdempotent()
    {
        Run(new CreateTableMigration());

        // ponytail: no-op instead of Skip, same pattern as SupportsSequences/SupportsFunctions — Oracle
        // genuinely can't run this even once (see SupportsAlterColumnKeepingNullable).
        if (!SupportsAlterColumnKeepingNullable)
            return;

        Run(new AlterColumnMigration());
        var ex = Record.Exception(() => Run(new AlterColumnMigration()));
        Assert.Null(ex);
    }

    [Fact]
    public void AlterColumnIfExists_ReturnsNullWhenColumnMissing()
    {
        Run(new CreateTableMigration());
        var ex = Record.Exception(() => Run(new AlterMissingColumnMigration()));
        Assert.Null(ex);
    }

    [Fact]
    public void CreateForeignKeyIfNotExists_IsIdempotent()
    {
        Run(new CreateTableMigration());
        Run(new CreatePrimaryTableForFkMigration());
        Run(new AddForeignKeyColumnMigration());
        Run(new AddForeignKeyMigration());
        var ex = Record.Exception(() => Run(new AddForeignKeyMigration()));
        Assert.Null(ex);
    }

    [Fact]
    public void DropForeignKeyIfExists_IsIdempotent()
    {
        Run(new CreateTableMigration());
        Run(new CreatePrimaryTableForFkMigration());
        Run(new AddForeignKeyColumnMigration());
        Run(new AddForeignKeyMigration());
        Run(new DropForeignKeyMigration());
        var ex = Record.Exception(() => Run(new DropForeignKeyMigration()));
        Assert.Null(ex);
    }

    [Fact]
    public void CreatePrimaryKeyIfNotExists_IsIdempotent()
    {
        Run(new AddPrimaryKeyTableMigration());
        Run(new CreatePrimaryKeyMigration());
        var ex = Record.Exception(() => Run(new CreatePrimaryKeyMigration()));
        Assert.Null(ex);
    }

    [Fact]
    public void CreateSequenceIfNotExists_IsIdempotent()
    {
        // ponytail: no-op instead of proper xunit Skip on MySQL (sequences unsupported there) — one flag beats a parallel Theory/Skip setup for two tests.
        if (!SupportsSequences)
            return;

        Run(new CreateSequenceMigration());
        var ex = Record.Exception(() => Run(new CreateSequenceMigration()));
        Assert.Null(ex);
    }

    [Fact]
    public void DropSequenceIfExists_IsIdempotent()
    {
        if (!SupportsSequences)
            return;

        Run(new CreateSequenceMigration());
        Run(new DropSequenceMigration());
        var ex = Record.Exception(() => Run(new DropSequenceMigration()));
        Assert.Null(ex);
    }

    [Fact]
    public void DropColumnDefaultIfExists_IsIdempotent()
    {
        Run(new CreateTableMigration());
        Run(new AddDefaultColumnMigration());
        Run(new DropColumnDefaultMigration());
        var ex = Record.Exception(() => Run(new DropColumnDefaultMigration()));
        Assert.Null(ex);
    }

    [Fact]
    public void CreateCheckConstraintIfNotExists_IsIdempotent()
    {
        Run(new CreateTableMigration());
        Run(new AddColumnMigration());
        Run(new CreateCheckConstraintMigration());
        var ex = Record.Exception(() => Run(new CreateCheckConstraintMigration()));
        Assert.Null(ex);
    }

    [Fact]
    public void AddColumnDefaultIfExists_IsIdempotent()
    {
        Run(new CreateTableMigration());
        Run(new AddColumnMigration());
        Run(new AddColumnDefaultMigration());
        var ex = Record.Exception(() => Run(new AddColumnDefaultMigration()));
        Assert.Null(ex);
    }

    [Fact]
    public void CreateViewIfNotExists_IsIdempotent()
    {
        Run(new CreateTableMigration());
        Run(new CreateViewMigration());
        var ex = Record.Exception(() => Run(new CreateViewMigration()));
        Assert.Null(ex);
    }

    [Fact]
    public void DropViewIfExists_IsIdempotent()
    {
        Run(new CreateTableMigration());
        Run(new CreateViewMigration());
        Run(new DropViewMigration());
        var ex = Record.Exception(() => Run(new DropViewMigration()));
        Assert.Null(ex);
    }

    [Fact]
    public void CreateTriggerIfNotExists_IsIdempotent()
    {
        Run(new CreateTableMigration());
        EnsureTriggerPrerequisites();

        var sql = BuildCreateTriggerSql("trg_test_users", "test_users");
        Run(new CreateTriggerMigration("trg_test_users", "test_users", sql));
        var ex = Record.Exception(() => Run(new CreateTriggerMigration("trg_test_users", "test_users", sql)));
        Assert.Null(ex);
    }

    [Fact]
    public void DropTriggerIfExists_IsIdempotent()
    {
        Run(new CreateTableMigration());
        EnsureTriggerPrerequisites();

        var sql = BuildCreateTriggerSql("trg_test_users", "test_users");
        Run(new CreateTriggerMigration("trg_test_users", "test_users", sql));
        Run(new DropTriggerMigration("trg_test_users", "test_users"));
        var ex = Record.Exception(() => Run(new DropTriggerMigration("trg_test_users", "test_users")));
        Assert.Null(ex);
    }

    [Fact]
    public void CreateFunctionIfNotExists_IsIdempotent()
    {
        // ponytail: no-op instead of proper xunit Skip on MySQL (no function feature there) — same pattern as SupportsSequences.
        if (!SupportsFunctions)
            return;

        var sql = BuildCreateFunctionSql("test_noop_fn");
        Run(new CreateFunctionMigration("test_noop_fn", sql));
        var ex = Record.Exception(() => Run(new CreateFunctionMigration("test_noop_fn", sql)));
        Assert.Null(ex);
    }

    [Fact]
    public void DropFunctionIfExists_IsIdempotent()
    {
        if (!SupportsFunctions)
            return;

        var sql = BuildCreateFunctionSql("test_noop_fn");
        Run(new CreateFunctionMigration("test_noop_fn", sql));
        Run(new DropFunctionMigration("test_noop_fn"));
        var ex = Record.Exception(() => Run(new DropFunctionMigration("test_noop_fn")));
        Assert.Null(ex);
    }

    [Fact]
    public void RenameIndexIfExists_DoesNotThrowWhenAlreadyRenamed()
    {
        Run(new CreateTableMigration());
        Run(new AddColumnMigration());
        Run(new AddIndexMigration());
        Run(new RenameIndexMigration());
        var ex = Record.Exception(() => Run(new RenameIndexMigration()));
        Assert.Null(ex);
    }

    [Fact]
    public void RenameConstraintIfExists_DoesNotThrowWhenAlreadyRenamed()
    {
        if (!SupportsConstraintRename)
            return;

        Run(new CreateTableMigration());
        Run(new AddUniqueConstraintMigration());
        Run(new RenameConstraintMigration());
        var ex = Record.Exception(() => Run(new RenameConstraintMigration()));
        Assert.Null(ex);
    }

    [Fact]
    public void UpdateDataIfExists_IsIdempotent()
    {
        Run(new CreateTableMigration());
        Run(new InsertSeedDataMigration());
        Run(new UpdateSeedDataMigration());
        var ex = Record.Exception(() => Run(new UpdateSeedDataMigration()));
        Assert.Null(ex);
    }

    [Fact]
    public void DeleteDataIfExists_IsIdempotent()
    {
        Run(new CreateTableMigration());
        Run(new InsertSeedDataMigration());
        Run(new DeleteSeedDataMigration());
        var ex = Record.Exception(() => Run(new DeleteSeedDataMigration()));
        Assert.Null(ex);

        Assert.Equal(0, CountRows("test_users"));
    }
}
