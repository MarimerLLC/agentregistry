using MarimerLLC.AgentRegistry.Domain.ApiKeys;
using MarimerLLC.AgentRegistry.Domain.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MarimerLLC.AgentRegistry.Infrastructure.Persistence;

public class AgentRegistryDbContext(DbContextOptions<AgentRegistryDbContext> options) : DbContext(options)
{
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<Endpoint> Endpoints => Set<Endpoint>();
    public DbSet<Capability> Capabilities => Set<Capability>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var agentIdConverter = new ValueConverter<AgentId, Guid>(v => v.Value, v => new AgentId(v));
        var endpointIdConverter = new ValueConverter<EndpointId, Guid>(v => v.Value, v => new EndpointId(v));
        var capabilityIdConverter = new ValueConverter<CapabilityId, Guid>(v => v.Value, v => new CapabilityId(v));

        modelBuilder.Entity<Agent>(a =>
        {
            a.ToTable("agents");
            a.HasKey(x => x.Id);
            a.Property(x => x.Id).HasConversion(agentIdConverter).HasColumnName("id");
            a.Property(x => x.Name).HasMaxLength(256).HasColumnName("name");
            a.Property(x => x.Description).HasMaxLength(2048).HasColumnName("description");
            a.Property(x => x.OwnerId).HasMaxLength(256).HasColumnName("owner_id");
            a.Property(x => x.CreatedAt).HasColumnName("created_at");
            a.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            // Labels stored as JSONB
            a.Property<Dictionary<string, string>>("_labels")
                .HasField("_labels")
                .HasColumnName("labels")
                .HasColumnType("jsonb");

            a.HasMany(x => x.Endpoints)
                .WithOne()
                .HasForeignKey(e => e.AgentId)
                .OnDelete(DeleteBehavior.Cascade);

            a.HasMany(x => x.Capabilities)
                .WithOne()
                .HasForeignKey(c => c.AgentId)
                .OnDelete(DeleteBehavior.Cascade);

            a.Navigation(x => x.Endpoints).AutoInclude();
            a.Navigation(x => x.Capabilities).AutoInclude();
        });

        modelBuilder.Entity<Endpoint>(e =>
        {
            e.ToTable("endpoints");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasConversion(endpointIdConverter).HasColumnName("id");
            e.Property(x => x.AgentId).HasConversion(agentIdConverter).HasColumnName("agent_id");
            e.Property(x => x.Name).HasMaxLength(256).HasColumnName("name");
            e.Property(x => x.Transport).HasConversion<string>().HasMaxLength(64).HasColumnName("transport");
            e.Property(x => x.Protocol).HasConversion<string>().HasMaxLength(64).HasColumnName("protocol");
            e.Property(x => x.Address).HasMaxLength(2048).HasColumnName("address");
            e.Property(x => x.LivenessModel).HasConversion<string>().HasMaxLength(32).HasColumnName("liveness_model");
            e.Property(x => x.TtlDuration).HasColumnName("ttl_seconds")
                .HasConversion(
                    v => v.HasValue ? (double?)v.Value.TotalSeconds : null,
                    v => v.HasValue ? TimeSpan.FromSeconds(v.Value) : null);
            e.Property(x => x.HeartbeatInterval).HasColumnName("heartbeat_interval_seconds")
                .HasConversion(
                    v => v.HasValue ? (double?)v.Value.TotalSeconds : null,
                    v => v.HasValue ? TimeSpan.FromSeconds(v.Value) : null);
            e.Property(x => x.ProtocolMetadata).HasColumnName("protocol_metadata").HasColumnType("jsonb");
            e.Property(x => x.LastAliveAt).HasColumnName("last_alive_at");

            e.HasIndex(x => x.AgentId).HasDatabaseName("ix_endpoints_agent_id");
            e.HasIndex(x => new { x.Transport, x.Protocol }).HasDatabaseName("ix_endpoints_transport_protocol");
        });

        modelBuilder.Entity<Capability>(c =>
        {
            c.ToTable("capabilities");
            c.HasKey(x => x.Id);
            c.Property(x => x.Id).HasConversion(capabilityIdConverter).HasColumnName("id");
            c.Property(x => x.AgentId).HasConversion(agentIdConverter).HasColumnName("agent_id");
            c.Property(x => x.Name).HasMaxLength(256).HasColumnName("name");
            c.Property(x => x.Description).HasMaxLength(2048).HasColumnName("description");

            // Tags stored as a text array
            c.Property<List<string>>("_tags")
                .HasField("_tags")
                .HasColumnName("tags")
                .HasColumnType("text[]");

            c.HasIndex(x => x.AgentId).HasDatabaseName("ix_capabilities_agent_id");
            c.HasIndex(x => x.Name).HasDatabaseName("ix_capabilities_name");
        });

        var apiKeyIdConverter = new ValueConverter<ApiKeyId, Guid>(v => v.Value, v => new ApiKeyId(v));

        modelBuilder.Entity<ApiKey>(k =>
        {
            k.ToTable("api_keys");
            k.HasKey(x => x.Id);
            k.Property(x => x.Id).HasConversion(apiKeyIdConverter).HasColumnName("id");
            k.Property(x => x.OwnerId).HasMaxLength(256).HasColumnName("owner_id");
            k.Property(x => x.Description).HasMaxLength(512).HasColumnName("description");
            k.Property(x => x.KeyHash).HasMaxLength(64).IsFixedLength().HasColumnName("key_hash");
            k.Property(x => x.KeyPrefix).HasMaxLength(16).HasColumnName("key_prefix");
            k.Property(x => x.Scope).HasConversion<string>().HasMaxLength(32).HasColumnName("scope");
            k.Property(x => x.CreatedAt).HasColumnName("created_at");
            k.Property(x => x.RevokedAt).HasColumnName("revoked_at");
            k.Property(x => x.LastUsedAt).HasColumnName("last_used_at");

            k.HasIndex(x => x.KeyHash).IsUnique().HasDatabaseName("ix_api_keys_key_hash");
            k.HasIndex(x => x.OwnerId).HasDatabaseName("ix_api_keys_owner_id");
        });
    }
}
