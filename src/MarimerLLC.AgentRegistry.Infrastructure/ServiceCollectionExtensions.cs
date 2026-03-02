using MarimerLLC.AgentRegistry.Application.Agents;
using MarimerLLC.AgentRegistry.Application.Auth;
using MarimerLLC.AgentRegistry.Infrastructure.Auth;
using MarimerLLC.AgentRegistry.Infrastructure.Liveness;
using MarimerLLC.AgentRegistry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using StackExchange.Redis;

namespace MarimerLLC.AgentRegistry.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string postgresConnectionString,
        string redisConnectionString)
    {
        services.AddDbContext<AgentRegistryDbContext>(options =>
            options.UseNpgsql(postgresConnectionString));

        // Lazy singleton — connection is made on first resolve, not at registration time.
        // This allows the test host to replace IConnectionMultiplexer before it is ever used.
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisConnectionString));

        services.AddScoped<IAgentRepository, SqlAgentRepository>();
        services.AddScoped<ILivenessStore, RedisLivenessStore>();
        services.AddScoped<IApiKeyService, SqlApiKeyService>();

        services.AddHostedService<EphemeralReseedService>();

        return services;
    }

    public static IHealthChecksBuilder AddInfrastructureHealthChecks(
        this IHealthChecksBuilder builder,
        string postgresConnectionString)
    {
        builder.Add(new HealthCheckRegistration(
            "postgres",
            _ => new PostgresHealthCheck(postgresConnectionString),
            failureStatus: HealthStatus.Unhealthy,
            tags: ["ready"]));

        builder.Add(new HealthCheckRegistration(
            "redis",
            sp =>
            {
                var mux = sp.GetService<IConnectionMultiplexer>();
                return mux is not null
                    ? new RedisHealthCheck(mux)
                    : new StaticHealthCheck(HealthCheckResult.Unhealthy("Redis connection not configured."));
            },
            failureStatus: HealthStatus.Unhealthy,
            tags: ["ready"]));

        return builder;
    }

    /// <summary>
    /// Applies any pending EF Core migrations on startup.
    /// Call from Program.cs during application initialization.
    /// </summary>
    public static async Task MigrateAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentRegistryDbContext>();
        await db.Database.MigrateAsync();
    }
}
