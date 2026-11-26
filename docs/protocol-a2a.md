# A2A Protocol Adapter

## What is A2A?

Agent-to-Agent (A2A) is a protocol developed by Google and standardized through the [a2aproject](https://a2a-protocol.org/). It defines how agents describe themselves and communicate with each other. The core discovery primitive is the **AgentCard** — a JSON document that tells consumers what an agent can do, how to reach it, and what authentication it requires.

This implementation targets **A2A 1.0** (1.0.0, March 2026; 1.0.1 made no card changes). The protocol buffer definition is normative.

## The registry's role in A2A

A2A's canonical discovery mechanism is `GET /.well-known/agent-card.json` on the agent's own host — each agent self-describes. This works well when you know an agent's URL in advance. The registry fills the gap when you don't: it acts as a searchable index so consumers can find agents by capability without prior knowledge of their location.

The registry participates in A2A in two ways:

1. **It is itself an A2A agent.** The registry's own card is served at `GET /.well-known/agent-card.json`, describing two skills: `agent-registration` and `agent-discovery`. An A2A client that discovers the registry can understand what it does and how to interact with it.

   The response carries `Cache-Control: public, max-age=3600`, a strong ETag (`If-None-Match` returns 304), and CORS headers. The pre-1.0 path `/.well-known/agent.json` is not served.

2. **It proxies agent cards.** Each registered A2A agent gets a card served at `GET /a2a/agents/{id}`. A consumer that receives an agent ID from any source can fetch the full A2A-compliant card without going to the agent's own host.

## Design decisions

### Why store the full card in ProtocolMetadata?

The registry's domain model is intentionally protocol-agnostic. `Agent`, `Endpoint`, and `Capability` capture what all protocols have in common: identity, addresses, and declared skills. A2A-specific fields — `version`, capability flags and extensions, security schemes and requirements, provider info, icon URL, each interface's binding and protocol version — have no generic equivalents.

Rather than widening the domain model for every protocol-specific field, these fields are serialised as JSON into `Endpoint.ProtocolMetadata`. When a card is served, the mapper reads domain fields first, then overlays stored metadata. This means:

- A2A agent cards round-trip without loss.
- Adding new A2A spec fields requires no migration — they're just extra JSON.
- The domain model stays clean.

### Why map capabilities to skills?

A2A skills and registry capabilities represent the same thing from different directions. Skills say "I can do X, described in A2A terms." Capabilities say "I can do X, described in registry terms." The mapping is lossless in both directions:

- `Capability.Name` → `AgentSkill.Name`
- `Capability.Description` → `AgentSkill.Description`
- `Capability.Tags` → `AgentSkill.Tags`

When a card was registered through `POST /a2a/agents`, each skill keeps the `id` the agent gave it, along with its `examples`, input/output modes and `securityRequirements` (matched by skill name). For agents registered through the generic API, the skill `id` is the capability's UUID, which is stable across fetches.

When registering via `POST /a2a/agents`, skills that don't already exist as capabilities are added. This means a native A2A registration produces the same internal representation as a generic registration — both are discoverable through `GET /discover/agents`.

### Why no /.well-known/agent-card.json per agent?

The A2A spec places agent cards at `/.well-known/agent-card.json` on the agent's own host. The registry can't serve that path for individual agents because it's a single host serving many agents. Instead, cards live at `/a2a/agents/{id}`.

This is a deliberate trade-off: consumers can't point an A2A client directly at the registry and have it auto-discover all agents via well-known URLs. But it keeps the registry's URL space unambiguous, avoids routing complexity, and means `/.well-known/agent-card.json` on the registry host describes the registry itself — which is the correct behaviour for that path.

### Interfaces and protocol bindings

A2A 1.0's `supportedInterfaces` lists every way to reach the agent, in preference order. Each entry is `{ url, protocolBinding, protocolVersion, tenant? }`:

| `protocolBinding` | Registry transport |
|---|---|
| `JSONRPC` | `Http` |
| `GRPC` | `Http` |
| `HTTP+JSON` | `Http` |
| `AMQP` *(registry-specific)* | `Amqp` |
| `AzureServiceBus` *(registry-specific)* | `AzureServiceBus` |

Registering a card stores each interface's binding, protocol version and tenant. Serving a card matches each endpoint back to its stored interface by URL. When nothing matches — agents registered through the generic API, rows stored before A2A 1.0, queue endpoints — the binding defaults to `JSONRPC` (or the queue binding) and `protocolVersion` to `"1.0"`.

Queue endpoints are represented with a synthetic URL `{registryBaseUrl}/a2a/queue/{endpointId}`, because A2A requires a URL and queues don't inherently have one. A consumer seeing that URL knows to treat it as an async endpoint routed through the registry.

This is an acknowledged approximation. The spec says custom bindings SHOULD be URIs; `AMQP` and `AzureServiceBus` are plain names kept for compatibility with existing queued agents, and moving them to URIs is deferred.

### Signatures are accepted, not re-served

A2A 1.0 cards can carry `signatures[]` (JWS, `{ protected, signature, header? }`). The registry accepts and stores them on registration but does not include them in the cards it serves. The served card is rebuilt — skills merged from capabilities, endpoints from the current registration, the registry's own view of name and description — so a signature over the original card would no longer verify, and serving it would mislead clients.

To check an agent's signed card, fetch it from the agent's own `/.well-known/agent-card.json` and verify it there.

### Provider

`provider.url` is required in A2A 1.0. Rows stored before 1.0 with a provider but no URL are served without a provider rather than with an invalid one.

## Registration flows

### Flow 1: Generic registration with A2A endpoint

```json
POST /agents
{
  "name": "Summarizer",
  "capabilities": [{ "name": "summarize", "tags": ["nlp"] }],
  "endpoints": [{
    "name": "primary",
    "transport": "Http",
    "protocol": "A2A",
    "address": "https://summarizer.example.com/a2a",
    "livenessModel": "Persistent",
    "heartbeatIntervalSeconds": 30
  }]
}
```

The agent card served at `GET /a2a/agents/{id}` is built from the domain model. Version defaults to `"1.0"`, capabilities default to `{ streaming: false }`, and the interface is `JSONRPC` / `1.0`, since no A2A-specific metadata was stored.

### Flow 2: A2A-native registration

```json
POST /a2a/agents
{
  "card": {
    "name": "Summarizer",
    "description": "Summarizes documents.",
    "version": "2.3.1",
    "capabilities": {
      "streaming": true,
      "extensions": [{ "uri": "https://example.com/ext/trace/v1", "required": false }]
    },
    "skills": [{
      "id": "summarize", "name": "Summarize", "description": "...", "tags": ["nlp"],
      "securityRequirements": [{ "oauth": ["read"] }]
    }],
    "supportedInterfaces": [
      { "url": "https://summarizer.example.com/a2a", "protocolBinding": "JSONRPC", "protocolVersion": "1.0" },
      { "url": "https://summarizer.example.com:8443", "protocolBinding": "GRPC", "protocolVersion": "1.0", "tenant": "acme" }
    ],
    "provider": { "organization": "Example Co", "url": "https://example.com" },
    "securitySchemes": { "oauth": { "type": "oauth2" } },
    "defaultInputModes": ["text/plain"],
    "defaultOutputModes": ["text/plain"]
  }
}
```

The card is mapped to the domain model, and the A2A-specific fields (`version`, `capabilities`, the full skill list, interfaces, `defaultInputModes`, etc.) are stored in `ProtocolMetadata`. The card returned from `GET /a2a/agents/{id}` matches the submitted card, except that `signatures` are not re-served (see above). The registry ID is in the `Location` header of the 201 response; A2A 1.0 cards have no `id` field.

This is a clean break from the pre-1.0 shape: an interface with `transport` instead of `protocolBinding` and `protocolVersion` is rejected with 400.

## Spec version

Targets **A2A 1.0**:

- `supportedInterfaces[]` of `{ url, protocolBinding, protocolVersion, tenant? }` with bindings `JSONRPC`, `GRPC`, `HTTP+JSON`.
- No card-level `id`.
- `capabilities` with `streaming`, `pushNotifications`, `extendedAgentCard`, and `extensions[]` (`uri`, `description`, `required`, `params`).
- `securitySchemes`, card-level and skill-level `securityRequirements`.
- `signatures[]` (`protected`, `signature`, `header`).
- `provider` with required `organization` and `url`.
- Well-known path `/.well-known/agent-card.json`, served with `Cache-Control` and an ETag.
