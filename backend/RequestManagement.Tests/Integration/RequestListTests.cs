using System.Net;
using System.Net.Http.Json;
using RequestManagement.Api.Contracts.Common;
using RequestManagement.Api.Contracts.Requests;
using RequestManagement.Api.Entities;
using RequestManagement.Tests.Infrastructure;

namespace RequestManagement.Tests.Integration;

public sealed class RequestListTests(SqlServerFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly DateTimeOffset T0 = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Filters_count_pages_and_orders_in_the_database_with_id_as_tie_breaker()
    {
        var org = UniqueOrganization();
        // 12 matching rows: 6 share one CreatedAt (tie breaker matters), 6 are spread out. Plus non-matching noise.
        var matching = Enumerable.Range(0, 12)
            .Select(i => NewRequest(org, RequestStatus.New, RequestPriority.High, i < 6 ? T0 : T0.AddHours(i)))
            .ToArray();
        await InsertAsync(matching);
        await InsertAsync(
            NewRequest(org, RequestStatus.Waiting, RequestPriority.High, T0),
            NewRequest(org, RequestStatus.New, RequestPriority.Low, T0));

        var expectedOrder = matching
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Select(r => r.Id)
            .ToList();

        var page1 = await ListAsync($"organizationName={Uri.EscapeDataString(org)}&status=New&priority=High&pageSize=5&page=1");
        var page3 = await ListAsync($"organizationName={Uri.EscapeDataString(org)}&status=New&priority=High&pageSize=5&page=3");

        Assert.Equal(12, page1.TotalCount);
        Assert.Equal(3, page1.TotalPages);
        Assert.Equal(expectedOrder.Take(5), page1.Items.Select(i => i.Id));
        Assert.Equal(expectedOrder.Skip(10), page3.Items.Select(i => i.Id));
        Assert.All(page1.Items, i => Assert.False(string.IsNullOrEmpty(i.RowVersion)));
    }

    [Fact]
    public async Task Ascending_sort_reverses_both_the_sort_key_and_the_tie_breaker()
    {
        var org = UniqueOrganization();
        var rows = await InsertAsync(
            NewRequest(org, createdAt: T0, title: "B"),
            NewRequest(org, createdAt: T0, title: "A"),
            NewRequest(org, createdAt: T0.AddDays(-1), title: "C"));

        var byCreated = await ListAsync($"organizationName={Uri.EscapeDataString(org)}&sortBy=createdAt&sortDirection=asc");
        var byTitle = await ListAsync($"organizationName={Uri.EscapeDataString(org)}&sortBy=title&sortDirection=asc");

        Assert.Equal([rows[2].Id, rows[0].Id, rows[1].Id], byCreated.Items.Select(i => i.Id));
        Assert.Equal(["A", "B", "C"], byTitle.Items.Select(i => i.Title));
    }

    [Fact]
    public async Task Search_is_a_case_insensitive_substring_match_on_title_or_organization()
    {
        var org = UniqueOrganization();
        var marker = Guid.NewGuid().ToString("N")[..10];
        await InsertAsync(
            NewRequest(org, title: $"Refund {marker.ToUpperInvariant()} pending"),
            NewRequest($"{org} {marker}", title: "Unrelated title"),
            NewRequest(org, title: "No match here"));

        var result = await ListAsync($"q={marker.ToLowerInvariant()}");

        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task Date_range_is_inclusive_from_and_exclusive_to()
    {
        var org = UniqueOrganization();
        var rows = await InsertAsync(
            NewRequest(org, createdAt: T0.AddSeconds(-1)),
            NewRequest(org, createdAt: T0),
            NewRequest(org, createdAt: T0.AddDays(1)));

        var result = await ListAsync(
            $"organizationName={Uri.EscapeDataString(org)}&createdFrom=2026-03-01T12:00:00Z&createdTo=2026-03-02T12:00:00Z");

        Assert.Equal([rows[1].Id], result.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Page_size_is_capped_at_100()
    {
        var atCap = await Client.GetAsync("/api/requests?pageSize=100");
        var overCap = await Client.GetAsync("/api/requests?pageSize=101");

        Assert.Equal(HttpStatusCode.OK, atCap.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, overCap.StatusCode);
    }

    private async Task<PagedResult<RequestDto>> ListAsync(string query) =>
        (await Client.GetFromJsonAsync<PagedResult<RequestDto>>($"/api/requests?{query}", TestJson.Options))!;
}
