# HCEP Release Notes

---

For the latest release, see [release_notes_v1.3.0.md](release_notes_v1.3.0.md).

---

# HCEP v1.2.0 — Avatar Expression & Contextual Intelligence

**Released:** July 3, 2026

## What's New

### Avatar Expression System — Complete Facial Communication

The HCEP avatar now communicates through its entire face, not just its eyes.

**Eyebrow Animation**
Both the 2D Happy Face and 3D Wireframe avatars now animate eyebrows in real-time, driven by Kinect Action Units and HCEP mode:

- AU3 (BrowLowerer) produces concentration furrow during LOGIC and THINK modes
- AU5 (OuterBrowRaiser) produces surprise/query raise
- HEART mode autonomously triggers the AU1 inner empathy raise
- 150ms EMA smoothing for biological naturalness

**Phoneme-Accurate Lip Sync (Phase 13 — Complete)**
The avatar's mouth now moves with the correct shape for each phoneme during speech:

- `VisemeController` maps all 21 SAPI phoneme groups to 5 mouth parameters (JawOpen, LipRound, LipSpread, LipCompressed, UpperLipRetract) per the Preston Blair animation canon
- Windows SAPI: phoneme-accurate via `VisemeReached` events (★★★★★)
- Cloud TTS (OpenAI, ElevenLabs): amplitude-driven approximate lip sync
- 60ms EMA co-articulation blending between phoneme transitions
- Scientific basis: McGurk Effect (McGurk & MacDonald, 1976) — wrong lip sync actively degrades speech intelligibility

### Contextual Intelligence (Phase 14 — Complete)

HCEP now understands when and where it exists:

- `ContextSnapshot` captures Time × Space × Situation in every LLM prompt
- `TimeContextProvider` classifies time-of-day, day type, season, derives `CommunicationRegister`
- `SilenceProtocolEvaluator` — 7 evidence-based rules determine when the avatar should stay silent (Jaworski 1993; Duncan 1972)
- Every `PromptAsync` call is enriched with `[TimeOfDay | Environment | Activity | Register | SilenceProtocol]`

### HCEP.Speech — New Project

New `src/HCEP.Speech/` project: `HybridTtsEngine`, `WindowsTtsSynthesizer`, `OpenAiTtsSynthesizer`, `ElevenLabsTtsSynthesizer`, `VisemeController`, `ISpeechSynthesizer`.

### Calibration Fix

Critical sign error fixed: `t >= 0f` guard incorrectly rejected all valid calibrations. Avatar head responsiveness improved (TrackingInfluence 0.04 → 0.15; HeadFollowTimeConstantSec 12.0 → 0.8).

### Test Suite

193 tests passing (up from 169). New: concurrency stress, ArcFace negative-path, circuit-breaker, knowledge store capacity tests.

---

# HCEP v1.1.0 — Production Hardening & Security Audit

**Released:** July 3, 2026

## Security

- **Thread-safety**: Replaced broken `Interlocked.CompareExchange` volatile-read with `Volatile.Read/Write` on all cross-thread `VisionPipeline` properties
- **Windows Credential Manager**: API keys stored in WCM vault via `WindowsCredentialStore` (P/Invoke) — never visible in process listings
- **Circuit Breaker**: Cloud LLM circuit breaker (threshold=3, cooldown=30s)

## Reliability

- `InMemoryKnowledgeStore`: capacity limits (500 subjects × 1,000 triples), LRU eviction, input validation
- `ArcFaceRecognizer.LoadModel()`: wrapped in try/catch — corrupted ONNX no longer crashes pipeline
- `AutoFallbackSeconds`: now a configurable property (was hardcoded const)

## Observability

- Frame-drop warnings on all channel back-pressure paths
- Audio flush errors escalated from LogDebug to LogWarning
- No-LLM fallback logged explicitly

---

# HCEP v1.0.0 Stable — First Production Release

**Released:** June 19, 2026

We are thrilled to announce the official **v1.0.0 Stable Release** of the **Human Communication Eye Protocol (HCEP)**. This version marks the transition of HCEP from an advanced experimental research prototype to a production-hardened, commercial-grade, multi-modal perception SDK.

## Key Core Features

### 1. Platform-Agnostic Webcam Fallback

- Unified Sensor Abstraction: Complete decoupling of legacy Kinect sensor SDK dependency. HCEP now falls back gracefully to a standard OpenCV-powered RGB webcam tracker or simulated developer source if specialized hardware is absent.

### 2. High-Precision Gaze Estimation

