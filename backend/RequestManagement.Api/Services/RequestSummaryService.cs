using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RequestManagement.Api.Contracts.Requests;
using RequestManagement.Api.Data;
using RequestManagement.Api.Entities;

namespace RequestManagement.Api.Services;

/// <summary>
/// Summary aggregations with a 30-second, process-local cache. Registered as a singleton because the cache
/// generation counter must be shared by every request in the process.
/// </summary>
/// <remarks>
/// Only this endpoint is cached: it scans the whole table, is read on every page load, and tolerates being a few
/// seconds old. Lists and single requests are never cached because they carry concurrency tokens that must be current.
/// IMemoryCache is per process: with several API instances, each has its own copy and invalidation only reaches the
/// instance that made the change (a distributed cache / pub-sub invalidation would be needed; see docs/DECISIONS.md).
/// </remarks>
public sealed class RequestSummaryService(
    IDbContextFactory<AppDbContext> dbContextFactory,
    IMemoryCache cache,
    TimeProvider timeProvider,
    ILogger<RequestSummaryService> logger)
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    // Versioned cache key. Invalidate() bumps the generation instead of only removing the entry: a summary query that
    // started before a status change commits and finishes after the invalidation writes under the OLD key, which no
    // reader looks up any more, so a stale in-flight fill cannot repopulate the active key.
    private long _generation;

    public async Task<RequestSummaryDto> GetAsync(CancellationToken cancellationToken)
    {
        var key = CacheKey(Interlocked.Read(ref _generation));
        if (cache.TryGetValue(key, out RequestSummaryDto? cached) && cached is not null)
        {
            return cached;
        }

        var summary = await ComputeAsync(cancellationToken);
        cache.Set(key, summary, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheDuration });
        return summary;
    }

    /// <summary>Call only after a status change has been committed to the database.</summary>
    public void Invalidate()
    {
        var previous = Interlocked.Increment(ref _generation) - 1;
        cache.Remove(CacheKey(previous));
        logger.LogDebug("Summary cache invalidated (generation {Generation})", previous + 1);
    }

    private async Task<RequestSummaryDto> ComputeAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Two GROUP BY queries executed by SQL Server; only 4 + 3 rows come back.
        var byStatus = await db.Requests
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, cancellationToken);

        var byPriority = await db.Requests
            .GroupBy(r => r.Priority)
            .Select(g => new { Priority = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Priority, x => x.Count, cancellationToken);

        // Status is NOT NULL, so the status groups partition the table and their sum is the total row count.
        return new RequestSummaryDto(
            TotalCount: byStatus.Values.Sum(),
            ByStatus: Enum.GetValues<RequestStatus>().Select(s => new StatusCount(s, byStatus.GetValueOrDefault(s))).ToList(),
            ByPriority: Enum.GetValues<RequestPriority>().Select(p => new PriorityCount(p, byPriority.GetValueOrDefault(p))).ToList(),
            GeneratedAt: timeProvider.GetUtcNow());
    }

    private static string CacheKey(long generation) => $"requests:summary:g{generation}";
}
