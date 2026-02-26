using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MarimerLLC.AgentRegistry.Client;

public static class ServiceCollectionExtensions
{
    private const string HttpClientName = "AgentRegistry";

    /// <summary>
    /// Registers <see cref="IAgentRegistryClient"/> in the DI container,
    /// configured with the given base URL and API key.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddAgentRegistryClient(options =>
    /// {
    ///     options.BaseUrl = "https://registry.example.com";
    ///     options.ApiKey  = "your-api-key";
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddAgentRegistryClient(
        this IServiceCollection services,
        Action<AgentRegistryClientOptions> configure)
    {
        services.Configure(configure);

        services.AddHttpClient(HttpClientName, (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<AgentRegistryClientOptions>>().Value;
            client.BaseAddress = new Uri(opts.BaseUrl);
            client.DefaultRequestHeaders.Add("X-Api-Key", opts.ApiKey);
        });

        // Singleton so it can be safely injected into AgentHeartbeatService (also a singleton).
        services.AddSingleton<IAgentRegistryClient>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new AgentRegistryClient(factory.CreateClient(HttpClientName));
        });

        return services;
    }

    /// <summary>
    /// Adds a hosted service that registers the agent on startup, sends heartbeats
    /// to all Persistent endpoints, and deregisters on shutdown.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddAgentHeartbeat(options =>
    /// {
    ///     options.Registration = new RegisterAgentRequest(
    ///         Name: "My Agent",
    ///         Endpoints: [new EndpointRequest(
    ///             Name:                   "primary",
    ///             Transport:              TransportType.Http,
    ///             Protocol:               ProtocolType.A2A,
    ///             Address:                "https://my-agent.example.com",
    ///             LivenessModel:          LivenessModel.Persistent,
    ///             HeartbeatIntervalSeconds: 60)]);
    ///     options.HeartbeatInterval = TimeSpan.FromSeconds(45);
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddAgentHeartbeat(
        this IServiceCollection services,
        Action<AgentHeartbeatOptions> configure)
    {
        services.Configure(configure);
        services.AddHostedService<AgentHeartbeatService>();
        return services;
    }
}
