using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using RequestManagement.Api.Contracts.Common;

namespace RequestManagement.Api.ErrorHandling;

/// <summary>Builds RFC 7807 responses that always carry a machine-readable <c>code</c> extension and a <c>traceId</c>.</summary>
public static class ProblemResults
{
    public const string ContentType = "application/problem+json";

    public static ObjectResult Create(
        HttpContext httpContext,
        int statusCode,
        string code,
        string title,
        string detail,
        IReadOnlyDictionary<string, object?>? extensions = null)
    {
        var factory = httpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = factory.CreateProblemDetails(httpContext, statusCode, title, type: null, detail, httpContext.Request.Path);
        problem.Extensions["code"] = code;
        foreach (var (key, value) in extensions ?? new Dictionary<string, object?>())
        {
            problem.Extensions[key] = value;
        }

        return new ObjectResult(problem) { StatusCode = statusCode, ContentTypes = { ContentType } };
    }

    /// <summary>Replacement for the default [ApiController] 400 response, adding <c>code = ValidationFailed</c>.</summary>
    public static IActionResult InvalidModelState(ActionContext context)
    {
        var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = factory.CreateValidationProblemDetails(
            context.HttpContext,
            context.ModelState,
            StatusCodes.Status400BadRequest,
            title: "One or more validation errors occurred.",
            detail: "See 'errors' for the invalid fields.",
            instance: context.HttpContext.Request.Path);
        problem.Extensions["code"] = ApiErrorCodes.ValidationFailed;
        return new BadRequestObjectResult(problem) { ContentTypes = { ContentType } };
    }
}
