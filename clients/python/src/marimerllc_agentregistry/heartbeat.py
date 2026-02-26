from __future__ import annotations

import asyncio
import logging
from typing import Optional

from .client import AgentRegistryClient
from .models import AgentResponse, RegisterAgentRequest

logger = logging.getLogger(__name__)


class HeartbeatService:
    """Manages the full agent lifecycle: register → heartbeat → deregister.

    Usage as an async context manager::

        async with HeartbeatService(
            client,
            registration=RegisterAgentRequest(
                name="My Agent",
                endpoints=[EndpointRequest(
                    name="primary",
                    transport=TransportType.Http,
                    protocol=ProtocolType.A2A,
                    address="https://my-agent.example.com",
                    liveness_model=LivenessModel.Persistent,
                    heartbeat_interval_seconds=60,
                )],
            ),
            heartbeat_interval=45.0,
        ) as svc:
            print(f"Running as agent {svc.agent_id}")
            await asyncio.Event().wait()  # run until cancelled

    Or manually::

        svc = HeartbeatService(client, registration=...)
        await svc.start()
        # ... your agent logic ...
        await svc.stop()
    """

    def __init__(
        self,
        client: AgentRegistryClient,
        registration: RegisterAgentRequest,
        heartbeat_interval: float = 30.0,
        deregister_on_stop: bool = True,
    ) -> None:
        self._client = client
        self._registration = registration
        self._interval = heartbeat_interval
        self._deregister_on_stop = deregister_on_stop
        self._agent: Optional[AgentResponse] = None
        self._task: Optional[asyncio.Task] = None

    @property
    def agent_id(self) -> Optional[str]:
        """The registered agent's ID, available after :meth:`start` completes."""
        return self._agent.id if self._agent else None

    @property
    def agent(self) -> Optional[AgentResponse]:
        """The full agent response, available after :meth:`start` completes."""
        return self._agent

    async def start(self) -> None:
        logger.info("Registering agent '%s'...", self._registration.name)
        self._agent = await self._client.register(self._registration)
        logger.info("Agent registered with ID %s", self._agent.id)

        persistent_ids = [
            e.id for e in self._agent.endpoints if e.liveness_model == "Persistent"
        ]
        if persistent_ids:
            self._task = asyncio.create_task(
                self._heartbeat_loop(self._agent.id, persistent_ids),
                name=f"heartbeat-{self._agent.id}",
            )

    async def stop(self) -> None:
        if self._task and not self._task.done():
            self._task.cancel()
            try:
                await self._task
            except asyncio.CancelledError:
                pass

        if self._deregister_on_stop and self._agent:
            logger.info("Deregistering agent %s...", self._agent.id)
            try:
                await self._client.deregister(self._agent.id)
                logger.info("Agent %s deregistered", self._agent.id)
            except Exception:
                logger.warning(
                    "Failed to deregister agent %s on shutdown",
                    self._agent.id,
                    exc_info=True,
                )

    async def _heartbeat_loop(self, agent_id: str, endpoint_ids: list[str]) -> None:
        while True:
            await asyncio.sleep(self._interval)
            for eid in endpoint_ids:
                try:
                    await self._client.heartbeat(agent_id, eid)
                    logger.debug("Heartbeat sent for endpoint %s", eid)
                except Exception:
                    logger.warning("Heartbeat failed for endpoint %s", eid, exc_info=True)

    async def __aenter__(self) -> "HeartbeatService":
        await self.start()
        return self

    async def __aexit__(self, *args) -> None:
        await self.stop()
