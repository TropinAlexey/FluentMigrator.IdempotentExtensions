using System.Data.Common;
using FluentMigrator.Runner;
using Npgsql;
using Testcontainers.PostgreSql;

namespace FluentMigrator.IdempotentExtensions.Tests.Integration;

public sealed class PostgresIdempotentExtensionsTests : IdempotentExtensionsIntegrationTestsBase
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:latest").Build();

    protected override async Task<string> StartContainerAsync()
    {
        await _container.StartAsync();

        // SSL Mode=Disable: the container doesn't request TLS, but Npgsql's default SSL negotiation
        // attempt against a just-started container has occasionally failed with an EndOfStreamException
        // mid-handshake ("SetupEncryption") — disabling the negotiation entirely removes that flake.
        return $"{_container.GetConnectionString()};SSL Mode=Disable";
    }

    protected override Task StopContainerAsync() => _container.DisposeAsync().AsTask();

    protected override void ConfigureProvider(IMigrationRunnerBuilder builder, string connectionString) =>
        builder.AddPostgres().WithGlobalConnectionString(connectionString);

    protected override DbConnection CreateConnection(string connectionString) => new NpgsqlConnection(connectionString);

    protected override void EnsureTriggerPrerequisites() => Run(new CreatePostgresTriggerFunctionMigration());

    protected override string BuildCreateTriggerSql(string triggerName, string tableName) =>
        $"CREATE TRIGGER {triggerName} AFTER INSERT ON {tableName} FOR EACH ROW EXECUTE FUNCTION test_trigger_noop_fn();";

    protected override string BuildCreateFunctionSql(string functionName) => $@"
CREATE FUNCTION {functionName}() RETURNS INT AS $$
BEGIN
    RETURN 1;
END;
$$ LANGUAGE plpgsql;";
}
