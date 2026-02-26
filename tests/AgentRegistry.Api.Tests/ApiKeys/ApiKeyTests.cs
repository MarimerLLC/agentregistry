using System.Net;
using System.Net.Http.Json;
using MarimerLLC.AgentRegistry.Api.ApiKeys.Models;
using MarimerLLC.AgentRegistry.Api.Tests.Infrastructure;
using MarimerLLC.AgentRegistry.Domain.ApiKeys;

namespace MarimerLLC.AgentRegistry.Api.Tests.ApiKeys;

public class ApiKeyTests(AgentRegistryFactory factory) : IClassFixture<AgentRegistryFactory>, IDisposable
{
    private readonly HttpClient _admin = factory.CreateAdminClient();
    private readonly HttpClient _agent = factory.CreateAgentClient();
    private readonly HttpClient _anon = factory.CreateClient();

    public void Dispose() => factory.Reset();

    // ── POST /api-keys — admin only ───────────────────────────────────────────

    [Fact]
    public async Task IssueKey_AsAdmin_Returns201WithRawKeyAndScope()
    {
        var response = await _admin.PostAsJsonAsync("/api-keys",
            new IssueApiKeyRequest("test key", ApiKeyScope.Agent));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<IssueApiKeyResponse>();
        Assert.NotNull(body);
        Assert.NotEmpty(body.RawKey);
        Assert.Equal(ApiKeyScope.Agent, body.Scope);
    }

    [Fact]
    public async Task IssueKey_AsAgent_Returns403()
    {
        var response = await _agent.PostAsJsonAsync("/api-keys",
            new IssueApiKeyRequest("should fail", ApiKeyScope.Agent));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task IssueKey_Unauthenticated_Returns401()
    {
        var response = await _anon.PostAsJsonAsync("/api-keys",
            new IssueApiKeyRequest(null));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── GET /api-keys — admin only ────────────────────────────────────────────

    [Fact]
    public async Task ListKeys_AsAdmin_Returns200()
    {
        await _admin.PostAsJsonAsync("/api-keys", new IssueApiKeyRequest("k1", ApiKeyScope.Agent));
        await _admin.PostAsJsonAsync("/api-keys", new IssueApiKeyRequest("k2", ApiKeyScope.Admin));

        var response = await _admin.GetAsync("/api-keys");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var keys = await response.Content.ReadFromJsonAsync<List<ApiKeyResponse>>();
        Assert.NotNull(keys);
        Assert.Equal(2, keys.Count);
    }

    [Fact]
    public async Task ListKeys_AsAgent_Returns403()
    {
        var response = await _agent.GetAsync("/api-keys");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── DELETE /api-keys/{id} — admin only ────────────────────────────────────

    [Fact]
    public async Task RevokeKey_AsAdmin_Returns204()
    {
        var issued = await PostAndDeserialize<IssueApiKeyResponse>(
            "/api-keys", new IssueApiKeyRequest("to revoke", ApiKeyScope.Agent));

        var response = await _admin.DeleteAsync($"/api-keys/{issued.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task RevokeKey_AsAgent_Returns403()
    {
        var response = await _agent.DeleteAsync("/api-keys/some-id");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── POST /api-keys/bootstrap ──────────────────────────────────────────────

    [Fact]
    public async Task Bootstrap_WhenNotConfigured_Returns404()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api-keys/bootstrap");
        request.Headers.Add("X-Bootstrap-Token", "anything");
        request.Content = JsonContent.Create(new BootstrapApiKeyRequest("admin", "initial"));

        var response = await _anon.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Agent key CAN register agents ─────────────────────────────────────────

    [Fact]
    public async Task RegisterAgent_AsAgent_Returns201()
    {
        var response = await _agent.PostAsJsonAsync("/agents",
            new { Name = "My Agent", Description = (string?)null, Labels = (object?)null,
                  Capabilities = (object?)null, Endpoints = (object?)null });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<T> PostAndDeserialize<T>(string url, object body)
    {
        var response = await _admin.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
