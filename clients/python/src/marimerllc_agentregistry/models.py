from __future__ import annotations

from dataclasses import dataclass, field
from enum import IntEnum
from typing import Optional


class TransportType(IntEnum):
    Http = 0
    Amqp = 1
    AzureServiceBus = 2


class ProtocolType(IntEnum):
    Unknown = 0
    A2A = 1
    MCP = 2
    ACP = 3


class LivenessModel(IntEnum):
    Ephemeral = 0
    Persistent = 1


@dataclass
class CapabilityRequest:
    name: str
    description: Optional[str] = None
    tags: Optional[list[str]] = None


@dataclass
class EndpointRequest:
    name: str
    transport: TransportType
    protocol: ProtocolType
    address: str
    liveness_model: LivenessModel
    ttl_seconds: Optional[int] = None
    heartbeat_interval_seconds: Optional[int] = None
    protocol_metadata: Optional[str] = None


@dataclass
class RegisterAgentRequest:
    name: str
    description: Optional[str] = None
    labels: Optional[dict[str, str]] = None
    capabilities: Optional[list[CapabilityRequest]] = None
    endpoints: Optional[list[EndpointRequest]] = None


@dataclass
class UpdateAgentRequest:
    name: str
    description: Optional[str] = None
    labels: Optional[dict[str, str]] = None


@dataclass
class CapabilityResponse:
    id: str
    name: str
    description: Optional[str]
    tags: list[str]

    @classmethod
    def from_dict(cls, d: dict) -> "CapabilityResponse":
        return cls(
            id=d["id"],
            name=d["name"],
            description=d.get("description"),
            tags=d.get("tags", []),
        )


@dataclass
class EndpointResponse:
    id: str
    name: str
    transport: str
    protocol: str
    address: str
    liveness_model: str
    ttl_seconds: Optional[int]
    heartbeat_interval_seconds: Optional[int]
    is_live: Optional[bool]
    protocol_metadata: Optional[str]

    @classmethod
    def from_dict(cls, d: dict) -> "EndpointResponse":
        return cls(
            id=d["id"],
            name=d["name"],
            transport=d["transport"],
            protocol=d["protocol"],
            address=d["address"],
            liveness_model=d["livenessModel"],
            ttl_seconds=d.get("ttlSeconds"),
            heartbeat_interval_seconds=d.get("heartbeatIntervalSeconds"),
            is_live=d.get("isLive"),
            protocol_metadata=d.get("protocolMetadata"),
        )


@dataclass
class AgentResponse:
    id: str
    name: str
    description: Optional[str]
    owner_id: str
    labels: dict[str, str]
    capabilities: list[CapabilityResponse]
    endpoints: list[EndpointResponse]
    created_at: str
    updated_at: str

    @classmethod
    def from_dict(cls, d: dict) -> "AgentResponse":
        return cls(
            id=d["id"],
            name=d["name"],
            description=d.get("description"),
            owner_id=d["ownerId"],
            labels=d.get("labels", {}),
            capabilities=[CapabilityResponse.from_dict(c) for c in d.get("capabilities", [])],
            endpoints=[EndpointResponse.from_dict(e) for e in d.get("endpoints", [])],
            created_at=d["createdAt"],
            updated_at=d["updatedAt"],
        )


@dataclass
class PagedAgentResponse:
    items: list[AgentResponse]
    total_count: int
    page: int
    page_size: int
    total_pages: int
    has_next_page: bool
    has_previous_page: bool

    @classmethod
    def from_dict(cls, d: dict) -> "PagedAgentResponse":
        return cls(
            items=[AgentResponse.from_dict(a) for a in d.get("items", [])],
            total_count=d["totalCount"],
            page=d["page"],
            page_size=d["pageSize"],
            total_pages=d["totalPages"],
            has_next_page=d["hasNextPage"],
            has_previous_page=d["hasPreviousPage"],
        )


@dataclass
class DiscoveryFilter:
    capability: Optional[str] = None
    tags: Optional[str] = None       # comma-separated, e.g. "nlp,summarize"
    protocol: Optional[str] = None   # "A2A", "MCP", "ACP", "Unknown"
    transport: Optional[str] = None  # "Http", "Amqp", "AzureServiceBus"
    live_only: bool = True
    page: int = 1
    page_size: int = 20
