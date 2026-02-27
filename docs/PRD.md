# HCEP — Product Requirements Document (PRD)

**Product Name:** HCEP — Human Communication Eye Protocol  
**Version:** 0.1.0 (Alpha)  
**Author:** Kirk LaSalle  
**Date:** February 23, 2026  
**Status:** Active Development  

---

## 1. Executive Summary

HCEP (Human Communication Eye Protocol) is a world-class, state-of-the-art real-time multi-modal perception platform that fuses Xbox 360 Kinect v1 sensor data with a hybrid LLM engine (local Ollama + cloud GPT-5-mini) to analyze and respond to human communication through eye contact patterns, facial expressions, and speech.

At its core, HCEP implements Kirk LaSalle's HCEP (Human Conversation Eye Points) theory — a novel 5-mode cognitive-emotional classification system that decodes the rich, unspoken language of eye contact during face-to-face conversation.

### 1.1 Vision

To build the first real-time system that understands *how* people communicate through eye contact, not just *what* they say — enabling machines to participate in the full spectrum of human conversational dynamics.

### 1.2 Mission

Deliver a commercially viable Windows desktop platform that:
- Tracks face, eyes, skeleton, and speech in real-time via Kinect v1
- Classifies the 5 HCEP modes (Logic, Affect, Spirit, Heart, Think)
- Routes conversation to local or cloud LLMs based on cognitive-emotional context
- Maintains persistent person-specific knowledge for ongoing relationships
- Provides a production-quality WPF dashboard for live monitoring

---

## 2. Problem Statement

Current human-computer interaction treats eye contact as a binary signal (looking vs. not-looking). This ignores decades of psychological research showing that eye contact patterns encode:

- **Cognitive state** — recall, construction, confusion, engagement
- **Emotional valence** — positive, negative, neutral
- **Communication mode** — analytical, emotional, deep rapport, empathic, reflective
- **Turn-taking signals** — pre-speech gaze aversion, mutual gaze holds

No existing system classifies these patterns in real-time or uses them to modulate AI responses. HCEP fills this gap.

---

## 3. Target Users & Expanded Use Cases

| User Segment | Description |
|---|---|
| **Autonomous Agents** | Intelligence systems (e.g., Nexus) requiring agentic access to hardware vision to "see" and interpret human states in real-time. |
| **Robotics** | Physical humanoid/companion robots utilizing HCEP to achieve human-like visual acuity, natural gaze interaction, and joint attention. |
| **AR / VR / Gaming** | NPC and avatar systems rendering true eye-contact geometry so characters look at each other and the player camera correctly, breaking the "dead eyes" barrier in spatial computing. |
| **Researchers & Science** | Psychologists and cognitive scientists utilizing HCEP as a standard for automated human behavior/psychology readings (strictly constrained by ethical AI Laws). |
| **Kirk LaSalle** | Primary stakeholder, HCEP theory inventor |

### 3.1 Future Expansion: Advanced Detection
While v0.1 establishes the foundational standard (face, gaze, Action Units), future iterations of HCEP will expand to **Full-Body Posture and Movement Detection** (kinesics and proxemics). This will allow the protocol to decode holistic human communication—merging eye contact patterns with body language, weight shifts, and spatial positioning.

---

## 4. HCEP Theory — The 5 Modes

The core innovation is Kirk LaSalle's HCEP (Human Communication Eye Points) classification:

| Mode | Eye Pattern | Cognitive State | Response Style |
|---|---|---|---|
| **LOGIC** | Structured gaze, engaged on-face | Analytical processing | Precise, factual, numbered lists |
| **AFFECT** | Social Triangle (eyes + mouth) | Emotional engagement | Warm, empathetic, feeling-first |
| **SPIRIT** | Sustained mutual gaze, high confidence | Deep authentic rapport | Personal, genuine, unstructured |
| **HEART** | Lower-face attention + empathic markers | Empathic resonance | Supportive, validating, caring |
| **THINK** | Gaze aversion, defocused | Internal processing | Brief, non-intrusive, space-giving |

---

## 5. Functional Requirements

### 5.1 Sensor Input (P0 — Must Have)

