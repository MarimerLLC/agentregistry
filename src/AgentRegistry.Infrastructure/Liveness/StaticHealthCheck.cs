using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AgentRegistry.Infrastructure.Liveness;

internal sealed class StaticHealthCheck(HealthCheckResult result) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        Task.FromResult(result);
}
