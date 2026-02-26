using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AgentRegistry.Infrastructure.Persistence;

/// <summary>
/// Used by EF Core tooling (dotnet ef migrations add/remove, database update ...) at design time.
/// Reads the connection string from the AGENTREGISTRY_DB environment variable so that
/// `database update` works against a real server without hardcoding credentials.
/// Falls back to a localhost placeholder that is sufficient for `migrations add`.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AgentRegistryDbContext>
{
    public AgentRegistryDbContext CreateDbContext(string[] args)
    {
        var connStr = Environment.GetEnvironmentVariable("AGENTREGISTRY_DB")
            ?? "Host=localhost;Database=agentregistry_design";

        var options = new DbContextOptionsBuilder<AgentRegistryDbContext>()
            .UseNpgsql(connStr)
            .Options;

        return new AgentRegistryDbContext(options);
    }
}
