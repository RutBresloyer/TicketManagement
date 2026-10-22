using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace RequestManagement.Api.Data;

/// <summary>
/// Explicit, developer-invoked database commands. The API never migrates, seeds or resets on normal startup.
/// <list type="bullet">
/// <item><c>migrate</c> — apply EF Core migrations.</item>
/// <item><c>seed [--count N]</c> — migrate, then create the demo dataset if it is not there yet (idempotent).</item>
/// <item><c>reset-and-seed [--count N]</c> — Development only, demo-marked (or empty) database only: delete and regenerate.</item>
/// </list>
/// They use <c>ConnectionStrings:RequestManagementAdmin</c> (a principal allowed to run DDL and bulk inserts),
/// falling back to <c>ConnectionStrings:RequestManagement</c>.
/// </summary>
public static class DemoCommands
{
    private static readonly string[] Commands = ["migrate", "seed", "reset-and-seed"];

    public static bool IsCommand(string[] args) => args.Length > 0 && Commands.Contains(args[0]);

    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        var command = args[0];
        var connectionString = app.Configuration.GetConnectionString("RequestManagementAdmin")
            ?? app.Configuration.GetConnectionString("RequestManagement");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("No connection string. Set ConnectionStrings__RequestManagementAdmin (see README § Configuration).");
            return 1;
        }

        if (command == "reset-and-seed" && !app.Environment.IsDevelopment())
        {
            Console.Error.WriteLine("reset-and-seed is only available when ASPNETCORE_ENVIRONMENT=Development.");
            return 2;
        }

        if (!TryReadCount(args, out var count))
        {
            Console.Error.WriteLine("--count must be an integer between 1 and 1000000.");
            return 1;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        // Print where we are writing, never the credentials.
        var target = new SqlConnectionStringBuilder(connectionString);
        Console.WriteLine($"Target: server={target.DataSource}; database={target.InitialCatalog}");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, sql => sql.CommandTimeout(600))
            .Options;
        await using var db = new AppDbContext(options);

        Console.WriteLine("Applying migrations...");
        await db.Database.MigrateAsync(cancellation.Token);
        if (command == "migrate")
        {
            Console.WriteLine("Migrations applied.");
            return 0;
        }

        var seeder = new DemoDataSeeder(db, TimeProvider.System);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var report = command == "seed"
                ? await seeder.SeedAsync(count, cancellation.Token)
                : await seeder.ResetAndSeedAsync(count, cancellation.Token);

            Console.WriteLine(report.Seeded
                ? $"Demo dataset created in {stopwatch.Elapsed.TotalSeconds:F1} s."
                : "Demo dataset already present; nothing inserted (use reset-and-seed to regenerate).");
            Console.WriteLine(
                $"Seed metadata: version={report.Info.SeedVersion}, randomSeed={report.Info.RandomSeed}, " +
                $"timeAnchorUtc={report.Info.TimeAnchorUtc:O}, seededAt={report.Info.SeededAt:O}");
            Console.WriteLine($"Rows: Requests={report.RequestRows:N0}, RequestStatusHistory={report.HistoryRows:N0}");
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    private static bool TryReadCount(string[] args, out int count)
    {
        count = DemoDataSeeder.DefaultRequestCount;
        var index = Array.IndexOf(args, "--count");
        if (index < 0)
        {
            return true;
        }

        return index + 1 < args.Length && int.TryParse(args[index + 1], out count) && count is >= 1 and <= 1_000_000;
    }
}
