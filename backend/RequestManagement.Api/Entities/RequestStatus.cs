namespace RequestManagement.Api.Entities;

/// <summary>Lifecycle status of a request.</summary>
/// <remarks>
/// Stored as <c>tinyint</c> with explicit values so the database representation never shifts if members are reordered.
/// Exposed over the API only as strings (e.g. <c>"InProgress"</c>).
/// </remarks>
public enum RequestStatus : byte
{
    New = 1,
    InProgress = 2,
    Waiting = 3,
    Completed = 4,
}
