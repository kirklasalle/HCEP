# HCEP — Product Requirements Document (PRD)

**Product Name:** HCEP — Human Communication Eye Protocol  
**Version:** 1.2.0 (Avatar Expression + Contextual Intelligence)  
**Author:** Kirk LaSalle  
**Date:** July 3, 2026  
**Status:** Active Development — Phases 9-12 Planned, Phases 13-14 Complete  

---

## 1. Executive Summary

HCEP (Human Communication Eye Protocol) is a world-class real-time multi-modal perception and expression platform that fuses sensor data — Kinect v1, standard USB webcams, depth cameras — with a hybrid LLM engine to *fully understand* and *authentically reciprocate* human communication through the complete nonverbal vocabulary: eye contact patterns, facial action units, head kinematics (nodding, shaking, tilting, thrusting), shoulder and torso behavior, proxemic dynamics, and speech prosody.

At its core, HCEP implements Kirk LaSalle's original theory — a 5-mode cognitive-emotional classification system grounded in five decades of psycholinguistic and social neuroscience research — that decodes the unspoken language of human interaction in real-time. HCEP is not merely a gaze tracker. It is the first commercially deployable **Social Signal Processing** (Vinciarelli et al., 2009) system that operates end-to-end from raw sensor data to AI behavioral adaptation.

### 1.1 Vision

To build the first real-time AI system that *fully understands* a human being — not just their words, but how they are communicating, what cognitive and emotional state they are in, and what their body is saying — and that can *genuinely respond in kind*, becoming a social partner rather than a passive listener.

This vision encompasses:

- An AI that reads gaze, expression, posture, and gesture with the fidelity of a trained clinician
- An AI that produces authentic nonverbal responses (nods, smiles, gaze behavior, head tilts) through its avatar
- A platform that can power companion robots, therapeutic systems, game characters, medical education tools, and social AI agents across every domain that involves human-AI interaction

### 1.2 Mission

Deliver a commercially viable, scientifically validated, production-grade platform that:

- Tracks face, eyes, skeleton, head kinematics, torso, and speech in real-time via Kinect v1 or standard USB webcams
- Classifies the 5 HCEP modes (LOGIC, AFFECT, SPIRIT, HEART, THINK) with validated accuracy (κ=0.8084)
- Routes conversation to local or cloud LLMs based on cognitive-emotional context, with cloud circuit-breaker resilience
- **Reciprocates** human nonverbal behavior through a biologically authentic avatar (nods, gaze, expression, head tilt)
- Maintains persistent person-specific knowledge for ongoing relationships
- Provides a production-quality WPF dashboard for live monitoring and SDK integration
- Exposes integration APIs for Unity, Unreal Engine, Python, .NET agents, and social robots (ROS2)

### 1.3 The Game-Changing Significance

HCEP addresses a fundamental gap in all current human-computer interaction paradigms: **machines do not understand how humans communicate**. Every conversational AI, every chatbot, every voice assistant processes only the *verbal* channel — the 7% of emotional communication that words carry (Mehrabian & Ferris, 1967). The remaining 93% — the eye contact that signals analytical vs. emotional engagement, the head nod that says "I'm with you", the gaze aversion that says "I'm thinking", the forward lean that says "tell me more" — is invisible to every existing AI system.

HCEP makes this invisible language visible and actionable:

| Domain | Without HCEP | With HCEP |
|---|---|---|
| **Conversational AI** | Responds to words | Responds to how the person is communicating |
| **Companion Robots** | Scripted eye/head behavior | Genuine social responsiveness |
| **Game NPCs** | Scripted gaze points | Characters that look at you meaningfully |
| **Medical Education** | Subjective instructor feedback | Objective real-time gaze behavior analysis |
| **Therapy** | Self-report measures | Continuous behavioral biomarker monitoring |
| **VR Social Presence** | Graphical fidelity focus | Social cognitive fidelity (Bailenson, 2001) |

---

## 2. Problem Statement

Current human-computer interaction treats eye contact as a binary signal (looking vs. not-looking), if it processes it at all. This ignores five decades of psychological research demonstrating that eye contact patterns encode:

