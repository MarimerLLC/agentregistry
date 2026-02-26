using MarimerLLC.AgentRegistry.Application.Agents;
using MarimerLLC.AgentRegistry.Application.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace MarimerLLC.AgentRegistry.Api.Tests.Infrastructure;

public class AgentRegistryFactory : WebApplicationFactory<Program>
{
    public InMemoryAgentRepository Repository { get; } = new();
    public InMemoryLivenessStore LivenessStore { get; } = new();
    public FakeApiKeyService ApiKeys { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Postgres", "Host=localhost;Database=test");
        builder.UseSetting("ConnectionStrings:Redis", "localhost:6379");
        builder.UseSetting("Database:AutoMigrate", "false");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IConnectionMultiplexer>();
            services.RemoveAll<IAgentRepository>();
            services.RemoveAll<ILivenessStore>();
            services.RemoveAll<IApiKeyService>();

            services.AddSingleton<IAgentRepository>(Repository);
            services.AddSingleton<ILivenessStore>(LivenessStore);
            services.AddSingleton<IApiKeyService>(ApiKeys);
        });
    }

    /// <summary>Admin-scoped client — can manage API keys and agents.</summary>
    public HttpClient CreateAuthenticatedClient() => CreateAdminClient();

    /// <summary>Admin-scoped client — can manage API keys and agents.</summary>
    public HttpClient CreateAdminClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", FakeApiKeyService.AdminKey);
        return client;
    }

    /// <summary>Agent-scoped client — can register and heartbeat agents, but not manage API keys.</summary>
    public HttpClient CreateAgentClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", FakeApiKeyService.AgentKey);
        return client;
    }

    public void Reset()
    {
        Repository.Clear();
        LivenessStore.Clear();
        ApiKeys.Clear();
    }
}
