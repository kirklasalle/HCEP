# HCEP — Development Roadmap

**Product:** HCEP — Human Communication Eye Points 
**Version:** 0.1.0 → 1.0.0  
**Author:** Kirk LaSalle  
**Last Updated:** February 23, 2026  

---

## Overview

This roadmap defines the phased path from the current alpha codebase (v0.1.0, 9 projects compiling, 102 unit tests green) to a production-ready v1.0 release. Each phase has clear acceptance criteria and builds incrementally on the previous phase.

---

## Current State (v0.1.0 Alpha)

| Metric | Value |
|---|---|
| Source projects | 11 |
| Source files | ~110 |
| Lines of code | ~7,500 |
| Test project | 1 (HCEP.Tests) |
| Unit tests | 102 (all passing) |
| Build status | Green (0 errors) |
| Runtime status | End-to-end verified with real Kinect v1 hardware |

---

## Phase 1 — Integration Testing & Runtime Wiring (v0.2.0)

**Timeline:** 2–3 weeks  
**Goal:** End-to-end pipeline running with simulated sensor data

### Milestones

- [ ] **M1.1 — Simulated pipeline end-to-end**
  - Wire `SimulatedSensorSource` → channels → `HcepPipelineOrchestrator`
  - Verify 30fps synthetic frames flow through full pipeline
  - Validate HCEP mode transitions occur with simulated gaze patterns

- [ ] **M1.2 — Knowledge store integration test**
  - End-to-end: face detected → person identified → knowledge stored → LLM context injected
  - Verify JSON persistence roundtrip in running app
  - Test UKS Strategy D fallback behavior (DLL absent → InMemoryKnowledgeStore)

- [ ] **M1.3 — LLM integration test**
  - Verify Ollama local inference with llama3:8b
  - Verify GPT-5-mini cloud call with agentic tool loop
  - Validate HCEP mode modulates system prompt
  - Test routing: THINK → local, SPIRIT → cloud

- [ ] **M1.4 — Telemetry validation**
  - Serilog structured logs written to file + console
  - FPS counter accurate within 1fps
  - Pipeline latency measured end-to-end

### Acceptance Criteria
- App starts with simulated sensor, displays HCEP mode in dashboard
- At least one full conversation cycle (detect person → LLM response) completes
- All 102 existing tests still green

---

## Phase 2 — Kinect Hardware Integration (v0.3.0)

**Timeline:** 2–3 weeks  
**Goal:** Real Kinect v1 sensor driving the pipeline

### Milestones

- [x] **M2.1 — Kinect sensor activation**
  - Connect to Xbox 360 Kinect via USB
  - Color, depth, skeleton, face, audio streams initialized
  - Verify 30fps throughput on real hardware
  - Full-body (20-joint) and seated (10-joint) tracking modes with runtime toggle

- [ ] **M2.2 — Gaze estimation calibration**
  - Test PnP head pose with real face landmarks
  - Tune eye-in-head rotation sensitivity
  - Calibrate confidence cone radius for real-world distances

- [ ] **M2.3 — Face recognition with real faces**
  - ArcFace ONNX model loaded and producing 512-d embeddings
  - Identity enrollment UI or automatic enrollment
  - Cosine similarity threshold tuning (0.6 default)

- [ ] **M2.4 — Speech pipeline with real audio**
  - Whisper.net model loaded (ggml-base.en.bin)
  - Real-time speech-to-text from Kinect mic array
  - VAD filtering noise/silence

### Acceptance Criteria
- Real Kinect feeds live data through the entire pipeline ✔
- A real person's face is tracked, recognized, and named in the knowledge store
- Spoken words appear in transcript and reach the LLM

---

## Phase 3 — HCEP Theory Validation (v0.4.0)

**Timeline:** 3–4 weeks  
**Goal:** Validate Kirk LaSalle's HCEP 5-mode theory against real conversations

### Milestones

- [ ] **M3.1 — HCEP mode ground truth dataset**
  - Record 10+ minutes of labeled face-to-face conversation
  - Expert label each segment with HCEP mode (LOGIC, AFFECT, SPIRIT, HEART, THINK)
  - Store as timestamped JSON ground truth

- [ ] **M3.2 — Mode classification accuracy measurement**
  - Run recorded sessions through `HcepModeAnalyzer`
  - Compute accuracy, precision, recall per mode
  - Target: > 80% overall accuracy

- [ ] **M3.3 — Temporal hysteresis tuning**
  - Experiment with `ModeStabilityFrames` (default 5)
  - Experiment with `ModeTransitionMinConfidence` (default 0.4)
  - Optimize for stability vs. responsiveness

- [ ] **M3.4 — Theory refinement**
  - Document cases where the 5-mode model is insufficient
  - Identify edge cases (glasses, low light, extreme angles)
  - Publish findings for HCEP theory paper

