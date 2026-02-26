using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MarimerLLC.AgentRegistry.Client;

namespace MarimerLLC.AgentRegistry.Client.Tests;

public class AgentHeartbeatServiceTests
{
    private static AgentHeartbeatService CreateService(
        IAgentRegistryClient client,
        AgentHeartbeatOptions options) =>
        new(client, Options.Create(options), NullLogger<AgentHeartbeatService>.Instance);

    [Fact]
    public async Task StartAsync_RegistersAgent_WhenRegistrationIsConfigured()
    {
        var fake = new FakeRegistryClient(livenessModel: "Persistent");
        var svc = CreateService(fake, new AgentHeartbeatOptions
        {
            Registration = new RegisterAgentRequest("My Agent"),
            HeartbeatInterval = TimeSpan.FromSeconds(60),
            DeregisterOnStop = false,
        });

        await svc.StartAsync(CancellationToken.None);
        await svc.StopAsync(CancellationToken.None);

        Assert.Equal(1, fake.RegisterCalls);
        Assert.NotNull(svc.AgentId);
    }

    [Fact]
    public async Task ExecuteAsync_SendsHeartbeats_ToPersistentEndpoints()
    {
        var fake = new FakeRegistryClient(livenessModel: "Persistent");
        var svc = CreateService(fake, new AgentHeartbeatOptions
        {
            Registration = new RegisterAgentRequest("My Agent"),
            HeartbeatInterval = TimeSpan.FromMilliseconds(30),
            DeregisterOnStop = false,
        });

        using var cts = new CancellationTokenSource();
        await svc.StartAsync(cts.Token);
        await Task.Delay(200); // let a few heartbeats fire
        await cts.CancelAsync();
        await svc.StopAsync(CancellationToken.None);

        Assert.True(fake.HeartbeatCalls > 0, "Expected at least one heartbeat");
    }

    [Fact]
    public async Task ExecuteAsync_SendsNoHeartbeats_ForEphemeralEndpoints()
    {
        var fake = new FakeRegistryClient(livenessModel: "Ephemeral");
        var svc = CreateService(fake, new AgentHeartbeatOptions
        {
            Registration = new RegisterAgentRequest("My Agent"),
            HeartbeatInterval = TimeSpan.FromMilliseconds(30),
            DeregisterOnStop = false,
        });

        using var cts = new CancellationTokenSource();
        await svc.StartAsync(cts.Token);
        await Task.Delay(150);
        await cts.CancelAsync();
        await svc.StopAsync(CancellationToken.None);

        Assert.Equal(0, fake.HeartbeatCalls);
    }

    [Fact]
    public async Task StopAsync_Deregisters_WhenDeregisterOnStopIsTrue()
    {
        var fake = new FakeRegistryClient(livenessModel: "Persistent");
        var svc = CreateService(fake, new AgentHeartbeatOptions
        {
            Registration = new RegisterAgentRequest("My Agent"),
            HeartbeatInterval = TimeSpan.FromSeconds(60),
            DeregisterOnStop = true,
        });

        await svc.StartAsync(CancellationToken.None);
        await svc.StopAsync(CancellationToken.None);

        Assert.Equal(1, fake.DeregisterCalls);
    }

    [Fact]
    public async Task StopAsync_DoesNotDeregister_WhenDeregisterOnStopIsFalse()
    {
        var fake = new FakeRegistryClient(livenessModel: "Persistent");
        var svc = CreateService(fake, new AgentHeartbeatOptions
        {
            Registration = new RegisterAgentRequest("My Agent"),
            HeartbeatInterval = TimeSpan.FromSeconds(60),
            DeregisterOnStop = false,
        });

        await svc.StartAsync(CancellationToken.None);
        await svc.StopAsync(CancellationToken.None);

        Assert.Equal(0, fake.DeregisterCalls);
    }

    // ── Fake ─────────────────────────────────────────────────────────────────

    private sealed class FakeRegistryClient(string livenessModel) : IAgentRegistryClient
    {
        private readonly Guid _agentId = Guid.NewGuid();
        private readonly Guid _endpointId = Guid.NewGuid();

        public int RegisterCalls { get; private set; }
        public int HeartbeatCalls { get; private set; }
        public int DeregisterCalls { get; private set; }

        public Task<AgentResponse> RegisterAsync(RegisterAgentRequest req, CancellationToken ct = default)
        {
            RegisterCalls++;
            return Task.FromResult(new AgentResponse(
                Id: _agentId.ToString(),
                Name: req.Name,
                Description: req.Description,
                OwnerId: "test-owner",
                Labels: [],
                Capabilities: [],
                Endpoints: [new EndpointResponse(
                    Id: _endpointId.ToString(),
                    Name: "primary",
                    Transport: "Http",
                    Protocol: "A2A",
                    Address: "https://example.com",
                    LivenessModel: livenessModel,
                    TtlSeconds: null,
                    HeartbeatIntervalSeconds: 30,
                    IsLive: true,
                    ProtocolMetadata: null)],
                CreatedAt: DateTimeOffset.UtcNow,
                UpdatedAt: DateTimeOffset.UtcNow));
        }

        public Task<AgentResponse?> GetAgentAsync(Guid agentId, CancellationToken ct = default)
            => Task.FromResult<AgentResponse?>(null);

        public Task<AgentResponse> UpdateAgentAsync(Guid agentId, UpdateAgentRequest req, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task DeregisterAsync(Guid agentId, CancellationToken ct = default)
        {
            DeregisterCalls++;
            return Task.CompletedTask;
        }

        public Task<EndpointResponse> AddEndpointAsync(Guid agentId, EndpointRequest req, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task RemoveEndpointAsync(Guid agentId, Guid endpointId, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task HeartbeatAsync(Guid agentId, Guid endpointId, CancellationToken ct = default)
        {
            HeartbeatCalls++;
            return Task.CompletedTask;
        }

        public Task RenewAsync(Guid agentId, Guid endpointId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<PagedAgentResponse> DiscoverAsync(DiscoveryFilter? filter = null, CancellationToken ct = default)
            => Task.FromResult(new PagedAgentResponse([], 0, 1, 20, 0, false, false));
    }
}
