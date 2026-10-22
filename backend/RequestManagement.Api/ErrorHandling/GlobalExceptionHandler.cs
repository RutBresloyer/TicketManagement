using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RequestManagement.Api.Contracts.Common;

namespace RequestManagement.Api.ErrorHandling;

/// <summary>
/// Last-resort handler: logs the exception server-side and returns a generic 500 ProblemDetails.
/// Exception types, messages and stack traces never reach the client; the traceId links the response to the log entry.
/// </summary>
public sealed class GlobalExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client went away; there is nobody to answer and nothing went wrong on our side.
            logger.LogInformation("Request {Method} {Path} was cancelled by the client", httpContext.Request.Method, httpContext.Request.Path);
            httpContext.Response.StatusCode = 499;
            return true;
        }

        logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Detail = "The error has been logged. Quote the traceId when reporting it.",
                Extensions = { ["code"] = ApiErrorCodes.UnexpectedError },
            },
        });
    }
}
