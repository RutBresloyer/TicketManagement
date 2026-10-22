using RequestManagement.Api.Entities;
using RequestManagement.Api.Services;

namespace RequestManagement.Tests.Unit;

public sealed class RequestStatusTransitionsTests
{
    [Theory]
    [InlineData(RequestStatus.New, RequestStatus.InProgress)]
    [InlineData(RequestStatus.New, RequestStatus.Waiting)]
    [InlineData(RequestStatus.InProgress, RequestStatus.Waiting)]
    [InlineData(RequestStatus.InProgress, RequestStatus.Completed)]
    [InlineData(RequestStatus.Waiting, RequestStatus.InProgress)]
    [InlineData(RequestStatus.Waiting, RequestStatus.Completed)]
    public void Documented_transitions_are_allowed(RequestStatus from, RequestStatus to) =>
        Assert.True(RequestStatusTransitions.IsAllowed(from, to));

    [Theory]
    [InlineData(RequestStatus.New, RequestStatus.Completed)]
    [InlineData(RequestStatus.InProgress, RequestStatus.New)]
    [InlineData(RequestStatus.Waiting, RequestStatus.New)]
    [InlineData(RequestStatus.Completed, RequestStatus.New)]
    [InlineData(RequestStatus.Completed, RequestStatus.InProgress)]
    [InlineData(RequestStatus.Completed, RequestStatus.Waiting)]
    public void Undocumented_transitions_are_forbidden(RequestStatus from, RequestStatus to) =>
        Assert.False(RequestStatusTransitions.IsAllowed(from, to));

    [Fact]
    public void Completed_is_terminal_and_no_status_transitions_to_itself()
    {
        Assert.Empty(RequestStatusTransitions.AllowedNext(RequestStatus.Completed));
        foreach (var status in Enum.GetValues<RequestStatus>())
        {
            Assert.DoesNotContain(status, RequestStatusTransitions.AllowedNext(status));
        }
    }
}
