using System.ComponentModel.DataAnnotations;
using RequestManagement.Api.Entities;
using RequestManagement.Api.Validation;

namespace RequestManagement.Api.Contracts.Requests;

/// <summary>Body of <c>PATCH /api/requests/bulk-status</c>.</summary>
public sealed record BulkStatusUpdateRequest : IValidatableObject
{
    public const int MaxItems = 100;

    /// <summary>1–100 status changes. Each request ID may appear only once.</summary>
    [Required]
    [Length(1, MaxItems, ErrorMessage = "items must contain between 1 and 100 entries.")]
    public IReadOnlyList<BulkStatusUpdateItem>? Items { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var duplicates = (Items ?? [])
            .GroupBy(i => i.Id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
        {
            yield return new ValidationResult(
                $"Each request ID may appear only once per batch. Duplicates: {string.Join(", ", duplicates)}.",
                [nameof(Items)]);
        }
    }
}

/// <summary>One requested status change inside a bulk update.</summary>
public sealed record BulkStatusUpdateItem
{
    /// <example>42</example>
    [Range(1, long.MaxValue, ErrorMessage = "id must be a positive integer.")]
    public long Id { get; init; }

    /// <example>InProgress</example>
    [Required]
    public RequestStatus? Status { get; init; }

    /// <summary>The <c>rowVersion</c> of this request exactly as last read (base64, 8 bytes).</summary>
    /// <example>AAAAAAAAB9E=</example>
    [Required]
    [RowVersionToken]
    public string? ExpectedVersion { get; init; }
}

/// <summary>Per-item results of a bulk update, in the same order as the input items.</summary>
/// <param name="Succeeded">Items whose outcome is <c>Updated</c> or <c>NoChange</c>.</param>
/// <param name="Failed">Items whose outcome is <c>NotFound</c>, <c>ConcurrencyConflict</c> or <c>InvalidTransition</c>.</param>
/// <param name="Results">Exactly one result per input item, in input order.</param>
public sealed record BulkStatusUpdateResponse(int Succeeded, int Failed, IReadOnlyList<BulkStatusUpdateItemResult> Results);

/// <summary>Outcome of one item of a bulk update. Each item is committed (or rejected) independently.</summary>
/// <param name="Id">Request ID from the input item.</param>
/// <param name="Outcome">What happened to this item.</param>
/// <param name="RowVersion">New (Updated) or current (NoChange) concurrency token; <c>null</c> for failures.</param>
/// <param name="Status">Status after the operation for successes; <c>null</c> for failures.</param>
/// <param name="Error">Error code and message for failures; <c>null</c> for successes.</param>
public sealed record BulkStatusUpdateItemResult(
    long Id,
    StatusChangeOutcome Outcome,
    string? RowVersion,
    RequestStatus? Status,
    BulkItemError? Error);

/// <param name="Code">Same values as the ProblemDetails <c>code</c> extension, e.g. <c>ConcurrencyConflict</c>.</param>
/// <param name="Message">Human-readable explanation.</param>
public sealed record BulkItemError(string Code, string Message);
