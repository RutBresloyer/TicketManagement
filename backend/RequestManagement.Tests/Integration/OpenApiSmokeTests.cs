using System.Net;
using System.Text.Json;
using RequestManagement.Tests.Infrastructure;

namespace RequestManagement.Tests.Integration;

/// <summary>Needs no database: the OpenAPI document and Swagger UI are generated from metadata only.</summary>
[Trait("Category", "Smoke")]
public sealed class OpenApiSmokeTests
{
    // Never opened: no endpoint that touches the database is called in these tests.
    private const string UnusedConnectionString = "Server=127.0.0.1,1;Database=Unused_Tests;Encrypt=True";

    [Fact]
    public async Task OpenApi_document_describes_every_operation_and_its_error_responses()
    {
        await using var factory = new ApiFactory(UnusedConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");

        string[] expectedPaths =
        [
            "/api/requests", "/api/requests/{id}", "/api/requests/{id}/status",
            "/api/requests/{id}/history", "/api/requests/bulk-status", "/api/requests/summary",
        ];
        foreach (var path in expectedPaths)
        {
            Assert.True(paths.TryGetProperty(path, out _), $"OpenAPI document is missing {path}");
        }

        var patchStatus = paths.GetProperty("/api/requests/{id}/status").GetProperty("patch");
        var responses = patchStatus.GetProperty("responses");
        foreach (var code in new[] { "200", "400", "404", "409", "422" })
        {
            Assert.True(responses.TryGetProperty(code, out _), $"PATCH status is missing response {code}");
        }

        Assert.Contains(
            patchStatus.GetProperty("parameters").EnumerateArray(),
            p => p.GetProperty("name").GetString() == "X-Actor-Id" && p.GetProperty("in").GetString() == "header");

        var listParameters = paths.GetProperty("/api/requests").GetProperty("get").GetProperty("parameters")
            .EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToList();
        Assert.Equivalent(
            new[] { "status", "priority", "assignedTo", "organizationName", "createdFrom", "createdTo", "q", "sortBy", "sortDirection", "page", "pageSize" },
            listParameters);

        var statusEnum = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("RequestStatus").GetProperty("enum").EnumerateArray().Select(e => e.GetString());
        Assert.Equal(["New", "InProgress", "Waiting", "Completed"], statusEnum);
    }

    [Fact]
    public async Task Swagger_ui_is_served_by_the_api_in_development()
    {
        await using var factory = new ApiFactory(UnusedConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("swagger-ui", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Mutations_are_refused_when_the_demo_identity_header_is_disabled()
    {
        await using var factory = new ApiFactory(UnusedConnectionString, demoIdentityEnabled: false);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/requests/1/status")
        {
            Content = new StringContent("""{"status":"InProgress","expectedVersion":"AAAAAAAAB9E="}""", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Actor-Id", "demo-user-1");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("DemoIdentityDisabled", await response.Content.ReadAsStringAsync());
    }
}
