# HCEP — Human Communication Eye Protocol

[![Build](https://img.shields.io/badge/build-passing-brightgreen)]()
[![Tests](https://img.shields.io/badge/tests-169%20passing-brightgreen)]()
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)]()
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078D6)]()
[![License](https://img.shields.io/badge/license-Proprietary-red)]()

**HCEP** is a real-time multi-modal perception platform that fuses sensor input (Kinect v1 or standard USB webcams) with a hybrid LLM engine to analyze human communication through eye contact patterns, facial expressions, body tracking, and speech.

It implements Kirk LaSalle's **HCEP (Human Conversation Eye Points)** theory — a novel 5-mode cognitive-emotional classification system that decodes the unspoken language of eye contact during face-to-face conversation.

---

## The 5 HCEP Modes

| Mode | Eye Pattern | Cognitive State | AI Response Style |
|---|---|---|---|
| **LOGIC** | Structured gaze, on-face | Analytical processing | Precise, factual, numbered lists |
| **AFFECT** | Social Triangle (eyes + mouth) | Emotional engagement | Warm, empathetic, feeling-first |
| **SPIRIT** | Sustained mutual gaze | Deep authentic rapport | Personal, genuine, unstructured |
| **HEART** | Lower-face + empathic markers | Empathic resonance | Supportive, validating, caring |
| **THINK** | Gaze aversion, defocused | Internal processing | Brief, non-intrusive, space-giving |

---

## Screenshot

The HCEP dashboard provides a real-time 3-column layout with live sensor feed, gaze/face visualization, and AI assistant:

- **Left**: Live Kinect/Webcam RGB video with skeleton wireframe overlay (20-joint full body or 10-joint seated), face bounding box, and 87-point facial feature wireframe
- **Center**: HCEP mode classification, gaze/cognitive/valence state, face schematic with gaze crosshair, action unit bars, head pose, and speech log
- **Right**: Pipeline metrics (FPS, latency, tracked persons) and LLM chat assistant

All panel boundaries are drag-resizable via visible GridSplitters.

---

## True Gaze™ Parallax Calibration

To eliminate gaze skewing caused by off-axis sensor placement (such as mounting a webcam on top of a monitor bezel), HCEP implements a dynamic 3D coordinate co-registration. This shifts tracking perspective from the sensor's lens center back to the active user-avatar focal line of sight:

![True Gaze Parallax Calibration](docs/images/parallax_calibration.svg)

---

## Features

### Sensor Input & Hardware Fallback

- 30fps color, depth, skeleton, face, and audio streams
- Full-body (20-joint) and seated (10-joint) skeleton tracking with runtime toggle
- **Webcam Sensor Source:** Native fallback to standard OpenCV USB webcams or simulated developer input when specialized hardware is absent
- 87+ 2D/3D facial feature points per frame
- 6 Action Units (lip raise, jaw lower, lip corner, brow lower, brow raise, outer brow raise)
- 4-microphone beam-formed audio array with source angle

### Gaze Estimation & Correction

- 3-stage pipeline: PnP Head Pose (with Levenberg-Marquardt optimizer) → Eye-in-Head Rotation → Hybrid Fusion
- **True Gaze™ Parallax Correction:** Calibrates gaze yaw/pitch relative to 3D socket centers to resolve camera off-axis perspective skews
- Confidence cone gaze target classification (13 regions)
- Temporal smoothing with exponential moving average and hysteresis threshold gating
- Saccade detection using Main Sequence equation
- **Empirical Accuracy:** Validated at 84.55% classification accuracy and Cohen's Kappa of 0.8084 (exceeding targets)

![Three-Stage Gaze Triangulation Geometry](docs/images/gaze_geometry.svg)

### Plugin API & LLM Integrations

- **Model Context Protocol (MCP):** Serves JSON-RPC tools list and HCEP state snapshots over `POST /mcp`
- **OpenAI Function Calling:** Auto-generates GPT-4/o1 tool invocation schemas at `/api/tools/openai`
- **SDK Integrations:** Built-in wrappers for LangChain, LlamaIndex, Semantic Kernel, Unity (C#), and Unreal Engine (C++)
- **Enterprise-Grade Compliance:** AES-256 equivalent DPAPI key storage, GDPR user erasure, and UI biometrics consent dialogs

### HCEP Analysis

- Real-time 5-mode classification (LOGIC, AFFECT, SPIRIT, HEART, THINK)
- Temporal hysteresis (5-frame stability, ~170ms at 30fps)
- 12 cognitive state classifications
- Emotional valence from Action Unit weights
- Social Triangle detection for AFFECT mode

### Video Overlays

- Full-body skeleton wireframe (green solid lines for tracked, dashed for inferred joints)
- Automatic sitting/standing detection with posture label
- Face bounding box (yellow rectangle)
- 87-point facial feature wireframe with edge chains (eyes, eyebrows, lips, jaw, nose)
- Pupil markers (magenta dots at indices 69, 73)
- Pinhole camera projection (fx=fy=525, cx=320, cy=240)

### Face & Identity

- ArcFace ONNX 512-dimensional face embedding extraction
- Cosine similarity identity matching (>0.6 threshold)
- Persistent identity enrollment and recognition across sessions

### Speech Recognition

- Whisper.net on-device speech-to-text (ggml-base.en model)
- Energy-based voice activity detection
- Real-time transcript display

### Intelligence Layer

- **Hybrid LLM**: Local Ollama (llama3:8b) + Cloud GPT-5-mini
- HCEP-aware system prompts that modulate AI behavior per mode
- Automatic local/cloud routing (THINK/LOGIC → local, SPIRIT/AFFECT/HEART → cloud)
- 5-step agentic reasoning loop with tools: `query_knowledge`, `get_hcep_state`, `store_knowledge`, `summarize_person`, `analyze_gaze_pattern`

### Knowledge & Memory

- Per-person knowledge accumulation (sightings, utterances, exchanges)
- Strategy D: UKS (BrainSim III) hybrid adapter with auto-fallback to in-memory store
- JSON persistence across sessions
- Natural-language summarization for LLM context injection

### Dashboard UI (WPF)

- Dark-themed 3-column resizable layout
- Live RGB video with skeleton/face overlays
- Real-time face schematic with gaze crosshair and region dots
- HCEP mode display with confidence bar
- Gaze, cognitive state, and valence indicators
- Action Unit bar charts
- Head pose (pitch, yaw, roll)
- Pipeline metrics (FPS, vision latency, tracked persons, beam angle)
- Speech transcript log
- LLM chat interface
- Full Body toggle button (switches Kinect between 20-joint and 10-joint tracking)
- Sensor Streams and Kinect Video child windows

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                          HCEP.App (WPF)                        │
│   MainWindow · MainViewModel · HcepPipelineOrchestrator        │
│   VideoOverlayControl · GazeVisualizationControl               │
├───────────────────────┬─────────────────────────────────────────┤
│   HCEP.Intelligence   │          HCEP.Knowledge                │
│  HybridLlmEngine      │  UksKnowledgeAdapter (Strategy D)      │
│  AgenticToolExecutor   │  InMemoryKnowledgeStore                │
│  HcepPromptBridge      │  PersonKnowledgeManager                │
├───────────────────────┼─────────────────────────────────────────┤
│   HCEP.Vision         │          HCEP.Audio                    │
│  ArcFaceRecognizer     │  WhisperSpeechRecognizer               │
│  HcepModeAnalyzer      │  AudioPipeline                        │
│  VisionPipeline        │                                       │
├───────────────────────┼─────────────────────────────────────────┤
│   HCEP.Spatial        │          HCEP.Kinect                   │
│  ThreeStageGaze        │  KinectSensorSource (native COM)      │
│  PnPSolver             │  SimulatedSensorSource                │
│  ConfidenceCone        │                                       │
├───────────────────────┴─────────────────────────────────────────┤
│   HCEP.Core (Enums · Models · Interfaces · Channels)           │
├─────────────────────────────────────────────────────────────────┤
│   HCEP.Telemetry (Serilog · Metrics · FPS)                     │
└─────────────────────────────────────────────────────────────────┘
```

**11 projects** | **102 unit tests** | **.NET 9.0** | **x64 only**

---

## Project Structure

```
HCEP/
├── HCEP.sln                          # Root solution
├── Directory.Build.props             # Shared MSBuild properties (net9.0-windows, x64)
├── run.bat                           # Build & run script
├── docs/
│   ├── PRD.md                        # Product Requirements Document
│   ├── ROADMAP.md                    # Development roadmap (v0.1 → v1.0)
│   ├── USER_GUIDE.md                 # End-user guide
│   └── DEVELOPER_GUIDE.md           # Developer reference
├── src/
│   ├── HCEP.Core/                    # Enums, models, interfaces, channels (zero deps)
│   ├── HCEP.Telemetry/              # Serilog logging, FPS counter, moving average
│   ├── HCEP.Spatial/                # Gaze math: PnP, ray-plane, confidence cone, coord mapper
│   ├── HCEP.Kinect/                 # Kinect v1 native COM + simulated source
│   ├── HCEP.Kinect.Bridge/         # .NET Framework 4.8.1 bridge for managed Kinect SDK
│   ├── HCEP.Vision/                 # ArcFace recognition, HCEP mode analyzer, vision pipeline
│   ├── HCEP.Audio/                  # Whisper speech-to-text, audio pipeline
│   ├── HCEP.Knowledge/             # Knowledge store, UKS adapter, person memory
│   ├── HCEP.Intelligence/          # Hybrid LLM engine, agentic tools, prompt bridge
│   └── HCEP.App/                    # WPF application, DI host, orchestrator, UI controls
└── tests/
    └── HCEP.Tests/                   # xUnit tests (102 passing)
        ├── Spatial/                  # Ray-plane, coordinate mapper, PnP, confidence cone
        ├── Knowledge/               # In-memory store, UKS adapter, person knowledge
        ├── Intelligence/            # Agentic tools, prompt bridge, tool definitions
        ├── Vision/                  # HCEP mode analyzer
        └── Core/                    # Models, enums, constants
```

---

## Prerequisites

| Component | Version | Required |
|---|---|---|
| Windows 10/11 | x64 | Yes |
| .NET SDK | 9.0+ | Yes |
| Kinect for Windows SDK | v1.8 | Yes (for live sensor) |
| Kinect Developer Toolkit | v1.8 | Yes (for face tracking) |
| Ollama | Latest | Optional (local AI) |
| OpenAI API key | — | Optional (cloud AI) |

### Model Files (not included in repo)

| Model | File | Size | Purpose |
|---|---|---|---|
| Whisper base.en | `ggml-base.en.bin` | ~140 MB | Speech recognition |
| ArcFace ResNet100 | `arcfaceresnet100-11-int8.onnx` | ~120 MB | Face recognition |

---

## Quick Start

### Build & Run

```powershell
# Clone the repository
git clone https://github.com/kirklasalle/HCEP.git
cd HCEP

# Build and run (using the helper script)
.\run.bat

# Or manually:
dotnet build HCEP.sln
dotnet run --project src/HCEP.App
```

### Run Tests

```powershell
dotnet test HCEP.sln
```

### Without Kinect Hardware

The app auto-detects Kinect availability. If no sensor is found, it silently falls back to `SimulatedSensorSource` which generates synthetic frames at 30fps.

---

## Key Dependencies

| Package | Version | License | Purpose |
|---|---|---|---|
| Microsoft.ML.OnnxRuntime | 1.20.1 | MIT | ArcFace face recognition inference |
| SixLabors.ImageSharp | 3.1.7 | Apache-2.0 | Image preprocessing |
| Whisper.net | 1.8.0 | MIT | On-device speech-to-text |
| NAudio | 2.2.1 | MIT | Audio capture & format conversion |
| Serilog | 4.2.0 | Apache-2.0 | Structured logging |
| CommunityToolkit.Mvvm | 8.4.0 | MIT | WPF MVVM framework |
| Microsoft.Extensions.Hosting | 9.0.0 | MIT | Dependency injection & hosting |
| xUnit | 2.9.2 | Apache-2.0 | Unit testing |

---

## Documentation

| Document | Description |
|---|---|
| [Product Requirements (PRD)](docs/PRD.md) | Full requirements, HCEP theory, architecture, success metrics |
| [Developer Guide](docs/DEVELOPER_GUIDE.md) | Architecture deep dive, layer-by-layer reference, coding conventions |
| [User Guide](docs/USER_GUIDE.md) | Installation, quick start, dashboard overview, troubleshooting |
| [Roadmap](docs/ROADMAP.md) | Development phases from v0.1 alpha to v1.0 release |

---

## HCEP Theory

Kirk LaSalle's **Human Conversation Eye Points (HCEP)** theory posits that eye contact patterns during face-to-face conversation reveal five distinct communication modes that people naturally cycle through. These modes encode cognitive state, emotional valence, and communicative intent — information that is invisible to speech-only analysis.

The system classifies modes in real-time using:

- **Gaze region** — where the person is looking (13 classified targets)
- **Gaze dynamics** — saccade patterns, fixation duration, social triangle cycling
- **Facial Action Units** — muscle movements indicating emotion
- **Temporal patterns** — mode stability and transition dynamics

![Telemetry Cones of Vision & 13 Regions](docs/images/cones_of_vision.svg)

This enables AI systems to respond not just to *what* people say, but to *how* they're communicating — adapting tone, depth, and style to match the human's current cognitive-emotional state.

---

## HCEP-SDK Integration

To build third-party client integrations, use the public [HCEP-SDK Repository](https://github.com/kirklasalle/HCEP-SDK). The SDK exposes multi-platform wrappers for real-time telemetry streaming and tool calls:

*   **Model Context Protocol (MCP):** Connects agent clients directly to the HCEP runtime via standard Anthropic MCP tool routers.
*   **Unity (C#):** Provides `HcepGazeController.cs` to map live eye/head bone rotations dynamically to 3D rig transforms.
*   **Unreal Engine (C++):** Native components driving actor eye/head sockets with configurable damping.
*   **Python:** Seamless integration with LangChain and LlamaIndex to feed raw gaze states directly into LLM prompts.
*   **Semantic Kernel:** A plugin mapping HCEP tool definitions straight to Semantic Kernel agents.

---

## Licensing Strategy

HCEP utilizes a dual-licensing hybrid model designed to protect core intellectual property while fostering open ecosystem integration:

### 1. Simple Summary (Basic Level)
*   **Core Desktop Application (Proprietary License):** The main HCEP application (perception engine, PnP solver, intelligence router, and WPF dashboard) is closed-source and proprietary. All rights are reserved by Kirk LaSalle. You may not copy, distribute, or modify the desktop client without explicit written permission.
*   **Integration SDKs (MIT License):** The HCEP-SDK codebase is open-source under the permissive MIT license. Developers are free to use, modify, and distribute the SDK libraries in games, robotics systems, and custom AI agents.

### 2. Architectural Analysis (Advanced Nerd Level)
*   **IP Isolation Boundary:** The API boundary acts as a strict firewall between the proprietary and open-source segments:
    *   **Proprietary Core (Closed-Source):** Contains the PnP head pose solver (Levenberg-Marquardt optimizer), Whisper.net speech transcriptions, ArcFace biometric recognizer, and the cognitive state classifiers. These reside inside the WPF application shell (`src/HCEP.App`, `src/HCEP.Kinect`, etc.).
    *   **Open SDK (Open-Source):** Client packages communicate over platform-agnostic channels (JSON-RPC MCP over HTTP, standard REST, and high-frequency WebSockets). 
*   **Compliance, Cryptography, and Directives:**
    *   **Immutability Safeguard:** Ethical limits (the 10 Augmented Laws) are governed by `Permanent_Active_Directives.txt`. The system computes a SHA-256 hash of this file and compares it to a hardcoded signature (`1A87DA...`) on startup. If modified, the application halts and falls back to a deep safety diagnostic state, neutralizing potential prompt injection or boundary exploits.
    *   **Encryption at Rest:** User API keys are protected using DPAPI (Data Protection API) via Windows CryptProtectData. Keys are encrypted at rest with user-scope machine-bound key blobs, ensuring configuration security.
    *   **Biometric Data Gating:** The facial recognition logic (ArcFace) is subject to biometric compliance controls. The application enforces explicit user confirmation dialogues before extracting 512-dimensional vector representations, meeting GDPR Art. 9 and BIPA standards.

---

## Author

**Kirk LaSalle** — HCEP theory inventor, product owner, and developer.

---

*HCEP — Human Communication Eye Protocol v1.0.0 (Stable Release)*
