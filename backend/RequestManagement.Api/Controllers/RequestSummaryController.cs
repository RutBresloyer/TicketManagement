using Microsoft.AspNetCore.Mvc;
using RequestManagement.Api.Contracts.Requests;
using RequestManagement.Api.ErrorHandling;
using RequestManagement.Api.Services;

namespace RequestManagement.Api.Controllers;

/// <summary>Aggregated request statistics.</summary>
[ApiController]
[Route("api/requests/summary")]
[Tags("Summary")]
public sealed class RequestSummaryController(RequestSummaryService summaryService) : ControllerBase
{
    /// <summary>Get total, per-status and per-priority request counts.</summary>
    /// <remarks>
    /// Computed with two SQL `GROUP BY` aggregations (status, priority); `totalCount` is the sum of the status groups.
    ///
    /// **Cached** in process memory for at most 30 seconds (`generatedAt` shows when it was computed). The cache is invalidated
    /// right after any committed status change, so a change made through this API instance is visible on the next call.
    /// With several API instances, other instances may serve a value up to 30 seconds old.
    /// </remarks>
    /// <response code="200">Current (or at most 30 s old) counts.</response>
    [HttpGet]
    [ProducesResponseType<RequestSummaryDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, ProblemResults.ContentType)]
    public async Task<ActionResult<RequestSummaryDto>> Get(CancellationToken cancellationToken) =>
        Ok(await summaryService.GetAsync(cancellationToken));
}
