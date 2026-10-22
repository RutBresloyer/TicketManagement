using Microsoft.EntityFrameworkCore;
using RequestManagement.Api.Contracts.Common;
using RequestManagement.Api.Contracts.Requests;
using RequestManagement.Api.Data;
using RequestManagement.Api.Entities;
using RequestManagement.Api.Validation;

namespace RequestManagement.Api.Services;

/// <summary>Result of one status change attempt.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Request">The request after the change (Updated) or as read (NoChange); otherwise <c>null</c>.</param>
/// <param name="CurrentStatus">For InvalidTransition: the status the request is actually in.</param>
public sealed record StatusChangeResult(
    StatusChangeOutcome Outcome,
    RequestDto? Request = null,
    RequestStatus? CurrentStatus = null);

/// <summary>Write side: single and bulk status changes with optimistic concurrency and an atomic audit record.</summary>
public sealed class RequestStatusService(
    IDbContextFactory<AppDbContext> dbContextFactory,
    RequestSummaryService summaryService,
    TimeProvider timeProvider,
    ILogger<RequestStatusService> logger)
{
    public const string ConflictMessage =
        "The request was modified by someone else after you read it. Refresh it and decide whether to retry.";

    public const string NotFoundMessage = "No request with this ID exists.";

    /// <summary>
    /// Changes the status of one request if <paramref name="expectedVersion"/> is still current.
    /// The unit of work is one short-lived DbContext and one SaveChanges call.
    /// </summary>
    public async Task<StatusChangeResult> ChangeStatusAsync(
        long requestId,
        RequestStatus newStatus,
        byte[] expectedVersion,
        string actorId,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var request = await db.Requests.SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request is null)
        {
            return new StatusChangeResult(StatusChangeOutcome.NotFound);
        }

        // Early, friendly rejection of a token that is already stale. It is NOT the concurrency guarantee: another
        // writer can commit between this read and SaveChanges. The database check below is what prevents lost updates.
        // A stale token is a conflict even when the target equals the current status (no silent "success").
        if (!request.RowVersion.AsSpan().SequenceEqual(expectedVersion))
        {
            logger.LogInformation(
                "Status change of request {RequestId} by {ActorId} rejected: stale version", requestId, actorId);
            return new StatusChangeResult(StatusChangeOutcome.ConcurrencyConflict);
        }

        if (request.Status == newStatus)
        {
            // Valid no-op: no write, so UpdatedAt, RowVersion and history stay untouched.
            return new StatusChangeResult(StatusChangeOutcome.NoChange, RequestDto.From(request));
        }

        if (!RequestStatusTransitions.IsAllowed(request.Status, newStatus))
        {
            return new StatusChangeResult(StatusChangeOutcome.InvalidTransition, CurrentStatus: request.Status);
        }

        // One sampled timestamp for both the request and its audit row.
        var changedAt = timeProvider.GetUtcNow();
        var previousStatus = request.Status;

        request.Status = newStatus;
        request.UpdatedAt = changedAt;

        // EF builds "UPDATE ... WHERE [Id] = @id AND [RowVersion] = @original" from the property's ORIGINAL value.
        // Pin it to the client's token explicitly, so the database (not just the check above) enforces the version the
        // client actually read. Using the freshly loaded value here is the classic lost-update bug.
        db.Entry(request).Property(r => r.RowVersion).OriginalValue = expectedVersion;

        db.RequestStatusHistory.Add(new RequestStatusHistory
        {
            RequestId = request.Id,
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            ChangedAt = changedAt,
            ChangedBy = actorId,
        });

        try
        {
            // UPDATE + INSERT go through a single SaveChanges, which EF wraps in one database transaction:
            // either both the status change and its audit row commit, or neither does.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Zero rows matched the version: someone else committed first. The transaction was rolled back, so no audit
            // row exists. The winner's change is left untouched; the caller must refresh and decide.
            logger.LogInformation(
                "Status change of request {RequestId} by {ActorId} rejected: concurrent update detected at save",
                requestId,
                actorId);
            return new StatusChangeResult(StatusChangeOutcome.ConcurrencyConflict);
        }

        // Strictly after the commit; see RequestSummaryService for why a versioned key is used.
        summaryService.Invalidate();

        logger.LogInformation(
            "Request {RequestId} status changed {PreviousStatus} -> {NewStatus} by {ActorId}",
            request.Id,
            previousStatus,
            newStatus,
            actorId);

        // request.RowVersion now holds the value SQL Server generated (read back via OUTPUT by EF).
        return new StatusChangeResult(StatusChangeOutcome.Updated, RequestDto.From(request));
    }

    /// <summary>
    /// Partial-success bulk update: each item is an independent unit of work with its own DbContext and transaction,
    /// so a missing row, conflict or invalid transition affects only that item and never rolls back the others.
    /// </summary>
    /// <remarks>
    /// Items are processed sequentially. A DbContext is not thread-safe, and 100 short single-row updates do not
    /// justify parallel connections. Results are returned in input order.
    /// An unexpected infrastructure error aborts the remaining items (500); items already committed stay committed.
    /// </remarks>
    public async Task<BulkStatusUpdateResponse> BulkChangeStatusAsync(
        IReadOnlyList<BulkStatusUpdateItem> items,
        string actorId,
        CancellationToken cancellationToken)
    {
        var results = new List<BulkStatusUpdateItemResult>(items.Count);

        foreach (var item in items)
        {
            var result = await ChangeStatusAsync(
                item.Id,
                item.Status!.Value,
                RowVersionToken.DecodeValidated(item.ExpectedVersion!),
                actorId,
                cancellationToken);

            results.Add(ToBulkResult(item, result));
        }

        var succeeded = results.Count(r => r.Outcome is StatusChangeOutcome.Updated or StatusChangeOutcome.NoChange);
        var response = new BulkStatusUpdateResponse(succeeded, results.Count - succeeded, results);

        logger.LogInformation(
            "Bulk status change by {ActorId}: {Total} items, {Succeeded} succeeded, {Failed} failed",
            actorId,
            results.Count,
            response.Succeeded,
            response.Failed);

        return response;
    }

    private static BulkStatusUpdateItemResult ToBulkResult(BulkStatusUpdateItem item, StatusChangeResult result)
    {
        var id = item.Id;
        return result.Outcome switch
        {
            StatusChangeOutcome.Updated or StatusChangeOutcome.NoChange =>
                new(id, result.Outcome, result.Request!.RowVersion, result.Request.Status, null),
            StatusChangeOutcome.NotFound =>
                new(id, result.Outcome, null, null, new BulkItemError(ApiErrorCodes.NotFound, NotFoundMessage)),
            StatusChangeOutcome.ConcurrencyConflict =>
                new(id, result.Outcome, null, null, new BulkItemError(ApiErrorCodes.ConcurrencyConflict, ConflictMessage)),
            StatusChangeOutcome.InvalidTransition =>
                new(id, result.Outcome, null, null, new BulkItemError(
                    ApiErrorCodes.InvalidStatusTransition,
                    InvalidTransitionMessage(result.CurrentStatus!.Value, item.Status!.Value))),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, null),
        };
    }

    public static string InvalidTransitionMessage(RequestStatus current, RequestStatus target)
    {
        var allowed = RequestStatusTransitions.AllowedNext(current);
        return allowed.Count == 0
            ? $"Cannot change status from '{current}' to '{target}': '{current}' is a terminal status."
            : $"Cannot change status from '{current}' to '{target}'. Allowed from '{current}': {string.Join(", ", allowed)}.";
    }
}
