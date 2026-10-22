using System.Text.Json.Serialization;

namespace RequestManagement.Api.Contracts.Requests;

/// <summary>Allow-listed sort fields for <c>GET /api/requests</c>. <c>id</c> is always appended as tie breaker.</summary>
public enum RequestSortField
{
    [JsonStringEnumMemberName("createdAt")] CreatedAt,
    [JsonStringEnumMemberName("updatedAt")] UpdatedAt,
    [JsonStringEnumMemberName("title")] Title,
    [JsonStringEnumMemberName("organizationName")] OrganizationName,
    [JsonStringEnumMemberName("priority")] Priority,
}

public enum SortDirection
{
    [JsonStringEnumMemberName("asc")] Asc,
    [JsonStringEnumMemberName("desc")] Desc,
}
