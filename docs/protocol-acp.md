# ACP Protocol Adapter

## What is ACP?

Agent Communication Protocol (ACP) is a REST-based protocol developed by IBM Research through the [i-am-bee/acp](https://github.com/i-am-bee/acp) project. It defines how agents describe their capabilities via a manifest and how clients execute runs against them via a standardised HTTP API.

**Spec version:** 0.2.0

### ACP vs A2A vs MCP

| | MCP | ACP | A2A |
|---|---|---|---|
| **Interaction model** | Model ↔ tools (hierarchical) | Agent ↔ agent (REST) | Agent ↔ agent (JSON-RPC/gRPC) |
| **Discovery** | Manual / config | `/agents` endpoint + manifests | `/.well-known/agent.json` |
| **Execution** | Tool calls | `POST /runs` (sync/async/stream) | Task protocol |
| **Schemas** | JSON Schema per tool | JSON Schema for input/output/config/thread state | Skills with I/O modes |
| **Sessions** | No session memory | Session-aware, thread state | Session-aware |
| **Content types** | Text-focused | Multimodal (MIME types) | Multimodal (modes) |
| **Streaming** | SSE (Streamable HTTP) | SSE via `mode: stream` | SSE + gRPC |

ACP was absorbed into A2A under Linux Foundation governance in August 2025, but the spec is stable and widely deployed. This registry treats ACP and A2A as distinct protocol adapters because they have meaningfully different manifest structures and discovery conventions.

## The ACP Agent Manifest

The central artifact in ACP is the **agent manifest**:

```json
{
  "name": "summarizer-agent",
  "description": "Summarizes long documents into key points",
  "input_content_types": ["text/plain", "application/pdf"],
  "output_content_types": ["text/plain", "application/json"],
  "metadata": {
    "capabilities": [
      { "name": "summarize", "description": "Produces concise summaries" },
      { "name": "extract-entities", "description": "Identifies named entities" }
    ],
    "tags": ["nlp", "summarization"],
    "domains": ["document-processing"],
    "framework": "LangChain",
    "natural_languages": ["en", "es"],
    "license": "MIT",
    "author": { "name": "Acme Corp", "url": "https://acme.example.com" },
    "input_schema": { "type": "object", "properties": { "text": { "type": "string" } } },
    "output_schema": { "type": "object", "properties": { "summary": { "type": "string" } } }
  },
  "status": {
    "avg_run_time_seconds": 2.3,
    "success_rate": 0.97
  }
}
```

Key points:
- `name` must be an RFC 1123 DNS label (lowercase alphanumeric + hyphens). The registry normalises agent names on ingest.
- `input_content_types` and `output_content_types` use MIME types with wildcard support (`text/*`, `*/*`).
- `metadata` is optional but carries the most useful discovery information.
- JSON Schemas in `metadata` (`input_schema`, `output_schema`, `config_schema`, `thread_state_schema`) are stored and returned verbatim — the registry does not validate them.
- `status` carries runtime performance metrics that agents can update over time.

## The registry's role in ACP

ACP agents discover each other via a `GET /agents` endpoint on each ACP server, or by embedding manifests in deployment packages. The registry centralises this: instead of knowing every ACP server's address in advance, consumers query the registry and get back manifests for all registered ACP agents.

The registry does **not** proxy ACP runs. `POST /runs` calls go directly to the agent's own endpoint — the `endpoint_url` field in the manifest tells consumers where that is.

## Design decisions

### Separate /acp namespace from /a2a

ACP and A2A manifests are structurally different enough that separate URL namespaces (`/acp/agents` vs `/a2a/agents`) are cleaner than trying to unify them. Both are filtered from the same underlying agent database; the namespaces simply determine which format the response takes.

ACP's `/agents` convention (on the agent's own server) maps naturally to `/acp/agents` in the registry namespace. A consumer already using ACP discovery patterns can point at the registry with minimal change.

### Name normalisation to RFC 1123

The registry's `Agent.Name` is a free-form string. ACP requires names to be RFC 1123 DNS-label-compatible (lowercase, alphanumeric, hyphens, max 63 chars). On manifest generation, `ToAcpName()` lowercases the name, replaces spaces and underscores with hyphens, and strips non-conforming characters. "My Summarizer Agent" becomes "my-summarizer-agent".

