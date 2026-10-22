using System.Net;
using System.Text;
using System.Text.Json;
using RequestManagement.Api.Entities;
using RequestManagement.Tests.Infrastructure;

namespace RequestManagement.Tests.Integration;

public sealed class ValidationTests(SqlServerFixture fixture) : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("page=0", "page")]
    [InlineData("status=Done", "status")]
    [InlineData("status=2", "status")] // numeric enum values are not part of the contract
    [InlineData("status=New,InProgress", "status")] // default MVC binding would turn this into "Waiting"
    [InlineData("priority=Urgent", "priority")]
    [InlineData("sortBy=id", "sortBy")]
    [InlineData("sortDirection=up", "sortDirection")]
    [InlineData("createdFrom=31/01/2026", "createdFrom")]
    [InlineData("createdFrom=2026-02-01T00:00:00Z&createdTo=2026-01-01T00:00:00Z", "createdFrom")]
    public async Task Invalid_list_parameters_return_400_validation_problem(string query, string field)
    {
        var response = await Client.GetAsync($"/api/requests?{query}");

        await AssertValidationProblemAsync(response, field);
    }

    [Fact]
    public async Task Search_term_longer_than_100_characters_is_rejected()
    {
        var response = await Client.GetAsync($"/api/requests?q={new string('x', 101)}");

        await AssertValidationProblemAsync(response, "q");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    public async Task Invalid_ids_return_400(string id)
    {
        var response = await Client.GetAsync($"/api/requests/{id}");

        await AssertValidationProblemAsync(response, "id");
    }

    [Theory]
    [InlineData("""{"status":"InProgress","expectedVersion":"not-base64!"}""", "expectedVersion")]
    [InlineData("""{"status":"InProgress","expectedVersion":"AAECAw=="}""", "expectedVersion")] // 4 bytes, not 8
    [InlineData("""{"status":"InProgress"}""", "expectedVersion")]
    [InlineData("""{"status":2,"expectedVersion":"AAAAAAAAB9E="}""", "$.status")] // numeric enum in JSON
    [InlineData("""{"status":"Done","expectedVersion":"AAAAAAAAB9E="}""", "$.status")]
    public async Task Malformed_status_bodies_return_400(string body, string field)
    {
        var response = await SendRawPatchAsync("/api/requests/1/status", body, "demo-user-1");

        await AssertValidationProblemAsync(response, field);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ab")] // too short
    [InlineData("someone@example.com")] // '@' not allowed: keeps e-mail addresses out of the audit table
    public async Task Missing_or_invalid_actor_returns_400(string? actor)
    {
        var response = await SendRawPatchAsync("/api/requests/1/status", """{"status":"InProgress","expectedVersion":"AAAAAAAAB9E="}""", actor);

        await AssertValidationProblemAsync(response, "X-Actor-Id");
    }

    [Fact]
    public async Task Forbidden_transition_returns_422_with_problem_code_and_allowed_statuses()
    {
        var request = (await InsertAsync(NewRequest(UniqueOrganization(), RequestStatus.New)))[0];
        var dto = await GetRequestAsync(request.Id);

        var response = await PatchStatusAsync(request.Id, RequestStatus.Completed, dto.RowVersion);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("InvalidStatusTransition", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal("New", problem.RootElement.GetProperty("currentStatus").GetString());
        Assert.Equal(["InProgress", "Waiting"], problem.RootElement.GetProperty("allowedNextStatuses").EnumerateArray().Select(e => e.GetString()));
        Assert.Empty(await HistoryFromDbAsync(request.Id));
    }

    [Fact]
    public async Task Unknown_request_returns_404_problem()
    {
        var response = await PatchStatusAsync(long.MaxValue, RequestStatus.InProgress, "AAAAAAAAB9E=");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("\"code\":\"NotFound\"", await response.Content.ReadAsStringAsync());
    }

    private async Task<HttpResponseMessage> SendRawPatchAsync(string url, string json, string? actor)
    {
        using var message = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (actor is not null)
        {
            message.Headers.Add("X-Actor-Id", actor);
        }

        return await Client.SendAsync(message);
    }

    private static async Task AssertValidationProblemAsync(HttpResponseMessage response, string expectedField)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("ValidationFailed", problem.RootElement.GetProperty("code").GetString());
        var fields = problem.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains(expectedField, fields);
    }
}