### Acceptance Criteria
- Labeled dataset with inter-rater reliability > 0.7 (Cohen's kappa)
- Mode classification accuracy > 80% on held-out test set
- Documented theory refinements based on empirical data

---

## Phase 4 — Agentic LLM Enhancement (v0.5.0)

**Timeline:** 2–3 weeks  
**Goal:** Full agentic reasoning with persistent memory

### Milestones

- [ ] **M4.1 — Multi-turn agentic conversation**
  - Verify 5-step agentic reasoning loop works reliably
  - Test all 5 HCEP tools: `query_knowledge`, `get_hcep_state`, `store_knowledge`, `summarize_person`, `analyze_gaze_pattern`
  - Handle tool errors gracefully

- [ ] **M4.2 — Conversation continuity**
  - Person returns after absence → system recalls previous context
  - Knowledge accumulates over multiple sessions
  - LLM responses reference prior conversation naturally

- [ ] **M4.3 — Response quality tuning**
  - Tune HCEP mode-specific system prompts for natural conversation
  - A/B test local vs. cloud responses
  - Measure user satisfaction (informal)

- [ ] **M4.4 — Latency optimization**
  - Profile agentic loop latency
  - Implement parallel tool execution where possible
  - Target: < 3s for local, < 5s for cloud agentic responses

### Acceptance Criteria
- Multi-turn conversations with tool use complete successfully
- Person-specific knowledge persists across app restarts
- Response latency within targets

---

## Phase 5 — Polish & Production Hardening (v0.9.0)

**Timeline:** 3–4 weeks  
**Goal:** Production-quality release candidate

### Milestones

- [x] **M5.1 — Error handling & resilience**
  - Global exception handler in WPF ✔
  - Sensor disconnect → graceful reconnect
  - LLM timeout → user-friendly fallback message
  - Model file missing → clear error + download instructions

- [ ] **M5.2 — Configuration system**
  - JSON-based settings file (`HCEP-config.json`)
  - Override: Kinect device index, LLM model names, API keys
  - Override: confidence thresholds, hysteresis parameters
  - Hot-reload support for non-destructive changes

- [x] **M5.3 — Dashboard enhancement**
  - Gaze visualization overlay on color feed ✔
  - Skeleton wireframe with full-body/seated toggle ✔
  - Face bounding box and 87-point wireframe overlay ✔
  - Sitting/standing auto-detection ✔
  - Face schematic with gaze crosshair and region dots ✔
  - Action unit bar charts and head pose display ✔
  - Drag-resizable panel layout (GridSplitters) ✔
  - HCEP mode history timeline
  - Person gallery with face thumbnails
  - Real-time confidence gauges

- [ ] **M5.4 — Performance optimization**
  - Memory profiling → eliminate allocations in hot path
  - `System.Threading.Channels` backpressure tuning
  - ONNX model warm-up on startup

- [ ] **M5.5 — Test coverage expansion**
  - Integration tests with `SimulatedSensorSource`
  - LLM mock tests for agentic loop
  - Target: > 70% line coverage

### Acceptance Criteria
- No unhandled exceptions during 30-minute continuous operation
- Memory stable (no leaks) over 1 hour
- Test coverage > 70%
- Dashboard shows all key metrics in real-time

---

## Phase 6 — Release (v1.0.0)

**Timeline:** 1–2 weeks  
**Goal:** First public/commercial release with the "True Gaze" Avatar capability

### Milestones

- [ ] **M6.1 — The "True Gaze" Avatar (Absolute Goal & Mark of Success)**
  - Implement the **Screen-to-Camera Calibration Matrix**: calculate the delta between the camera's optical axis and the avatar's screen position.
  - Implement **Avatar Eye-Controller Logic**: map human eye-socket tracking to the avatar's IK rig.
  - Implement **Micro-Saccades**: the avatar must look dynamically between the user's left and right eye sockets, not just a static center-face stare.
  - Save the Calibration State for persistent "True Gaze" parallax correction.

- [ ] **M6.2 — Documentation & Packaging**
  - PRD finalized and User Guide polished.
  - Developer Guide reviewed.
  - Installer / MSIX package and Model file download script.

- [ ] **M6.3 — Release artifacts**
  - Tagged release (v1.0.0)
  - Release notes, binary distribution, and demo video proving the True Gaze parallax fix.

### Acceptance Criteria
- Clean install on fresh Windows 10/11 machine.
- End-to-end demo: Avatar's eyes follow the user in real-time, correcting for the camera-to-screen parallax, and performing natural micro-saccades between the user's eye sockets.

---

## Future Phases (Post-1.0)

| Phase | Feature | Priority |
|---|---|---|
| 2.0 | **Azure Kinect / Kinect v2 support** | High |
| 2.0 | **Webcam support** (dlib/MediaPipe gaze) | High |
| 2.x | **Multi-person conversation** (> 2 people) | Medium |
| 2.x | **Real-time 3D gaze visualization** | Medium |
| 3.0 | **Web API / REST endpoint** | Medium |
| 3.0 | **Cross-platform** (.NET MAUI / Avalonia) | Low |
| 3.x | **Diarization** (multi-speaker identification) | Low |
| 3.x | **Emotion recognition** (from voice prosody) | Medium |

---

## Risk Register

| Risk | Phase | Mitigation |
|---|---|---|
| Kinect v1 hardware failure | Phase 2 | Maintain SimulatedSensorSource as always-available fallback |
| GPT-5-mini pricing/availability | Phase 4 | Ollama local fallback, abstracted `ILlmEngine` interface |
| HCEP theory accuracy < target | Phase 3 | Iterative threshold tuning, potential mode consolidation |
| Windows-only limitation | Phase 6+ | Cross-platform abstraction in Core + Spatial layers |

---

*Copyright © 2026 Kirk LaSalle. All rights reserved.*