| ID | Requirement | Priority |
|---|---|---|
| FR-S01 | Capture 30fps color, depth, skeleton, face streams from Kinect v1 | P0 |
| FR-S02 | Track 2 simultaneous skeletons (Kinect v1 limit) | P0 |
| FR-S03 | Extract 87+ 2D/3D face feature points per frame | P0 |
| FR-S04 | Extract 6 Kinect v1 Action Units (AU) per frame | P0 |
| FR-S05 | Beam-formed 4-mic array audio capture with source angle | P0 |
| FR-S06 | Simulated sensor source for development without hardware | P0 |

### 5.2 Gaze Estimation (P0 — Must Have)

| ID | Requirement | Priority |
|---|---|---|
| FR-G01 | 3-stage gaze pipeline: Head Pose → Eye-in-Head → Hybrid Fusion | P0 |
| FR-G02 | SolvePnP head pose from 6 canonical face landmarks | P0 |
| FR-G03 | Eye-in-head rotation from pupil feature point deltas | P0 |
| FR-G04 | Confidence cone gaze target classification (13 regions) | P0 |
| FR-G05 | Temporal smoothing with exponential moving average | P0 |
| FR-G06 | Saccade detection using Main Sequence equation | P1 |

### 5.3 HCEP Analysis (P0 — Must Have)

| ID | Requirement | Priority |
|---|---|---|
| FR-H01 | Real-time 5-mode HCEP classification from multi-modal input | P0 |
| FR-H02 | Temporal hysteresis (5-frame stability minimum for mode transitions) | P0 |
| FR-H03 | Cognitive state inference (12 states) from gaze + AU patterns | P0 |
| FR-H04 | Emotional valence classification from AU weights | P0 |
| FR-H05 | Social Triangle detection (eyes + mouth gaze cycle) | P0 |

### 5.4 Face Recognition (P1 — Should Have)

| ID | Requirement | Priority |
|---|---|---|
| FR-F01 | ArcFace ONNX 512-dimensional face embedding extraction | P1 |
| FR-F02 | Cosine similarity identity matching (>0.6 threshold) | P1 |
| FR-F03 | Persistent identity enrollment and recognition across sessions | P1 |

### 5.5 Speech Recognition (P1 — Should Have)

| ID | Requirement | Priority |
|---|---|---|
| FR-A01 | Whisper.net on-device speech-to-text | P1 |
| FR-A02 | Energy-based voice activity detection (VAD) | P1 |
| FR-A03 | 16kHz mono PCM → float32 conversion | P1 |
| FR-A04 | Chunked buffering with configurable window size | P1 |

### 5.6 Knowledge Store (P0 — Must Have)

| ID | Requirement | Priority |
|---|---|---|
| FR-K01 | Triple-store knowledge graph (subject, relation, object) | P0 |
| FR-K02 | Strategy D: UKS (BrainSim III) hybrid adapter with auto-fallback | P0 |
| FR-K03 | Per-person knowledge accumulation (sightings, utterances, exchanges) | P0 |
| FR-K04 | JSON persistence (save/load) | P0 |
| FR-K05 | Natural-language summarization for LLM context injection | P0 |

### 5.7 Intelligence Layer (P0 — Must Have)

| ID | Requirement | Priority |
|---|---|---|
| FR-I01 | Hybrid LLM engine: local Ollama (llama3:8b) + cloud GPT-5-mini | P0 |
| FR-I02 | HCEP-aware system prompts that modulate LLM behavior per mode | P0 |
| FR-I03 | Agentic multi-step reasoning loop with 5 HCEP tools | P0 |
| FR-I04 | Automatic local↔cloud routing based on HCEP mode + query complexity | P0 |
| FR-I05 | Streaming token output from Ollama | P1 |
| FR-I06 | Latency threshold failover (local > 3s → cloud) | P1 |

### 5.8 Dashboard UI (P1 — Should Have)

