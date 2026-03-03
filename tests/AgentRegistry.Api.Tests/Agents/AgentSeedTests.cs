using System.Net.Http.Json;
using MarimerLLC.AgentRegistry.Api.Agents.Models;
using MarimerLLC.AgentRegistry.Api.Tests.Infrastructure;
using MarimerLLC.AgentRegistry.Domain.Agents;
using MarimerLLC.AgentRegistry.Infrastructure.Liveness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MarimerLLC.AgentRegistry.Api.Tests.Agents;

public class AgentSeedTests(AgentRegistryFactory factory) : IClassFixture<AgentRegistryFactory>, IDisposable
{
    private readonly HttpClient _anonClient = factory.CreateClient();
    private readonly HttpClient _agentClient = factory.CreateAgentClient();

    public void Dispose() => factory.Reset();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private AgentSeedService CreateSeedService(AgentSeedConfig config)
    {
        var scopeFactory = factory.Services.GetRequiredService<IServiceScopeFactory>();
        return new AgentSeedService(
            scopeFactory,
            Options.Create(config),
            NullLogger<AgentSeedService>.Instance);
    }

    private static AgentSeedConfig SingleEphemeralAgent(string name = "Seeded Agent", string ownerId = "system") =>
        new()
        {
            Agents =
            [
                new AgentSeedEntry
                {
                    Name = name,
                    OwnerId = ownerId,
                    Description = "A config-seeded agent",
                    Endpoints =
                    [
                        new EndpointSeedEntry
                        {
                            Name = "primary",
                            Transport = TransportType.Http,
                            Protocol = ProtocolType.A2A,
                            Address = "https://seeded-agent.internal/",
                            LivenessModel = LivenessModel.Ephemeral,
                            TtlSeconds = 300
                        }
                    ]
                }
            ]
        };

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Seed_AgentNotYetInRegistry_CreatesAndMakesDiscoverable()
    {
        var svc = CreateSeedService(SingleEphemeralAgent());
        await svc.StartAsync(CancellationToken.None);

        var result = await _anonClient.GetFromJsonAsync<PagedAgentResponse>("/discover/agents?liveOnly=true");

        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Equal("Seeded Agent", result.Items[0].Name);
    }

    [Fact]
    public async Task Seed_AgentAlreadyExists_ReseededAfterLivenessClear()
    {
        // First seed: creates the agent
        var svc = CreateSeedService(SingleEphemeralAgent());
        await svc.StartAsync(CancellationToken.None);

        // Simulate registry restart
        factory.LivenessStore.Clear();

        var afterClear = await _anonClient.GetFromJsonAsync<PagedAgentResponse>("/discover/agents?liveOnly=true");
        Assert.NotNull(afterClear);
        Assert.Empty(afterClear.Items);

        // Second seed (same config): finds existing agent, reseeds unconditionally
        await svc.StartAsync(CancellationToken.None);

        var afterReseed = await _anonClient.GetFromJsonAsync<PagedAgentResponse>("/discover/agents?liveOnly=true");
        Assert.NotNull(afterReseed);
        Assert.Single(afterReseed.Items);
        Assert.Equal("Seeded Agent", afterReseed.Items[0].Name);
    }

    [Fact]
    public async Task Seed_ExistingAgentOutsideEphemeralReseedWindow_IsStillReseeded()
    {
        // Create the agent and reseed with standard service
        var svc = CreateSeedService(SingleEphemeralAgent());
        await svc.StartAsync(CancellationToken.None);

        factory.LivenessStore.Clear();

        // EphemeralReseedService with a window that excludes everything (since = future)
        var scopeFactory = factory.Services.GetRequiredService<IServiceScopeFactory>();
        var dbReseedService = new EphemeralReseedService(
            scopeFactory,
            NullLogger<EphemeralReseedService>.Instance,
            reseedWindow: TimeSpan.FromDays(-2)); // since = UtcNow + 2 days — excludes all
        await dbReseedService.StartAsync(CancellationToken.None);

        // EphemeralReseedService found nothing — still not discoverable
        var afterDbReseed = await _anonClient.GetFromJsonAsync<PagedAgentResponse>("/discover/agents?liveOnly=true");
        Assert.NotNull(afterDbReseed);
        Assert.Empty(afterDbReseed.Items);

        // AgentSeedService ignores the window — always reseeds
        await svc.StartAsync(CancellationToken.None);

        var afterConfigReseed = await _anonClient.GetFromJsonAsync<PagedAgentResponse>("/discover/agents?liveOnly=true");
        Assert.NotNull(afterConfigReseed);
        Assert.Single(afterConfigReseed.Items);
    }

    [Fact]
    public async Task Seed_EmptyConfig_DoesNothing()
    {
        var svc = CreateSeedService(new AgentSeedConfig());
        await svc.StartAsync(CancellationToken.None);

        var result = await _anonClient.GetFromJsonAsync<PagedAgentResponse>("/discover/agents");
        Assert.NotNull(result);
        Assert.Empty(result.Items);
    }
}
