using System.Net.Http.Json;
using System.Text.Json;
using MarimerLLC.AgentRegistry.Api.Protocols.A2A.Models;
using MarimerLLC.AgentRegistry.Api.Protocols.MCP.Models;
using MarimerLLC.AgentRegistry.Api.Tests.Infrastructure;

namespace MarimerLLC.AgentRegistry.Api.Tests;

/// <summary>
/// The registry's version is set once (VersionPrefix in Directory.Build.props) and every
/// surface that reports it must agree.
/// </summary>
public class VersionTests(AgentRegistryFactory factory) : IClassFixture<AgentRegistryFactory>
{
    // Semantic Versioning 2.0.0, from semver.org.
    private const string SemVer =
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)" +
        @"(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?" +
        @"(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$";

    private readonly HttpClient _anon = factory.CreateClient();

    [Fact]
    public void Version_IsSemVer()
    {
        Assert.Matches(SemVer, RegistryIdentity.Version);
    }

    [Fact]
    public void Version_MatchesAssemblyVersion()
    {
        var assembly = typeof(RegistryIdentity).Assembly.GetName().Version!;
        var core = RegistryIdentity.Version.Split('-', '+')[0];
        Assert.Equal($"{assembly.Major}.{assembly.Minor}.{assembly.Build}", core);
    }

    [Fact]
    public async Task AllSurfacesReportTheSameVersion()
    {
        var a2a = await _anon.GetFromJsonAsync<AgentCard>("/.well-known/agent-card.json");
        var mcp = await _anon.GetFromJsonAsync<ServerCard>("/mcp/server-card");
        var openApi = await _anon.GetFromJsonAsync<JsonDocument>("/openapi/v1.json");

        Assert.Equal(RegistryIdentity.Version, a2a!.Version);
        Assert.Equal(RegistryIdentity.Version, mcp!.Version);
        Assert.Equal(RegistryIdentity.Version,
            openApi!.RootElement.GetProperty("info").GetProperty("version").GetString());
    }
}
