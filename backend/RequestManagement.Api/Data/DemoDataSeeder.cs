using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RequestManagement.Api.Entities;

namespace RequestManagement.Api.Data;

/// <summary>Seed metadata and resulting row counts, printed by the CLI commands.</summary>
public sealed record SeedReport(bool Seeded, DemoSeedInfo Info, int RequestRows, int HistoryRows);

/// <summary>
/// Deterministic demo dataset: 100,000 requests and their status history, generated from a fixed random seed and a
/// fixed time anchor, so every run produces identical rows (same IDs, titles, dates and distributions) on a given runtime.
/// </summary>
/// <remarks>
/// Rows are streamed with SqlBulkCopy inside one transaction rather than added through the EF change tracker:
/// ~250K inserts through tracked entities would mean thousands of batched round trips and large memory use, while
/// bulk copy writes them in a few seconds. CheckConstraints keeps the FK and CHECK constraints trusted.
/// </remarks>
public sealed class DemoDataSeeder(AppDbContext db, TimeProvider timeProvider)
{
    public const string SeedVersion = "v1";
    public const int RandomSeed = 20261001;
    public const int DefaultRequestCount = 100_000;
    public static readonly DateTimeOffset TimeAnchorUtc = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private const int CreatedSpanDays = 540;

    private static readonly string[] Actions =
    [
        "Review", "Update", "Investigate", "Configure", "Escalate", "Approve", "Migrate", "Reconcile", "Document", "Validate",
    ];

    private static readonly string[] Subjects =
    [
        "payment provider timeout", "payment failure alert", "refund request", "invoice discrepancy", "chargeback dispute",
        "settlement report", "merchant account limits", "card terminal setup", "compliance review", "API integration",
        "fraud alert", "merchant onboarding", "payout schedule", "currency conversion fees", "monthly statement",
        "user access rights", "webhook delivery", "tax configuration",
    ];

    private static readonly string[] OrgAdjectives =
    [
        "Blue", "Silver", "Northern", "Bright", "Golden", "Rapid", "Green", "Crystal", "Summit", "Coastal",
        "Urban", "Prime", "Atlas", "Nova", "Vertex", "Maple", "Cedar", "Granite", "Lunar", "Iron",
    ];

    private static readonly string[] OrgNouns =
    [
        "Harbor", "River", "Peak", "Valley", "Bridge", "Forge", "Field", "Point", "Gate", "Works",
    ];

    private static readonly string[] OrgSuffixes =
    [
        "Logistics", "Retail", "Holdings", "Systems", "Partners", "Labs", "Group", "Ltd",
    ];

    public async Task<SeedReport> SeedAsync(int requestCount, CancellationToken cancellationToken)
    {
        var existing = await db.DemoSeedInfo.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            // Idempotent: the dataset is already there; never duplicate it.
            return await ReportAsync(seeded: false, existing, cancellationToken);
        }

        if (await db.Requests.AnyAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "The database contains requests but no demo seed marker. Refusing to seed into a non-demo database.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var info = await InsertDatasetAsync(transaction, requestCount, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await ReportAsync(seeded: true, info, cancellationToken);
    }

