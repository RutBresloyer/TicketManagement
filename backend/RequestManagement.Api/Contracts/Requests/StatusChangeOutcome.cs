namespace RequestManagement.Api.Contracts.Requests;

/// <summary>Result of attempting one status change.</summary>
public enum StatusChangeOutcome
{
    /// <summary>Status changed; one audit row written.</summary>
    Updated,

    /// <summary>Target equals the current status and the token matched; nothing written.</summary>
    NoChange,

    /// <summary>No request with this ID.</summary>
    NotFound,

    /// <summary>The expected version is stale; nothing written.</summary>
    ConcurrencyConflict,

    /// <summary>The transition is not allowed from the current status; nothing written.</summary>
    InvalidTransition,
}
