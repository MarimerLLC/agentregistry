# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build
dotnet build

# Run all tests
dotnet test

# Run a single test project
dotnet test tests/AgentRegistry.Api.Tests/

# Run a specific test by name
dotnet test --filter "FullyQualifiedName~RegistrationTests.Register_WithValidRequest"

# Run the API (requires Postgres + Redis via user secrets)
dotnet run --project src/AgentRegistry.Api

# Add a migration after domain/EF model changes
dotnet ef migrations add <Name> -p src/AgentRegistry.Infrastructure -s src/AgentRegistry.Api

# Apply migrations
dotnet ef database update -p src/AgentRegistry.Infrastructure -s src/AgentRegistry.Api
```

## Architecture

**Four-layer clean architecture.** No layer may reference a layer above it.

```
Domain → Application → Infrastructure
                    → Api (references Application, not Infrastructure directly)
```

- **`AgentRegistry.Domain`** — pure value objects and entities (`Agent`, `Endpoint`, `Capability`, `ApiKey`). No external dependencies. IDs are strongly-typed structs wrapping `Guid` (e.g. `AgentId`, `EndpointId`).
- **`AgentRegistry.Application`** — use-case logic in `AgentService`. Defines interfaces `IAgentRepository`, `ILivenessStore`, `IApiKeyService` that Infrastructure implements. Exceptions (`NotFoundException`, `ForbiddenException`) are declared here.
- **`AgentRegistry.Infrastructure`** — EF Core (Postgres via Npgsql) for agent/key persistence; Redis for endpoint liveness TTLs. `AgentRegistryDbContext` maps strongly-typed IDs via `ValueConverter`.
- **`AgentRegistry.Api`** — ASP.NET Core 10 minimal APIs. Composed in `Program.cs`. Protocol adapters live under `Protocols/` (A2A, MCP, ACP), each with a `*Mapper` class and endpoint registration.

## Key design patterns

**Liveness is split across two stores.** Agent identity and capabilities live in Postgres. Endpoint liveness is purely Redis TTL keys (`endpoint:liveness:{endpointId}`). Discovery queries Postgres first, then does a single batched Redis check to filter live endpoints. The two liveness models are:
- `Ephemeral` — TTL expires automatically; agent calls `POST /agents/{id}/endpoints/{eid}/renew` on each invocation.
- `Persistent` — agent calls `POST /agents/{id}/endpoints/{eid}/heartbeat`; grace period is 2.5× `heartbeatIntervalSeconds`.

**Protocol metadata round-trips as raw JSON.** Protocol-specific fields (A2A skill schemas, MCP tool descriptors, ACP content types) are stored in `Endpoint.ProtocolMetadata` as a JSONB column. Mappers serialize in and deserialize out — nothing protocol-specific reaches the domain model.

**Authentication uses a "Smart" policy scheme.** `X-Api-Key` header → `ApiKeyAuthenticationHandler` (validates against Postgres hash, sets `registry_scope` claim). `Authorization: Bearer` → standard `JwtBearer`. The registry does **not** issue JWTs; it validates tokens from an external IdP. Auth policies (`AdminOnly`, `AgentOrAdmin`) in `RegistryPolicies.cs` check `registry_scope` claim or `roles` claim.

**Central Package Management** is enabled (`Directory.Packages.props`). Add `<PackageVersion>` entries there; use `<PackageReference>` without `Version` in project files. `tests/Directory.Build.props` chains to the root and auto-includes `GitHubActionsTestLogger` + enables `UseMicrosoftTestingPlatformRunner` for all test projects.

## Testing

Integration tests use `WebApplicationFactory<Program>` (`AgentRegistryFactory`). Infrastructure dependencies are replaced with in-memory fakes:
- `InMemoryAgentRepository` — thread-safe `ConcurrentDictionary`
- `InMemoryLivenessStore` — in-memory equivalent of Redis TTL store
- `FakeApiKeyService` — exposes `FakeApiKeyService.AdminKey` and `FakeApiKeyService.AgentKey` constants

Use `factory.CreateAdminClient()` or `factory.CreateAgentClient()` to get pre-authenticated `HttpClient` instances. Call `factory.Reset()` in `Dispose()` to clear state between tests. Tests are `IClassFixture<AgentRegistryFactory>`.

Application-layer tests use `Rocks` (source-generator mocks) to mock `IAgentRepository` and `ILivenessStore`.

## Adding a new protocol adapter

1. Add a `Protocols/<Name>/` folder under `AgentRegistry.Api` with:
   - `Models/` — request/response records matching the protocol's wire format
   - `<Name>AgentManifestMapper.cs` (or equivalent) — bidirectional mapping to/from domain types using `Endpoint.ProtocolMetadata` for protocol-specific fields
   - `<Name>Endpoints.cs` — minimal API route registrations with `.WithTags()`, `.WithSummary()`, `.WithDescription()`, `.Produces<T>()`, and `.ProducesProblem()` on every route
2. Call `Map<Name>Endpoints()` from `Program.cs`
3. Add integration tests under `tests/AgentRegistry.Api.Tests/Protocols/<Name>/`
