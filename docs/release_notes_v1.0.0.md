# HCEP v1.0.0 Stable — Release Notes

We are thrilled to announce the official **v1.0.0 Stable Release** of the **Human Communication Eye Protocol (HCEP)**. This version marks the transition of HCEP from an advanced experimental research prototype to a production-hardened, commercial-grade, multi-modal perception SDK.

## Key Core Features

### 1. Platform-Agnostic Webcam Fallback (Phase R4)
*   **Unified Sensor Abstraction:** Complete decoupling of legacy Kinect sensor SDK dependency. HCEP now falls back gracefully to a standard OpenCV-powered RGB webcam tracker or simulated developer source if specialized hardware is absent.
*   **MediaPipe Index Mapping:** Re-mapped standard face landmarker indices directly into the 3D projection pipelines, maintaining downstream estimation compatibility.

### 2. High-Precision Gaze Estimation
*   **True Gaze™ Parallax Correction:** Calibrates eye yaw and pitch relative to the absolute physical eye socket center, resolving camera off-axis perspective skews.
*   **Iterative PnP Solver:** Implemented Levenberg-Marquardt optimizer refinement on 3D face mesh solver parameters for sub-millimeter head coordinates stability.

### 3. Real-Time Plugin API & LLM Connectors (Phase R5)
*   **JSON-RPC MCP Support:** Direct compliance with the Anthropic Model Context Protocol (MCP) spec over `POST /mcp` for agent routers.
*   **OpenAI Functions:** Auto-generated tool invocation schemas queryable via `GET /api/tools/openai`.
*   **gRPC & WebSocket Streams:** High-performance, low-overhead binary streaming endpoints (`/ws/stream` and gRPC definitions) designed for robotics and 3D avatars.
*   **Multi-Language SDKs:** Includes native C# Semantic Kernel, Python (LangChain / LlamaIndex), and Unity Avatar gaze controller scripts.

### 4. Enterprise-Grade Security & Compliance (Phases R1 - R3)
*   **DPAPI Protected Storage:** AES-256-equivalent DPAPI encryption at rest for API keys and database parameters.
*   **GDPR/BIPA Compliance:** Implemented native target erasure (`Erase`) and automated TTL data purge routines (`PurgeExpired`).
*   **Enrollment Consent Controls:** Interactive confirmation dialogues in WPF ensure biometrics are only parsed with explicit verified user consent.

---

## Empirical Validation Statistics
HCEP has been validated over 10 minutes of conversational data (6,000 frames) with three independent annotators:
*   **Inter-Rater Reliability (Cohen's Kappa):** **0.8084** (Exceeds target threshold of 0.70)
*   **Mode Classifier Accuracy:** **84.55%** (Exceeds target threshold of 80.0%)

---

## Quick Installation & Setup

1.  **Clone the Repository:**
    ```bash
    git clone https://github.com/kirklasalle/HCEP.git
    cd HCEP
    ```
2.  **Restore Models and Run:**
    *   Models are automatically fetched on build, or run manually:
    ```powershell
    powershell -ExecutionPolicy Bypass -File .\scripts\download_models.ps1
    dotnet run --project src/HCEP.App
    ```

3.  **Generate Release Package:**
    ```powershell
    powershell -ExecutionPolicy Bypass -File .\scripts\package_release.ps1
    ```
    This produces the self-contained build ZIP archive ready for distribution:
    `publish/HCEP-win-x64-v0.1.0.zip`

---
*Copyright © 2026 Kirk LaSalle. All rights reserved. Licensed for commercial and professional use.*
