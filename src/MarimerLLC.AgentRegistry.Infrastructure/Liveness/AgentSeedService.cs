using MarimerLLC.AgentRegistry.Application.Agents;
using MarimerLLC.AgentRegistry.Domain.Agents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarimerLLC.AgentRegistry.Infrastructure.Liveness;

/// <summary>
/// Ensures every agent defined in the "AgentSeeds" configuration section exists in the
/// registry and has its ephemeral endpoints reseeded into the liveness store on startup.
/// Unlike <see cref="EphemeralReseedService"/>, this service always reseeds regardless
/// of the 48-hour window — config-defined agents are always considered alive.
/// </summary>
public sealed class AgentSeedService(
    IServiceScopeFactory scopeFactory,
    IOptions<AgentSeedConfig> config,
    ILogger<AgentSeedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        var seeds = config.Value.Agents;
        if (seeds.Count == 0) return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAgentRepository>();
        var store = scope.ServiceProvider.GetRequiredService<ILivenessStore>();

        var reseeded = 0;
        var created = 0;

        foreach (var seed in seeds)
        {
            var (wasCreated, endpointCount) = await EnsureAgentAsync(repo, store, seed, ct);
            if (wasCreated) created++;
            reseeded += endpointCount;
        }

        logger.LogInformation(
            "Agent seed complete: {Created} agent(s) created, {Reseeded} ephemeral endpoint(s) reseeded.",
            created, reseeded);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private static async Task<(bool wasCreated, int endpointsReseeded)> EnsureAgentAsync(
        IAgentRepository repo, ILivenessStore store, AgentSeedEntry seed, CancellationToken ct)
    {
        // Search for an existing agent matching name + ownerId.
        // Seed files are small so loading up to 1000 agents per owner is acceptable.
        var filter = new AgentSearchFilter(seed.OwnerId, null, null, null, null, false, 1, 1000);
        var page = await repo.SearchAsync(filter, ct);
        var existing = page.Items.FirstOrDefault(a => a.Name == seed.Name);

        if (existing is null)
        {
            var agent = new Agent(AgentId.New(), seed.Name, seed.Description, seed.OwnerId, seed.Labels);

            foreach (var cap in seed.Capabilities)
                agent.AddCapability(cap.Name, cap.Description, cap.Tags);

            var addedEndpoints = new List<Endpoint>();
            foreach (var ep in seed.Endpoints)
            {
                var ttl = ep.TtlSeconds.HasValue ? TimeSpan.FromSeconds(ep.TtlSeconds.Value) : (TimeSpan?)null;
                var heartbeat = ep.HeartbeatIntervalSeconds.HasValue
                    ? TimeSpan.FromSeconds(ep.HeartbeatIntervalSeconds.Value)
                    : (TimeSpan?)null;
                addedEndpoints.Add(agent.AddEndpoint(
                    ep.Name, ep.Transport, ep.Protocol, ep.Address,
                    ep.LivenessModel, ttl, heartbeat, ep.ProtocolMetadata));
            }

            await repo.AddAsync(agent, ct);

            var ephemeralEndpoints = addedEndpoints.Where(e => e.LivenessModel == LivenessModel.Ephemeral).ToList();
            foreach (var endpoint in ephemeralEndpoints)
                await store.SetAliveAsync(endpoint.Id, endpoint.EffectiveLivenessTtl(), ct);

            return (wasCreated: true, endpointsReseeded: ephemeralEndpoints.Count);
        }
        else
        {
            // Agent already exists — reseed all its ephemeral endpoints unconditionally.
            var ephemeralEndpoints = existing.Endpoints
                .Where(e => e.LivenessModel == LivenessModel.Ephemeral)
                .ToList();

            foreach (var endpoint in ephemeralEndpoints)
                await store.SetAliveAsync(endpoint.Id, endpoint.EffectiveLivenessTtl(), ct);

            return (wasCreated: false, endpointsReseeded: ephemeralEndpoints.Count);
        }
    }
}
