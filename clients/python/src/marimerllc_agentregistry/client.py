from __future__ import annotations

import dataclasses
from typing import Optional

import httpx

from .models import (
    AgentResponse,
    DiscoveryFilter,
    EndpointRequest,
    EndpointResponse,
    PagedAgentResponse,
    RegisterAgentRequest,
    UpdateAgentRequest,
)


def _to_camel_case(snake: str) -> str:
    parts = snake.split("_")
    return parts[0] + "".join(p.capitalize() for p in parts[1:])


def _serialize(obj) -> object:
    """Recursively convert a dataclass to a JSON-serializable dict with camelCase keys."""
    if dataclasses.is_dataclass(obj) and not isinstance(obj, type):
        result = {}
        for f in dataclasses.fields(obj):
            val = getattr(obj, f.name)
            if val is None:
                continue
            result[_to_camel_case(f.name)] = _serialize(val)
        return result
    if isinstance(obj, list):
        return [_serialize(v) for v in obj]
    if isinstance(obj, dict):
        return {k: _serialize(v) for k, v in obj.items()}
    if hasattr(obj, "value"):  # IntEnum
        return int(obj)
    return obj


class AgentRegistryClient:
    """Async client for the MarimerLLC Agent Registry.

    Usage::

        async with AgentRegistryClient("https://registry.example.com", api_key="...") as client:
            agent = await client.register(RegisterAgentRequest(name="My Agent", ...))
    """

    def __init__(self, base_url: str, api_key: str, **httpx_kwargs):
        self._http = httpx.AsyncClient(
            base_url=base_url,
            headers={"X-Api-Key": api_key},
            **httpx_kwargs,
        )

    async def __aenter__(self) -> "AgentRegistryClient":
        return self

    async def __aexit__(self, *args) -> None:
        await self.aclose()

    async def aclose(self) -> None:
        await self._http.aclose()

    async def register(self, request: RegisterAgentRequest) -> AgentResponse:
        r = await self._http.post("/agents", json=_serialize(request))
        r.raise_for_status()
        return AgentResponse.from_dict(r.json())

    async def get_agent(self, agent_id: str) -> Optional[AgentResponse]:
        r = await self._http.get(f"/agents/{agent_id}")
        if r.status_code == 404:
            return None
        r.raise_for_status()
        return AgentResponse.from_dict(r.json())

    async def update_agent(self, agent_id: str, request: UpdateAgentRequest) -> AgentResponse:
        r = await self._http.put(f"/agents/{agent_id}", json=_serialize(request))
        r.raise_for_status()
        return AgentResponse.from_dict(r.json())

    async def deregister(self, agent_id: str) -> None:
        r = await self._http.delete(f"/agents/{agent_id}")
        r.raise_for_status()

    async def add_endpoint(self, agent_id: str, request: EndpointRequest) -> EndpointResponse:
        r = await self._http.post(f"/agents/{agent_id}/endpoints", json=_serialize(request))
        r.raise_for_status()
        return EndpointResponse.from_dict(r.json())

    async def remove_endpoint(self, agent_id: str, endpoint_id: str) -> None:
        r = await self._http.delete(f"/agents/{agent_id}/endpoints/{endpoint_id}")
        r.raise_for_status()

    async def heartbeat(self, agent_id: str, endpoint_id: str) -> None:
        r = await self._http.post(
            f"/agents/{agent_id}/endpoints/{endpoint_id}/heartbeat"
        )
        r.raise_for_status()

    async def renew(self, agent_id: str, endpoint_id: str) -> None:
        r = await self._http.post(f"/agents/{agent_id}/endpoints/{endpoint_id}/renew")
        r.raise_for_status()

    async def discover(self, filter: Optional[DiscoveryFilter] = None) -> PagedAgentResponse:
        f = filter or DiscoveryFilter()
        params: dict[str, str] = {}
        if f.capability:    params["capability"] = f.capability
        if f.tags:          params["tags"] = f.tags
        if f.protocol:      params["protocol"] = f.protocol
        if f.transport:     params["transport"] = f.transport
        if not f.live_only: params["liveOnly"] = "false"
        if f.page != 1:     params["page"] = str(f.page)
        if f.page_size != 20: params["pageSize"] = str(f.page_size)
        r = await self._http.get("/discover/agents", params=params)
        r.raise_for_status()
        return PagedAgentResponse.from_dict(r.json())


class SyncAgentRegistryClient:
    """Synchronous client for the MarimerLLC Agent Registry.

    Usage::

        with SyncAgentRegistryClient("https://registry.example.com", api_key="...") as client:
            agent = client.register(RegisterAgentRequest(name="My Agent", ...))
    """

    def __init__(self, base_url: str, api_key: str, **httpx_kwargs):
        self._http = httpx.Client(
            base_url=base_url,
            headers={"X-Api-Key": api_key},
            **httpx_kwargs,
        )

    def __enter__(self) -> "SyncAgentRegistryClient":
        return self

    def __exit__(self, *args) -> None:
        self.close()

    def close(self) -> None:
        self._http.close()

    def register(self, request: RegisterAgentRequest) -> AgentResponse:
        r = self._http.post("/agents", json=_serialize(request))
        r.raise_for_status()
        return AgentResponse.from_dict(r.json())

    def get_agent(self, agent_id: str) -> Optional[AgentResponse]:
        r = self._http.get(f"/agents/{agent_id}")
        if r.status_code == 404:
            return None
        r.raise_for_status()
        return AgentResponse.from_dict(r.json())

    def deregister(self, agent_id: str) -> None:
        r = self._http.delete(f"/agents/{agent_id}")
        r.raise_for_status()

    def heartbeat(self, agent_id: str, endpoint_id: str) -> None:
        r = self._http.post(f"/agents/{agent_id}/endpoints/{endpoint_id}/heartbeat")
        r.raise_for_status()

    def renew(self, agent_id: str, endpoint_id: str) -> None:
        r = self._http.post(f"/agents/{agent_id}/endpoints/{endpoint_id}/renew")
        r.raise_for_status()

    def discover(self, filter: Optional[DiscoveryFilter] = None) -> PagedAgentResponse:
        f = filter or DiscoveryFilter()
        params: dict[str, str] = {}
        if f.capability:    params["capability"] = f.capability
        if f.tags:          params["tags"] = f.tags
        if f.protocol:      params["protocol"] = f.protocol
        if f.transport:     params["transport"] = f.transport
        if not f.live_only: params["liveOnly"] = "false"
        if f.page != 1:     params["page"] = str(f.page)
        if f.page_size != 20: params["pageSize"] = str(f.page_size)
        r = self._http.get("/discover/agents", params=params)
        r.raise_for_status()
        return PagedAgentResponse.from_dict(r.json())
