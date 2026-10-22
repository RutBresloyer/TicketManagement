using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using RequestManagement.Api.Contracts.Common;
using RequestManagement.Api.ErrorHandling;

namespace RequestManagement.Api.Configuration;

/// <summary>
/// Refuses a mutation with 403 unless the demo identity header is explicitly enabled for this environment,
/// so the header can never silently act as "authentication" in a non-demo deployment.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireDemoIdentityAttribute : Attribute, IAsyncActionFilter, IOrderedFilter
{
    // Run before model validation results are acted on, so a disabled environment never reveals validation details.
    public int Order => int.MinValue;

    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var options = context.HttpContext.RequestServices.GetRequiredService<IOptions<DemoIdentityOptions>>().Value;
        if (options.Enabled)
        {
            return next();
        }

        context.Result = ProblemResults.Create(
            context.HttpContext,
            StatusCodes.Status403Forbidden,
            ApiErrorCodes.DemoIdentityDisabled,
            "Mutations are disabled in this environment.",
            $"The development-only {DemoIdentityOptions.HeaderName} header is not enabled here and no authentication is configured.");
        return Task.CompletedTask;
    }
}
