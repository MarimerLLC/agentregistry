"""Tests for AgentRegistryClient and HeartbeatService."""
from __future__ import annotations

import asyncio
import json

import httpx
import pytest
import respx

from marimerllc_agentregistry import (
    AgentRegistryClient,
    HeartbeatService,
    RegisterAgentRequest,
)
from marimerllc_agentregistry.models import (
    AgentResponse,
    CapabilityRequest,
    DiscoveryFilter,
    EndpointRequest,
    EndpointResponse,
    LivenessModel,
    PagedAgentResponse,
    ProtocolType,
    TransportType,
    UpdateAgentRequest,
)

BASE_URL = "https://registry.example.com"
API_KEY = "test-key"


# ── Fixtures ──────────────────────────────────────────────────────────────────

def _agent_dict(
    agent_id: str = "agent-1",
    name: str = "Test Agent",
    endpoints: list[dict] | None = None,
) -> dict:
    return {
        "id": agent_id,
        "name": name,
        "description": None,
        "ownerId": "owner-1",
        "labels": {},
        "capabilities": [],
        "endpoints": endpoints or [],
        "createdAt": "2024-01-01T00:00:00Z",
        "updatedAt": "2024-01-01T00:00:00Z",
    }


def _endpoint_dict(
    endpoint_id: str = "ep-1",
    liveness_model: str = "Persistent",
    heartbeat_interval_seconds: int | None = 30,
) -> dict:
    return {
        "id": endpoint_id,
        "name": "primary",
        "transport": "Http",
        "protocol": "MCP",
        "address": "https://agent.example.com",
        "livenessModel": liveness_model,
        "ttlSeconds": None,
        "heartbeatIntervalSeconds": heartbeat_interval_seconds,
        "isLive": True,
        "protocolMetadata": None,
    }


def _paged_dict(agents: list[dict] | None = None) -> dict:
    items = agents or []
    return {
        "items": items,
        "totalCount": len(items),
        "page": 1,
        "pageSize": 20,
        "totalPages": 1,
        "hasNextPage": False,
        "hasPreviousPage": False,
    }


# ── AgentRegistryClient tests ─────────────────────────────────────────────────

@respx.mock
async def test_register_posts_to_agents_and_returns_response():
    agent = _agent_dict()
    respx.post(f"{BASE_URL}/agents").mock(
        return_value=httpx.Response(201, json=agent)
    )

    async with AgentRegistryClient(BASE_URL, api_key=API_KEY) as client:
        result = await client.register(RegisterAgentRequest(name="Test Agent"))

    assert isinstance(result, AgentResponse)
    assert result.id == "agent-1"
    assert result.name == "Test Agent"


@respx.mock
async def test_register_sends_api_key_header():
    captured: list[httpx.Request] = []

    def capture(request: httpx.Request, route) -> httpx.Response:
        captured.append(request)
        return httpx.Response(201, json=_agent_dict())

    respx.post(f"{BASE_URL}/agents").mock(side_effect=capture)

    async with AgentRegistryClient(BASE_URL, api_key=API_KEY) as client:
        await client.register(RegisterAgentRequest(name="X"))

    assert captured[0].headers["x-api-key"] == API_KEY


@respx.mock
async def test_get_agent_returns_agent():
    respx.get(f"{BASE_URL}/agents/agent-1").mock(
        return_value=httpx.Response(200, json=_agent_dict())
    )

    async with AgentRegistryClient(BASE_URL, api_key=API_KEY) as client:
        result = await client.get_agent("agent-1")

    assert result is not None
    assert result.id == "agent-1"


@respx.mock
async def test_get_agent_returns_none_on_404():
    respx.get(f"{BASE_URL}/agents/missing").mock(
        return_value=httpx.Response(404)
    )

    async with AgentRegistryClient(BASE_URL, api_key=API_KEY) as client:
        result = await client.get_agent("missing")

    assert result is None


@respx.mock
async def test_update_agent_sends_put_and_returns_response():
    updated = _agent_dict(name="New Name")
    respx.put(f"{BASE_URL}/agents/agent-1").mock(
        return_value=httpx.Response(200, json=updated)
    )

    async with AgentRegistryClient(BASE_URL, api_key=API_KEY) as client:
        result = await client.update_agent("agent-1", UpdateAgentRequest(name="New Name"))

    assert result.name == "New Name"


@respx.mock
async def test_deregister_sends_delete():
    route = respx.delete(f"{BASE_URL}/agents/agent-1").mock(
        return_value=httpx.Response(204)
    )

    async with AgentRegistryClient(BASE_URL, api_key=API_KEY) as client:
        await client.deregister("agent-1")

    assert route.called


@respx.mock
async def test_heartbeat_posts_to_correct_url():
    route = respx.post(
        f"{BASE_URL}/agents/agent-1/endpoints/ep-1/heartbeat"
    ).mock(return_value=httpx.Response(204))

    async with AgentRegistryClient(BASE_URL, api_key=API_KEY) as client:
        await client.heartbeat("agent-1", "ep-1")

    assert route.called


@respx.mock
async def test_discover_returns_paged_response():
    respx.get(f"{BASE_URL}/discover/agents").mock(
        return_value=httpx.Response(200, json=_paged_dict([_agent_dict()]))
    )

    async with AgentRegistryClient(BASE_URL, api_key=API_KEY) as client:
        result = await client.discover()

    assert isinstance(result, PagedAgentResponse)
    assert result.total_count == 1
    assert result.items[0].id == "agent-1"


