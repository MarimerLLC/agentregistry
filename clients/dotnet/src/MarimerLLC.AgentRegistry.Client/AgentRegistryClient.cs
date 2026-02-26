using System.Net;
using System.Net.Http.Json;

namespace MarimerLLC.AgentRegistry.Client;

public class AgentRegistryClient(HttpClient http) : IAgentRegistryClient
{
    public async Task<AgentResponse> RegisterAsync(RegisterAgentRequest request, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("/agents", request, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AgentResponse>(ct))!;
    }

    public async Task<AgentResponse?> GetAgentAsync(Guid agentId, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"/agents/{agentId}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AgentResponse>(ct);
    }

    public async Task<AgentResponse> UpdateAgentAsync(Guid agentId, UpdateAgentRequest request, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync($"/agents/{agentId}", request, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AgentResponse>(ct))!;
    }

    public async Task DeregisterAsync(Guid agentId, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"/agents/{agentId}", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<EndpointResponse> AddEndpointAsync(Guid agentId, EndpointRequest request, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync($"/agents/{agentId}/endpoints", request, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EndpointResponse>(ct))!;
    }

    public async Task RemoveEndpointAsync(Guid agentId, Guid endpointId, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"/agents/{agentId}/endpoints/{endpointId}", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task HeartbeatAsync(Guid agentId, Guid endpointId, CancellationToken ct = default)
    {
        var response = await http.PostAsync($"/agents/{agentId}/endpoints/{endpointId}/heartbeat", content: null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RenewAsync(Guid agentId, Guid endpointId, CancellationToken ct = default)
    {
        var response = await http.PostAsync($"/agents/{agentId}/endpoints/{endpointId}/renew", content: null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<PagedAgentResponse> DiscoverAsync(DiscoveryFilter? filter = null, CancellationToken ct = default)
    {
        filter ??= new DiscoveryFilter();

        var query = new List<string>();
        if (filter.Capability is not null) query.Add($"capability={Uri.EscapeDataString(filter.Capability)}");
        if (filter.Tags is not null)       query.Add($"tags={Uri.EscapeDataString(filter.Tags)}");
        if (filter.Protocol is not null)   query.Add($"protocol={Uri.EscapeDataString(filter.Protocol)}");
        if (filter.Transport is not null)  query.Add($"transport={Uri.EscapeDataString(filter.Transport)}");
        if (!filter.LiveOnly)              query.Add("liveOnly=false");
        if (filter.Page != 1)             query.Add($"page={filter.Page}");
        if (filter.PageSize != 20)        query.Add($"pageSize={filter.PageSize}");

        var url = query.Count > 0
            ? $"/discover/agents?{string.Join("&", query)}"
            : "/discover/agents";

        return (await http.GetFromJsonAsync<PagedAgentResponse>(url, ct))!;
    }
}
