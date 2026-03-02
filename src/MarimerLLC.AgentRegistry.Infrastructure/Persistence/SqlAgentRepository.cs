using MarimerLLC.AgentRegistry.Application.Agents;
using MarimerLLC.AgentRegistry.Domain.Agents;
using Microsoft.EntityFrameworkCore;

namespace MarimerLLC.AgentRegistry.Infrastructure.Persistence;

public class SqlAgentRepository(AgentRegistryDbContext db) : IAgentRepository
{
    public async Task<Agent?> FindByIdAsync(AgentId id, CancellationToken ct = default) =>
        await db.Agents.FindAsync([id], ct);

    public async Task<PagedResult<Agent>> SearchAsync(AgentSearchFilter filter, CancellationToken ct = default)
    {
        var query = db.Agents.AsQueryable();

        if (filter.OwnerId is not null)
            query = query.Where(a => a.OwnerId == filter.OwnerId);

        if (filter.CapabilityName is not null)
            query = query.Where(a => a.Capabilities.Any(c =>
                EF.Functions.ILike(c.Name, $"%{filter.CapabilityName}%")));

        if (filter.Protocol.HasValue)
            query = query.Where(a => a.Endpoints.Any(e => e.Protocol == filter.Protocol.Value));

        if (filter.Transport.HasValue)
            query = query.Where(a => a.Endpoints.Any(e => e.Transport == filter.Transport.Value));

        if (filter.Tags is { Count: > 0 })
        {
            // Agent must have at least one capability containing ALL requested tags.
            // EF/Npgsql translates .Contains() on a mapped array column to `tag = ANY(tags)`.
            foreach (var tag in filter.Tags)
                query = query.Where(a => a.Capabilities.Any(c =>
                    EF.Property<List<string>>(c, "_tags").Contains(tag)));
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(a => a.Name)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return new PagedResult<Agent>(items, total, filter.Page, filter.PageSize);
    }

    public async Task AddAsync(Agent agent, CancellationToken ct = default)
    {
        db.Agents.Add(agent);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Agent agent, CancellationToken ct = default)
    {
        db.Agents.Update(agent);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(AgentId id, CancellationToken ct = default)
    {
        var deleted = await db.Agents.Where(a => a.Id == id).ExecuteDeleteAsync(ct);
        return deleted > 0;
    }

    public async Task<IReadOnlyList<Endpoint>> GetEphemeralEndpointsAliveAfterAsync(
        DateTimeOffset since, CancellationToken ct = default)
        => await db.Set<Endpoint>()
               .Where(e => e.LivenessModel == LivenessModel.Ephemeral && e.LastAliveAt >= since)
               .ToListAsync(ct);
}
