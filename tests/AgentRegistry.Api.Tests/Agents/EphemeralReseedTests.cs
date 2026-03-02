using System.Net.Http.Json;
using MarimerLLC.AgentRegistry.Api.Agents.Models;
using MarimerLLC.AgentRegistry.Api.Tests.Infrastructure;
using MarimerLLC.AgentRegistry.Domain.Agents;
using MarimerLLC.AgentRegistry.Infrastructure.Liveness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MarimerLLC.AgentRegistry.Api.Tests.Agents;

public class EphemeralReseedTests(AgentRegistryFactory factory) : IClassFixture<AgentRegistryFactory>, IDisposable
{
    private readonly HttpClient _client = factory.CreateAgentClient();
    private readonly HttpClient _anonClient = factory.CreateClient();

    public void Dispose() => factory.Reset();

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reseed_EphemeralEndpointAliveRecently_ReappearsInDiscoveryAfterLivenessClear()
    {
        // Register an ephemeral agent (sets LastAliveAt = now in Endpoint ctor)
        var response = await _client.PostAsJsonAsync("/agents",
            new RegisterAgentRequest("Reseed Agent", null, null, null,
            [new EndpointRequest("primary", TransportType.Http, ProtocolType.A2A,
                "https://example.com/agent", LivenessModel.Ephemeral, 300, null, null)]));
        response.EnsureSuccessStatusCode();

        // Confirm initially discoverable
        var before = await _anonClient.GetFromJsonAsync<PagedAgentResponse>("/discover/agents?liveOnly=true");
        Assert.NotNull(before);
        Assert.Single(before.Items);

        // Simulate registry restart: clear liveness store
        factory.LivenessStore.Clear();

        // Confirm no longer discoverable
        var afterClear = await _anonClient.GetFromJsonAsync<PagedAgentResponse>("/discover/agents?liveOnly=true");
        Assert.NotNull(afterClear);
        Assert.Empty(afterClear.Items);

        // Run reseed service (default 48-hour window includes the just-registered endpoint)
        var scopeFactory = factory.Services.GetRequiredService<IServiceScopeFactory>();
        var reseedService = new EphemeralReseedService(
            scopeFactory,
            NullLogger<EphemeralReseedService>.Instance);
        await reseedService.StartAsync(CancellationToken.None);

        // Confirm discoverable again after reseed
        var afterReseed = await _anonClient.GetFromJsonAsync<PagedAgentResponse>("/discover/agents?liveOnly=true");
        Assert.NotNull(afterReseed);
        Assert.Single(afterReseed.Items);
        Assert.Equal("Reseed Agent", afterReseed.Items[0].Name);
    }

    [Fact]
    public async Task Reseed_WithWindowThatExcludesEndpoint_DoesNotRestoreLiveness()
    {
        // Register an ephemeral agent
        var response = await _client.PostAsJsonAsync("/agents",
            new RegisterAgentRequest("Old Agent", null, null, null,
            [new EndpointRequest("primary", TransportType.Http, ProtocolType.A2A,
                "https://example.com/old", LivenessModel.Ephemeral, 300, null, null)]));
        response.EnsureSuccessStatusCode();

        // Simulate restart
        factory.LivenessStore.Clear();

        // Run reseed with a negative window: since = UtcNow + 2 days.
        // The endpoint's LastAliveAt (= now) is before that threshold, so it is excluded.
        var scopeFactory = factory.Services.GetRequiredService<IServiceScopeFactory>();
        var reseedService = new EphemeralReseedService(
            scopeFactory,
            NullLogger<EphemeralReseedService>.Instance,
            reseedWindow: TimeSpan.FromDays(-2));
        await reseedService.StartAsync(CancellationToken.None);

        // Endpoint should still not appear in live-only discovery
        var result = await _anonClient.GetFromJsonAsync<PagedAgentResponse>("/discover/agents?liveOnly=true");
        Assert.NotNull(result);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Reseed_PersistentEndpoint_IsNotReseeded()
    {
        // Register a persistent agent
        var response = await _client.PostAsJsonAsync("/agents",
            new RegisterAgentRequest("Persistent Agent", null, null, null,
            [new EndpointRequest("primary", TransportType.Http, ProtocolType.A2A,
                "https://example.com/persistent", LivenessModel.Persistent,
                TtlSeconds: null, HeartbeatIntervalSeconds: 30, ProtocolMetadata: null)]));
        response.EnsureSuccessStatusCode();

        // Simulate restart
        factory.LivenessStore.Clear();

        // Run reseed (48-hour window)
        var scopeFactory = factory.Services.GetRequiredService<IServiceScopeFactory>();
        var reseedService = new EphemeralReseedService(
            scopeFactory,
            NullLogger<EphemeralReseedService>.Instance);
        await reseedService.StartAsync(CancellationToken.None);

        // Persistent endpoint is NOT reseeded — agent still not discoverable (liveOnly)
        var result = await _anonClient.GetFromJsonAsync<PagedAgentResponse>("/discover/agents?liveOnly=true");
        Assert.NotNull(result);
        Assert.Empty(result.Items);
    }
}
