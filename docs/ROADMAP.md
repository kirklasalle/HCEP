# HCEP — Development Roadmap

**Product:** HCEP — Human Communication Eye Points  
**Version:** v1.0.0 (Stable Release)  
**Author:** Kirk LaSalle  
**Last Updated:** June 6, 2026  

---

## Overview

This roadmap documents the phased path from the initial alpha codebase (v0.1.0) to the final production-ready v1.0.0 stable commercial release. Every phase has been completed and verified.

---

## Final Project State (v1.0.0 Stable)

| Metric | Value |
|---|---|
| Source projects | 12 (including HCEP.Plugin.Api) |
| Source files | ~150 |
| Lines of code | ~12,500 |
| Test project | 1 (HCEP.Tests) |
| Unit & Integration tests | 169 (all passing) |
| Build status | Green (0 warnings, 0 errors, TreatWarningsAsErrors active) |
| SDK Platforms | Python (LangChain/LlamaIndex), C# (Semantic Kernel), Unity, Unreal Engine C++ |
| API Layer | REST, WebSockets, Model Context Protocol (MCP) |

---

## Completed Phases

### Phase 1 — Integration Testing & Runtime Wiring — [COMPLETED]
*   **Goal:** End-to-end pipeline running with simulated sensor data.
*   **Milestones:**
    *   [x] Synthetic frames flow through channels at 30fps.
    *   [x] Knowledge store Persisted in JSON format.
    *   [x] Strategy D hybrid adapter fallback verified.
    *   [x] Ollama and GPT-5-mini routing and prompt adaptation verified.
    *   [x] serilog structured logging and latency metrics implemented.

### Phase 2 — Kinect Hardware Integration — [COMPLETED]
*   **Goal:** Real Kinect v1 sensor driving the pipeline.
*   **Milestones:**
    *   [x] Active skeleton/face streams running on Kinect v1.
    *   [x] PnP head pose solver tuned with real-world anthropometrics.
    *   [x] ArcFace ONNX face embedding recognition active.
    *   [x] Whisper speech-to-text with VAD filtering.

### Phase 3 — HCEP Theory Validation — [COMPLETED]
*   **Goal:** Validate HCEP 5-mode theory empirically.
*   **Milestones:**
    *   [x] Cohen's Kappa score of **0.8084** achieved on ground truth segment classification.
    *   [x] Classifier accuracy validated at **84.55%** (target $\ge 80\%$).
    *   [x] Stability hysteresis (5-frame buffer) and confidence cone thresholds optimized.

### Phase 4 — Security & Platform Independence — [COMPLETED]
*   **Goal:** Protect user privacy and expand sensor support.
*   **Milestones:**
    *   [x] DPAPI-based key encryption for local LLM key storage.
    *   [x] Explicit UI biometric consent prompts on start.
    *   [x] GDPR-compliant erase methods added to data stores.
    *   [x] Webcam Sensor Source implemented using OpenCV fallback.

### Phase 5 — LLM Plugin & Multi-Platform SDKs — [COMPLETED]
*   **Goal:** Expose HCEP as an agentic tool and character driver.
*   **Milestones:**
    *   [x] Model Context Protocol (MCP) server endpoints mapped.
    *   [x] OpenAI Function calling schemas generated on the fly.
    *   [x] LangChain and LlamaIndex tool wrappers completed (Python).
    *   [x] Semantic Kernel plugin completed (C#).
    *   [x] Unity real-time bone-tracking animation controller script.
    *   [x] Unreal Engine native C++ character animation actor component.

### Phase 6 — Commercial Packaging & Release — [COMPLETED]
*   **Goal:** First public/commercial release with True Gaze™ parallax correction.
*   **Milestones:**
    *   [x] True Gaze™ parallax offset calibration implemented to resolve camera off-axis angle skew.
    *   [x] Interactive True Gaze™ Parallax Simulator built for web browser showcase.
    *   [x] MSIX AppxManifest.xml generated.
    *   [x] release packaging script (`package_release.ps1`) automated.
    *   [x] Tagged v1.0.0 release packages compiled and zipped.

### Phase 7 — Autonomous Avatar Responsiveness Tuning — [COMPLETED]
*   **Goal:** Decouple avatar movements from user pose mirroring to establish autonomous observer behaviors.
*   **Milestones:**
    *   [x] Head rotation decoupled from direct tracking inputs (passing Vector3.Zero to SetHeadPose).
    *   [x] Double-projection mesh pipeline built to construct static/neutral projected face shape (NeutralFaceMeshVertices2D).
    *   [x] 3D wireframe rendering updated to use neutral mesh, enabling independent animation float/gaze responsiveness.
    *   [x] Isolated the Permanent Active Directives (PAD) from the responsive behavior modifications.

---

## Future Post-v1.0 Roadmap

1.  **Multi-Person Telemetry Extension:** Extend the pipeline to analyze cognitive-emotional modes of 3+ simultaneous participants.
2.  **Voice Prosody Fusion:** Train an audio model to augment the face Action Unit weights with pitch/prosody emotion classifiers.
3.  **Cross-Platform Client UI:** Port the WPF app to Avalonia UI for native Linux and macOS support.
