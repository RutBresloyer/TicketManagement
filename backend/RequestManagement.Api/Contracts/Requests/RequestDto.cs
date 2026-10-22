using RequestManagement.Api.Entities;
using RequestManagement.Api.Services;
using RequestManagement.Api.Validation;

namespace RequestManagement.Api.Contracts.Requests;

/// <summary>A managed request, including the concurrency token needed to change it.</summary>
public sealed record RequestDto
{
    /// <summary>Database identifier.</summary>
    /// <example>42</example>
    public required long Id { get; init; }

    /// <example>Investigate payment provider timeout (REF-000042)</example>
    public required string Title { get; init; }

    /// <example>Blue Harbor Logistics</example>
    public required string OrganizationName { get; init; }

    /// <example>New</example>
    public required RequestStatus Status { get; init; }

    /// <example>High</example>
    public required RequestPriority Priority { get; init; }

    /// <summary>Assignee identifier, or <c>null</c> when unassigned.</summary>
    /// <example>agent-07</example>
    public string? AssignedTo { get; init; }

    /// <summary>Creation time (UTC, ISO 8601).</summary>
    /// <example>2026-03-14T09:15:00+00:00</example>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Time of the last successful status change, or the creation time (UTC, ISO 8601).</summary>
    /// <example>2026-03-15T11:02:00+00:00</example>
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Opaque concurrency token: base64 of the SQL Server 8-byte <c>rowversion</c>. Changes on every successful update.
    /// Send it back unchanged as <c>expectedVersion</c> when changing the status; never construct it yourself.
    /// </summary>
    /// <example>AAAAAAAAB9E=</example>
    public required string RowVersion { get; init; }

    /// <summary>Statuses this request may move to next, according to the transition rules. Empty for <c>Completed</c>.</summary>
    public required IReadOnlyList<RequestStatus> AllowedNextStatuses { get; init; }

    public static RequestDto From(Request request) => new()
    {
        Id = request.Id,
        Title = request.Title,
        OrganizationName = request.OrganizationName,
        Status = request.Status,
        Priority = request.Priority,
        AssignedTo = request.AssignedTo,
        CreatedAt = request.CreatedAt,
        UpdatedAt = request.UpdatedAt,
        RowVersion = RowVersionToken.Encode(request.RowVersion),
        AllowedNextStatuses = RequestStatusTransitions.AllowedNext(request.Status),
    };
}
