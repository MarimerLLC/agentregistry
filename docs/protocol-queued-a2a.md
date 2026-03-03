# Queued A2A Protocol Adapter

## What is Queued A2A?

Queued A2A describes agents that use the **A2A wire protocol over an async message broker** instead of HTTP. The message format — task requests, status updates, results — is identical to standard A2A. What changes is the transport: callers publish messages to a queue or topic on a broker (RabbitMQ, Azure Service Bus) and receive responses asynchronously on a reply topic, rather than making a synchronous HTTP call.

This pattern is common in:

- **KEDA-scaled workers** — agents that are scaled to zero when idle. A KEDA trigger watches the queue depth; the worker spins up when messages arrive.
- **Long-running tasks** — research, code generation, batch processing where a 30-second HTTP timeout is impractical.
- **Decoupled pipelines** — agent chains where intermediate results pass through a broker, enabling retries, dead-lettering, and fan-out without tight coupling between agents.

The rockbot project demonstrates this pattern concretely: `SampleAgent` and `ResearchAgent` run as KEDA-scalable workers, communicate via RabbitMQ, and use A2A message shapes for all task exchange.

## Supported broker technologies

| Technology | `TransportType` | Key fields |
|---|---|---|
| RabbitMQ (AMQP 0-9-1) | `Amqp` | `host`, `port`, `virtualHost`, `exchange`, `taskTopic` |
| Azure Service Bus | `AzureServiceBus` | `namespace`, `entityPath`, `taskTopic` |

## The QueuedAgentCard

The discovery and registration format is a `QueuedAgentCard` — an A2A agent card extended with a `queueEndpoint` object:

```json
{
  "name": "ResearchAgent",
  "description": "On-demand research agent using web search and page fetching",
  "version": "1.0",
  "skills": [
    {
      "id": "research",
      "name": "Research",
      "description": "Research a topic using web search",
      "tags": ["search", "web"]
    }
  ],
  "defaultInputModes": ["application/json"],
  "defaultOutputModes": ["application/json"],
  "queueEndpoint": {
    "technology": "rabbitmq",
    "host": "rabbitmq.example.com",
    "port": 5672,
    "virtualHost": "/",
    "exchange": "rockbot",
    "taskTopic": "agent.task.ResearchAgent",
    "responseTopic": "agent.response.{callerName}"
  },
  "id": "3fa85f64-...",
  "isLive": true
}
```

### `queueEndpoint` fields

| Field | Required | Description |
|---|---|---|
| `technology` | Yes | `"rabbitmq"` or `"azure-service-bus"` |
| `taskTopic` | Yes | Routing key or topic path that callers publish task messages to |
| `host` | RabbitMQ | Broker hostname (e.g. `rabbitmq.example.com`) |
| `port` | No | Broker port; defaults to 5672 (AMQP) or 5671 (AMQPS) if omitted |
| `virtualHost` | No | AMQP virtual host; typically `/` |
| `exchange` | No | AMQP exchange name (topic exchange, e.g. `rockbot`) |
| `responseTopic` | No | Pattern callers subscribe to for responses (e.g. `agent.response.{callerName}`) |
| `namespace` | Azure SB | Fully-qualified Service Bus namespace (e.g. `mybus.servicebus.windows.net`) |
| `entityPath` | Azure SB | Service Bus queue or topic path |

### Registry-added fields

`id` and `isLive` are added by the registry on discovery responses. Omit them when registering.

## API endpoints

| Method | Path | Auth | Description |
|---|---|---|---|
| `POST` | `/a2a/async/agents` | Agent or Admin | Register an agent with queue endpoint details |
| `GET` | `/a2a/async/agents` | Public | List / discover queued agents (paginated) |
| `GET` | `/a2a/async/agents/{id}` | Public | Retrieve a specific queued agent card |

The list endpoint supports the same query parameters as other protocol list endpoints: `capability`, `tags`, `liveOnly` (default `true`), `page`, `pageSize` (max 100).

## Design decisions

### ProtocolType stays A2A

The A2A message shapes (task request, status update, result) are unchanged. Only the transport layer differs. Using a new `ProtocolType` would fragment discovery — a consumer searching for A2A agents would miss queue-backed ones. Keeping `ProtocolType.A2A` means `GET /discover/agents?protocol=A2A` returns both HTTP and queued A2A agents.

`TransportType` distinguishes them: `Http` for classic A2A over HTTP, `Amqp` for RabbitMQ, `AzureServiceBus` for Azure.

### Separate adapter from the HTTP A2A adapter

The HTTP A2A adapter serves and accepts standard A2A agent cards where `supportedInterfaces[].url` is an HTTP URL. For queued agents, the relevant connection information (exchange, routing key, virtual host) doesn't fit cleanly into a URL field. A dedicated `/a2a/async/agents` surface with a `queueEndpoint` object is clearer for consumers than trying to encode broker details into a URL.

The existing `GET /a2a/agents/{id}` endpoint continues to work for HTTP A2A agents and will return a synthetic placeholder URL for any non-HTTP endpoints it encounters. The dedicated queued endpoint is the intended surface for agents that are truly queue-native.

### Connection details in ProtocolMetadata

All `queueEndpoint` fields and the full card (version, skills, I/O modes) are serialised into `Endpoint.ProtocolMetadata` at registration time. On discovery, the mapper deserialises them to reconstruct the original card exactly. This is the same round-trip strategy used by A2A, MCP, and ACP — no domain model changes required for new fields.

