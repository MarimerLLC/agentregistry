# AgentRegistry

A protocol-agnostic registry service for AI agents. AgentRegistry lets agents discover each other without knowing in advance how they communicate or where they run.

## What it does

Modern AI systems increasingly involve multiple agents collaborating — a summarizer talking to a search agent, a workflow orchestrator delegating to specialist tools. These agents may speak different protocols (A2A, MCP, ACP), run on different transports (HTTP, RabbitMQ, Azure Service Bus), and spin up ephemerally on-demand rather than running as always-on services.

AgentRegistry is the connective tissue. An agent registers itself when it starts, declares what it can do and how to reach it, and renews its registration while it's running. Consumers query the registry to find agents by capability, protocol, or transport — and only see agents that are currently reachable.

### Key design points

- **Protocol-agnostic** — agents declare their protocol (A2A, MCP, ACP, or custom) per endpoint. The registry stores and filters by protocol but doesn't speak any of them.
- **Transport-agnostic** — HTTP endpoints and queue-based endpoints (AMQP, Azure Service Bus) are first-class. A queue-backed agent doesn't need to be running when discovered; the queue address is the endpoint.
- **Ephemeral-native** — two liveness models coexist. *Ephemeral* agents (Azure Functions, KEDA-scaled workers) register with a TTL and renew it on each invocation. *Persistent* agents (long-lived pods, services) send periodic heartbeats. Both are stored uniformly in Redis as TTL keys.
- **Discovery is public; management is authenticated** — `GET /discover/agents` requires no credentials. Registration, heartbeating, and key management require an API key or JWT.

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                       AgentRegistry.Api                         │
│  ASP.NET Core 10 · Minimal APIs · Scalar UI · OpenTelemetry     │
│  Auth: API key (Admin/Agent scopes) + JWT Bearer                │
│  Protocol adapters: A2A · MCP                                   │
└────────────┬───────────────────────┬────────────────────────────┘
             │                       │
┌────────────▼───────┐   ┌───────────▼────────────────────────────┐
│ AgentRegistry.     │   │ AgentRegistry.Infrastructure            │
│ Application        │   │  SqlAgentRepository (EF Core + Npgsql)  │
│  AgentService      │   │  RedisLivenessStore                     │
│  IApiKeyService    │   │  SqlApiKeyService                       │
│  IAgentRepository  │   │  PostgreSQL · Redis                     │
│  ILivenessStore    │   └────────────────────────────────────────-┘
└────────────┬───────┘
             │