| ID | Requirement | Priority |
|---|---|---|
| FR-U01 | WPF dark-theme dashboard with live HCEP mode display | P1 ✔ |
| FR-U02 | Real-time metrics grid (FPS, latency, confidence) | P1 ✔ |
| FR-U03 | Speech transcript log | P1 ✔ |
| FR-U04 | LLM chat interface with send/receive | P1 ✔ |
| FR-U05 | Gaze region indicator overlay | P1 ✔ |
| FR-U06 | Skeleton wireframe overlay on live video feed | P1 ✔ |
| FR-U07 | Face bounding box and 87-point wireframe overlay | P1 ✔ |
| FR-U08 | Full body / seated skeleton toggle with runtime mode switch | P1 ✔ |
| FR-U09 | Drag-resizable panel layout (horizontal + vertical GridSplitters) | P2 ✔ |
| FR-U10 | Face schematic with gaze crosshair and action unit bars | P2 ✔ |
| FR-U11 | Sitting/standing auto-detection with posture label | P2 ✔ |

---

## 6. Non-Functional Requirements

| ID | Requirement | Target |
|---|---|---|
| NFR-01 | End-to-end pipeline latency | < 100ms (30fps budget) |
| NFR-02 | Gaze estimation accuracy | < 5° mean angular error |
| NFR-03 | HCEP mode classification accuracy | > 80% on labeled data |
| NFR-04 | LLM local response latency | < 3 seconds |
| NFR-05 | Memory footprint (steady state) | < 500 MB |
| NFR-06 | Startup time | < 10 seconds |
| NFR-07 | Graceful degradation | Must run without Kinect, without LLM, without model files |
| NFR-08 | Target platform | Windows 10/11, x64, .NET 9.0 |

---

## 7. Technical Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                          HCEP.App (WPF)                        │
│   MainWindow  ·  MainViewModel  ·  HCEPPipelineOrchestrator   │
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
│  ThreeStageGaze        │  KinectSensorSource                   │
│  PnPSolver             │  SimulatedSensorSource                │
│  ConfidenceCone        │                                       │
├───────────────────────┴─────────────────────────────────────────┤
│   HCEP.Core (Enums · Models · Interfaces · Channels)           │
├─────────────────────────────────────────────────────────────────┤
│   HCEP.Telemetry (Serilog logging · Metrics · FPS counter)     │
└─────────────────────────────────────────────────────────────────┘
```

---

## 8. Key Dependencies

| Component | Version | License | Purpose |
|---|---|---|---|
| .NET 9.0 | 9.0.311 | MIT | Runtime |
| Kinect SDK v1.8 | 1.8 | Microsoft EULA | Sensor access |
| Microsoft.ML.OnnxRuntime | 1.20.1 | MIT | ArcFace inference |
| SixLabors.ImageSharp | 3.1.7 | Apache-2.0 | Image processing |
| Whisper.net | 1.8.0 | MIT | Speech-to-text |
| NAudio | 2.2.1 | MIT | Audio capture |
| Serilog | 4.2.0 | Apache-2.0 | Structured logging |
| CommunityToolkit.Mvvm | 8.4.0 | MIT | WPF MVVM |
| UKS / BrainSim III | MIT | MIT | Knowledge graph (optional) |

---

## 9. Success Metrics

| Metric | Target | Measurement |
|---|---|---|
| Pipeline latency | < 100ms p95 | HCEPTelemetryService timing |
| Gaze accuracy | < 5° MAE | Synthetic + human eval |
| Mode stability | > 85% agreement with human labels | Labeled video dataset |
| Test coverage | > 70% line coverage | Coverlet |
| Build status | Green CI | 0 errors, tests passing |

---

## 10. Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Kinect v1 EOL — no driver updates | Medium | Late-bound COM interop, graceful fallback |
| GPT-5-mini API changes | Medium | Abstracted behind ILlmEngine interface |
| UKS API instability | Low | Strategy D adapter isolates HCEP from UKS internals |
| Pupil tracking accuracy (Kinect v1 IR) | High | Confidence cone with generous radius, head pose fallback |
| Commercial licensing complexity | Medium | All dependencies MIT/Apache-2.0 compatible |

---

## 11. Out of Scope (v0.1)

- Kinect v2 / Azure Kinect / webcam support
- Multi-person conversation tracking (> 2 people)
- Cloud deployment / web API
- Mobile / cross-platform
- Real-time 3D avatar rendering
- Diarization-based multi-speaker identification

---

## 12. Approval

| Role | Name | Date |
|---|---|---|
| Product Owner | Kirk LaSalle | Feb 22, 2026 |
| Technical Lead | Kirk LaSalle | Feb 22, 2026 |

---

*Copyright © 2026 Kirk LaSalle. All rights reserved.*
