# MCP Protocol Adapter

## What is MCP?

Model Context Protocol (MCP) is an open standard developed by Anthropic for connecting AI models to data sources, tools, and services. An MCP server exposes three kinds of capabilities:

- **Tools** — functions the model can call (with JSON Schema-typed inputs and outputs)
- **Resources** — data sources the model can read (files, database rows, live data)
- **Prompts** — reusable prompt templates with typed arguments

The spec is maintained at [modelcontextprotocol.io](https://modelcontextprotocol.io/). This implementation targets **spec 2025-11-25**.

## Transport: Streamable HTTP only

MCP defines three transports:

| Transport | Status | Supported |
|---|---|---|
| stdio | Active | No — not applicable to a registry |
| HTTP+SSE (2024-11-05) | Deprecated | No |
| Streamable HTTP (2025-03-26+) | Current | **Yes** |

### Why not stdio?

stdio requires the MCP server to be installed as a local process on the same machine as the client. A registry service — whose entire purpose is connecting clients to servers they can't pre-configure — has no meaningful role in a stdio deployment. The registry is only useful for remotely-accessible servers.

### Why not the deprecated HTTP+SSE transport?

The original HTTP+SSE transport (spec 2024-11-05) used separate endpoints for sending messages (POST) and receiving server-initiated events (GET SSE stream), with no session management or resumability. It was deprecated in March 2025 and replaced by Streamable HTTP.

Streamable HTTP unifies these into a single endpoint supporting both POST and GET, adds `Mcp-Session-Id` session management, event ID-based resumability, and multiple concurrent connections. Servers still on the old transport should upgrade; supporting two transport variants in the registry would complicate the adapter for diminishing returns.

## The registry's role in MCP

Unlike A2A — where the registry can legitimately present itself as an A2A agent — MCP servers communicate via JSON-RPC 2.0 with a specific handshake (`initialize` / `initialized`). The registry does not implement this protocol. It is a **discovery service for MCP servers**, not an MCP server itself.

This distinction drives several design decisions.

### No /.well-known/mcp.json for the registry

The proposed `/.well-known/mcp.json` convention (still a draft/SEP in the MCP spec) is intended for MCP servers to advertise their own endpoint. If the registry served this path, it would imply to MCP clients that they could `initialize` a session with the registry — which would fail. Serving a misleading well-known document would break automated discovery tooling.

Instead:

- `GET /mcp/servers/{id}` — returns the MCP server card for a specific registered server
- `GET /mcp/servers` — returns a filtered, paginated list of registered MCP server cards

Consumers use the registry's REST API to find servers, then connect directly to the servers using the URLs in the cards.

### MCP server cards in the registry

The registry represents an MCP server as an `McpServerCard`:

```json
{
  "mcpVersion": "2025-11-25",
  "serverInfo": { "name": "My Server", "version": "1.0.0" },
  "endpoints": { "streamableHttp": "https://server.example.com/mcp" },
  "capabilities": {
    "tools": { "listChanged": true },
    "resources": { "subscribe": true, "listChanged": true },
    "prompts": { "listChanged": false }
  },
  "tools": [ ... ],
  "resources": [ ... ],
  "prompts": [ ... ],
  "id": "<registry-assigned-id>",
  "isLive": true
}
```

The `id` and `isLive` fields are registry additions — they're not in the MCP spec. `isLive` reflects real-time Redis liveness for the server's endpoint and lets consumers skip servers that are registered but not currently reachable.

## Design decisions

### Tool/resource/prompt descriptors in ProtocolMetadata

Like the A2A adapter, MCP-specific fields that have no equivalent in the generic domain model are serialised into `Endpoint.ProtocolMetadata`. This includes the full `McpCapabilities` object, `mcpVersion`, `serverVersion`, the tool/resource/prompt descriptor lists with their JSON Schema payloads, authentication config, and instructions.

The domain model stores only what all protocols share: agent name, description, endpoint URL, and transport type. Protocol-specific richness lives in metadata, keeping the domain clean and making it trivial to add new MCP spec fields without a migration.

JSON Schema for tool inputs and outputs is stored as `JsonObject` (raw JSON), so the full schema is preserved without the registry needing to understand it. A consumer fetching a server card gets back exactly the schemas the server author published.

### Mapping capabilities

When registering an MCP server, tools, resources, and prompts are mapped to generic registry capabilities. Each tool becomes a capability with tags `["tool", "mcp"]`, each resource with `["resource", "mcp"]`, each prompt with `["prompt", "mcp"]`. This means MCP servers are discoverable through the generic `GET /discover/agents?tags=tool,mcp` query alongside A2A agents and any other protocol.

When capability declarations are present but no descriptors are given (e.g., `capabilities.tools` is set but `tools: []` is empty), a single synthetic capability is created (`"tools"`, `"Exposes callable tools"`) so the server appears in capability-filtered queries.

### Liveness for MCP servers

MCP servers registered via `POST /mcp/servers` are created with `LivenessModel=Persistent` and a 30-second heartbeat interval. This is appropriate for the Streamable HTTP transport: the server is a long-lived process, not a per-request ephemeral function.

Servers registered through the generic API can choose their own liveness model. A KEDA-scaled MCP server that spins up to handle requests could use `Ephemeral` with a short TTL, renewing on each startup.

The `isLive` field in the card is derived from the liveness store at query time — it's not stored in the database. A server card returned from `GET /mcp/servers/{id}` always reflects the server's current live status.

### GET /mcp/servers vs GET /discover/agents

`GET /discover/agents` returns the registry's domain model — agents with their endpoints and capabilities in generic form. `GET /mcp/servers` returns MCP server cards: the protocol-native format that an MCP-aware consumer expects. Both query the same underlying data; the difference is in presentation.

`GET /mcp/servers` implicitly filters to `Protocol=MCP, Transport=Http` and returns only servers with MCP-over-HTTP endpoints. It also hydrates `ProtocolMetadata` back into the typed card structure so consumers get tool schemas and capability flags without having to understand the registry's internal format.

## Registration flows

### Flow 1: Generic registration

```json
POST /agents
{
  "name": "Weather Server",
  "capabilities": [
    { "name": "get_weather", "description": "Gets current weather", "tags": ["tool", "mcp"] }
  ],
  "endpoints": [{
    "name": "streamable-http",
    "transport": "Http",
    "protocol": "MCP",
    "address": "https://weather.example.com/mcp",
    "livenessModel": "Persistent",
    "heartbeatIntervalSeconds": 30
  }]
}
```

The server card at `GET /mcp/servers/{id}` is synthesised from domain data. Capabilities tagged `tool` produce `capabilities.tools`, and so on. No tool descriptors or JSON schemas are present because they weren't registered.

### Flow 2: MCP-native registration

```json
POST /mcp/servers
{
  "serverCard": {
    "mcpVersion": "2025-11-25",
    "serverInfo": { "name": "Weather Server", "version": "1.0.0" },
    "endpoints": { "streamableHttp": "https://weather.example.com/mcp" },
    "capabilities": { "tools": { "listChanged": true } },
    "tools": [{
      "name": "get_weather",
      "description": "Gets current weather for a location",
      "inputSchema": {
        "type": "object",
        "properties": { "location": { "type": "string" } },
        "required": ["location"]
      }
    }],
    "instructions": "Use get_weather to fetch live weather data."
  }
}
```

The tool descriptor (including `inputSchema`) is preserved in `ProtocolMetadata`. The returned card from `GET /mcp/servers/{id}` is identical to the submitted card — the JSON Schema is round-tripped exactly.

## Spec version

Targets **MCP 2025-11-25**. Key spec elements used:

- `initialize` response shape for `serverInfo` and `capabilities` field names
- Streamable HTTP transport (single endpoint, POST + GET, `Mcp-Session-Id` session header)
- Tool descriptor with `name`, `title`, `description`, `inputSchema`, `outputSchema`
- Resource descriptor with `uri`, `name`, `mimeType`
- Prompt descriptor with `name`, `arguments`
- Capability flags: `tools.listChanged`, `resources.subscribe`, `resources.listChanged`, `prompts.listChanged`
