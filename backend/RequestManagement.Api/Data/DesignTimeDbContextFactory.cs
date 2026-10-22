using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RequestManagement.Api.Data;

/// <summary>
/// Used only by <c>dotnet ef</c> (e.g. <c>migrations add</c>), so tooling never has to boot the web host.
/// Reads the admin connection string from the environment; generating a migration does not connect to a server.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__RequestManagementAdmin")
            ?? "Server=localhost;Database=RequestManagementDemo;Integrated Security=false;Encrypt=True";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new AppDbContext(options);
    }
}
