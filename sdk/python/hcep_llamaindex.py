# ──────────────────────────────────────────────────────────────
# HCEP SDK — LlamaIndex Tool Wrapper
# Copyright © 2026 Kirk LaSalle. All rights reserved.
# ──────────────────────────────────────────────────────────────

import requests
from llama_index.core.tools import FunctionTool

def get_hcep_state(base_url: str = "http://localhost:5000") -> str:
    """
    Queries HCEP (Human Communication Eye Protocol) for the active user's real-time gaze,
    attention target, head coordinates, and inferred cognitive-emotional mode (e.g. Logic, Affect, Spirit, Heart, Think).
    """
    try:
        response = requests.get(f"{base_url}/api/state", timeout=5)
        if response.status_code == 200:
            return response.text
        return f"Error: HCEP API returned status code {response.status_code}"
    except Exception as e:
        return f"Error connecting to HCEP Plugin API: {str(e)}"

def create_hcep_tool(base_url: str = "http://localhost:5000") -> FunctionTool:
    """Helper to instantiate the HCEP LlamaIndex tool wrapper."""
    return FunctionTool.from_defaults(
        fn=lambda: get_hcep_state(base_url),
        name="get_hcep_state",
        description="Query the real-time cognitive state, gaze region, and identity of the tracked person from the HCEP engine."
    )
