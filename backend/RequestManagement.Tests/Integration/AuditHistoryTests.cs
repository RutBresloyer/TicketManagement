using System.Net;
using System.Net.Http.Json;
using RequestManagement.Api.Contracts.Requests;
using RequestManagement.Api.Entities;
using RequestManagement.Tests.Infrastructure;

namespace RequestManagement.Tests.Integration;

public sealed class AuditHistoryTests(SqlServerFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Successful_change_writes_exactly_one_audit_row_with_the_same_timestamp_as_UpdatedAt()
    {
        var request = (await InsertAsync(NewRequest(UniqueOrganization(), RequestStatus.Waiting)))[0];
        var before = await GetRequestAsync(request.Id);

        var response = await PatchStatusAsync(request.Id, RequestStatus.Completed, before.RowVersion, "auditor-7");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await ReadFromDbAsync(request.Id);
        var audit = Assert.Single(await HistoryFromDbAsync(request.Id));
        Assert.Equal(RequestStatus.Waiting, audit.PreviousStatus);
        Assert.Equal(RequestStatus.Completed, audit.NewStatus);
        Assert.Equal("auditor-7", audit.ChangedBy);
        Assert.Equal(stored.UpdatedAt, audit.ChangedAt);
        Assert.True(stored.UpdatedAt > before.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, audit.ChangedAt.Offset);
    }

    [Fact]
    public async Task Rejected_changes_leave_request_and_history_untouched()
    {
        var request = (await InsertAsync(NewRequest(UniqueOrganization(), RequestStatus.New)))[0];
        var original = await GetRequestAsync(request.Id);
        Assert.Equal(HttpStatusCode.OK, (await PatchStatusAsync(request.Id, RequestStatus.InProgress, original.RowVersion)).StatusCode);
        var afterFirst = await ReadFromDbAsync(request.Id);

        var stale = await PatchStatusAsync(request.Id, RequestStatus.Waiting, original.RowVersion);
        var forbidden = await PatchStatusAsync(request.Id, RequestStatus.New, Convert.ToBase64String(afterFirst.RowVersion));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, forbidden.StatusCode);
        var afterRejections = await ReadFromDbAsync(request.Id);
        Assert.Equal(afterFirst.RowVersion, afterRejections.RowVersion);
        Assert.Equal(afterFirst.UpdatedAt, afterRejections.UpdatedAt);
        Assert.Single(await HistoryFromDbAsync(request.Id));
    }

    [Fact]
    public async Task History_endpoint_returns_newest_first_empty_for_unchanged_and_404_for_missing()
    {
        var org = UniqueOrganization();
        var rows = await InsertAsync(NewRequest(org, RequestStatus.New), NewRequest(org, RequestStatus.New));
        var changed = rows[0];

        var v1 = (await GetRequestAsync(changed.Id)).RowVersion;
        var v2 = (await ReadAsync<RequestDto>(await PatchStatusAsync(changed.Id, RequestStatus.InProgress, v1, "first-actor"))).RowVersion;
        await PatchStatusAsync(changed.Id, RequestStatus.Waiting, v2, "second-actor");

        var history = (await Client.GetFromJsonAsync<List<RequestHistoryEntryDto>>($"/api/requests/{changed.Id}/history", TestJson.Options))!;
        var empty = await Client.GetFromJsonAsync<List<RequestHistoryEntryDto>>($"/api/requests/{rows[1].Id}/history", TestJson.Options);
        var missing = await Client.GetAsync($"/api/requests/{long.MaxValue}/history");

        Assert.Equal(["second-actor", "first-actor"], history.Select(h => h.ChangedBy));
        Assert.Equal((RequestStatus.InProgress, RequestStatus.Waiting), (history[0].PreviousStatus, history[0].NewStatus));
        Assert.Empty(empty!);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
