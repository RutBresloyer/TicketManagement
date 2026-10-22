using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using RequestManagement.Api.Contracts.Requests;
using RequestManagement.Api.Entities;

namespace RequestManagement.Tests.Infrastructure;

[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public abstract class IntegrationTestBase(SqlServerFixture fixture)
{
    protected const string Actor = "test-actor-1";

    protected SqlServerFixture Fixture { get; } = fixture;

    protected HttpClient Client { get; } = fixture.Factory.CreateClient();

    /// <summary>A unique organization per test isolates its rows from every other test in the shared database.</summary>
    protected static string UniqueOrganization() => $"Test Org {Guid.NewGuid():N}";

    protected static Request NewRequest(
        string organization,
        RequestStatus status = RequestStatus.New,
        RequestPriority priority = RequestPriority.Medium,
        DateTimeOffset? createdAt = null,
        string title = "Integration test request",
        string? assignedTo = "agent-01")
    {
        var created = createdAt ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return new Request
        {
            Title = title,
            OrganizationName = organization,
            Status = status,
            Priority = priority,
            AssignedTo = assignedTo,
            CreatedAt = created,
            UpdatedAt = created,
        };
    }

    protected async Task<IReadOnlyList<Request>> InsertAsync(params Request[] requests)
    {
        await using var db = Fixture.CreateDbContext();
        db.Requests.AddRange(requests);
        await db.SaveChangesAsync();
        return requests;
    }

    protected async Task<Request> ReadFromDbAsync(long id)
    {
        await using var db = Fixture.CreateDbContext();
        return await db.Requests.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    protected async Task<List<RequestStatusHistory>> HistoryFromDbAsync(long requestId)
    {
        await using var db = Fixture.CreateDbContext();
        return await db.RequestStatusHistory.AsNoTracking().Where(h => h.RequestId == requestId).OrderBy(h => h.Id).ToListAsync();
    }

    protected Task<HttpResponseMessage> PatchStatusAsync(
        long id, RequestStatus status, string expectedVersion, string actor = Actor, HttpClient? client = null) =>
        SendPatchAsync(client ?? Client, $"/api/requests/{id}/status", new { status, expectedVersion }, actor);

    protected Task<HttpResponseMessage> PatchBulkAsync(object body, string actor = Actor) =>
        SendPatchAsync(Client, "/api/requests/bulk-status", body, actor);

    protected async Task<RequestDto> GetRequestAsync(long id) =>
        (await Client.GetFromJsonAsync<RequestDto>($"/api/requests/{id}", TestJson.Options))!;

    protected static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(TestJson.Options))!;

    private static async Task<HttpResponseMessage> SendPatchAsync(HttpClient client, string url, object body, string actor)
    {
        using var message = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = JsonContent.Create(body, options: TestJson.Options),
        };
        message.Headers.Add("X-Actor-Id", actor);
        return await client.SendAsync(message);
    }
}
