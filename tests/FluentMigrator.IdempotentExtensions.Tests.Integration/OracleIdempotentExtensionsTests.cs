using System.Data.Common;
using FluentMigrator.Runner;
using Oracle.ManagedDataAccess.Client;
using Testcontainers.Oracle;

namespace FluentMigrator.IdempotentExtensions.Tests.Integration;

public sealed class OracleIdempotentExtensionsTests : IdempotentExtensionsIntegrationTestsBase
{
    // The version number in the tag matters: Testcontainers.Oracle infers the service name (FREEPDB1 for
    // 23+, XEPDB1 for 12-22, XE for 11-) from it — an unversioned tag like "slim-faststart" fails to parse
    // and silently falls back to the legacy "XE" service, which this image doesn't register (ORA-12514).
    private readonly OracleContainer _container = new OracleBuilder("gvenzl/oracle-free:23-slim-faststart").Build();

    protected override bool SupportsAlterColumnKeepingNullable => false;

    protected override async Task<string> StartContainerAsync()
    {
        await _container.StartAsync();
        return _container.GetConnectionString();
    }

    protected override Task StopContainerAsync() => _container.DisposeAsync().AsTask();

    // 12C-managed: the managed (cross-platform) ODP.NET driver, paired with the 12c+ SQL generator
    // that emits native GENERATED AS IDENTITY — required by WithIdColumn's .Identity() call, which
    // the pre-12c generator can't express.
    protected override void ConfigureProvider(IMigrationRunnerBuilder builder, string connectionString) =>
        builder.AddOracle12CManaged().WithGlobalConnectionString(connectionString);

    protected override DbConnection CreateConnection(string connectionString) => new OracleConnection(connectionString);

    protected override string BuildCreateTriggerSql(string triggerName, string tableName) => $@"
CREATE TRIGGER {triggerName} AFTER INSERT ON {tableName} FOR EACH ROW
BEGIN
    NULL;
END;";

    protected override string BuildCreateFunctionSql(string functionName) => $@"
CREATE FUNCTION {functionName} RETURN NUMBER IS
BEGIN
    RETURN 1;
END;";
}
