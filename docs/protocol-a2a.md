# A2A Protocol Adapter

## What is A2A?

Agent-to-Agent (A2A) is a protocol developed by Google and standardized through the [a2aproject](https://a2a-protocol.org/). It defines how agents describe themselves and communicate with each other. The core discovery primitive is the **AgentCard** — a JSON document that tells consumers what an agent can do, how to reach it, and what authentication it requires.

The spec targets **v1.0 RC** as of this implementation.

## The registry's role in A2A

A2A's canonical discovery mechanism is `GET /.well-known/agent.json` on the agent's own host — each agent self-describes. This works well when you know an agent's URL in advance. The registry fills the gap when you don't: it acts as a searchable index so consumers can find agents by capability without prior knowledge of their location.

The registry participates in A2A in two ways:

1. **It is itself an A2A agent.** The registry's own card is served at `GET /.well-known/agent.json`, describing two skills: `agent-registration` and `agent-discovery`. An A2A client that discovers the registry can understand what it does and how to interact with it.

2. **It proxies agent cards.** Each registered A2A agent gets a card served at `GET /a2a/agents/{id}`. A consumer that receives an agent ID from any source can fetch the full A2A-compliant card without going to the agent's own host.

## Design decisions

### Why store the full card in ProtocolMetadata?

The registry's domain model is intentionally protocol-agnostic. `Agent`, `Endpoint`, and `Capability` capture what all protocols have in common: identity, addresses, and declared skills. A2A-specific fields — `version`, streaming capability, security schemes, provider info, icon URL — have no generic equivalents.

Rather than widening the domain model for every protocol-specific field, these fields are serialised as JSON into `Endpoint.ProtocolMetadata`. When a card is served, the mapper reads domain fields first, then overlays stored metadata. This means:

- A2A agent cards round-trip without loss.
- Adding new A2A spec fields requires no migration — they're just extra JSON.
- The domain model stays clean.

### Why map capabilities to skills?

A2A skills and registry capabilities represent the same thing from different directions. Skills say "I can do X, described in A2A terms." Capabilities say "I can do X, described in registry terms." The mapping is lossless in both directions:

- `Capability.Name` → `AgentSkill.Name`
- `Capability.Description` → `AgentSkill.Description`
- `Capability.Tags` → `AgentSkill.Tags`

The `id` field on skills is the capability's UUID stringified, which means the same skill has a stable identifier across card fetches.

When registering via `POST /a2a/agents`, skills that don't already exist as capabilities are added. This means a native A2A registration produces the same internal representation as a generic registration — both are discoverable through `GET /discover/agents`.

### Why no /.well-known/agent.json per agent?

The A2A spec places agent cards at `/.well-known/agent.json` on the agent's own host. The registry can't serve that path for individual agents because it's a single host serving many agents. Instead, cards live at `/a2a/agents/{id}`.

This is a deliberate trade-off: consumers can't point an A2A client directly at the registry and have it auto-discover all agents via well-known URLs. But it keeps the registry's URL space unambiguous, avoids routing complexity, and means `/.well-known/agent.json` on the registry host describes the registry itself — which is the correct behaviour for that path.

### Transports and A2A

A2A's `supportedInterfaces` field carries the endpoint URLs and transport types. The registry supports both HTTP (mapped from `Transport=Http`) and queue-based transports (AMQP, Azure Service Bus). Queue endpoints are represented with a synthetic URL `{registryBaseUrl}/a2a/queue/{endpointId}` in the card, because A2A requires a URL and queues don't inherently have one. A consumer seeing that URL knows to treat it as an async endpoint routed through the registry.

This is an acknowledged approximation. A fully queue-native A2A extension would define a dedicated transport type; the registry is positioned to support that if the spec adds it.

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

The agent card served at `GET /a2a/agents/{id}` is built from the domain model. Version defaults to `"1.0"` and capabilities default to `{ streaming: false }` since no A2A-specific metadata was stored.

### Flow 2: A2A-native registration

```json
POST /a2a/agents
{
  "card": {
    "name": "Summarizer",
    "version": "2.3.1",
    "capabilities": { "streaming": true },
    "skills": [{ "id": "summarize", "name": "Summarize", "description": "...", "tags": ["nlp"] }],
    "supportedInterfaces": [{ "url": "https://summarizer.example.com/a2a", "transport": "JSONRPC" }],
    "defaultInputModes": ["text/plain"],
    "defaultOutputModes": ["text/plain"]
  }
}
```

The card is mapped to the domain model, and the A2A-specific fields (`version`, `capabilities`, the full skill list, `defaultInputModes`, etc.) are stored in `ProtocolMetadata`. The returned card from `GET /a2a/agents/{id}` is identical to the submitted card.

## Spec version

Targets **A2A v1.0 RC**. The `AgentCard` structure matches the RC field set including `supportedInterfaces` (replacing the earlier `url` top-level field), `securitySchemes` as an OpenAPI 3.0 security scheme map, and `AgentCapabilities` with `streaming`, `pushNotifications`, and `extendedAgentCard` flags.
