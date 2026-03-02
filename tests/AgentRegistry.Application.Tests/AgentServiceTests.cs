using MarimerLLC.AgentRegistry.Application;
using MarimerLLC.AgentRegistry.Application.Agents;
using MarimerLLC.AgentRegistry.Domain.Agents;
using Rocks;

[assembly: Rock(typeof(IAgentRepository), BuildType.Create)]
[assembly: Rock(typeof(ILivenessStore), BuildType.Create)]

namespace MarimerLLC.AgentRegistry.Application.Tests;

public class AgentServiceTests
{
    // ── RegisterAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_WithEndpoints_PersistsAndSetsLiveness()
    {
        var repoExpectations = new IAgentRepositoryCreateExpectations();
        repoExpectations.Setups.AddAsync(Arg.Any<Agent>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.CompletedTask);

        var livenessExpectations = new ILivenessStoreCreateExpectations();
        livenessExpectations.Setups.SetAliveAsync(Arg.Any<EndpointId>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.CompletedTask);

        var sut = new AgentService(repoExpectations.Instance(), livenessExpectations.Instance());

        var endpoints = new[] { new RegisterEndpointRequest(
            "primary", TransportType.Http, ProtocolType.A2A,
            "https://example.com", LivenessModel.Ephemeral,
            TimeSpan.FromMinutes(5), null) };

        var agent = await sut.RegisterAsync("Test", null, "owner-1", null, null, endpoints);

        repoExpectations.Verify();
        livenessExpectations.Verify();
        Assert.Single(agent.Endpoints);
    }

    [Fact]
    public async Task Register_WithCapabilities_AddsThemToAgent()
    {
        var repoExpectations = new IAgentRepositoryCreateExpectations();
        repoExpectations.Setups.AddAsync(Arg.Any<Agent>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.CompletedTask);

        var livenessExpectations = new ILivenessStoreCreateExpectations();

        var sut = new AgentService(repoExpectations.Instance(), livenessExpectations.Instance());

        var caps = new[] { new RegisterCapabilityRequest("summarize", "desc", ["nlp"]) };
        var agent = await sut.RegisterAsync("Test", null, "owner-1", null, caps, null);

        repoExpectations.Verify();
        Assert.Single(agent.Capabilities);
        Assert.Equal("summarize", agent.Capabilities[0].Name);
    }

    // ── UpdateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_WhenOwnerMatches_UpdatesAgent()
    {
        var existing = new Agent(AgentId.New(), "Old", null, "owner-1");

        var repoExpectations = new IAgentRepositoryCreateExpectations();
        repoExpectations.Setups.FindByIdAsync(existing.Id, Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<Agent?>(existing));
        repoExpectations.Setups.UpdateAsync(existing, Arg.Any<CancellationToken>())
            .ReturnValue(Task.CompletedTask);

        var livenessExpectations = new ILivenessStoreCreateExpectations();

        var sut = new AgentService(repoExpectations.Instance(), livenessExpectations.Instance());

        await sut.UpdateAsync(existing.Id, "New Name", "desc", null, "owner-1");

        repoExpectations.Verify();
        Assert.Equal("New Name", existing.Name);
    }

