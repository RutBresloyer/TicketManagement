using RequestManagement.Api.Entities;

namespace RequestManagement.Api.Services;

/// <summary>
/// Allowed status transitions. This matrix is an implementation ASSUMPTION documented in docs/DECISIONS.md;
/// the original exam does not define one.
/// </summary>
public static class RequestStatusTransitions
{
    private static readonly IReadOnlyDictionary<RequestStatus, RequestStatus[]> Allowed =
        new Dictionary<RequestStatus, RequestStatus[]>
        {
            [RequestStatus.New] = [RequestStatus.InProgress, RequestStatus.Waiting],
            [RequestStatus.InProgress] = [RequestStatus.Waiting, RequestStatus.Completed],
            [RequestStatus.Waiting] = [RequestStatus.InProgress, RequestStatus.Completed],
            [RequestStatus.Completed] = [], // terminal
        };

    public static IReadOnlyList<RequestStatus> AllowedNext(RequestStatus current) => Allowed[current];

    public static bool IsAllowed(RequestStatus from, RequestStatus to) => Allowed[from].Contains(to);
}