`Endpoint.Address` holds `taskTopic` — the routing key / entity path where callers publish. This is the primary "address" of the agent from the registry's perspective, analogous to an HTTP URL.

### No credentials in the card

Connection strings with usernames, passwords, or SAS tokens are not stored. The card contains only the structural connection details (host, port, exchange, topic). Callers are expected to hold broker credentials separately — via Kubernetes secrets, Azure Key Vault, or equivalent — and combine them with the structural details from the registry.

### Skills and domain capabilities

Skills in the `QueuedAgentCard` map directly to registry capabilities, enabling queued agents to be discovered through the generic `GET /discover/agents?capability=research` endpoint alongside HTTP agents. When an agent registers with multiple skills, all skills become capabilities in the domain model.

On discovery, the stored skills are returned verbatim (preserving original string IDs like `"research"` rather than internal Guid IDs) because they are read from `ProtocolMetadata`, not reconstructed from capabilities.

### isLive reflects heartbeat state

Queued agents typically use the `Persistent` liveness model and call `POST /agents/{id}/endpoints/{eid}/heartbeat` periodically. KEDA-scaled workers may use `Ephemeral` liveness — registering when a pod starts, calling `POST /agents/{id}/endpoints/{eid}/renew` on each task invocation, and relying on TTL expiry when the pod scales to zero.

`isLive: false` does not mean the agent's queue is unavailable — it means the registry has not seen a heartbeat or renewal within the grace period. Consumers can choose to route work to stale endpoints if they know the queue is durable.

## Registration flows

### RabbitMQ agent

```json
POST /a2a/async/agents
Authorization: X-Api-Key: ar_...

{
  "name": "ResearchAgent",
  "description": "On-demand research agent",
  "version": "1.0",
  "skills": [
    { "id": "research", "name": "Research", "description": "Researches a topic", "tags": ["search"] }
  ],
  "defaultInputModes": ["application/json"],
  "defaultOutputModes": ["application/json"],
  "queueEndpoint": {
    "technology": "rabbitmq",
    "host": "rabbitmq.prod.example.com",
    "port": 5672,
    "virtualHost": "/",
    "exchange": "rockbot",
    "taskTopic": "agent.task.ResearchAgent",
    "responseTopic": "agent.response.{callerName}"
  }
}
```

Response `201 Created`:
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "ResearchAgent",
  "description": "On-demand research agent",
  "version": "1.0",
  "skills": [{ "id": "research", "name": "Research", "description": "Researches a topic", "tags": ["search"] }],
  "defaultInputModes": ["application/json"],
  "defaultOutputModes": ["application/json"],
  "queueEndpoint": {
    "technology": "rabbitmq",
    "host": "rabbitmq.prod.example.com",
    "port": 5672,
    "virtualHost": "/",
    "exchange": "rockbot",
    "taskTopic": "agent.task.ResearchAgent",
    "responseTopic": "agent.response.{callerName}"
  },
  "isLive": true
}
```

### Azure Service Bus agent

```json
POST /a2a/async/agents
{
  "name": "InvoiceProcessor",
  "description": "Processes invoice documents",
  "version": "1.0",
  "skills": [
    { "id": "process-invoice", "name": "Process Invoice", "description": "Extracts and validates invoice data", "tags": ["finance", "ocr"] }
  ],
  "defaultInputModes": ["application/json", "application/pdf"],
  "defaultOutputModes": ["application/json"],
  "queueEndpoint": {
    "technology": "azure-service-bus",
    "namespace": "mybus.servicebus.windows.net",
    "entityPath": "invoice-processor-tasks",
    "taskTopic": "invoice-processor-tasks",
    "responseTopic": "invoice-processor-responses"
  }
}
```

### Keeping liveness alive (KEDA worker pattern)

An ephemeral, KEDA-scaled worker registers on pod startup and renews on each task:

```bash
# On pod startup — register with 5-minute TTL
curl -X POST https://registry.example.com/a2a/async/agents \
  -H "X-Api-Key: $REGISTRY_KEY" \
  -d '{ "name": "ResearchAgent", ..., "livenessModel": "Ephemeral", "ttlSeconds": 300 }'

# Capture the endpoint ID from the response, then on each task invocation:
curl -X POST https://registry.example.com/agents/$AGENT_ID/endpoints/$ENDPOINT_ID/renew \
  -H "X-Api-Key: $REGISTRY_KEY"
```

When the pod scales to zero, the TTL expires and the agent is no longer returned in `liveOnly=true` queries. The queue address in the card remains valid — KEDA will scale a new pod when messages arrive.

For persistent workers, use `heartbeatIntervalSeconds` instead and call `POST .../heartbeat` on schedule.

## Discovering queued agents

```bash
# All live queued A2A agents
GET /a2a/async/agents

# Filter by capability
GET /a2a/async/agents?capability=research&liveOnly=true

# Filter by tag
GET /a2a/async/agents?tags=finance,ocr
```

Discovery returns a paginated list:

```json
{
  "agents": [...],
  "totalCount": 12,
  "page": 1,
  "pageSize": 20,
  "totalPages": 1,
  "hasNextPage": false
}
```

Queued agents also appear in the generic discovery endpoint (`GET /discover/agents?protocol=A2A`) alongside HTTP A2A agents, since they share `ProtocolType.A2A`.
