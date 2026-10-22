using Microsoft.EntityFrameworkCore;
using RequestManagement.Api.Contracts.Common;
using RequestManagement.Api.Contracts.Requests;
using RequestManagement.Api.Data;
using RequestManagement.Api.Entities;
using RequestManagement.Api.Validation;

namespace RequestManagement.Api.Services;

/// <summary>Read side: list, details and history. Every filter, sort and page is translated to SQL.</summary>
public sealed class RequestQueryService(IDbContextFactory<AppDbContext> dbContextFactory)
{
    public async Task<PagedResult<RequestDto>> ListAsync(RequestListQuery query, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var filtered = ApplyFilters(db.Requests.AsNoTracking(), query);

        // Two round trips (COUNT, then the page) over the same WHERE clause. Both run in SQL Server;
        // no row is materialised in .NET except the requested page.
        var totalCount = await filtered.CountAsync(cancellationToken);

        var items = await ApplyOrdering(filtered, query.SortBy, query.SortDirection)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(r => new RequestDto
            {
                Id = r.Id,
                Title = r.Title,
                OrganizationName = r.OrganizationName,
                Status = r.Status,
                Priority = r.Priority,
                AssignedTo = r.AssignedTo,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
                // Evaluated in .NET on the 1–100 projected rows only (top-level projection).
                RowVersion = RowVersionToken.Encode(r.RowVersion),
                AllowedNextStatuses = RequestStatusTransitions.AllowedNext(r.Status),
            })
            .ToListAsync(cancellationToken);

        var totalPages = (int)Math.Ceiling(totalCount / (double)query.PageSize);
        return new PagedResult<RequestDto>(items, query.Page, query.PageSize, totalCount, totalPages);
    }

    public async Task<RequestDto?> GetAsync(long id, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var request = await db.Requests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        return request is null ? null : RequestDto.From(request);
    }

    /// <returns><c>null</c> when the request does not exist; an empty list when it exists but never changed.</returns>
    public async Task<IReadOnlyList<RequestHistoryEntryDto>?> GetHistoryAsync(long id, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (!await db.Requests.AnyAsync(r => r.Id == id, cancellationToken))
        {
            return null;
        }

        // Range seek on IX_RequestStatusHistory_RequestId_ChangedAt_Id; the index order matches, so no sort.
        return await db.RequestStatusHistory
            .AsNoTracking()
            .Where(h => h.RequestId == id)
            .OrderByDescending(h => h.ChangedAt)
            .ThenByDescending(h => h.Id)
            .Select(h => new RequestHistoryEntryDto(h.Id, h.PreviousStatus, h.NewStatus, h.ChangedAt, h.ChangedBy))
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<Request> ApplyFilters(IQueryable<Request> source, RequestListQuery query)
    {
        if (query.Status is { } status)
        {
            source = source.Where(r => r.Status == status);
        }

        if (query.Priority is { } priority)
        {
            source = source.Where(r => r.Priority == priority);
        }

        if (!string.IsNullOrWhiteSpace(query.AssignedTo))
        {
            var assignedTo = query.AssignedTo.Trim();
            source = source.Where(r => r.AssignedTo == assignedTo);
        }

        if (!string.IsNullOrWhiteSpace(query.OrganizationName))
        {
            var organizationName = query.OrganizationName.Trim();
            source = source.Where(r => r.OrganizationName == organizationName);
        }

        if (query.CreatedFrom is { } createdFrom)
        {
            source = source.Where(r => r.CreatedAt >= createdFrom);
        }

        if (query.CreatedTo is { } createdTo)
        {
            source = source.Where(r => r.CreatedAt < createdTo);
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            // Becomes LIKE '%term%' (EF escapes %, _ and [ in the parameter). A leading wildcard cannot seek a B-tree
            // index, so this predicate is evaluated against every row that survives the other filters.
            var term = query.Q.Trim();
            source = source.Where(r => r.Title.Contains(term) || r.OrganizationName.Contains(term));
        }

        return source;
    }

    private static IOrderedQueryable<Request> ApplyOrdering(
        IQueryable<Request> source, RequestSortField sortBy, SortDirection direction)
    {
        var descending = direction == SortDirection.Desc;

        // Id is always the final key so that pages are deterministic when the sort column has duplicates.
        return sortBy switch
        {
            RequestSortField.UpdatedAt => Order(source, r => r.UpdatedAt, descending),
            RequestSortField.Title => Order(source, r => r.Title, descending),
            RequestSortField.OrganizationName => Order(source, r => r.OrganizationName, descending),
            RequestSortField.Priority => Order(source, r => r.Priority, descending),
            _ => Order(source, r => r.CreatedAt, descending),
        };

        static IOrderedQueryable<Request> Order<TKey>(
            IQueryable<Request> source, System.Linq.Expressions.Expression<Func<Request, TKey>> key, bool descending) =>
            descending
                ? source.OrderByDescending(key).ThenByDescending(r => r.Id)
                : source.OrderBy(key).ThenBy(r => r.Id);
    }
}