@respx.mock
async def test_discover_with_filter_sends_query_params():
    captured: list[httpx.Request] = []

    def capture(request: httpx.Request, route) -> httpx.Response:
        captured.append(request)
        return httpx.Response(200, json=_paged_dict())

    respx.get(f"{BASE_URL}/discover/agents").mock(side_effect=capture)

    async with AgentRegistryClient(BASE_URL, api_key=API_KEY) as client:
        await client.discover(DiscoveryFilter(capability="summarize", live_only=False))

    params = dict(captured[0].url.params)
    assert params["capability"] == "summarize"
    assert params["liveOnly"] == "false"


@respx.mock
async def test_discover_default_params_not_sent():
    """Default live_only=True, page=1, page_size=20 should not be included."""
    captured: list[httpx.Request] = []

    def capture(request: httpx.Request, route) -> httpx.Response:
        captured.append(request)
        return httpx.Response(200, json=_paged_dict())

    respx.get(f"{BASE_URL}/discover/agents").mock(side_effect=capture)

    async with AgentRegistryClient(BASE_URL, api_key=API_KEY) as client:
        await client.discover()

    params = dict(captured[0].url.params)
    assert "liveOnly" not in params
    assert "page" not in params
    assert "pageSize" not in params


# ── HeartbeatService tests ─────────────────────────────────────────────────────

@respx.mock
async def test_heartbeat_service_registers_on_start():
    agent = _agent_dict(endpoints=[_endpoint_dict(liveness_model="Persistent")])
    respx.post(f"{BASE_URL}/agents").mock(return_value=httpx.Response(201, json=agent))
    respx.post(f"{BASE_URL}/agents/agent-1/endpoints/ep-1/heartbeat").mock(
        return_value=httpx.Response(204)
    )
    respx.delete(f"{BASE_URL}/agents/agent-1").mock(return_value=httpx.Response(204))

    async with AgentRegistryClient(BASE_URL, api_key=API_KEY) as client:
        svc = HeartbeatService(
            client,
            registration=RegisterAgentRequest(name="Test Agent"),
            heartbeat_interval=3600.0,
        )
        await svc.start()
        try:
            assert svc.agent_id == "agent-1"
            assert svc.agent is not None
            assert svc.agent.name == "Test Agent"
        finally:
            await svc.stop()


@respx.mock
async def test_heartbeat_service_sends_heartbeats_for_persistent_endpoints():
    agent = _agent_dict(endpoints=[_endpoint_dict(liveness_model="Persistent")])
    register_route = respx.post(f"{BASE_URL}/agents").mock(
        return_value=httpx.Response(201, json=agent)
    )
    hb_route = respx.post(
        f"{BASE_URL}/agents/agent-1/endpoints/ep-1/heartbeat"
    ).mock(return_value=httpx.Response(204))
    respx.delete(f"{BASE_URL}/agents/agent-1").mock(return_value=httpx.Response(204))

    async with AgentRegistryClient(BASE_URL, api_key=API_KEY) as client:
        svc = HeartbeatService(
            client,
            registration=RegisterAgentRequest(name="Test Agent"),
            heartbeat_interval=0.05,  # 50ms so test is fast
        )
        await svc.start()
        await asyncio.sleep(0.15)  # allow ~2 heartbeats
        await svc.stop()

    assert hb_route.call_count >= 1


@respx.mock
async def test_heartbeat_service_no_task_for_ephemeral_only_agent():
    """No heartbeat task should be created when all endpoints are Ephemeral."""
    agent = _agent_dict(
        endpoints=[_endpoint_dict(liveness_model="Ephemeral", heartbeat_interval_seconds=None)]
    )
    respx.post(f"{BASE_URL}/agents").mock(return_value=httpx.Response(201, json=agent))
    respx.delete(f"{BASE_URL}/agents/agent-1").mock(return_value=httpx.Response(204))

    async with AgentRegistryClient(BASE_URL, api_key=API_KEY) as client:
        svc = HeartbeatService(
            client,
            registration=RegisterAgentRequest(name="Ephemeral Agent"),
            heartbeat_interval=0.05,
        )
        await svc.start()
        assert svc._task is None
        await svc.stop()


@respx.mock
async def test_heartbeat_service_deregisters_on_stop():
    agent = _agent_dict()
    respx.post(f"{BASE_URL}/agents").mock(return_value=httpx.Response(201, json=agent))
    delete_route = respx.delete(f"{BASE_URL}/agents/agent-1").mock(
        return_value=httpx.Response(204)
    )

    async with AgentRegistryClient(BASE_URL, api_key=API_KEY) as client:
        async with HeartbeatService(
            client,
            registration=RegisterAgentRequest(name="Test Agent"),
            deregister_on_stop=True,
        ):
            pass

    assert delete_route.called


@respx.mock
async def test_heartbeat_service_skips_deregister_when_disabled():
    agent = _agent_dict()
    respx.post(f"{BASE_URL}/agents").mock(return_value=httpx.Response(201, json=agent))

    async with AgentRegistryClient(BASE_URL, api_key=API_KEY) as client:
        async with HeartbeatService(
            client,
            registration=RegisterAgentRequest(name="Test Agent"),
            deregister_on_stop=False,
        ):
            pass

    # No DELETE route registered — would raise if called
