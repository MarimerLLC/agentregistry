using AgentRegistry.Domain.Agents;

namespace AgentRegistry.Domain.Tests;

public class AgentTests
{
    [Fact]
    public void Agent_WithValidData_Initializes()
    {
        var agent = new Agent(AgentId.New(), "Summarizer", "Summarizes documents", "owner-1");

        Assert.Equal("Summarizer", agent.Name);
        Assert.Equal("owner-1", agent.OwnerId);
        Assert.Empty(agent.Endpoints);
        Assert.Empty(agent.Capabilities);
    }

    [Fact]
    public void Agent_AddEndpoint_EphemeralWithTtl_Succeeds()
    {
        var agent = new Agent(AgentId.New(), "Test", null, "owner-1");

        var endpoint = agent.AddEndpoint(
            "primary",
            TransportType.Http,
            ProtocolType.A2A,
            "https://example.com/agent",
            LivenessModel.Ephemeral,
            ttlDuration: TimeSpan.FromMinutes(5),
            heartbeatInterval: null);

        Assert.Single(agent.Endpoints);
        Assert.Equal(LivenessModel.Ephemeral, endpoint.LivenessModel);
        Assert.Equal(TimeSpan.FromMinutes(5), endpoint.TtlDuration);
    }

    [Fact]
    public void Agent_AddEndpoint_PersistentWithHeartbeat_Succeeds()
    {
        var agent = new Agent(AgentId.New(), "Test", null, "owner-1");

        var endpoint = agent.AddEndpoint(
            "primary",
            TransportType.Http,
            ProtocolType.MCP,
            "https://example.com/mcp",
            LivenessModel.Persistent,
            ttlDuration: null,
            heartbeatInterval: TimeSpan.FromSeconds(30));

        Assert.Equal(LivenessModel.Persistent, endpoint.LivenessModel);
        Assert.Equal(TimeSpan.FromSeconds(30), endpoint.HeartbeatInterval);
    }

    [Fact]
    public void Endpoint_EffectiveLivenessTtl_Ephemeral_ReturnsTtl()
    {
        var agent = new Agent(AgentId.New(), "Test", null, "owner-1");
        var endpoint = agent.AddEndpoint(
            "q", TransportType.AzureServiceBus, ProtocolType.A2A,
            "my-queue", LivenessModel.Ephemeral,
            ttlDuration: TimeSpan.FromMinutes(10), heartbeatInterval: null);

        Assert.Equal(TimeSpan.FromMinutes(10), endpoint.EffectiveLivenessTtl());
    }

    [Fact]
    public void Endpoint_EffectiveLivenessTtl_Persistent_Returns2Point5xInterval()
    {
        var agent = new Agent(AgentId.New(), "Test", null, "owner-1");
        var endpoint = agent.AddEndpoint(
            "primary", TransportType.Http, ProtocolType.MCP,
            "https://example.com", LivenessModel.Persistent,
            ttlDuration: null, heartbeatInterval: TimeSpan.FromSeconds(20));

        Assert.Equal(TimeSpan.FromSeconds(50), endpoint.EffectiveLivenessTtl());
    }

    [Fact]
    public void Agent_RemoveEndpoint_ReturnsTrue_WhenFound()
    {
        var agent = new Agent(AgentId.New(), "Test", null, "owner-1");
        var endpoint = agent.AddEndpoint(
            "primary", TransportType.Http, ProtocolType.A2A,
            "https://example.com", LivenessModel.Ephemeral,
            TimeSpan.FromMinutes(5), null);

        var removed = agent.RemoveEndpoint(endpoint.Id);

        Assert.True(removed);
        Assert.Empty(agent.Endpoints);
    }

    [Fact]
    public void Agent_RemoveEndpoint_ReturnsFalse_WhenNotFound()
    {
        var agent = new Agent(AgentId.New(), "Test", null, "owner-1");
        var removed = agent.RemoveEndpoint(EndpointId.New());
        Assert.False(removed);
    }

    [Fact]
    public void Agent_AddCapability_WithTags_Succeeds()
    {
        var agent = new Agent(AgentId.New(), "Test", null, "owner-1");
        var cap = agent.AddCapability("summarize", "Summarizes text", ["nlp", "text"]);

        Assert.Single(agent.Capabilities);
        Assert.Equal(["nlp", "text"], cap.Tags);
    }

    [Fact]
    public void Agent_Update_ChangesNameAndDescription()
    {
        var agent = new Agent(AgentId.New(), "Old Name", "Old desc", "owner-1");
        agent.Update("New Name", "New desc", null);

        Assert.Equal("New Name", agent.Name);
        Assert.Equal("New desc", agent.Description);
    }

    [Fact]
    public void AgentId_New_ProducesUniqueIds()
    {
        var a = AgentId.New();
        var b = AgentId.New();
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void AgentId_Parse_RoundTrips()
    {
        var id = AgentId.New();
        var parsed = AgentId.Parse(id.ToString());
        Assert.Equal(id, parsed);
    }
}
