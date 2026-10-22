using System.Net;
using RequestManagement.Api.Contracts.Requests;
using RequestManagement.Api.Entities;
using RequestManagement.Tests.Infrastructure;

namespace RequestManagement.Tests.Integration;

public sealed class BulkStatusTests(SqlServerFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Mixed_batch_commits_valid_items_and_reports_one_outcome_per_item_in_input_order()
    {
        var org = UniqueOrganization();
        var rows = await InsertAsync(
            NewRequest(org, RequestStatus.New),        // 0: valid change
            NewRequest(org, RequestStatus.InProgress), // 1: same status, current token -> NoChange
            NewRequest(org, RequestStatus.New),        // 2: stale token -> conflict
            NewRequest(org, RequestStatus.Completed),  // 3: terminal -> invalid transition
            NewRequest(org, RequestStatus.Waiting));   // 4: valid change, after the failures
        var tokens = new List<string>();
        foreach (var row in rows)
        {
            tokens.Add((await GetRequestAsync(row.Id)).RowVersion);
        }

        // Make item 2's token stale before the batch runs.
        Assert.Equal(HttpStatusCode.OK, (await PatchStatusAsync(rows[2].Id, RequestStatus.Waiting, tokens[2])).StatusCode);

        var response = await PatchBulkAsync(new
        {
            items = new object[]
            {
                new { id = rows[0].Id, status = "InProgress", expectedVersion = tokens[0] },
                new { id = rows[1].Id, status = "InProgress", expectedVersion = tokens[1] },
                new { id = rows[2].Id, status = "InProgress", expectedVersion = tokens[2] },
                new { id = rows[3].Id, status = "New", expectedVersion = tokens[3] },
                new { id = long.MaxValue, status = "InProgress", expectedVersion = tokens[0] },
                new { id = rows[4].Id, status = "Completed", expectedVersion = tokens[4] },
            },
        }, actor: "bulk-actor");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadAsync<BulkStatusUpdateResponse>(response);
        Assert.Equal(3, result.Succeeded);
        Assert.Equal(3, result.Failed);
        Assert.Equal(
            [StatusChangeOutcome.Updated, StatusChangeOutcome.NoChange, StatusChangeOutcome.ConcurrencyConflict,
             StatusChangeOutcome.InvalidTransition, StatusChangeOutcome.NotFound, StatusChangeOutcome.Updated],
            result.Results.Select(r => r.Outcome));
        Assert.Equal([rows[0].Id, rows[1].Id, rows[2].Id, rows[3].Id, long.MaxValue, rows[4].Id], result.Results.Select(r => r.Id));
        Assert.Equal("ConcurrencyConflict", result.Results[2].Error!.Code);
        Assert.Equal("InvalidStatusTransition", result.Results[3].Error!.Code);
        Assert.Null(result.Results[2].RowVersion);

        // Successes are committed and audited; the new token returned is the persisted one.
        var first = await ReadFromDbAsync(rows[0].Id);
        Assert.Equal(RequestStatus.InProgress, first.Status);
        Assert.Equal(Convert.ToBase64String(first.RowVersion), result.Results[0].RowVersion);
        Assert.Equal("bulk-actor", Assert.Single(await HistoryFromDbAsync(rows[0].Id)).ChangedBy);
        Assert.Equal(RequestStatus.Completed, (await ReadFromDbAsync(rows[4].Id)).Status);
        Assert.Single(await HistoryFromDbAsync(rows[4].Id));

        // Failures and the no-op wrote nothing.
        Assert.Empty(await HistoryFromDbAsync(rows[1].Id));
        Assert.Equal(RequestStatus.Waiting, (await ReadFromDbAsync(rows[2].Id)).Status);
        Assert.Single(await HistoryFromDbAsync(rows[2].Id)); // only the earlier single-item change
        Assert.Empty(await HistoryFromDbAsync(rows[3].Id));
    }

    [Fact]
    public async Task Contract_errors_reject_the_whole_batch_and_process_nothing()
    {
        var request = (await InsertAsync(NewRequest(UniqueOrganization(), RequestStatus.New)))[0];
        var token = (await GetRequestAsync(request.Id)).RowVersion;

        var duplicate = await PatchBulkAsync(new
        {
            items = new[]
            {
                new { id = request.Id, status = "InProgress", expectedVersion = token },
                new { id = request.Id, status = "Waiting", expectedVersion = token },
            },
        });
        var tooMany = await PatchBulkAsync(new
        {
            items = Enumerable.Range(1, 101).Select(i => new { id = (long)i, status = "InProgress", expectedVersion = token }),
        });
        var empty = await PatchBulkAsync(new { items = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(RequestStatus.New, (await ReadFromDbAsync(request.Id)).Status);
        Assert.Empty(await HistoryFromDbAsync(request.Id));
    }
}
