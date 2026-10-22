using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using RequestManagement.Api.Entities;

namespace RequestManagement.Api.Contracts.Requests;

/// <summary>Query-string parameters of <c>GET /api/requests</c>. All filters are optional and combined with AND.</summary>
public sealed class RequestListQuery : IValidatableObject
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;
    public const int MaxPage = 1_000_000;
    public const int MaxSearchLength = 100;

    /// <summary>Exact status match: <c>New</c>, <c>InProgress</c>, <c>Waiting</c> or <c>Completed</c>.</summary>
    [FromQuery(Name = "status")]
    public RequestStatus? Status { get; init; }

    /// <summary>Exact priority match: <c>Low</c>, <c>Medium</c> or <c>High</c>.</summary>
    [FromQuery(Name = "priority")]
    public RequestPriority? Priority { get; init; }

    /// <summary>Exact assignee match, case-insensitive (column collation <c>Latin1_General_100_CI_AS</c>).</summary>
    [StringLength(100, ErrorMessage = "assignedTo must be at most 100 characters.")]
    [FromQuery(Name = "assignedTo")]
    public string? AssignedTo { get; init; }

    /// <summary>Exact organization name match, case-insensitive (column collation <c>Latin1_General_100_CI_AS</c>).</summary>
    [StringLength(150, ErrorMessage = "organizationName must be at most 150 characters.")]
    [FromQuery(Name = "organizationName")]
    public string? OrganizationName { get; init; }

    /// <summary>Inclusive lower bound: <c>createdAt &gt;= createdFrom</c>. ISO 8601; a value without offset is treated as UTC.</summary>
    [FromQuery(Name = "createdFrom")]
    public DateTimeOffset? CreatedFrom { get; init; }

    /// <summary>Exclusive upper bound: <c>createdAt &lt; createdTo</c>. ISO 8601; a value without offset is treated as UTC.</summary>
    [FromQuery(Name = "createdTo")]
    public DateTimeOffset? CreatedTo { get; init; }

    /// <summary>Case-insensitive substring search in title OR organization name (max 100 characters).</summary>
    [StringLength(MaxSearchLength, ErrorMessage = "q must be at most 100 characters.")]
    [FromQuery(Name = "q")]
    public string? Q { get; init; }

    /// <summary>Sort field: <c>createdAt</c> (default), <c>updatedAt</c>, <c>title</c>, <c>organizationName</c> or <c>priority</c>.</summary>
    [FromQuery(Name = "sortBy")]
    public RequestSortField SortBy { get; init; } = RequestSortField.CreatedAt;

    /// <summary>Sort direction: <c>asc</c> or <c>desc</c> (default).</summary>
    [FromQuery(Name = "sortDirection")]
    public SortDirection SortDirection { get; init; } = SortDirection.Desc;

    /// <summary>1-based page number. Default 1.</summary>
    [Range(1, MaxPage, ErrorMessage = "page must be between 1 and 1000000.")]
    [FromQuery(Name = "page")]
    public int Page { get; init; } = 1;

    /// <summary>Page size, 1–100. Default 25.</summary>
    [Range(1, MaxPageSize, ErrorMessage = "pageSize must be between 1 and 100.")]
    [FromQuery(Name = "pageSize")]
    public int PageSize { get; init; } = DefaultPageSize;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CreatedFrom is { } from && CreatedTo is { } to && from >= to)
        {
            yield return new ValidationResult(
                "createdFrom must be earlier than createdTo (createdFrom is inclusive, createdTo is exclusive).",
                ["createdFrom", "createdTo"]);
        }
    }
}
