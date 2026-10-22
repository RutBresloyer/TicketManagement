namespace RequestManagement.Api.Entities;

/// <summary>Immutable audit record of one status change. The application never updates or deletes these rows.</summary>
public sealed class RequestStatusHistory
{
    public long Id { get; set; }

    public long RequestId { get; set; }

    public RequestStatus PreviousStatus { get; set; }

    public RequestStatus NewStatus { get; set; }

    public DateTimeOffset ChangedAt { get; set; }

    public required string ChangedBy { get; set; }
}
