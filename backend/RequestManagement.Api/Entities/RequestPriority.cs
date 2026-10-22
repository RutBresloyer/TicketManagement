namespace RequestManagement.Api.Entities;

/// <summary>Business priority of a request.</summary>
/// <remarks>
/// Stored as <c>tinyint</c> so that <c>ORDER BY Priority</c> follows business order (Low &lt; Medium &lt; High)
/// rather than alphabetical order, which a string column would give.
/// </remarks>
public enum RequestPriority : byte
{
    Low = 1,
    Medium = 2,
    High = 3,
}
