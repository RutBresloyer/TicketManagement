using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RequestManagement.Api.Data;

namespace RequestManagement.Tests.Infrastructure;

/// <summary>
/// Shared real SQL Server database for integration tests. EF InMemory/SQLite are deliberately not used:
/// the tests prove SQL Server rowversion and transaction behaviour.
/// </summary>
/// <remarks>
/// Reads <c>ConnectionStrings__RequestManagementTests</c> (see scripts/dev-env.sh). The database is dropped and
/// re-created from the migrations on every run, so the name MUST end with "_Tests" — the fixture refuses anything
/// else, which keeps destructive test setup away from the demo database.
/// </remarks>
public sealed class SqlServerFixture : IAsyncLifetime
{
    public const string EnvironmentVariable = "ConnectionStrings__RequestManagementTests";

    public string ConnectionString { get; private set; } = "";

    public ApiFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        ConnectionString = Environment.GetEnvironmentVariable(EnvironmentVariable)
            ?? throw new InvalidOperationException(
                $"BLOCKED: SQL Server integration tests need {EnvironmentVariable}. " +
                "Start SQL Server and run 'source scripts/dev-env.sh' (README § Tests).");

        var database = new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;
        if (!database.EndsWith("_Tests", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Refusing to run destructive test setup against database '{database}': the name must end with '_Tests'.");
        }

        await using (var db = CreateDbContext())
        {
            await db.Database.EnsureDeletedAsync();
            await db.Database.MigrateAsync();
        }

        Factory = new ApiFactory(ConnectionString);
    }

    public AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options);

    public async Task DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    // Integration tests share one database and run sequentially within this collection,
    // so whole-table assertions (e.g. summary counts) are not disturbed by other tests.
    public const string Name = "SqlServer";
}
