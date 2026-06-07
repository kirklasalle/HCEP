# ──────────────────────────────────────────────────────────────
# HCEP SDK — LangChain Tool Wrapper
# Copyright © 2026 Kirk LaSalle. All rights reserved.
# ──────────────────────────────────────────────────────────────

import requests
from typing import Type, Optional
from pydantic import BaseModel, Field
from langchain.tools import BaseTool

class HcepStateInput(BaseModel):
    """Input for HcepStateTool."""
    pass

class HcepStateTool(BaseTool):
    name: str = "get_hcep_state"
    description: str = (
        "Queries HCEP (Human Communication Eye Protocol) for the active user's real-time gaze, "
        "attention target, head coordinates, and inferred cognitive-emotional mode (e.g. Logic, Affect, Spirit, Heart, Think)."
    )
    args_schema: Type[BaseModel] = HcepStateInput
    base_url: str = "http://localhost:5000"

    def _run(self, *args, **kwargs) -> str:
        """Use the tool synchronously."""
        try:
            response = requests.get(f"{self.base_url}/api/state", timeout=5)
            if response.status_code == 200:
                return response.text
            return f"Error: HCEP returned status code {response.status_code}"
        except Exception as e:
            return f"Error connecting to HCEP Plugin API: {str(e)}"

    async def _arun(self, *args, **kwargs) -> str:
        """Use the tool asynchronously."""
        # Standard HTTP client run in executor or using httpx
        import asyncio
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, self._run)
