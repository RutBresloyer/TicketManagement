using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using RequestManagement.Api.Configuration;
using RequestManagement.Api.Contracts.Common;
using RequestManagement.Api.Contracts.Requests;
using RequestManagement.Api.ErrorHandling;
using RequestManagement.Api.Services;
using RequestManagement.Api.Validation;

namespace RequestManagement.Api.Controllers;

/// <summary>Read and change requests.</summary>
[ApiController]
[Route("api/requests")]
[Tags("Requests")]
public sealed class RequestsController(RequestQueryService queries, RequestStatusService statusService) : ControllerBase
{
    private const string PositiveIdMessage = "id must be a positive integer.";
    private const string ActorRequiredMessage =
        "The X-Actor-Id header is required (development-only demo identity, e.g. 'demo-user-1').";

    /// <summary>List requests with server-side filtering, search, sorting and pagination.</summary>
    /// <remarks>
    /// All filters are optional and combined with AND. Everything — filter, search, sort, `COUNT` and page — runs in
    /// SQL Server; only the requested page is returned.
    ///
    /// * `status`: `New`, `InProgress`, `Waiting`, `Completed`. `priority`: `Low`, `Medium`, `High`. Names only, no numbers.
    /// * `assignedTo`, `organizationName`: exact match, case-insensitive.
    /// * `createdFrom` is **inclusive**, `createdTo` is **exclusive** (`createdFrom &lt;= createdAt &lt; createdTo`).
    ///   ISO 8601; a timestamp without an offset is interpreted as UTC.
    /// * `q`: case-insensitive substring match in title **or** organization name, max 100 characters.
    /// * `sortBy`: `createdAt` (default), `updatedAt`, `title`, `organizationName`, `priority` (Low &lt; Medium &lt; High).
    ///   `sortDirection`: `asc` or `desc` (default). `id` is always the final tie breaker, so pages are deterministic.
    /// * `page` is 1-based (default 1); `pageSize` is 1–100 (default 25). A page past the end returns an empty `items`.
    ///
    /// Example: `GET /api/requests?status=New&amp;priority=High&amp;q=payment&amp;page=1&amp;pageSize=25&amp;sortBy=createdAt&amp;sortDirection=desc`
    /// </remarks>
    /// <response code="200">The requested page and the total number of matches.</response>
    /// <response code="400">Invalid parameter (unknown enum value or sort field, page size out of range, malformed or reversed date range, `q` too long).</response>
    [HttpGet]
    [ProducesResponseType<PagedResult<RequestDto>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemResults.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, ProblemResults.ContentType)]
    public async Task<ActionResult<PagedResult<RequestDto>>> List(
        [FromQuery] RequestListQuery query, CancellationToken cancellationToken) =>
        Ok(await queries.ListAsync(query, cancellationToken));

    /// <summary>Get one request, including its current `rowVersion`.</summary>
    /// <remarks>Use this to refresh a request (and its token) after a `409 Conflict`.</remarks>
    /// <param name="id">Request ID (positive integer).</param>
    /// <response code="200">The request.</response>
    /// <response code="400">The ID is not a positive integer.</response>
    /// <response code="404">No request with this ID.</response>
    [HttpGet("{id}")]
    [ProducesResponseType<RequestDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemResults.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemResults.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, ProblemResults.ContentType)]
    public async Task<ActionResult<RequestDto>> Get([FromRoute, Range(1, long.MaxValue, ErrorMessage = PositiveIdMessage)] long id, CancellationToken cancellationToken)
    {
        var request = await queries.GetAsync(id, cancellationToken);
        return request is null ? RequestNotFound(id) : Ok(request);
    }

    /// <summary>Get the status-change history of a request, newest first.</summary>
    /// <remarks>
    /// Ordered by `changedAt desc, id desc`. Audit records are immutable: the API has no endpoint that writes or deletes them,
    /// and they are only created together with a successful status change.
    /// </remarks>
    /// <param name="id">Request ID (positive integer).</param>
    /// <response code="200">History entries; `[]` when the request exists but has never changed status.</response>
    /// <response code="400">The ID is not a positive integer.</response>
    /// <response code="404">No request with this ID.</response>
    [HttpGet("{id}/history")]
    [ProducesResponseType<IReadOnlyList<RequestHistoryEntryDto>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemResults.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemResults.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, ProblemResults.ContentType)]
    public async Task<ActionResult<IReadOnlyList<RequestHistoryEntryDto>>> GetHistory(
        [FromRoute, Range(1, long.MaxValue, ErrorMessage = PositiveIdMessage)] long id, CancellationToken cancellationToken)
    {
        var history = await queries.GetHistoryAsync(id, cancellationToken);
        return history is null ? RequestNotFound(id) : Ok(history);
    }

    /// <summary>Change the status of one request (optimistic concurrency).</summary>
    /// <remarks>
    /// Send the `rowVersion` you last read as `expectedVersion`. The database only applies the change if the row still has
    /// that version, so a concurrent change is never overwritten: you get `409` and must refresh (GET) and decide.
    ///
    /// Allowed transitions (an implementation assumption, see docs/DECISIONS.md):
    /// `New → InProgress | Waiting`, `InProgress → Waiting | Completed`, `Waiting → InProgress | Completed`; `Completed` is terminal.
    ///
    /// * A successful change updates `status` and `updatedAt`, gets a new `rowVersion`, and writes exactly one history
    ///   entry with the same timestamp — in one transaction.
    /// * Setting the current status again with a **current** token is a no-op: `200`, nothing changes, no history.
    ///   With a **stale** token it is still `409`.
    /// * `X-Actor-Id` is a **development-only demo header, not authentication** (e.g. `demo-user-1`). It is stored as `changedBy`.
    ///
    /// Sample request body (take `expectedVersion` from a real GET response):
    ///
    ///     { "status": "InProgress", "expectedVersion": "AAAAAAAAB9E=" }
    ///
    /// Problem responses carry a `code` extension: `ValidationFailed`, `NotFound`, `ConcurrencyConflict`,
    /// `InvalidStatusTransition` (with `currentStatus` and `allowedNextStatuses`), `DemoIdentityDisabled`.
    /// </remarks>
    /// <param name="id">Request ID (positive integer).</param>
    /// <param name="body">Target status and the version you last read.</param>
    /// <response code="200">Updated (new `rowVersion`), or a token-valid no-op (unchanged request).</response>
    /// <response code="400">Malformed body, unknown status, invalid/missing token, missing or invalid `X-Actor-Id`, invalid ID.</response>
    /// <response code="403">The demo identity header is disabled in this environment (`code = DemoIdentityDisabled`).</response>
    /// <response code="404">No request with this ID.</response>
    /// <response code="409">`expectedVersion` is stale: the request changed since you read it. Nothing was written.</response>
    /// <response code="422">The status is known but the transition is not allowed from the current status. Nothing was written.</response>
    /// <response code="500">Unexpected server error (logged; response contains only a traceId).</response>
    [HttpPatch("{id}/status")]
    [RequireDemoIdentity]
    [Consumes("application/json")]
    [ProducesResponseType<RequestDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemResults.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemResults.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemResults.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemResults.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ProblemResults.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, ProblemResults.ContentType)]
    public async Task<ActionResult<RequestDto>> UpdateStatus(
        [FromRoute, Range(1, long.MaxValue, ErrorMessage = PositiveIdMessage)] long id,
        [FromBody, Required] UpdateStatusRequest body,
        [FromHeader(Name = DemoIdentityOptions.HeaderName), Required(ErrorMessage = ActorRequiredMessage), ActorId] string actorId,
        CancellationToken cancellationToken)
    {
        var result = await statusService.ChangeStatusAsync(
            id,
            body.Status!.Value,
            RowVersionToken.DecodeValidated(body.ExpectedVersion!),
            actorId,
            cancellationToken);

        return result.Outcome switch
        {
            StatusChangeOutcome.Updated or StatusChangeOutcome.NoChange => Ok(result.Request),
            StatusChangeOutcome.NotFound => RequestNotFound(id),
            StatusChangeOutcome.ConcurrencyConflict => ProblemResults.Create(
                HttpContext,
                StatusCodes.Status409Conflict,
                ApiErrorCodes.ConcurrencyConflict,
                "The request was changed by someone else.",
                RequestStatusService.ConflictMessage),
            StatusChangeOutcome.InvalidTransition => ProblemResults.Create(
                HttpContext,
                StatusCodes.Status422UnprocessableEntity,
                ApiErrorCodes.InvalidStatusTransition,
                "This status transition is not allowed.",
                RequestStatusService.InvalidTransitionMessage(result.CurrentStatus!.Value, body.Status.Value),
                new Dictionary<string, object?>
                {
                    ["currentStatus"] = result.CurrentStatus.Value.ToString(),
                    ["requestedStatus"] = body.Status.Value.ToString(),
                    ["allowedNextStatuses"] = RequestStatusTransitions.AllowedNext(result.CurrentStatus.Value)
                        .Select(s => s.ToString())
                        .ToArray(),
                }),
            _ => throw new InvalidOperationException($"Unhandled outcome {result.Outcome}."),
        };
    }

    /// <summary>Change the status of up to 100 requests (partial success).</summary>
    /// <remarks>
    /// **Policy: partial success.** Every item is processed independently, in input order, with its own transaction and the
    /// same rules as the single-item endpoint (transition matrix, `expectedVersion`, audit entry, actor). A missing request,
    /// stale version or forbidden transition fails **only that item**; other items still commit.
    ///
    /// **Why `200` even when items failed:** the batch call itself was valid and fully processed; the per-item business outcome
    /// is in `results` (exactly one entry per input item, same order). Inspect `failed` / each `outcome` rather than the HTTP status.
    ///
    /// Item outcomes: `Updated` (new `rowVersion`), `NoChange` (token valid, status already set), `NotFound`,
    /// `ConcurrencyConflict`, `InvalidTransition`. `succeeded` counts `Updated` + `NoChange`.
    ///
    /// **Top-level `400`** (nothing is processed) is reserved for contract errors: `items` missing/empty/over 100, a duplicated
    /// ID, or any item with an invalid ID, unknown status or malformed `expectedVersion`; or a missing/invalid `X-Actor-Id`.
    ///
    /// Sample request body:
    ///
    ///     {
    ///       "items": [
    ///         { "id": 1, "status": "InProgress", "expectedVersion": "AAAAAAAAB9E=" },
    ///         { "id": 2, "status": "Waiting", "expectedVersion": "AAAAAAAAB9I=" }
    ///       ]
    ///     }
    /// </remarks>
    /// <param name="body">1–100 items with unique IDs.</param>
    /// <response code="200">Batch processed; see per-item `results`. Some or all items may have failed.</response>
    /// <response code="400">Contract error; no item was processed.</response>
    /// <response code="403">The demo identity header is disabled in this environment (`code = DemoIdentityDisabled`).</response>
    /// <response code="500">Unexpected server error; items processed before it remain committed.</response>
    [HttpPatch("bulk-status")]
    [RequireDemoIdentity]
    [RequestSizeLimit(64 * 1024)]
    [Consumes("application/json")]
    [ProducesResponseType<BulkStatusUpdateResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemResults.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemResults.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, ProblemResults.ContentType)]
    public async Task<ActionResult<BulkStatusUpdateResponse>> BulkUpdateStatus(
        [FromBody, Required] BulkStatusUpdateRequest body,
        [FromHeader(Name = DemoIdentityOptions.HeaderName), Required(ErrorMessage = ActorRequiredMessage), ActorId] string actorId,
        CancellationToken cancellationToken) =>
        Ok(await statusService.BulkChangeStatusAsync(body.Items!, actorId, cancellationToken));

    private ObjectResult RequestNotFound(long id) => ProblemResults.Create(
        HttpContext,
        StatusCodes.Status404NotFound,
        ApiErrorCodes.NotFound,
        "Request not found.",
        $"No request with ID {id} exists.");
}
