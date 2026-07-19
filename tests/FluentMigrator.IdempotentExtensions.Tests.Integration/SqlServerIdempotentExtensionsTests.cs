using System.Data.Common;
using FluentMigrator.Runner;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace FluentMigrator.IdempotentExtensions.Tests.Integration;

public sealed class SqlServerIdempotentExtensionsTests : IdempotentExtensionsIntegrationTestsBase
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:latest").Build();

    protected override async Task<string> StartContainerAsync()
    {
        await _container.StartAsync();
        return _container.GetConnectionString();
    }

    protected override Task StopContainerAsync() => _container.DisposeAsync().AsTask();

    protected override void ConfigureProvider(IMigrationRunnerBuilder builder, string connectionString) =>
        builder.AddSqlServer().WithGlobalConnectionString(connectionString);

    protected override DbConnection CreateConnection(string connectionString) => new SqlConnection(connectionString);

    protected override string BuildCreateTriggerSql(string triggerName, string tableName) => $@"
CREATE TRIGGER {triggerName} ON {tableName} AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
END";

    protected override string BuildCreateFunctionSql(string functionName) =>
        $"CREATE FUNCTION {functionName}() RETURNS INT AS BEGIN RETURN 1; END";
}