    /// <summary>Deletes all requests and history and regenerates the dataset, atomically. Demo databases only.</summary>
    public async Task<SeedReport> ResetAndSeedAsync(int requestCount, CancellationToken cancellationToken)
    {
        var isMarkedDemo = await db.DemoSeedInfo.AnyAsync(cancellationToken);
        if (!isMarkedDemo && await db.Requests.AnyAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "The database contains requests but no demo seed marker. reset-and-seed only runs against a demo database.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.RequestStatusHistory.ExecuteDeleteAsync(cancellationToken);
        await db.Requests.ExecuteDeleteAsync(cancellationToken);
        await db.DemoSeedInfo.ExecuteDeleteAsync(cancellationToken);
        var info = await InsertDatasetAsync(transaction, requestCount, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await ReportAsync(seeded: true, info, cancellationToken);
    }

    private async Task<DemoSeedInfo> InsertDatasetAsync(
        IDbContextTransaction transaction, int requestCount, CancellationToken cancellationToken)
    {
        var (requests, history) = Generate(requestCount);

        var connection = (SqlConnection)db.Database.GetDbConnection();
        var sqlTransaction = (SqlTransaction)transaction.GetDbTransaction();
        await BulkInsertAsync(connection, sqlTransaction, "dbo.Requests", requests, cancellationToken);
        await BulkInsertAsync(connection, sqlTransaction, "dbo.RequestStatusHistory", history, cancellationToken);

        // IDs were supplied explicitly (KeepIdentity) so they are reproducible; move each identity past the max value.
        await db.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('dbo.Requests', RESEED) WITH NO_INFOMSGS", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('dbo.RequestStatusHistory', RESEED) WITH NO_INFOMSGS", cancellationToken);

        var info = new DemoSeedInfo
        {
            SeedVersion = SeedVersion,
            RandomSeed = RandomSeed,
            TimeAnchorUtc = TimeAnchorUtc,
            RequestCount = requests.Rows.Count,
            HistoryCount = history.Rows.Count,
            SeededAt = timeProvider.GetUtcNow(),
        };
        db.DemoSeedInfo.Add(info);
        await db.SaveChangesAsync(cancellationToken);
        return info;
    }

    private static async Task BulkInsertAsync(
        SqlConnection connection, SqlTransaction transaction, string table, DataTable rows, CancellationToken cancellationToken)
    {
        using var bulkCopy = new SqlBulkCopy(
            connection,
            SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.TableLock,
            transaction)
        {
            DestinationTableName = table,
            BatchSize = 10_000,
            BulkCopyTimeout = 600,
        };
        foreach (DataColumn column in rows.Columns)
        {
            bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await bulkCopy.WriteToServerAsync(rows, cancellationToken);
    }

    /// <summary>Pure, deterministic generation; no I/O. Public for unit tests of reproducibility.</summary>
    public static (DataTable Requests, DataTable History) Generate(int requestCount)
    {
        var random = new Random(RandomSeed);
        var organizations = BuildOrganizations();

        var requests = new DataTable();
        requests.Columns.Add(nameof(Request.Id), typeof(long));
        requests.Columns.Add(nameof(Request.Title), typeof(string));
        requests.Columns.Add(nameof(Request.OrganizationName), typeof(string));
        requests.Columns.Add(nameof(Request.Status), typeof(byte));
        requests.Columns.Add(nameof(Request.Priority), typeof(byte));
        requests.Columns.Add(nameof(Request.AssignedTo), typeof(string));
        requests.Columns.Add(nameof(Request.CreatedAt), typeof(DateTimeOffset));
        requests.Columns.Add(nameof(Request.UpdatedAt), typeof(DateTimeOffset));

        var history = new DataTable();
        history.Columns.Add(nameof(RequestStatusHistory.Id), typeof(long));
        history.Columns.Add(nameof(RequestStatusHistory.RequestId), typeof(long));
        history.Columns.Add(nameof(RequestStatusHistory.PreviousStatus), typeof(byte));
        history.Columns.Add(nameof(RequestStatusHistory.NewStatus), typeof(byte));
        history.Columns.Add(nameof(RequestStatusHistory.ChangedAt), typeof(DateTimeOffset));
        history.Columns.Add(nameof(RequestStatusHistory.ChangedBy), typeof(string));

        long historyId = 0;
        for (long id = 1; id <= requestCount; id++)
        {
            var title = $"{Pick(random, Actions)} {Pick(random, Subjects)} (REF-{id:D6})";
            var organization = Pick(random, organizations);
            var priority = PickPriority(random);
            string? assignedTo = random.NextDouble() < 0.15 ? null : $"agent-{random.Next(1, 41):D2}";
            var createdAt = TimeAnchorUtc.AddSeconds(-random.NextInt64(1, CreatedSpanDays * 24L * 3600));

            var path = PickStatusPath(random);
            var changedAt = createdAt;
            foreach (var (from, to) in path)
            {
                // Each change happens 5–40% of the way from the previous change to the anchor: increasing, never in the future.
                var remainingSeconds = (TimeAnchorUtc - changedAt).TotalSeconds;
                changedAt = changedAt.AddSeconds(Math.Floor(remainingSeconds * (0.05 + random.NextDouble() * 0.35)));
                history.Rows.Add(++historyId, id, (byte)from, (byte)to, changedAt, assignedTo ?? "seed-dispatcher");
            }

            var status = path.Count == 0 ? RequestStatus.New : path[^1].To;
            requests.Rows.Add(id, title, organization, (byte)status, (byte)priority, (object?)assignedTo ?? DBNull.Value, createdAt, changedAt);
        }

        return (requests, history);
    }

    // 20 adjectives x 10 nouns = 200 distinct organization names, e.g. "Blue Harbor Logistics".
    private static string[] BuildOrganizations() =>
        OrgAdjectives
            .SelectMany((adjective, a) => OrgNouns.Select((noun, n) => $"{adjective} {noun} {OrgSuffixes[(a + n) % OrgSuffixes.Length]}"))
            .ToArray();

    private static T Pick<T>(Random random, T[] values) => values[random.Next(values.Length)];

    private static RequestPriority PickPriority(Random random) => random.Next(100) switch
    {
        < 35 => RequestPriority.Low,
        < 80 => RequestPriority.Medium,
        _ => RequestPriority.High,
    };

    /// <summary>A legal sequence of transitions ending in the request's final status (weights ≈ 25/30/15/30 %).</summary>
    private static List<(RequestStatus From, RequestStatus To)> PickStatusPath(Random random)
    {
        const RequestStatus n = RequestStatus.New, p = RequestStatus.InProgress, w = RequestStatus.Waiting, c = RequestStatus.Completed;
        var roll = random.Next(100);
        var variant = random.NextDouble();
        return roll switch
        {
            < 25 => [],
            < 55 => variant < 0.3 ? [(n, w), (w, p)] : [(n, p)],
            < 70 => variant < 0.5 ? [(n, p), (p, w)] : [(n, w)],
            _ => variant < 0.4 ? [(n, p), (p, w), (w, c)] : [(n, p), (p, c)],
        };
    }

    private async Task<SeedReport> ReportAsync(bool seeded, DemoSeedInfo info, CancellationToken cancellationToken) =>
        new(
            seeded,
            info,
            await db.Requests.CountAsync(cancellationToken),
            await db.RequestStatusHistory.CountAsync(cancellationToken));
}