┌────────────▼───────┐
│ AgentRegistry.     │
│ Domain             │
│  Agent · Endpoint  │
│  Capability        │
│  ApiKey · Scope    │
└────────────────────┘
```

**Storage**

| Concern | Store |
|---|---|
| Agent identity, capabilities, endpoint metadata | PostgreSQL (EF Core) |
| Endpoint liveness (TTL-based) | Redis |
| API keys (hashed, never plaintext) | PostgreSQL |

**Observability** — OpenTelemetry traces and metrics; Serilog structured logging to console with OTLP export when `Otel:Endpoint` is configured.

## Protocol support

Detailed design rationale for each adapter is in [`/docs`](docs/):

- [A2A adapter design](docs/protocol-a2a.md)
- [MCP adapter design](docs/protocol-mcp.md)
- [ACP adapter design](docs/protocol-acp.md)

### A2A (Agent-to-Agent)

Targets the [A2A v1.0 RC spec](https://a2a-protocol.org/). The registry serves A2A agent cards and accepts A2A-native registration.

- `GET /.well-known/agent.json` — the registry's own A2A agent card
- `GET /a2a/agents/{id}` — agent card for any registered A2A agent (public)
- `POST /a2a/agents` — register by submitting an A2A agent card directly (Agent or Admin)

Agent capabilities map to A2A skills. Protocol-specific fields (streaming capability, security schemes, provider, icon URL, etc.) round-trip through `Endpoint.ProtocolMetadata` so nothing is lost.

### MCP (Model Context Protocol)

Targets the [MCP spec 2025-11-25](https://modelcontextprotocol.io/), **Streamable HTTP transport only** — the deprecated HTTP+SSE transport (2024-11-05) and stdio are not supported.

**The registry is itself an MCP server.** Any MCP-capable model or agent can connect at `POST /mcp` and use five built-in tools to discover agents: `discover_agents`, `get_agent`, `get_a2a_card`, `get_mcp_server_card`, and `get_acp_manifest`. This means an AI model can ask the registry "find me a live summarisation agent that speaks A2A" and get a usable answer without any human in the loop.

The registry also acts as a discovery service for other MCP servers — storing their server cards and exposing them for lookup:

- `POST /mcp` — the registry's own MCP server endpoint (Streamable HTTP, public)
- `GET /mcp/servers/{id}` — MCP server card for a registered server (public)
- `GET /mcp/servers` — filtered list of MCP server cards (public)
- `POST /mcp/servers` — register by submitting an MCP server card directly (Agent or Admin)

Tool, resource, and prompt descriptors (including JSON Schema) round-trip through `Endpoint.ProtocolMetadata`. The `isLive` field on returned cards reflects real-time Redis liveness.

### ACP (Agent Communication Protocol)

Targets [ACP spec 0.2.0](https://github.com/i-am-bee/acp) (IBM Research / BeeAI). ACP was absorbed into A2A under Linux Foundation governance in August 2025 but remains widely deployed. Both protocols are supported concurrently.

- `GET /acp/agents/{id}` — ACP agent manifest for a registered agent (public)
- `GET /acp/agents` — filtered list of ACP manifests, with optional `domain` filter (public)
- `POST /acp/agents` — register by submitting an ACP manifest + endpoint URL (Agent or Admin)

The manifest carries MIME-typed content types, JSON Schema for input/output/config/thread state, performance status metrics, and rich metadata (framework, natural languages, license, author). All fields round-trip through `Endpoint.ProtocolMetadata`. Agent names are normalised to RFC 1123 DNS-label format on manifest generation.

### Generic (protocol-agnostic)

All protocols can also be registered and discovered through the generic API, which returns the registry's own domain model rather than protocol-native card formats.

- `POST /agents` — register with explicit `protocol` and `transport` fields
- `GET /discover/agents?protocol=MCP&transport=Http` — filter by any combination

## Prerequisites

- .NET 10 SDK
- PostgreSQL 14+
- Redis 7+
- `dotnet-ef` tool: `dotnet tool install --global dotnet-ef`

## Getting started

### 1. Clone and restore

```bash
git clone https://github.com/MarimerLLC/agentregistry
cd AgentRegistry
dotnet restore
```

### 2. Set connection strings

Connection strings are managed with [.NET user secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) to keep credentials out of source control.

```bash
dotnet user-secrets set "ConnectionStrings:Postgres" \
  "Host=<host>;Port=5432;Database=agentregistry;Username=agentregistry;Password=<password>" \
  --project src/AgentRegistry.Api

dotnet user-secrets set "ConnectionStrings:Redis" \
  "<host>:6379,password=<password>" \
  --project src/AgentRegistry.Api
```

### 3. Create the database and run migrations

```bash
# Create the database (once)
psql -U postgres -c "CREATE DATABASE agentregistry;"
psql -U postgres -d agentregistry -c "CREATE USER agentregistry WITH PASSWORD '<password>';"
psql -U postgres -d agentregistry -c "GRANT ALL ON SCHEMA public TO agentregistry;"

# Apply migrations
dotnet ef database update -p src/AgentRegistry.Infrastructure -s src/AgentRegistry.Api
```

If you can't reach PostgreSQL directly, apply via the pod:

```bash
dotnet ef migrations script --idempotent \
  -p src/AgentRegistry.Infrastructure -s src/AgentRegistry.Api \
  -o /tmp/migration.sql

kubectl cp /tmp/migration.sql <namespace>/<postgres-pod>:/tmp/migration.sql
kubectl exec -n <namespace> <postgres-pod> -- psql -U postgres -d agentregistry -f /tmp/migration.sql
```

### 4. Run

```bash
dotnet run --project src/AgentRegistry.Api
```

The API is available at `http://localhost:5000`. Scalar API explorer: `http://localhost:5000/scalar/v1`.

### 5. Get your first Admin key

On first run, no API keys exist. Bootstrap one using a pre-shared token:

**Step 1.** Set `Bootstrap:Token` — a secret string you choose — in user secrets (locally) or a Kubernetes secret (production):

```bash
dotnet user-secrets set "Bootstrap:Token" "your-one-time-bootstrap-token" \
  --project src/AgentRegistry.Api
```

**Step 2.** Call the bootstrap endpoint:

```bash
curl -X POST http://localhost:5000/api-keys/bootstrap \
  -H "X-Bootstrap-Token: your-one-time-bootstrap-token" \
  -H "Content-Type: application/json" \
  -d '{"ownerId": "platform-team", "description": "Initial admin key"}'
```

The response contains the raw key — **copy it now, it is never shown again**:

```json
{
  "id": "...",
  "ownerId": "platform-team",
  "scope": "Admin",
  "keyPrefix": "ar_Abc123",
  "rawKey": "ar_Abc123DefGhi...",
  "createdAt": "..."
}
```

