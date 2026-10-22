using RequestManagement.Api.Entities;

namespace RequestManagement.Api.Contracts.Requests;

/// <summary>Aggregated request counts, computed by SQL <c>GROUP BY</c>. Cached for up to 30 seconds.</summary>
/// <param name="TotalCount">Total number of requests.</param>
/// <param name="ByStatus">One entry per status (including zero counts), in lifecycle order.</param>
/// <param name="ByPriority">One entry per priority (including zero counts), Low to High.</param>
/// <param name="GeneratedAt">UTC time the aggregation ran; shows how old a cached value is.</param>
public sealed record RequestSummaryDto(
    int TotalCount,
    IReadOnlyList<StatusCount> ByStatus,
    IReadOnlyList<PriorityCount> ByPriority,
    DateTimeOffset GeneratedAt);

public sealed record StatusCount(RequestStatus Status, int Count);

public sealed record PriorityCount(RequestPriority Priority, int Count);