    [Fact]
    public async Task Update_WhenAgentNotFound_Throws()
    {
        var repoExpectations = new IAgentRepositoryCreateExpectations();
        repoExpectations.Setups.FindByIdAsync(Arg.Any<AgentId>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<Agent?>(null));

        var livenessExpectations = new ILivenessStoreCreateExpectations();

        var sut = new AgentService(repoExpectations.Instance(), livenessExpectations.Instance());

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.UpdateAsync(AgentId.New(), "x", null, null, "owner-1"));
    }

    [Fact]
    public async Task Update_WhenWrongOwner_ThrowsForbidden()
    {
        var agent = new Agent(AgentId.New(), "Test", null, "owner-1");

        var repoExpectations = new IAgentRepositoryCreateExpectations();
        repoExpectations.Setups.FindByIdAsync(agent.Id, Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<Agent?>(agent));

        var livenessExpectations = new ILivenessStoreCreateExpectations();

        var sut = new AgentService(repoExpectations.Instance(), livenessExpectations.Instance());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            sut.UpdateAsync(agent.Id, "x", null, null, "owner-2"));
    }

    // ── Heartbeat ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Heartbeat_PersistentEndpoint_ResetsLiveness()
    {
        var agent = new Agent(AgentId.New(), "Test", null, "owner-1");
        var endpoint = agent.AddEndpoint(
            "primary", TransportType.Http, ProtocolType.MCP,
            "https://example.com", LivenessModel.Persistent, null, TimeSpan.FromSeconds(30));

        var repoExpectations = new IAgentRepositoryCreateExpectations();
        repoExpectations.Setups.FindByIdAsync(agent.Id, Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<Agent?>(agent));

        var livenessExpectations = new ILivenessStoreCreateExpectations();
        livenessExpectations.Setups.SetAliveAsync(endpoint.Id, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.CompletedTask);

        var sut = new AgentService(repoExpectations.Instance(), livenessExpectations.Instance());

        await sut.HeartbeatAsync(agent.Id, endpoint.Id, "owner-1");

        livenessExpectations.Verify();
    }

    [Fact]
    public async Task Heartbeat_OnEphemeralEndpoint_Throws()
    {
        var agent = new Agent(AgentId.New(), "Test", null, "owner-1");
        var endpoint = agent.AddEndpoint(
            "primary", TransportType.Http, ProtocolType.A2A,
            "https://example.com", LivenessModel.Ephemeral, TimeSpan.FromMinutes(5), null);

        var repoExpectations = new IAgentRepositoryCreateExpectations();
        repoExpectations.Setups.FindByIdAsync(agent.Id, Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<Agent?>(agent));

        var livenessExpectations = new ILivenessStoreCreateExpectations();

        var sut = new AgentService(repoExpectations.Instance(), livenessExpectations.Instance());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.HeartbeatAsync(agent.Id, endpoint.Id, "owner-1"));
    }

    // ── Renew ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Renew_EphemeralEndpoint_ExtendsLiveness()
    {
        var agent = new Agent(AgentId.New(), "Test", null, "owner-1");
        var endpoint = agent.AddEndpoint(
            "fn", TransportType.AzureServiceBus, ProtocolType.A2A,
            "my-queue", LivenessModel.Ephemeral, TimeSpan.FromMinutes(5), null);

        var repoExpectations = new IAgentRepositoryCreateExpectations();
        repoExpectations.Setups.FindByIdAsync(agent.Id, Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<Agent?>(agent));
        repoExpectations.Setups.UpdateAsync(agent, Arg.Any<CancellationToken>())
            .ReturnValue(Task.CompletedTask);

        var livenessExpectations = new ILivenessStoreCreateExpectations();
        livenessExpectations.Setups.SetAliveAsync(endpoint.Id, TimeSpan.FromMinutes(5), Arg.Any<CancellationToken>())
            .ReturnValue(Task.CompletedTask);

        var sut = new AgentService(repoExpectations.Instance(), livenessExpectations.Instance());

        await sut.RenewAsync(agent.Id, endpoint.Id, "owner-1");

        livenessExpectations.Verify();
        repoExpectations.Verify();
    }

    // ── Deregister ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Deregister_RemovesLivenessAndDeletesFromRepo()
    {
        var agent = new Agent(AgentId.New(), "Test", null, "owner-1");
        agent.AddEndpoint("e1", TransportType.Http, ProtocolType.A2A,
            "https://example.com", LivenessModel.Ephemeral, TimeSpan.FromMinutes(5), null);

        var repoExpectations = new IAgentRepositoryCreateExpectations();
        repoExpectations.Setups.FindByIdAsync(agent.Id, Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<Agent?>(agent));
        repoExpectations.Setups.DeleteAsync(agent.Id, Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult(true));

        var livenessExpectations = new ILivenessStoreCreateExpectations();
        livenessExpectations.Setups.RemoveAllForAgentAsync(Arg.Any<IEnumerable<EndpointId>>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.CompletedTask);

        var sut = new AgentService(repoExpectations.Instance(), livenessExpectations.Instance());

        await sut.DeregisterAsync(agent.Id, "owner-1");

        repoExpectations.Verify();
        livenessExpectations.Verify();
    }

    // ── Discovery ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Discover_LiveOnly_FiltersOutDeadAgents()
    {
        var agent = new Agent(AgentId.New(), "Test", null, "owner-1");
        agent.AddEndpoint("e1", TransportType.Http, ProtocolType.A2A,
            "https://example.com", LivenessModel.Persistent, null, TimeSpan.FromSeconds(30));

        var repoExpectations = new IAgentRepositoryCreateExpectations();
        repoExpectations.Setups.SearchAsync(Arg.Any<AgentSearchFilter>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult(new PagedResult<Agent>([agent], 1, 1, 20)));

        var livenessExpectations = new ILivenessStoreCreateExpectations();
        livenessExpectations.Setups.FilterAliveAsync(Arg.Any<IEnumerable<EndpointId>>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IReadOnlySet<EndpointId>>(new HashSet<EndpointId>()));

        var sut = new AgentService(repoExpectations.Instance(), livenessExpectations.Instance());

        var result = await sut.DiscoverAsync(new AgentSearchFilter(LiveOnly: true));

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Discover_LiveOnly_IncludesAgentsWithLiveEndpoints()
    {
        var agent = new Agent(AgentId.New(), "Test", null, "owner-1");
        var endpoint = agent.AddEndpoint("e1", TransportType.Http, ProtocolType.A2A,
            "https://example.com", LivenessModel.Persistent, null, TimeSpan.FromSeconds(30));

        var repoExpectations = new IAgentRepositoryCreateExpectations();
        repoExpectations.Setups.SearchAsync(Arg.Any<AgentSearchFilter>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult(new PagedResult<Agent>([agent], 1, 1, 20)));

        var livenessExpectations = new ILivenessStoreCreateExpectations();
        livenessExpectations.Setups.FilterAliveAsync(Arg.Any<IEnumerable<EndpointId>>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IReadOnlySet<EndpointId>>(new HashSet<EndpointId> { endpoint.Id }));

        var sut = new AgentService(repoExpectations.Instance(), livenessExpectations.Instance());

        var result = await sut.DiscoverAsync(new AgentSearchFilter(LiveOnly: true));

        Assert.Single(result.Items);
        Assert.Contains(endpoint.Id, result.Items[0].LiveEndpointIds);
    }
}