**Step 3.** Remove `Bootstrap:Token` from configuration. The endpoint returns `404` when the token is absent, making it permanently dark.

### 6. Issue Agent keys

With your Admin key, issue scoped keys for agents and systems:

```bash
# Issue an Agent-scoped key (can register/heartbeat, cannot manage keys)
curl -X POST http://localhost:5000/api-keys \
  -H "X-Api-Key: ar_Abc123DefGhi..." \
  -H "Content-Type: application/json" \
  -d '{"description": "summarizer-agent", "scope": "Agent"}'
```

## Authorization

### Scopes

| Scope | Who has it | What it can do |
|---|---|---|
| `Admin` | Platform operators | Issue/list/revoke keys, register agents, full discovery |
| `Agent` | Individual agents and services | Register agents, heartbeat/renew, discovery |

### Auth schemes

The registry accepts two authentication methods, selected by header:

- **API key** — `X-Api-Key: ar_...` header. Scope comes from the key record in the database.
- **JWT Bearer** — standard `Authorization: Bearer <token>` header. The token must carry one of:
  - A `registry_scope` claim with value `Admin` or `Agent`
  - A `roles` claim with value `registry.admin` or `registry.agent`

Discovery, the MCP server endpoint, and protocol card endpoints (`/discover/agents`, `/mcp`, `/a2a/agents/*`, `/mcp/servers/*`) are always public — no auth required.

## API overview

### Generic agent management

| Method | Path | Auth | Description |
|---|---|---|---|
| `POST` | `/agents` | Agent or Admin | Register an agent |
| `GET` | `/agents/{id}` | Agent or Admin | Get agent with liveness status |
| `PUT` | `/agents/{id}` | Agent or Admin (owner) | Update agent metadata |
| `DELETE` | `/agents/{id}` | Agent or Admin (owner) | Deregister agent |
| `POST` | `/agents/{id}/endpoints` | Agent or Admin (owner) | Add an endpoint |
| `DELETE` | `/agents/{id}/endpoints/{eid}` | Agent or Admin (owner) | Remove an endpoint |
| `POST` | `/agents/{id}/endpoints/{eid}/heartbeat` | Agent or Admin (owner) | Persistent liveness reset |
| `POST` | `/agents/{id}/endpoints/{eid}/renew` | Agent or Admin (owner) | Ephemeral TTL renewal |
| `GET` | `/discover/agents` | Public | Discover live agents |

### A2A protocol

| Method | Path | Auth | Description |
|---|---|---|---|
| `GET` | `/.well-known/agent.json` | Public | Registry's own A2A agent card |
| `GET` | `/a2a/agents/{id}` | Public | A2A agent card for a registered agent |
| `POST` | `/a2a/agents` | Agent or Admin | Register by submitting an A2A agent card |

### MCP protocol

| Method | Path | Auth | Description |
|---|---|---|---|
| `POST/GET` | `/mcp` | Public | Registry's own MCP server (Streamable HTTP) |
| `GET` | `/mcp/servers/{id}` | Public | MCP server card for a registered server |
| `GET` | `/mcp/servers` | Public | Filtered list of MCP server cards |
| `POST` | `/mcp/servers` | Agent or Admin | Register by submitting an MCP server card |

### ACP protocol

| Method | Path | Auth | Description |
|---|---|---|---|
| `GET` | `/acp/agents/{id}` | Public | ACP agent manifest for a registered agent |
| `GET` | `/acp/agents` | Public | Filtered list of ACP agent manifests |
| `POST` | `/acp/agents` | Agent or Admin | Register by submitting an ACP agent manifest |

### Key management and system

| Method | Path | Auth | Description |
|---|---|---|---|
| `POST` | `/api-keys` | Admin | Issue a new API key |
| `GET` | `/api-keys` | Admin | List your API keys |
| `DELETE` | `/api-keys/{id}` | Admin | Revoke an API key |
| `POST` | `/api-keys/bootstrap` | Bootstrap token | Issue first Admin key |
| `GET` | `/healthz` | Public | Liveness probe |
| `GET` | `/readyz` | Public | Readiness probe (checks Postgres + Redis) |
| `GET` | `/scalar/v1` | Public | Interactive API explorer |
| `GET` | `/openapi/v1.json` | Public | OpenAPI spec |

Full interactive docs are available at `/scalar/v1` when the service is running.

## Liveness models

Agents choose their liveness model per endpoint at registration time.

**Ephemeral** — suited to serverless / KEDA-scaled workloads that spin up per-job:

```json
{
  "livenessModel": "Ephemeral",
  "ttlSeconds": 300
}
```

