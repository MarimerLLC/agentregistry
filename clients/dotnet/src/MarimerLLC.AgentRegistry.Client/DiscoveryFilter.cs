namespace MarimerLLC.AgentRegistry.Client;

public record DiscoveryFilter(
    string? Capability = null,
    string? Tags = null,       // comma-separated, e.g. "nlp,summarize"
    string? Protocol = null,   // "A2A", "MCP", "ACP", "Unknown"
    string? Transport = null,  // "Http", "Amqp", "AzureServiceBus"
    bool LiveOnly = true,
    int Page = 1,
    int PageSize = 20);
