using RequestManagement.Api.Entities;

namespace RequestManagement.Api.Contracts.Requests;

/// <summary>One immutable status-change audit record.</summary>
/// <param name="Id">Audit record identifier (also the tie breaker for equal timestamps).</param>
/// <param name="PreviousStatus">Status before the change.</param>
/// <param name="NewStatus">Status after the change.</param>
/// <param name="ChangedAt">UTC time of the change; identical to the request's <c>updatedAt</c> written by the same change.</param>
/// <param name="ChangedBy">Actor that made the change (demo: the <c>X-Actor-Id</c> header).</param>
public sealed record RequestHistoryEntryDto(
    long Id,
    RequestStatus PreviousStatus,
    RequestStatus NewStatus,
    DateTimeOffset ChangedAt,
    string ChangedBy);
