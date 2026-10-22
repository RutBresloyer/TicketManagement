using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RequestManagement.Api.Contracts.Requests;
using RequestManagement.Api.Data;
using RequestManagement.Api.Entities;
using RequestManagement.Tests.Infrastructure;

namespace RequestManagement.Tests.Integration;

public sealed class ConcurrencyTests(SqlServerFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Two_clients_with_the_same_token_exactly_one_wins_and_the_loser_gets_409()
    {
        var request = (await InsertAsync(NewRequest(UniqueOrganization(), RequestStatus.New)))[0];

        // Both users read the same version.
        var readByAlice = await GetRequestAsync(request.Id);
        var readByBob = await GetRequestAsync(request.Id);
        Assert.Equal(readByAlice.RowVersion, readByBob.RowVersion);

        var alice = await PatchStatusAsync(request.Id, RequestStatus.InProgress, readByAlice.RowVersion, "alice-01");
        var bob = await PatchStatusAsync(request.Id, RequestStatus.Waiting, readByBob.RowVersion, "bob-01");

        Assert.Equal(HttpStatusCode.OK, alice.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, bob.StatusCode);
        Assert.Contains("\"code\":\"ConcurrencyConflict\"", await bob.Content.ReadAsStringAsync());

        // No lost update: Alice's change survives, and only her change is audited.
        var stored = await ReadFromDbAsync(request.Id);
        Assert.Equal(RequestStatus.InProgress, stored.Status);
        var history = Assert.Single(await HistoryFromDbAsync(request.Id));
        Assert.Equal("alice-01", history.ChangedBy);

        // The token returned to Alice is the one actually persisted.
        var aliceDto = await ReadAsync<RequestDto>(alice);
        Assert.Equal(Convert.ToBase64String(stored.RowVersion), aliceDto.RowVersion);
        Assert.NotEqual(readByAlice.RowVersion, aliceDto.RowVersion);
    }

    [Fact]
    public async Task A_write_committed_between_read_and_save_is_detected_by_the_database_not_overwritten()
    {
        var request = (await InsertAsync(NewRequest(UniqueOrganization(), RequestStatus.New)))[0];
        var token = (await GetRequestAsync(request.Id)).RowVersion;

        var interceptor = new CompetingWriteInterceptor(Fixture.ConnectionString);
        await using var racingApi = Fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(interceptor), ServiceLifetime.Singleton)));
        using var client = racingApi.CreateClient();

        // The competing writer moves the request to Waiting after the API's pre-check but before its UPDATE.
        interceptor.Arm(request.Id, RequestStatus.Waiting);
        var response = await PatchStatusAsync(request.Id, RequestStatus.InProgress, token, client: client);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(RequestStatus.Waiting, (await ReadFromDbAsync(request.Id)).Status); // the competing write survives
        Assert.Empty(await HistoryFromDbAsync(request.Id)); // the audit INSERT was rolled back with the failed UPDATE
    }

    [Fact]
    public async Task Parallel_requests_with_the_same_token_produce_exactly_one_change_and_one_audit_row()
    {
        var request = (await InsertAsync(NewRequest(UniqueOrganization(), RequestStatus.New)))[0];
        var token = (await GetRequestAsync(request.Id)).RowVersion;

        var attempts = Enumerable.Range(0, 8)
            .Select(i => PatchStatusAsync(
                request.Id, i % 2 == 0 ? RequestStatus.InProgress : RequestStatus.Waiting, token, $"parallel-{i}"))
            .ToList();
        var responses = await Task.WhenAll(attempts);

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var history = Assert.Single(await HistoryFromDbAsync(request.Id));
        Assert.Equal(history.NewStatus, (await ReadFromDbAsync(request.Id)).Status);
    }

    [Fact]
    public async Task Same_status_is_a_no_op_with_a_current_token_and_a_conflict_with_a_stale_token()
    {
        var request = (await InsertAsync(NewRequest(UniqueOrganization(), RequestStatus.New)))[0];
        var before = await GetRequestAsync(request.Id);

        var noOp = await PatchStatusAsync(request.Id, RequestStatus.New, before.RowVersion);

        Assert.Equal(HttpStatusCode.OK, noOp.StatusCode);
        var afterNoOp = await GetRequestAsync(request.Id);
        Assert.Equal(before.RowVersion, afterNoOp.RowVersion);
        Assert.Equal(before.UpdatedAt, afterNoOp.UpdatedAt);
        Assert.Empty(await HistoryFromDbAsync(request.Id));

        // Someone else changes it; a "no-op" with the old token must not report success.
        Assert.Equal(HttpStatusCode.OK, (await PatchStatusAsync(request.Id, RequestStatus.InProgress, before.RowVersion)).StatusCode);
        var staleNoOp = await PatchStatusAsync(request.Id, RequestStatus.InProgress, before.RowVersion);
        Assert.Equal(HttpStatusCode.Conflict, staleNoOp.StatusCode);
    }
}
