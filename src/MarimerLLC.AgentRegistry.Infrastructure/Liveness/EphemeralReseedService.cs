using MarimerLLC.AgentRegistry.Application.Agents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MarimerLLC.AgentRegistry.Infrastructure.Liveness;

public sealed class EphemeralReseedService(
    IServiceScopeFactory scopeFactory,
    ILogger<EphemeralReseedService> logger,
    TimeSpan? reseedWindow = null) : IHostedService
{
    private readonly TimeSpan _window = reseedWindow ?? TimeSpan.FromHours(48);

    public async Task StartAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAgentRepository>();
        var store = scope.ServiceProvider.GetRequiredService<ILivenessStore>();

        var since = DateTimeOffset.UtcNow - _window;
        var endpoints = await repo.GetEphemeralEndpointsAliveAfterAsync(since, ct);

        foreach (var ep in endpoints)
            await store.SetAliveAsync(ep.Id, ep.EffectiveLivenessTtl(), ct);

        logger.LogInformation("Reseeded {Count} ephemeral endpoint(s) into liveness store.", endpoints.Count);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