- **True Gaze™ Parallax Correction:** Calibrates eye yaw and pitch relative to the absolute physical eye socket center, resolving camera off-axis perspective skews.
- **Iterative PnP Solver:** Implemented Levenberg-Marquardt optimizer refinement on 3D face mesh solver parameters for sub-millimeter head coordinates stability.

### 3. Real-Time Plugin API & LLM Connectors

- JSON-RPC MCP Support, OpenAI Functions, gRPC & WebSocket Streams
- Multi-Language SDKs: C# Semantic Kernel, Python (LangChain / LlamaIndex), Unity, Unreal Engine C++

### 4. Enterprise-Grade Security & Compliance

- DPAPI Protected Storage, GDPR/BIPA Compliance, Biometric Enrollment Consent Controls

## Empirical Validation Statistics

- **Inter-Rater Reliability (Cohen's Kappa):** **0.8084** (Exceeds target ≥ 0.70)
- **Mode Classifier Accuracy:** **84.55%** (Exceeds target ≥ 80.0%)

---
*Copyright © 2026 Kirk LaSalle. All rights reserved. Licensed for commercial and professional use.*

We are thrilled to announce the official **v1.0.0 Stable Release** of the **Human Communication Eye Protocol (HCEP)**. This version marks the transition of HCEP from an advanced experimental research prototype to a production-hardened, commercial-grade, multi-modal perception SDK.

## Key Core Features

### 1. Platform-Agnostic Webcam Fallback (Phase R4)

* **Unified Sensor Abstraction:** Complete decoupling of legacy Kinect sensor SDK dependency. HCEP now falls back gracefully to a standard OpenCV-powered RGB webcam tracker or simulated developer source if specialized hardware is absent.
- **MediaPipe Index Mapping:** Re-mapped standard face landmarker indices directly into the 3D projection pipelines, maintaining downstream estimation compatibility.

### 2. High-Precision Gaze Estimation

* **True Gaze™ Parallax Correction:** Calibrates eye yaw and pitch relative to the absolute physical eye socket center, resolving camera off-axis perspective skews.
- **Iterative PnP Solver:** Implemented Levenberg-Marquardt optimizer refinement on 3D face mesh solver parameters for sub-millimeter head coordinates stability.

### 3. Real-Time Plugin API & LLM Connectors (Phase R5)

* **JSON-RPC MCP Support:** Direct compliance with the Anthropic Model Context Protocol (MCP) spec over `POST /mcp` for agent routers.
- **OpenAI Functions:** Auto-generated tool invocation schemas queryable via `GET /api/tools/openai`.
- **gRPC & WebSocket Streams:** High-performance, low-overhead binary streaming endpoints (`/ws/stream` and gRPC definitions) designed for robotics and 3D avatars.
- **Multi-Language SDKs:** Includes native C# Semantic Kernel, Python (LangChain / LlamaIndex), and Unity Avatar gaze controller scripts.

### 4. Enterprise-Grade Security & Compliance (Phases R1 - R3)

* **DPAPI Protected Storage:** AES-256-equivalent DPAPI encryption at rest for API keys and database parameters.
- **GDPR/BIPA Compliance:** Implemented native target erasure (`Erase`) and automated TTL data purge routines (`PurgeExpired`).
- **Enrollment Consent Controls:** Interactive confirmation dialogues in WPF ensure biometrics are only parsed with explicit verified user consent.

---

## Empirical Validation Statistics

HCEP has been validated over 10 minutes of conversational data (6,000 frames) with three independent annotators:
- **Inter-Rater Reliability (Cohen's Kappa):** **0.8084** (Exceeds target threshold of 0.70)
- **Mode Classifier Accuracy:** **84.55%** (Exceeds target threshold of 80.0%)

---

## Quick Installation & Setup

1. **Clone the Repository:**

    ```bash
    git clone https://github.com/kirklasalle/HCEP.git
    cd HCEP
    ```

2. **Restore Models and Run:**
    - Models are automatically fetched on build, or run manually:

    ```powershell
    powershell -ExecutionPolicy Bypass -File .\scripts\download_models.ps1
    dotnet run --project src/HCEP.App
    ```

3. **Generate Release Package:**

    ```powershell
    powershell -ExecutionPolicy Bypass -File .\scripts\package_release.ps1
    ```

    This produces the self-contained build ZIP archive ready for distribution:
    `publish/HCEP-win-x64-v0.1.0.zip`

---
*Copyright © 2026 Kirk LaSalle. All rights reserved. Licensed for commercial and professional use.*
