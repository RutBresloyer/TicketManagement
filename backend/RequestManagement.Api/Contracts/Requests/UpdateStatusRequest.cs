using System.ComponentModel.DataAnnotations;
using RequestManagement.Api.Entities;
using RequestManagement.Api.Validation;

namespace RequestManagement.Api.Contracts.Requests;

/// <summary>Body of <c>PATCH /api/requests/{id}/status</c>.</summary>
public sealed record UpdateStatusRequest
{
    /// <summary>Target status. Must be an allowed transition from the current status.</summary>
    /// <example>InProgress</example>
    [Required]
    public RequestStatus? Status { get; init; }

    /// <summary>
    /// The <c>rowVersion</c> exactly as you last read it (base64, 8 bytes). If the request changed since then, the update
    /// is rejected with <c>409</c> instead of overwriting the other change.
    /// </summary>
    /// <example>AAAAAAAAB9E=</example>
    [Required]
    [RowVersionToken]
    public string? ExpectedVersion { get; init; }
}
