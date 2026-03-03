# Async Messaging Support Plan

## Background

The A2A, MCP, and ACP protocol adapters in this registry assume synchronous HTTP transport.
The rockbot project demonstrates a different pattern: agents communicate via A2A **over async
message brokers** (RabbitMQ, Azure Service Bus, etc.), using fire-and-forget task publication
with a correlated async response channel.

This plan adds first-class support for registering and discovering agents that are reachable
via a message queue rather than an HTTP endpoint.

## Design Decisions

### Reuse `ProtocolType.A2A`, add a new adapter

The wire protocol between agents (task request/status/result message shapes) is still A2A.
What changes is the **transport**: instead of `POST /` over HTTP, callers publish a JSON
message to an exchange/topic on a broker.

Rather than shoehorn queue connection details into the existing HTTP-oriented A2A adapter,
we add a dedicated **`Protocols/QueuedA2A/`** adapter with its own models and API surface.

### Technology-agnostic model

Supported queue technologies (first release):

| Technology       | `TransportType`          | Key connection fields                              |
|------------------|--------------------------|----------------------------------------------------|
| RabbitMQ / AMQP  | `Amqp`                   | host, port, virtualHost, exchange, taskRoutingKey  |
| Azure Service Bus| `AzureServiceBus`        | namespace (FQDN), entityPath                       |

No domain enum changes are required; `TransportType.Amqp` and
`TransportType.AzureServiceBus` already exist.

### Connection details stored in `ProtocolMetadata`

All broker-specific fields are stored as JSONB in `Endpoint.ProtocolMetadata`, following the
same round-trip pattern used by the other adapters. The `Endpoint.Address` field carries the
primary queue/topic name (the address a client publishes task messages to).

### New API surface

```
POST /a2a/async/agents              Register an agent with queue endpoint details
GET  /a2a/async/agents              List/discover queued agents (filterable)
GET  /a2a/async/agents/{id}         Retrieve a specific queued agent card
```

Discovery endpoints are public. Registration requires `AgentOrAdmin` auth.

---

## Queued Agent Card Model

```jsonc
{
  // Standard A2A fields
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "ResearchAgent",
  "description": "On-demand research agent using web search",
  "version": "1.0",
  "skills": [
    { "id": "research", "name": "Research", "description": "...", "tags": ["search"] }
  ],
  "defaultInputModes":  ["application/json"],
  "defaultOutputModes": ["application/json"],

  // Queue endpoint (required for registration, returned on discovery)
  "queueEndpoint": {
    "technology": "rabbitmq",          // "rabbitmq" | "azure-service-bus"
    "host": "rabbitmq.example.com",    // broker host (omit for Azure SB — use namespace)
    "port": 5672,                      // optional; defaults: AMQP=5672, AMQPS=5671
    "virtualHost": "/",                // AMQP virtual host (default "/")
    "exchange": "rockbot",             // AMQP exchange name (topic exchange)
    "taskTopic": "agent.task.ResearchAgent",   // routing key callers publish tasks to
    "responseTopic": "agent.response.{callerName}", // pattern callers subscribe to for results
    "namespace": null,                 // Azure SB namespace (e.g. "mybus.servicebus.windows.net")
    "entityPath": null                 // Azure SB queue or topic path
  },

  // Liveness (set by registry on discovery)
  "isLive": true
}
```

---

## File Changes

### New files

```
src/MarimerLLC.AgentRegistry.Api/Protocols/QueuedA2A/
  Models/
    QueuedAgentCard.cs          # top-level card (mirrors A2A card + queueEndpoint)
    QueueEndpoint.cs            # queue connection details
  QueuedA2AMapper.cs            # Domain ↔ QueuedAgentCard, ProtocolMetadata storage
  QueuedA2AEndpoints.cs         # minimal API route registrations

tests/AgentRegistry.Api.Tests/Protocols/QueuedA2A/
  QueuedA2AEndpointTests.cs     # integration tests (WebApplicationFactory)
```

### Modified files

| File | Change |
|------|--------|
| `src/MarimerLLC.AgentRegistry.Api/Program.cs` | Call `app.MapQueuedA2AEndpoints()` |

---

## Implementation Tasks

