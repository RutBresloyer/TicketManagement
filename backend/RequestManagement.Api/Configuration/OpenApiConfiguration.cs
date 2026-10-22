using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace RequestManagement.Api.Configuration;

/// <summary>Document-level text and the demo actor header description for the built-in OpenAPI generator.</summary>
public static class OpenApiConfiguration
{
    public static void Configure(OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info = new OpenApiInfo
            {
                Title = "Request Management API",
                Version = "v1",
                Description =
                    "Take-home request-management API: server-side filtering/search/sort/paging, optimistic concurrency with SQL Server " +
                    "rowversion, immutable status history, partial-success bulk updates and a cached summary.\n\n" +
                    "**Concurrency:** every request carries `rowVersion`; send it back unchanged as `expectedVersion`. A stale value yields `409`.\n\n" +
                    "**Identity:** mutations require the development-only `X-Actor-Id` header (e.g. `demo-user-1`). " +
                    "It is NOT authentication and is disabled outside Development.\n\n" +
                    "**Errors:** RFC 7807 problem details with a `code` extension (`ValidationFailed`, `NotFound`, `ConcurrencyConflict`, " +
                    "`InvalidStatusTransition`, `DemoIdentityDisabled`, `UnexpectedError`) and a `traceId`.",
            };
            return Task.CompletedTask;
        });

        // XML <param> docs cannot describe a [FromHeader(Name = "X-Actor-Id")] parameter reliably, so describe it here.
        options.AddOperationTransformer((operation, _, _) =>
        {
            foreach (var parameter in operation.Parameters?.OfType<OpenApiParameter>() ?? [])
            {
                if (parameter.In == ParameterLocation.Header && parameter.Name == DemoIdentityOptions.HeaderName)
                {
                    parameter.Description =
                        "**Development-only demo actor, NOT authentication.** Recorded as `changedBy` in the status history. " +
                        "3–64 characters: letters, digits, '.', '_' or '-', starting with a letter or digit.";
                    parameter.Example = JsonValue.Create("demo-user-1");
                }
            }

            return Task.CompletedTask;
        });
    }
}