- **Cognitive state** — recall vs. construction vs. confusion vs. engagement (Kendon, 1967; Glenberg et al., 1998)
- **Emotional valence** — positive vs. negative vs. neutral, with PAD (Pleasure-Arousal-Dominance) dimensionality (Russell, 1980; Mehrabian & Russell, 1974)
- **Communication mode** — analytical, emotional, deep rapport, empathic, reflective (LaSalle's 5-mode HCEP theory)
- **Turn-taking signals** — pre-speech gaze aversion, floor-yield gaze, backchannel regulation (Kendon, 1967; Duncan, 1974)
- **Social relationship dynamics** — dominance, submission, intimacy, deception (Argyle & Cook, 1976)

Furthermore, head movements carry specific semantic content (Chovil, 1991, 1992):

- Nods signal agreement, understanding, and turn-continuation
- Shakes signal negation, uncertainty, disbelief
- Tilts signal curiosity, interest, and empathic attention
- Forward/backward orientation signals engagement vs. withdrawal

And body posture encodes the full proxemic and affective context (Hall, 1966; Mehrabian, 1969; Pentland, 2010):

- Forward lean → high engagement and approach motivation
- Backward lean → withdrawal or evaluation
- Open posture → receptivity and confidence
- Shoulder shrug → epistemic uncertainty

**No existing commercial system classifies these signals in real-time or uses them to modulate AI behavior.** HCEP fills this gap.

---

## 3. Scientific Foundation

HCEP is built on a rigorous scientific foundation spanning cognitive psychology, social neuroscience, computational linguistics, and human-robot interaction. Key citations and theoretical grounding:

### 3.1 The Neurological Architecture of Social Perception

The human brain devotes dedicated neural circuitry to social signal processing:

- **Eye Direction Detector (EDD)**: Baron-Cohen's (1994, 1995) proposed module for automatic gaze-direction processing, triggering within 100-150ms of eye contact onset (Calder et al., 2002)
- **Mirror Neuron System**: Neurons in premotor and inferior parietal cortex that fire both during action execution and observation (Rizzolatti & Craighero, 2004) — the neural substrate for imitation, empathy, and social understanding
- **Superior Temporal Sulcus (STS)**: Responds to biological motion, gaze direction, and communicative acts — a core node in the social brain network
- **Amygdala**: Activated within 50-100ms by facial expressions and direct gaze — pre-conscious emotional tagging of social signals

### 3.2 Validated Measurement Norms

Argyle and Cook (1976) established the foundational behavioral norms that HCEP's classifiers are calibrated against:

- Speakers make eye contact ~40% of the time while speaking
- Listeners make eye contact ~70% of the time while listening
- Mutual gaze occupies ~30% of dyadic interaction
- Social triangle scanning (eyes + mouth) characterizes affective engagement

Mehrabian and Ferris (1967) established that nonverbal channels carry 93% of emotional communication content (55% visual/kinesic, 38% vocal, 7% verbal) — the empirical justification for HCEP's multi-modal architecture.

### 3.3 Full Reference List

See `HCEP_SCIENCE_FOUNDATION.md` for the complete 70+ citation research compendium, organized by topic area (gaze, head kinematics, kinesics, FACS, mirror neurons, social signal processing, clinical applications, AI expression).

---

## 4. HCEP Theory — The 5 Modes (Updated)

The core classification system implements Kirk LaSalle's HCEP 5-mode theory, grounded in Kendon's (1967) gaze taxonomy, Russell's (1980) circumplex affect model, and the social triangle research of Argyle et al. (1973).

| Mode | Eye Pattern | Head/Body | AUs | Cognitive State | AI Response |
|---|---|---|---|---|---|
| **LOGIC** | Structured gaze, on-face | Forward orientation, stable | AU4 mild, low AU12 | Analytical processing | Precise, factual, numbered lists |
| **AFFECT** | Social triangle (eyes↔mouth) | Slight lean, animated | AU12 > 0.2, AU6 > 0.1 | Emotional engagement | Warm, empathetic, feeling-first |
| **SPIRIT** | Sustained mutual gaze (>3s) | Relaxed, centered | AU6 presence, low activation | Deep authentic rapport | Personal, genuine, unstructured |
| **HEART** | Lower-face + empathic | Forward lean, gentle nod | AU1+AU4, AU15 | Empathic resonance | Supportive, validating, caring |
| **THINK** | Gaze aversion (>15°), defocus | Any; often down-left | AU4, low AU12 | Internal processing | Brief, non-intrusive, space-giving |

### 4.1 PAD Mapping

The 5 HCEP modes map onto the Pleasure-Arousal-Dominance (PAD) space (Mehrabian & Russell, 1974):

| Mode | Pleasure | Arousal | Dominance |
|---|---|---|---|
| LOGIC | Moderate | Moderate | Moderate-High |
| AFFECT | High | High | Moderate |
| SPIRIT | High | Moderate | Low |
| HEART | High | Low-Moderate | Low |
| THINK | Variable | Low | Variable |

### 4.2 Temporal Dynamics

Mode transitions follow a temporal hysteresis model (5-frame minimum stability at 30fps ≈ 167ms), preventing noise-driven flickering while remaining responsive to genuine state changes. The minimum dwell time per mode reflects empirical observation that genuine mode shifts take >150ms to establish.

---

## 5. Functional Requirements

### 5.1 Sensor Input (P0 — Must Have)

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

### 3.1 Future Expansion: Advanced Detection

While v0.1 establishes the foundational standard (face, gaze, Action Units), future iterations of HCEP will expand to **Full-Body Posture and Movement Detection** (kinesics and proxemics). This will allow the protocol to decode holistic human communication—merging eye contact patterns with body language, weight shifts, and spatial positioning.

### 3.2 Advanced Use Cases & Human-Avatar Applications

As HCEP expands to capture full kinesics, the protocol will support specialized performance, cloning, and educational domains:

- **Human Physical Motion & Performance Cloning (Mimicry)**: Enabling photorealistic virtual avatars or robotic entities to mirror, clone, and replicate human movement, gestures, and expressions with micro-second fidelity.
- **Acting, Pretending & Reciprocation**: Driving conversational agents to perform socially reciprocal physical behaviors, mirroring human posture shifts, acting out physical cues, and establishing mutual, natural gestural responses.
- **Sign Language Recognition & Translation**: Fusing hand articulation with micro-expressions and gaze cues to map, parse, and translate sign languages into textual or spoken representations.
- **Human Performance Evaluation**: Analyzing muscle fatigue, range of motion, postural stability, and fine motor coordination in medical rehabilitation or athletic contexts.
- **Training & Support for Human Excellence**: Providing immersive physical coaching, feedback loops, and bio-mechanical assessments to accelerate skill acquisition in athletics, performing arts, and vocational training.

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
| Phase 13-14 Review | Kirk LaSalle | July 3, 2026 |

---

## 13. Phase 13 — Phoneme-to-Viseme Lip Sync (Implemented July 2026)

### 13.1 Requirement

The avatar's mouth must move with phoneme-accurate synchronization to its speech output. Incorrect or absent lip sync actively degrades speech intelligibility (McGurk & MacDonald, 1976) and triggers uncanny valley responses (Tinwell et al., 2011).

### 13.2 Functional Requirements

| ID | Requirement | Status |
|---|---|---|
| FR-V01 | `ISpeechSynthesizer.VisemeChanged` event fires per-phoneme during TTS synthesis | ✅ Implemented |
| FR-V02 | `VisemeData` struct encodes: JawOpen, LipRound, LipSpread, LipCompressed, UpperLipRetract | ✅ Implemented |
| FR-V03 | `VisemeController` maps all 21 SAPI phoneme groups to `VisemeData` per Preston Blair (1949) | ✅ Implemented |
| FR-V04 | `AvatarCoreControl.SetViseme()` animates 2D Happy Face mouth (MouthFill + SmilePath) | ✅ Implemented |
| FR-V05 | `Avatar3DControl.SetViseme()` draws proportional bezier mouth arc on wireframe | ✅ Implemented |
| FR-V06 | Co-articulation blending: 60ms EMA between successive phoneme shapes | ✅ Implemented |
| FR-V07 | Windows SAPI backend: per-phoneme timing, ★★★★★ accuracy | ✅ Implemented |
| FR-V08 | Cloud TTS backends: amplitude-driven approximate visemes, ★★☆☆☆ accuracy | ✅ Implemented |
| FR-V09 | `HybridTtsEngine` relays `VisemeChanged` from whichever backend is active | ✅ Implemented |
| FR-V10 | `AvatarWindow` subscribes to `orchestrator.TtsEngine.VisemeChanged`; dispatches to both avatars | ✅ Implemented |

### 13.3 Scientific Basis

- **McGurk & MacDonald (1976)**: Visual mouth movement is processed by auditory cortex as a genuine speech signal. Wrong lip sync degrades intelligibility.
- **Sumby & Pollack (1954)**: Accurate lip sync provides up to 15 dB SNR improvement in noisy environments.
- **Preston Blair (1949)**: 18 canonical mouth shapes governing animation lip sync since Disney's golden age.
- **Cohen & Massaro (1994)**: DOMINANCE model — co-articulation means mouth shapes carry predictive information about upcoming sounds.

---

## 14. Phase 14 — Contextual Intelligence: Time, Space & Situation (Implemented July 2026)

### 14.1 Requirement

The avatar must be aware of *when* and *where* it exists — time of day, physical environment, activity context, and privacy level. This information must modulate the AI's conversational register and activate the Silence Protocol when appropriate.

### 14.2 Functional Requirements

| ID | Requirement | Status |
|---|---|---|
| FR-C01 | `ContextSnapshot` model captures Time × Space × Situation with `ToPromptString()` | ✅ Implemented |
| FR-C02 | `TimeContextProvider` classifies time-of-day band, day type, season from system clock | ✅ Implemented |
| FR-C03 | User-configurable: EnvironmentType, Activity, UserDefinedLocation, Privacy | ✅ Implemented |
| FR-C04 | `CommunicationRegister` derived from time + environment (Professional/Personal/Intimate/Formal) | ✅ Implemented |
| FR-C05 | `SilenceProtocolEvaluator` — 7 evidence-based rules from HCEP mode + facial AUs + context | ✅ Implemented |
| FR-C06 | LLM context injection: `ContextSnapshot.ToPromptString()` injected into every `PromptAsync()` | ✅ Implemented |
| FR-C07 | "SILENCE PROTOCOL: ACTIVE" message in LLM prompt when `SilenceProtocolActive = true` | ✅ Implemented |
| FR-C08 | Direct gaze to avatar overrides silence protocol (Duncan, 1972 primary floor-yield cue) | ✅ Implemented |

### 14.3 The Silence Protocol — Rules (Priority Order)

1. Direct gaze toward avatar → override; avatar may respond
2. Raised brows (AU5) + direct gaze → override; question signal
3. THINK mode + gaze aversion → silence (processing silence, Jaworski 1993)
4. HEART mode + evening/night + no direct gaze → silence (empathic presence)
5. Bedroom + night + no direct gaze → affiliative silence
6. Sustained brow furrow (AU3) + gaze aversion → deep work silence
7. Lab/Studio environment + no direct gaze → deep work silence

### 14.4 Scientific Basis

- **Hall (1959, 1983)** Chronemics: time is a silent communication medium with cross-cultural norms.
- **Barker (1968)** Behavior Settings: physical spaces prescribe appropriate behaviors.
- **Jaworski (1993)** The Power of Silence: 6 types of meaningful silence; none = absence of communication.
- **Sacks, Schegloff & Jefferson (1974)** Turn-Taking: gaze is the primary floor-yield signal.
- **Duncan (1972)** Speaker Yield Cues: 6 signals, gaze toward listener is the most reliable.

---

*Copyright © 2026 Kirk LaSalle. All rights reserved.*
