# MarimerLLC Agent Registry Python Client

Async and sync Python client for the MarimerLLC Agent Registry.

## Installation

```bash
pip install marimerllc-agentregistry
```

## Quick start

```python
from marimerllc_agentregistry import AgentRegistryClient, RegisterAgentRequest

async with AgentRegistryClient("https://registry.example.com", api_key="...") as client:
    agent = await client.register(RegisterAgentRequest(name="My Agent"))
```
