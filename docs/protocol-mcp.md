# MCP Protocol Adapter

## What is MCP?

Model Context Protocol (MCP) is an open standard developed by Anthropic for connecting AI models to data sources, tools, and services. An MCP server exposes three kinds of capabilities:

- **Tools** — functions the model can call (with JSON Schema-typed inputs and outputs)
- **Resources** — data sources the model can read (files, database rows, live data)
- **Prompts** — reusable prompt templates with typed arguments

The spec is maintained at [modelcontextprotocol.io](https://modelcontextprotocol.io/). This implementation targets **spec 2026-07-28**, with down-level support for **2025-11-25** clients, and describes servers with **MCP Server Cards** ([SEP-2127](https://github.com/modelcontextprotocol/modelcontextprotocol/pull/2127), extension `io.modelcontextprotocol/server-card`).

The registry plays two MCP roles:

1. **It is an MCP server.** `POST /mcp` serves five read-only discovery tools.
2. **It is a directory of MCP servers.** It stores Server Cards for registered servers and publishes them through REST endpoints and an AI Catalog.

## Transport: Streamable HTTP only

| Transport | Status | Supported |
|---|---|---|
| stdio | Active | No — not applicable to a registry |
| HTTP+SSE (2024-11-05) | Deprecated | No |
| Streamable HTTP (2025-03-26+) | Current | **Yes** |

### Why not stdio?

stdio requires the MCP server to be installed as a local process on the same machine as the client. A registry service — whose entire purpose is connecting clients to servers they can't pre-configure — has no meaningful role in a stdio deployment. The registry is only useful for remotely-accessible servers.

### Why not the deprecated HTTP+SSE transport?

The original HTTP+SSE transport (spec 2024-11-05) used separate endpoints for sending messages (POST) and receiving server-initiated events (GET SSE stream). It was deprecated in March 2025 and replaced by Streamable HTTP. Server Cards can still describe an `sse` remote; the registry ignores those remotes on registration, and a card with only `sse` remotes is rejected.

## The registry's own MCP server

`POST /mcp` is a public, **stateless** Streamable HTTP endpoint (C# SDK 2.x, `Stateless = true`). Every tool is read-only, so nothing needs a session.

| Tool | Returns |
|---|---|
| `discover_agents` | Live agents matching capability, tags, protocol, transport |
| `get_agent` | One agent's full registry record |
| `get_a2_a_card` | The agent's A2A 1.0 agent card |
| `get_mcp_server_card` | The agent's MCP Server Card (SEP-2127) |
| `get_acp_manifest` | The agent's ACP manifest |

All tools are annotated `readOnlyHint: true`, `idempotentHint: true`, `openWorldHint: false`. The server sends `instructions` that orient a model: discovery is public, cards come in each protocol's own format, and clients connect to agents directly — the registry does not proxy calls.

### Protocol revisions

- **2026-07-28 clients** send no `initialize`. Each request carries `_meta["io.modelcontextprotocol/protocolVersion"]` and `_meta["io.modelcontextprotocol/clientCapabilities"]`, plus the `Mcp-Method` header (and `Mcp-Name` for `tools/call`). `server/discover` returns `supportedVersions`, capabilities, instructions, and `serverInfo` under `_meta["io.modelcontextprotocol/serverInfo"]`. A request missing a required header is rejected with 400.
- **2025-11-25 clients** use the `initialize` handshake as before. Because the server is stateless, no `Mcp-Session-Id` is issued and GET/DELETE on `/mcp` are unavailable.

`serverInfo.name` is `llc.marimer/agentregistry`, matching the registry's own Server Card so the card and the live connection agree.

## MCP Server Cards (SEP-2127)

A Server Card describes how to reach a server — identity, transport, protocol versions — and nothing more. **Tools, resources, prompts and capabilities are deliberately excluded**; clients list those at runtime. The registry follows the schema at [`experimental-ext-server-card`](https://github.com/modelcontextprotocol/experimental-ext-server-card) (`schema.json`, `$defs/ServerCard`).

```json
{
  "$schema": "https://static.modelcontextprotocol.io/schemas/v1/server-card.schema.json",
  "name": "com.example/weather",
  "version": "1.2.0",
  "description": "Weather and web search.",
  "title": "Weather Server",
  "websiteUrl": "https://example.com/weather",
  "repository": { "url": "https://github.com/example/weather", "source": "github" },
  "remotes": [{
    "type": "streamable-http",
    "url": "https://weather.example.com/mcp",
    "supportedProtocolVersions": ["2026-07-28", "2025-11-25"],
    "headers": [{ "name": "Authorization", "isRequired": true, "isSecret": true }]
  }],
  "_meta": {
    "llc.marimer/agentregistry": {
      "id": "<registry-assigned-id>",
      "isLive": true,
      "instructions": "Use get_weather for live data.",
      "tools": [{ "name": "get_weather", "description": "...", "inputSchema": { ... } }],
      "resources": [ ... ],
      "prompts": [ ... ]
    }
  }
}
```

| Field | Rule |
|---|---|
| `$schema` | Must be exactly the v1 URL above |
| `name` | Reverse-DNS, exactly one slash: `^[a-zA-Z0-9.-]+/[a-zA-Z0-9._-]+$`, 3–200 characters |
| `version` | Required, at most 255 characters; semver recommended |
| `description` | 1–100 characters |
| `title` | Optional, 1–100 characters |
| `remotes[]` | `type` (`streamable-http` or `sse`), `url`, optional `headers`, `variables`, `supportedProtocolVersions`, `extensionsInfo` |

### The registry `_meta` block

Registry data lives under `_meta["llc.marimer/agentregistry"]`. The key follows the `_meta` reverse-DNS rule and avoids the reserved `mcp` / `modelcontextprotocol` labels.

| Member | Set by | Meaning |
|---|---|---|
| `id` | Registry | Registry agent ID (ignored on registration) |
| `isLive` | Registry | True when any of the server's endpoints is live in Redis at query time (ignored on registration) |
| `instructions` | Server | Free-text orientation, carried through |
| `tools` / `resources` / `prompts` | Server | Descriptors (name, title, description, JSON Schemas). These drive capability search |
| `authentication` | Server | Optional authentication details, carried through. Registrations made before Server Cards had this as a top-level card field; it now appears here |

A registry block that is present but doesn't parse (for example a tool with no `name`) is rejected with a 400 error under `_meta`, rather than silently dropped.

Other `_meta` keys a server publishes are stored and served back unchanged.

## Endpoints

| Method | Path | Notes |
|---|---|---|
| `POST` | `/mcp` | The registry's MCP server |
| `GET` | `/mcp/server-card` | The registry's own Server Card (`<streamable-http-url>/server-card`) |
| `GET` | `/.well-known/ai-catalog.json` | AI Catalog of Server Cards |
| `GET` | `/mcp/servers/{id}` | Server Card for a registered server |
| `GET` | `/mcp/servers` | Paginated list of Server Cards (`capability`, `tags`, `liveOnly`, `page`, `pageSize`) |
| `POST` | `/mcp/servers` | Register by submitting a Server Card (Agent or Admin) |

### AI Catalog

`GET /.well-known/ai-catalog.json` (`application/ai-catalog+json`) is the registry's domain-level discovery document:

```json
{
  "specVersion": "1.0",
  "entries": [
    { "identifier": "urn:air:registry.example.com:mcp:agentregistry",
      "type": "application/mcp-server-card+json",
      "url": "https://registry.example.com/mcp/server-card" },
    { "identifier": "urn:air:registry.example.com:mcp:6b2d43be0a18400ab1a4a59fe0d6e265",
      "type": "application/mcp-server-card+json",
      "url": "https://registry.example.com/mcp/servers/6b2d43be-0a18-400a-b1a4-a59fe0d6e265" }
  ]
}
```

The first entry is always the registry's own card. Then comes one entry per MCP server whose Streamable HTTP endpoint is **live** (a live endpoint of another protocol doesn't count). Entries link to cards rather than inlining them, so a catalog fetch stays small.

The catalog replaces the earlier "no well-known document" stance: the old draft `/.well-known/mcp.json` implied that the host itself was the MCP server. The AI Catalog is explicitly a list of servers, which is what a registry is.

### Content type, CORS and caching

Card and catalog responses follow the Server Card hosting rules:

- **Media type.** Send `Accept: application/mcp-server-card+json` and the card comes back with that `Content-Type`; otherwise it is `application/json`. The catalog is always `application/ai-catalog+json`.
- **CORS.** `Access-Control-Allow-Origin: *`, `Access-Control-Allow-Methods: GET`, `Access-Control-Allow-Headers: Content-Type, If-None-Match`, `Access-Control-Expose-Headers: ETag`. `OPTIONS` preflight returns 204 with the same headers.
- **Cache-Control.** `public, max-age=60` for registry entries and the catalog, because liveness changes; `public, max-age=3600` for the registry's own card.
- **ETag.** A strong ETag (SHA-256 of media type and body). `If-None-Match` with a matching tag returns `304 Not Modified`.

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
    "transport": 0,
    "protocol": 2,
    "address": "https://weather.example.com/mcp",
    "livenessModel": 1,
    "heartbeatIntervalSeconds": 30
  }]
}
```

The generic API takes enum fields as numbers: `transport` `0` (Http), `protocol` `2` (MCP), `livenessModel` `1` (Persistent). See [Enum values in request bodies](../README.md#enum-values-in-request-bodies).

The card at `GET /mcp/servers/{id}` is synthesised from domain data: `name` is `agentregistry/{agentId}` (UUID without dashes), `title` is the agent name, `description` is the agent description (or name) truncated to 100 characters, and `version` is `1.0.0`. Each MCP HTTP endpoint becomes a `streamable-http` remote.

### Flow 2: MCP-native registration

`POST /mcp/servers` takes a bare Server Card as the body. The earlier `{ "serverCard": { ... } }` wrapper and the pre-SEP-2127 shape (`mcpVersion`, `serverInfo`, `endpoints`, `capabilities`) are rejected with 400.

```json
POST /mcp/servers
{
  "$schema": "https://static.modelcontextprotocol.io/schemas/v1/server-card.schema.json",
  "name": "com.example/weather",
  "version": "1.0.0",
  "title": "Weather Server",
  "description": "Live weather data.",
  "remotes": [{ "type": "streamable-http", "url": "https://weather.example.com/mcp",
                "supportedProtocolVersions": ["2026-07-28"] }],
  "_meta": {
    "llc.marimer/agentregistry": {
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
}
```

Validation failures return a 400 ValidationProblem, keyed by field (`$schema`, `name`, `version`, `description`, `title`, `remotes`). The registry needs at least one `streamable-http` remote with a concrete `http(s)` URL; templated URLs and `sse` remotes are not registered as endpoints.

On success:

- The agent's `Name` is `title`, falling back to `name`. Its `Description` is `description`.
- Each distinct `streamable-http` URL becomes an endpoint (`LivenessModel=Persistent`, 30-second heartbeat).
- Each tool, resource and prompt in the `_meta` block becomes a capability tagged `["tool","mcp"]`, `["resource","mcp"]` or `["prompt","mcp"]`. That makes them discoverable through `GET /discover/agents?tags=tool,mcp` and `GET /mcp/servers?capability=get_weather`.

## Design decisions

### The card round-trips through ProtocolMetadata

The submitted card, including its `_meta`, is stored in each endpoint's `Endpoint.ProtocolMetadata` (JSONB). Registry-computed fields (`id`, `isLive`) are stripped before storing. When a card is served, the mapper rebuilds it from the domain model and overlays the stored card:

- `remotes` are rebuilt from the agent's current MCP HTTP endpoints, live endpoints first. Each remote's `headers`, `variables`, `supportedProtocolVersions` and `extensionsInfo` come from the stored remote with the same URL.
- `title` and `description` follow the agent, so `PUT /agents/{id}` edits show up in the card.
- `name`, `version`, `websiteUrl`, `repository`, `icons` and other `_meta` keys come from the stored card.

JSON Schemas for tool inputs and outputs are stored as raw JSON, so consumers get back exactly what the server author published.

### Rows stored before Server Cards

No migration is needed. Rows written by the earlier adapter kept `mcpVersion`, `serverVersion`, `instructions`, `authentication` and `tools`/`resources`/`prompts` at the top level of the metadata. The mapper still reads them: `serverVersion` becomes `version`, `mcpVersion` becomes each remote's `supportedProtocolVersions`, and `instructions`, `authentication` and the descriptors move into the `_meta` block.

### Liveness

MCP servers registered via `POST /mcp/servers` are `Persistent` with a 30-second heartbeat, which suits a long-lived Streamable HTTP process. Servers registered through the generic API choose their own liveness model; a KEDA-scaled server could use `Ephemeral` with a short TTL.

`isLive` is derived from the liveness store at query time and never stored.

### GET /mcp/servers vs GET /discover/agents

`GET /discover/agents` returns the registry's domain model. `GET /mcp/servers` returns Server Cards, the format an MCP-aware consumer expects. Both query the same data; `GET /mcp/servers` implicitly filters to `Protocol=MCP, Transport=Http`.

### Cards are advisory

Per SEP-2127, a card is fetched before connecting and is not authoritative. Clients should check it against `server/discover` once connected and prefer the runtime values. The registry adds a second layer of indirection — it serves what the server's owner registered — so this applies doubly to registry cards.

## Spec versions

- **MCP 2026-07-28** for the registry's own server (stateless Streamable HTTP, `server/discover`, per-request `_meta`, `Mcp-Method`/`Mcp-Name` headers), with 2025-11-25 down-level via `initialize`.
- **MCP Server Cards, SEP-2127** (`io.modelcontextprotocol/server-card`, schema v1) for registered servers and the registry's own card.
- **AI Catalog 1.0** at `/.well-known/ai-catalog.json`.
