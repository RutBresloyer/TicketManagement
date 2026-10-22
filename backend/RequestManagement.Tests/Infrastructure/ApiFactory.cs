using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RequestManagement.Tests.Infrastructure;

/// <summary>Hosts the real API in-memory (Development environment) against the given connection string.</summary>
public sealed class ApiFactory(string connectionString, bool demoIdentityEnabled = true) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:RequestManagement", connectionString);
        builder.UseSetting("DemoIdentity:Enabled", demoIdentityEnabled ? "true" : "false");
        builder.UseSetting("Logging:LogLevel:Default", "Warning");
        builder.UseSetting("Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command", "Warning");
        builder.UseSetting("Logging:LogLevel:Microsoft.AspNetCore.HttpLogging.HttpLoggingMiddleware", "Warning");
    }
}
