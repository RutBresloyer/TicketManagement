namespace RequestManagement.Api.Contracts.Common;

/// <summary>Stable machine-readable values of the <c>code</c> extension on every ProblemDetails response.</summary>
public static class ApiErrorCodes
{
    public const string ValidationFailed = "ValidationFailed";
    public const string NotFound = "NotFound";
    public const string ConcurrencyConflict = "ConcurrencyConflict";
    public const string InvalidStatusTransition = "InvalidStatusTransition";
    public const string DemoIdentityDisabled = "DemoIdentityDisabled";
    public const string UnexpectedError = "UnexpectedError";
}
