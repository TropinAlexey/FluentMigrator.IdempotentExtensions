using System.Data.Common;
using FluentMigrator.Runner;
using MySqlConnector;
using Testcontainers.MySql;

namespace FluentMigrator.IdempotentExtensions.Tests.Integration;

public sealed class MySqlIdempotentExtensionsTests : IdempotentExtensionsIntegrationTestsBase
{
    // --log-bin-trust-function-creators: the official MySQL image enables binary logging by default, which
    // then requires SUPER privilege to CREATE TRIGGER/FUNCTION unless this is set — without it, trigger tests
    // fail with "You do not have the SUPER privilege and binary logging is enabled".
    private readonly MySqlContainer _container = new MySqlBuilder("mysql:latest")
        .WithCommand("--log-bin-trust-function-creators=1")
        .Build();

    protected override bool SupportsSequences => false;
    protected override bool SupportsFunctions => false;
    protected override bool SupportsConstraintRename => false;

    protected override async Task<string> StartContainerAsync()
    {
        await _container.StartAsync();
        return _container.GetConnectionString();
    }

    protected override Task StopContainerAsync() => _container.DisposeAsync().AsTask();

    protected override void ConfigureProvider(IMigrationRunnerBuilder builder, string connectionString) =>
        builder.AddMySql8().WithGlobalConnectionString(connectionString);

    protected override DbConnection CreateConnection(string connectionString) => new MySqlConnection(connectionString);

    // Body intentionally empty (no user-defined "@var" statements): MySqlConnector's parser treats
    // "@name" as a bound query parameter by default and rejects it unless the connection string sets
    // "Allow User Variables=true", which the plain Testcontainers connection string doesn't set.
    protected override string BuildCreateTriggerSql(string triggerName, string tableName) => $@"
CREATE TRIGGER {triggerName} AFTER INSERT ON {tableName} FOR EACH ROW
BEGIN
END";
}