The registration expires after `ttlSeconds`. The agent calls `POST /agents/{id}/endpoints/{eid}/renew` to extend it. If the agent stops running, the registration expires automatically with no cleanup needed.

**Persistent** — suited to long-lived pods or services:

```json
{
  "livenessModel": "Persistent",
  "heartbeatIntervalSeconds": 30
}
```

The agent calls `POST /agents/{id}/endpoints/{eid}/heartbeat` every `heartbeatIntervalSeconds`. The registry grants a 2.5× grace period before marking the endpoint stale.

Both models are stored uniformly in Redis as TTL keys. Discovery queries SQL and filters for live endpoints in a single batched Redis call.

## Queue-backed agents

Agents using AMQP or Azure Service Bus don't need to be running when discovered. The registry stores the queue address as the endpoint:

```json
{
  "name": "async-processor",
  "transport": "AzureServiceBus",
  "protocol": "A2A",
  "address": "agents/summarizer/requests",
  "livenessModel": "Ephemeral",
  "ttlSeconds": 60
}
```

A KEDA-scaled worker registers on startup, processes jobs, and the TTL expires naturally when the scaling group idles. Consumers route work to the queue address — whether the worker is currently running or not is KEDA's concern.

## Configuration reference

| Key | Description | Default |
|---|---|---|
| `ConnectionStrings:Postgres` | Npgsql connection string | Required in production |
| `ConnectionStrings:Redis` | StackExchange.Redis connection string | Required in production |
| `Database:AutoMigrate` | Apply pending migrations on startup | `false` |
| `Bootstrap:Token` | Enables `POST /api-keys/bootstrap` when set | Unset (endpoint is 404) |
| `Jwt:Authority` | OIDC authority for JWT Bearer validation | Optional |
| `Jwt:Audience` | Expected JWT audience | `agentregistry` |
| `Otel:Endpoint` | OTLP gRPC endpoint for traces and metrics | Optional |

## Kubernetes deployment

Manifests are in `k8s/`:

```bash
# Apply config (non-sensitive)
kubectl apply -f k8s/agentregistry/configmap.yaml

# Create the secret (do not commit real values)
kubectl create secret generic agentregistry-secrets \
  --from-literal=ConnectionStrings__Postgres="Host=...;Database=agentregistry;Username=agentregistry;Password=..." \
  --from-literal=ConnectionStrings__Redis="...:6379,password=..." \
  --from-literal=Bootstrap__Token="your-one-time-bootstrap-token"

# Deploy
kubectl apply -f k8s/agentregistry/deployment.yaml
kubectl apply -f k8s/agentregistry/service.yaml
```

The service is exposed via Tailscale (annotated `tailscale.com/expose: "true"`). Remove `Bootstrap__Token` from the secret after issuing your first Admin key.

## Development

```bash
# Run all tests
dotnet test

# Add a migration after model changes
dotnet ef migrations add <Name> -p src/AgentRegistry.Infrastructure -s src/AgentRegistry.Api

# Apply migrations (uses AGENTREGISTRY_DB env var or falls back to user secrets)
export AGENTREGISTRY_DB="Host=...;Database=agentregistry;..."
dotnet ef database update -p src/AgentRegistry.Infrastructure -s src/AgentRegistry.Api

# Build Docker image
docker build -t agentregistry:latest .
```

### Project structure

```
src/
  AgentRegistry.Domain/         Pure domain model — no external dependencies
  AgentRegistry.Application/    Use cases, interfaces, service logic
  AgentRegistry.Infrastructure/ EF Core (PostgreSQL), Redis, SQL API key service
  AgentRegistry.Api/            ASP.NET Core 10 minimal API, auth, Scalar
    Protocols/
      A2A/                      A2A v1.0 RC agent card adapter
      MCP/                      MCP 2025-11-25 server card adapter (Streamable HTTP)
      ACP/                      ACP 0.2.0 agent manifest adapter
tests/
  AgentRegistry.Domain.Tests/       Domain unit tests
  AgentRegistry.Application.Tests/  Service tests using Rocks source-gen mocks
  AgentRegistry.Api.Tests/          Integration tests via WebApplicationFactory
    Protocols/
      A2A/                          A2A endpoint tests
      MCP/                          MCP endpoint tests
      ACP/                          ACP endpoint tests
k8s/
  redis.yaml                    Redis StatefulSet + Service
  agentregistry/
    configmap.yaml              Non-sensitive configuration
    secret.example.yaml         Secret template — create imperatively, do not commit
    deployment.yaml             Deployment with liveness/readiness probes
    service.yaml                LoadBalancer with Tailscale annotation
```