### Task 1 — Models (`QueuedAgentCard`, `QueueEndpoint`)

Create `Protocols/QueuedA2A/Models/`:

- **`QueueEndpoint`** record:
  - `Technology` (string — "rabbitmq" | "azure-service-bus")
  - `Host` (string?)
  - `Port` (int?)
  - `VirtualHost` (string?) — AMQP
  - `Exchange` (string?) — AMQP
  - `TaskTopic` (string) — routing key / entity path clients publish to
  - `ResponseTopic` (string?) — pattern for caller response subscription
  - `Namespace` (string?) — Azure SB FQDN
  - `EntityPath` (string?) — Azure SB queue or topic

- **`QueuedAgentCard`** record (mirrors A2A card fields relevant to queued agents):
  - `Id`, `Name`, `Description`, `Version`
  - `Skills` (list of `AgentSkill` — reuse from A2A models)
  - `DefaultInputModes`, `DefaultOutputModes`
  - `QueueEndpoint` (required on registration; present on discovery)
  - `IsLive` (bool, set by registry on discovery)

### Task 2 — Mapper (`QueuedA2AMapper`)

```
FromCard(QueuedAgentCard) → MappedRegistration
  - capabilities: each skill → RegisterCapabilityRequest
  - endpoint:
      Transport = technology == "azure-service-bus" ? AzureServiceBus : Amqp
      Protocol  = ProtocolType.A2A
      Address   = card.QueueEndpoint.TaskTopic
      LivenessModel = Persistent
      HeartbeatInterval = 30s
      ProtocolMetadata = JSON of StoredQueuedA2AMetadata (all card + queueEndpoint fields)

ToCard(AgentWithLiveness) → QueuedAgentCard?
  - filter endpoints: Protocol == A2A && Transport != Http
  - deserialize ProtocolMetadata → StoredQueuedA2AMetadata
  - reconstruct QueuedAgentCard faithfully; set IsLive from LiveEndpointIds
```

### Task 3 — Endpoints (`QueuedA2AEndpoints`)

Follow the ACP endpoints pattern closely:

```csharp
POST /a2a/async/agents   (AgentOrAdmin auth)
GET  /a2a/async/agents   (public, paginated; ?capability=&tags=&liveOnly=&page=&pageSize=)
GET  /a2a/async/agents/{id}   (public)
```

Every route decorated with `.WithTags("QueuedA2A")`, `.WithSummary()`, `.WithDescription()`,
`.Produces<T>()`, `.ProducesProblem()`.

Discovery filter: `Protocol = A2A`, `Transport = Amqp | AzureServiceBus` (or filter in the
mapper by checking technology stored in metadata).

### Task 4 — Wire up in `Program.cs`

Add `app.MapQueuedA2AEndpoints();` after the other `Map*Endpoints()` calls.

### Task 5 — Integration tests

`tests/AgentRegistry.Api.Tests/Protocols/QueuedA2A/QueuedA2AEndpointTests.cs`:

- `Register_WithRabbitMQ_Returns201_WithId`
- `Register_WithAzureServiceBus_Returns201_WithId`
- `Register_MissingTaskTopic_Returns400`
- `Register_Unauthenticated_Returns401`
- `GetCard_ReturnsRegisteredCard_WithQueueEndpoint`
- `GetCard_UnknownId_Returns404`
- `ListCards_FiltersToQueuedAgents`
- `ListCards_LiveOnly_ExcludesDeadEndpoints`
- `RoundTrip_AllFields_PreservedOnDiscovery`

---

## Open Questions / Out of Scope (v1)

- **Kafka** transport — `TransportType` would need a new `Kafka` value; deferred.
- **TLS/auth parameters** — connection strings with credentials are sensitive; v1 omits them
  from the card (clients are expected to have credentials out of band, as they do in rockbot).
- **Queued endpoint heartbeat/renew** — the standard `POST /agents/{id}/endpoints/{eid}/heartbeat`
  route works unchanged; no new liveness plumbing needed.
- **Discovery endpoint that returns *both* HTTP and queued endpoints** — callers use the
  existing generic `/agents` discovery or query protocol-specific endpoints; no aggregated view needed in v1.
