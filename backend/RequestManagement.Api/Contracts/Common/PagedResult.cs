namespace RequestManagement.Api.Contracts.Common;

/// <summary>One page of a server-side paginated result.</summary>
/// <param name="Items">Items on this page, in the requested order.</param>
/// <param name="Page">1-based page number that was returned.</param>
/// <param name="PageSize">Requested page size (1–100).</param>
/// <param name="TotalCount">Number of items matching the filters across all pages.</param>
/// <param name="TotalPages">Number of pages for the filters and page size; 0 when nothing matches.</param>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
