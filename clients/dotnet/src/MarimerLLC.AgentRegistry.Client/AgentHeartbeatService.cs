using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarimerLLC.AgentRegistry.Client;

/// <summary>
/// Hosted service that registers the agent on startup, sends heartbeats to all
/// Persistent endpoints at a regular interval, and deregisters on shutdown.
/// </summary>
public sealed class AgentHeartbeatService(
    IAgentRegistryClient client,
    IOptions<AgentHeartbeatOptions> options,
    ILogger<AgentHeartbeatService> logger) : BackgroundService
{
    private readonly AgentHeartbeatOptions _options = options.Value;
    private Guid? _agentId;
    private IReadOnlyList<Guid> _persistentEndpointIds = [];

    /// <summary>The registered agent's ID. Available after <see cref="StartAsync"/> completes.</summary>
    public Guid? AgentId => _agentId;

    public override async Task StartAsync(CancellationToken ct)
    {
        if (_options.Registration is { } reg)
        {
            logger.LogInformation("Registering agent '{Name}' with the registry...", reg.Name);
            var agent = await client.RegisterAsync(reg, ct);
            _agentId = Guid.Parse(agent.Id);
            _persistentEndpointIds = agent.Endpoints
                .Where(e => e.LivenessModel == nameof(LivenessModel.Persistent))
                .Select(e => Guid.Parse(e.Id))
                .ToList();
            logger.LogInformation("Agent registered with ID {AgentId} ({Count} persistent endpoint(s))",
                _agentId, _persistentEndpointIds.Count);
        }

        await base.StartAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_agentId is null || _persistentEndpointIds.Count == 0)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(_options.HeartbeatInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }

            foreach (var endpointId in _persistentEndpointIds)
            {
                try
                {
                    await client.HeartbeatAsync(_agentId.Value, endpointId, stoppingToken);
                    logger.LogDebug("Heartbeat sent for endpoint {EndpointId}", endpointId);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "Heartbeat failed for endpoint {EndpointId}", endpointId);
                }
            }
        }
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        await base.StopAsync(ct);

        if (_agentId.HasValue && _options.DeregisterOnStop)
        {
            logger.LogInformation("Deregistering agent {AgentId}...", _agentId);
            try
            {
                await client.DeregisterAsync(_agentId.Value, ct);
                logger.LogInformation("Agent {AgentId} deregistered", _agentId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to deregister agent {AgentId} on shutdown", _agentId);
            }
        }
    }
}
