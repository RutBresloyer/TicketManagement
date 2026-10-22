using System.Net.Http.Json;
using RequestManagement.Api.Contracts.Requests;
using RequestManagement.Api.Entities;
using RequestManagement.Tests.Infrastructure;

namespace RequestManagement.Tests.Integration;

public sealed class SummaryCacheTests(SqlServerFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Cached_summary_reflects_a_committed_status_change_on_the_next_call()
    {
        var request = (await InsertAsync(NewRequest(UniqueOrganization(), RequestStatus.New, RequestPriority.High)))[0];
        var token = (await GetRequestAsync(request.Id)).RowVersion;
        await StartWithFreshCacheEntryAsync();

        var cached = await GetSummaryAsync();

        // A row inserted behind the API's back is NOT visible yet: proves the summary really is served from cache.
        await InsertAsync(NewRequest(UniqueOrganization(), RequestStatus.New));
        var stillCached = await GetSummaryAsync();
        Assert.Equal(cached.GeneratedAt, stillCached.GeneratedAt);
        Assert.Equal(cached.TotalCount, stillCached.TotalCount);

        // A committed change through the API invalidates the cache; the next call recomputes from SQL.
        await PatchStatusAsync(request.Id, RequestStatus.InProgress, token);
        var fresh = await GetSummaryAsync();

        Assert.Equal(cached.TotalCount + 1, fresh.TotalCount); // includes the row inserted directly
        Assert.Equal(Count(cached, RequestStatus.New), Count(fresh, RequestStatus.New)); // +1 inserted, -1 changed
        Assert.Equal(Count(cached, RequestStatus.InProgress) + 1, Count(fresh, RequestStatus.InProgress));
        Assert.True(fresh.GeneratedAt > cached.GeneratedAt);
        Assert.Equal(fresh.TotalCount, fresh.ByPriority.Sum(p => p.Count));
    }

    [Fact]
    public async Task Rejected_changes_do_not_invalidate_the_cache()
    {
        var request = (await InsertAsync(NewRequest(UniqueOrganization(), RequestStatus.Completed)))[0];
        var token = (await GetRequestAsync(request.Id)).RowVersion;
        await StartWithFreshCacheEntryAsync();
        var cached = await GetSummaryAsync();

        await PatchStatusAsync(request.Id, RequestStatus.New, token); // 422: terminal status
        await PatchBulkAsync(new { items = new[] { new { id = request.Id, status = "New", expectedVersion = token } } });

        Assert.Equal(cached.GeneratedAt, (await GetSummaryAsync()).GeneratedAt);
    }

    /// <summary>
    /// Commits a real change so the cache is invalidated, then fills it: the test then runs well inside the 30 s TTL
    /// regardless of what earlier tests left in the cache.
    /// </summary>
    private async Task StartWithFreshCacheEntryAsync()
    {
        var throwaway = (await InsertAsync(NewRequest(UniqueOrganization(), RequestStatus.New)))[0];
        await PatchStatusAsync(throwaway.Id, RequestStatus.Waiting, (await GetRequestAsync(throwaway.Id)).RowVersion);
    }

    private async Task<RequestSummaryDto> GetSummaryAsync() =>
        (await Client.GetFromJsonAsync<RequestSummaryDto>("/api/requests/summary", TestJson.Options))!;

    private static int Count(RequestSummaryDto summary, RequestStatus status) =>
        summary.ByStatus.Single(s => s.Status == status).Count;
}
