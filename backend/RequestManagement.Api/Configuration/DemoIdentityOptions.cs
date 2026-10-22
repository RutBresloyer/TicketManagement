namespace RequestManagement.Api.Configuration;

/// <summary>
/// Controls the development-only <c>X-Actor-Id</c> header. THIS IS NOT AUTHENTICATION: any caller can send any value.
/// It is enabled only in appsettings.Development.json; everywhere else mutations are refused until a real
/// authenticated principal (e.g. JWT claims) replaces it.
/// </summary>
public sealed class DemoIdentityOptions
{
    public const string SectionName = "DemoIdentity";
    public const string HeaderName = "X-Actor-Id";

    public bool Enabled { get; init; }
}