The original name is preserved in the domain model; only the manifest representation is normalised. This means the same agent is discoverable by its human-readable name in the generic API and by its normalised ACP name in the ACP API.

### EndpointUrl as a required registration field

ACP's manifest does not include the agent's own URL — the URL is contextual (the manifest is served from the agent's host, so `/.well-known/agent.yml` doesn't need to repeat the host). In our registry, the endpoint URL must be explicitly supplied at registration time, either as the `Endpoint.Address` in a generic registration or as the `endpoint_url` field in the ACP-native `POST /acp/agents` body.

This is the only place where the ACP adapter diverges from the spec's assumptions — a necessary trade-off for a centralised registry.

### ProtocolMetadata round-tripping

ACP-specific fields not covered by the generic domain model — content types, JSON schemas, status metrics, author info, framework, natural languages, domains — are serialised into `Endpoint.ProtocolMetadata`. When building a manifest for `GET /acp/agents/{id}`, domain fields are populated first, then stored metadata is overlaid.

This ensures a manifest submitted via `POST /acp/agents` comes back from `GET /acp/agents/{id}` unchanged, including the full JSON Schema payloads.

### Capability mapping

ACP capabilities (`metadata.capabilities[]`) map to registry capabilities with tags drawn from `metadata.tags`. This makes ACP agents discoverable through the generic `GET /discover/agents?tags=nlp` query alongside A2A and MCP agents.

Content types are stored in `ProtocolMetadata` rather than mapped to capabilities, since content types are a transport concern rather than a semantic capability. A consumer filtering by content type should use `GET /acp/agents` with domain-specific filters, not the generic discovery endpoint.

### Domain filtering

ACP manifests have a `domains` field (`metadata.domains`) that captures subject-matter domains like "document-processing", "code-generation", "customer-service". The `GET /acp/agents` endpoint accepts a `domain` query parameter that is merged into the tag filter, because domains are stored as tags in the generic capability model when registering via `POST /acp/agents`.

### isLive on the manifest

The `is_live` field is a registry extension — it's not part of the ACP spec. It reflects whether the agent's registered HTTP endpoint is currently live in Redis. Consumers can use this to skip agents that are registered but not currently accepting runs.

## Registration flows

### Flow 1: Generic registration

```json
POST /agents
{
  "name": "Summarizer",
  "description": "Summarizes documents",
  "capabilities": [
    { "name": "summarize", "tags": ["nlp", "acp"] }
  ],
  "endpoints": [{
    "name": "acp-http",
    "transport": "Http",
    "protocol": "ACP",
    "address": "https://summarizer.example.com",
    "livenessModel": "Persistent",
    "heartbeatIntervalSeconds": 30
  }]
}
```

The manifest at `GET /acp/agents/{id}` is synthesised from domain data. Content types default to `["text/plain", "application/json"]` and no JSON schemas are included.

### Flow 2: ACP-native registration

```json
POST /acp/agents
{
  "manifest": {
    "name": "summarizer-agent",
    "description": "Summarizes documents",
    "input_content_types": ["text/plain", "application/pdf"],
    "output_content_types": ["text/plain"],
    "metadata": {
      "capabilities": [{ "name": "summarize", "description": "Produces concise summaries" }],
      "tags": ["nlp"],
      "input_schema": { "type": "object", "properties": { "text": { "type": "string" } } }
    }
  },
  "endpoint_url": "https://summarizer.example.com"
}
```

All manifest fields including JSON schemas are preserved in `ProtocolMetadata` and returned intact from subsequent `GET /acp/agents/{id}` calls.

## Spec version and status

Targets **ACP 0.2.0** from [i-am-bee/acp](https://github.com/i-am-bee/acp).

ACP was officially absorbed into A2A under Linux Foundation governance in August 2025. The spec itself is stable and the OpenAPI definition remains available. This adapter is maintained because ACP agents are widely deployed and the manifest format carries richer metadata (content types, JSON schemas, performance status) than A2A agent cards do. Operators running ACP-native agents should consider whether migrating to A2A makes sense for their deployment; the registry supports both concurrently.
