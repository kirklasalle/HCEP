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

* **Goal:** End-to-end pipeline running with simulated sensor data.
* **Milestones:**
  * [x] Synthetic frames flow through channels at 30fps.
  * [x] Knowledge store Persisted in JSON format.
  * [x] Strategy D hybrid adapter fallback verified.
  * [x] Ollama and GPT-5-mini routing and prompt adaptation verified.
  * [x] serilog structured logging and latency metrics implemented.

### Phase 2 — Kinect Hardware Integration — [COMPLETED]

* **Goal:** Real Kinect v1 sensor driving the pipeline.
* **Milestones:**
  * [x] Active skeleton/face streams running on Kinect v1.
  * [x] PnP head pose solver tuned with real-world anthropometrics.
  * [x] ArcFace ONNX face embedding recognition active.
  * [x] Whisper speech-to-text with VAD filtering.

### Phase 3 — HCEP Theory Validation — [COMPLETED]

* **Goal:** Validate HCEP 5-mode theory empirically.
* **Milestones:**
  * [x] Cohen's Kappa score of **0.8084** achieved on ground truth segment classification.
  * [x] Classifier accuracy validated at **84.55%** (target $\ge 80\%$).
  * [x] Stability hysteresis (5-frame buffer) and confidence cone thresholds optimized.

### Phase 4 — Security & Platform Independence — [COMPLETED]

* **Goal:** Protect user privacy and expand sensor support.
* **Milestones:**
  * [x] DPAPI-based key encryption for local LLM key storage.
  * [x] Explicit UI biometric consent prompts on start.
  * [x] GDPR-compliant erase methods added to data stores.
  * [x] Webcam Sensor Source implemented using OpenCV fallback.

### Phase 5 — LLM Plugin & Multi-Platform SDKs — [COMPLETED]

* **Goal:** Expose HCEP as an agentic tool and character driver.
* **Milestones:**
  * [x] Model Context Protocol (MCP) server endpoints mapped.
  * [x] OpenAI Function calling schemas generated on the fly.
  * [x] LangChain and LlamaIndex tool wrappers completed (Python).
  * [x] Semantic Kernel plugin completed (C#).
  * [x] Unity real-time bone-tracking animation controller script.
  * [x] Unreal Engine native C++ character animation actor component.

### Phase 6 — Commercial Packaging & Release — [COMPLETED]

* **Goal:** First public/commercial release with True Gaze™ parallax correction.
* **Milestones:**
  * [x] True Gaze™ parallax offset calibration implemented to resolve camera off-axis angle skew.
  * [x] Interactive True Gaze™ Parallax Simulator built for web browser showcase.
  * [x] MSIX AppxManifest.xml generated.
  * [x] release packaging script (`package_release.ps1`) automated.
  * [x] Tagged v1.0.0 release packages compiled and zipped.

### Phase 7 — Autonomous Avatar Responsiveness Tuning — [COMPLETED]

* **Goal:** Decouple avatar movements from user pose mirroring to establish autonomous observer behaviors.
* **Milestones:**
  * [x] Head rotation decoupled from direct tracking inputs (passing Vector3.Zero to SetHeadPose).
  * [x] Double-projection mesh pipeline built to construct static/neutral projected face shape (NeutralFaceMeshVertices2D).
  * [x] 3D wireframe rendering updated to use neutral mesh, enabling independent animation float/gaze responsiveness.
  * [x] Isolated the Permanent Active Directives (PAD) from the responsive behavior modifications.

---

## Future Post-v1.0 Roadmap

1. **Multi-Person Telemetry Extension:** Extend the pipeline to analyze cognitive-emotional modes of 3+ simultaneous participants.
2. **Voice Prosody Fusion:** Train an audio model to augment the face Action Unit weights with pitch/prosody emotion classifiers.
3. **Cross-Platform Client UI:** Port the WPF app to Avalonia UI for native Linux and macOS support.

---

## Phase 8 — Production Hardening & Security Audit — [COMPLETED — July 2026]

**Goal:** Harden all production-critical code paths, add comprehensive test coverage, and implement security best practices.

**Milestones:**

* [x] **Thread-safety fix**: Replaced incorrect `Interlocked.CompareExchange` volatile-read anti-pattern with `Volatile.Read/Write` on all cross-thread `VisionPipeline` shared-state properties — eliminates race conditions causing silent speech/recognition result drops
* [x] **Cloud circuit breaker**: `HybridLlmEngine` now implements a configurable circuit breaker (threshold=3 failures, 30s cool-down) — prevents hammering dead cloud APIs
* [x] **Windows Credential Manager integration**: `WindowsCredentialStore` (P/Invoke wrapper for `advapi32.dll`) — API keys stored in WCM vault, never in process listings or environment dumps
* [x] **Knowledge store capacity limits**: `InMemoryKnowledgeStore` now enforces `MaxSubjects` (500) and `MaxTriplesPerSubject` (1000) with LRU eviction — prevents unbounded memory growth
* [x] **Input validation**: String length bounds on all knowledge store writes (subject ≤255, relation ≤100, object ≤10,000 chars)
* [x] **Lock discipline**: `Query()` and `QueryAll()` now snapshot keys before releasing the lock — LINQ execution outside critical section
* [x] **ArcFace fault tolerance**: `LoadModel()` wrapped in try/catch — corrupted ONNX no longer crashes the pipeline
* [x] **AutoFallback configurability**: `AutoFallbackSeconds` promoted from `const` to public property — operators can now adjust or disable auto-fallback
* [x] **Observability**: Frame-drop `LogWarning` on all channel back-pressure paths; audio flush errors escalated from `LogDebug` to `LogWarning`
* [x] **WebSocket correctness**: `CloseAsync` now sends `InternalServerError` on error paths vs. always `NormalClosure`
* [x] **Avatar fixes**: `TrackingInfluence` increased from 0.04 → 0.15; `HeadFollowTimeConstantSec` reduced from 12.0s → 0.8s; eye socket smoothing positions reset on topology change; HUD "LOST" state added
* [x] **Enrollment UX**: `RefreshMetrics()` now polls `EnrolledFaceCount` to detect completion and updates status to "✓ '{name}' enrolled successfully"
* [x] **Calibration correctness**: `SizeChanged` hooked to `PositionCrosshair` — crosshair stays centred after window moves or multi-monitor drag
* [x] **21 new tests**: Concurrency stress, negative-path (corrupted ONNX, capacity limits), circuit-breaker verification — 193 total passing

---

## Phase 9 — Full Kinesics: Head Gestures + Body Language — [PLANNED — Q3-Q4 2026]

**Goal:** Extend HCEP's perception pipeline to decode the full kinesic vocabulary: head kinematics (nod, shake, tilt, thrust), shoulder movements (shrug), torso orientation (lean, orientation), and integrate these into the HCEP mode classification and AI response modulation.

**Scientific Basis:** Chovil (1991, 1992); Kendon (1967); Argyle & Cook (1976); Birdwhistell (1970); Mutlu et al. (2009); Vinciarelli et al. (2009). See `HCEP_SCIENCE_FOUNDATION.md` §Part II-III.

**Milestones:**

* [ ] **Head Gesture Detector** — `HCEP.Spatial.HeadGestureClassifier`
  * Nod detection: Δpitch > 8°/frame × ≥80ms → reversal
  * Shake detection: Δyaw > 10°/frame × ≥80ms → reversal
  * Tilt detection: Δroll > 12°/frame × ≥500ms sustained
  * Forward/backward thrust: sustained pitch change > 1500ms
  * 5-state HMM with minimum event duration and inter-event refractory period
  
* [ ] **Shoulder/Torso Extractor** — `HCEP.Kinect.TorsoAnalyzer`
  * Shoulder elevation differential (bilateral shrug detection)
  * Torso forward/backward lean angle from shoulder-to-hip vector
  * Torso rotation angle relative to camera axis
  * Proxemic zone classification (intimate/personal/social/public per Hall, 1966)

* [ ] **HCEP Mode Extension** — Add kinesic modifiers to 5-mode classification
  * Head nod during LOGIC → confirm/agreement signal
  * Head shake during LOGIC → disagreement/correction signal
  * Forward lean during SPIRIT → approach/intimacy signal
  * Shoulder shrug during THINK → epistemic uncertainty
  * Head tilt during HEART → deep listening / empathy posture

* [ ] **Updated Validation Dataset** — 2,000+ frames with kinesic ground truth annotations

---

## Phase 10 — AI Reciprocal Expression: The Expressive Agent — [PLANNED — Q4 2026 - Q1 2027]

**Goal:** Transform HCEP from a purely perceptual system into a **bidirectional social agent** — one that not only reads human expression but authentically generates reciprocal expressions in real-time through the avatar. This is the realization of HCEP's full vision: AI that participates in the complete nonverbal vocabulary of human communication.

**Scientific Basis:** Rizzolatti & Craighero (2004); Cassell et al. (1999); Mutlu et al. (2009); Hatfield et al. (1993); Dimberg et al. (2000); Bavelas et al. (2000). See `HCEP_SCIENCE_FOUNDATION.md` §Part V-VII.

**Core Concept:** The *Reciprocation Pipeline* — a parallel real-time system that plans and executes social signal synthesis in the avatar based on the current HCEP mode, the human's behavioral state, and the conversational context.

**Milestones:**

* [ ] **Backchannel Engine** — `HCEP.App.BackchannelController`
  * Real-time head nod generation during human speech at prosodic boundaries
  * Nod amplitude and rate modulated by HCEP mode (SPIRIT → slow, sustained; LOGIC → brief acknowledgments)
  * Biological timing: average 1-3 nods per 10 seconds during active listening
  * 3D avatar implementation: `Avatar3DControl.TriggerNod(amplitude, duration)`

* [ ] **Smile and Expression Reciprocation** — `HCEP.App.ExpressionMirror`
  * Detect human AU12 (smile) → delay 200-400ms (biological reaction time) → trigger avatar micro-smile
  * Detect human surprise (AU1+AU2+AU5) → avatar brow-raise response
  * Distinguish genuine (Duchenne: AU6+AU12) from social smile (AU12 only) — respond differently
  * Emotional contagion simulation: 30% probability of mirroring detected affect with 300-500ms delay

* [ ] **Gaze Pattern Reciprocation**
  * Social triangle scanning when in AFFECT/SPIRIT mode: avatar eyes scan Left Eye → Right Eye → Mouth at biologically realistic rates (~3 fixations/second)
  * Mutual gaze hold in SPIRIT mode (2-4 seconds) → break → return
  * Gaze aversion in THINK mode: avatar looks slightly away, signaling "I'm processing your request"
  * Eye contact on turn-yield: avatar establishes direct eye contact at natural conversational turn boundaries

* [ ] **Binocular Convergence** — `AvatarCoreControl` and `Avatar3DControl` update
  * Implement `convergenceAngle = atan(IOD/2 / max(0.3f, userDistM))` where IOD ≈ 65mm
  * Left eye: `yaw + convergenceAngle`; Right eye: `yaw - convergenceAngle`
  * Visible and neurologically authentic at distances 0.5-2.0m

* [ ] **Head Gesture Reciprocation**
  * Detect human nod → avatar produces confirming single nod with 250ms delay
  * SPIRIT mode: avatar produces head tilt (curiosity/interest posture) during human's personal disclosures
  * HEART mode: avatar produces slow forward head lean during empathic content
  * LOGIC mode: avatar head orientation stabilizes (active listening posture)

* [ ] **Proxemic Response**
  * Human approaches within 60cm → avatar pupils dilate (simulated via iris ring scaling)
  * Human moves beyond 180cm → subtle backward head lean (social vs. intimate register)

---

## Phase 11 — Multi-Modal Transformer Integration — [PLANNED — Q2 2027]

**Goal:** Replace the rule-based HCEP mode classifier with a learned transformer model trained on the expanded multimodal feature set (gaze + head kinematics + AUs + torso + speech prosody), achieving higher accuracy and better generalization across demographics and cultural contexts.

**Scientific Basis:** LeCun et al. (2015); Baltrusaitis et al. (2018); Vinciarelli et al. (2009).

**Milestones:**

* [ ] Collect 50,000+ labeled frames across diverse demographics (gender, age, culture, lighting)
* [ ] Train HCEP-Transformer v1 (12-layer, 256-dim, 8-head attention over 150ms temporal window)
* [ ] Target: κ ≥ 0.92, accuracy ≥ 93% (vs. current rule-based κ=0.81, 84.6%)
* [ ] Distill to SLM for on-device deployment (< 100MB, < 10ms inference)
* [ ] Cultural adaptation: separate classifier heads for East Asian, Western, and MENA interaction norms

---

## Phase 12 — Domain-Specific Deployments — [PLANNED — 2027]

### 12.1 Medical Education Platform

* Real-time gaze feedback for medical students during standardized patient simulations

* HCEP mode overlay for clinical instructors with session replay
* Integration with existing OSCE (Objective Structured Clinical Examination) scoring systems

### 12.2 Autism Spectrum Disorder Support

* HCEP-guided social skills training application

* Integration with existing ASD-specific platforms (VABS, ADOS-2 supplement)
* Caregiver dashboard with longitudinal gaze behavior tracking

### 12.3 Game Engine Integration

* Unreal Engine 5 HCEP Plugin: full MetaHuman gaze animation from live HCEP data

* Unity HCEP Avatar SDK v2.0: biologically accurate eye, brow, and mouth expression
* Middleware API for NPC social intelligence in AAA game titles

### 12.4 Companion Robot Platform

* ROS2 (Robot Operating System 2) node for HCEP perception and expression

* Tested deployment on Boston Dynamics Spot, Unitree H1, and custom mobile platforms
* Latency target: < 100ms perception-to-expression pipeline for genuine social responsiveness

---

## Metrics Targets by Phase

| Phase | Cohen's κ | Accuracy | Latency | Modalities |
|---|---|---|---|---|
| Current (v1.1) | 0.81 | 84.6% | <50ms | Gaze + 6 AUs + Speech |
| Phase 9 (Kinesics) | 0.87 | 89% | <60ms | + Head gestures + Torso |
| Phase 10 (Reciprocation) | 0.87 | 89% | <60ms | + Expression synthesis |
| Phase 11 (Transformer) | 0.92 | 93% | <70ms | All modalities, learned |
| Phase 12 (Domain) | 0.94+ | 95%+ | <80ms | Domain-specific fine-tuning |

---

## Phase 13 — Phoneme-to-Viseme Lip Sync: Biologically Accurate Avatar Speech — [PLANNED — Q2-Q3 2027]

**Goal:** Give the HCEP avatar a biologically authentic mouth — one whose movements are precisely synchronized to each phoneme of its speech output, driven by the 21-viseme SAPI taxonomy mapped to the Preston Blair animation canon. Combined with the TTS system (Phase 10 speech), this completes the avatar's expressive speech loop: the avatar speaks, and its mouth moves with the exact shape each sound requires.

**Scientific Basis:**
* **McGurk Effect** (McGurk & MacDonald, 1976): Visual mouth movement is a first-class speech perception channel. When auditory and visual phonemes conflict, listeners perceive a blend. Incorrect or absent lip sync actively *degrades* speech intelligibility — not merely aesthetics.
* **Preston Blair (1949)**: Established the canonical 18 mouth-shape key poses used in Disney and Warner Bros. animation. Each pose is a distinct visual configuration of the oral articulators corresponding to one or more phonemes.
* **Cohen & Massaro (1994) DOMINANCE Model**: Audiovisual speech integration is weighted by channel reliability. In noise or at distance, visual lip movement *dominates* — making accurate lip sync even more critical in real-world deployment scenarios.
* **Sumby & Pollack (1954)**: Visual speech contributes up to 15 dB of effective SNR improvement in noise — accurate lip sync makes speech more intelligible in real listening environments.
* **SAPI Viseme Taxonomy**: Microsoft's Speech API defines 21 viseme groups covering all English phonemes, with timing events fired per phoneme during TTS synthesis.

**Architecture — Already Implemented in `HCEP.Speech`:**

| Component | Status | Description |
|---|---|---|
| `VisemeData` struct | ✅ **Built** | 6 normalized mouth parameters per phoneme |
| `VisemeController` | ✅ **Built** | Full 22-row Preston Blair / SAPI lookup table |
| `ISpeechSynthesizer.VisemeChanged` | ✅ **Built** | Per-phoneme event on the TTS interface |
| `WindowsTtsSynthesizer` | ✅ **Built** | Native SAPI VisemeReached → VisemeChanged (phoneme-accurate) |
| `OpenAiTtsSynthesizer` | ✅ **Built** | Amplitude-driven approximate visemes (fallback) |
| `ElevenLabsTtsSynthesizer` | ✅ **Built** | Amplitude-driven approximate visemes (fallback) |
| `HybridTtsEngine` | ✅ **Built** | Relays VisemeChanged from active backend |

**Remaining Milestones:**

* [ ] **`AvatarCoreControl.SetViseme(VisemeData)`** — 2D Happy Face mouth animation
  * Jaw opening: arc height proportional to `JawOpen`
  * Lip rounding: draw pursed ellipse for O/U vowels
  * Lip spreading: wide thin line for I/EE phonemes
  * Bilabial closure: flat line for M/B/P (`LipCompressed = 1.0`)
  * Upper lip retraction: show teeth edge for F/V
  * Smooth co-articulation: `VisemeController.Lerp()` at 30Hz between successive visemes

* [ ] **`Avatar3DControl.SetViseme(VisemeData)`** — 3D Wireframe mouth animation
  * Map `JawOpen` to vertical displacement of lower-jaw mesh vertices
  * Map `LipRound` to radial contraction of lip outline vertices
  * Map `LipSpread` to horizontal stretch of mouth corners
  * Map `LipCompressed` to bilateral lip-vertex convergence
  * All transformations applied to FaceTrackLib mesh vertex indices 44-68 (lip region)

* [ ] **`IAvatarComponent.SetViseme(VisemeData)`** — Add to interface for both 2D and 3D controls

* [ ] **Co-articulation Blending** — Run `VisemeController.Lerp()` at display refresh rate (30Hz) to smooth between successive visemes, eliminating the "snapping" visible in naive phoneme-by-phoneme rendering

* [ ] **Phase 14 integration** — Connect avatar viseme input to `HybridTtsEngine.VisemeChanged` event in `AvatarWindow`

* [ ] **Viseme diagnostics HUD** — Show current phoneme name (`VisemeController.GetName()`) and mouth parameters in the Avatar telemetry bar during speech

---

## Phase 14 — Contextual Intelligence: Time, Space & Situation Awareness — [PLANNED — Q3 2027]

**Goal:** Give the avatar a persistent, grounded awareness of *when* and *where* it exists — time of day, day of week, season, geographic location, physical environment type (bedroom, office, lab, park), and situational context. This information modulates every behavioral dimension: greeting style, conversational register, response timing, and above all, the **Silence Protocol** — knowing when *not* to speak.

**Scientific Basis:** Hall (1959, 1983) Chronemics; Barker (1968) Behavior Settings Theory; Sacks, Schegloff & Jefferson (1974) Turn-Taking; Forgas (1979) Social Situation Theory; Duncan (1972) Speaker Turn-Yielding Cues; Jaworski (1993) The Power of Silence. See `HCEP_SCIENCE_FOUNDATION.md` §Part XI.

**Core Concept: The Silence Protocol**

> The most sophisticated communicative act available to the avatar is not speech — it is *knowing when to be silent*. Every existing AI speaks when asked. HCEP's avatar will also know when to simply *be present*, driven by real-time facial cues.

**Silence triggers (avatar should NOT initiate speech):**
* THINK mode (gaze aversion + defocus) → user is internally processing; any AI speech interrupts
* Sustained brow furrow (AU4 > 3s) → deep cognitive engagement; user has not invited response
* Speech accompanied by away-gaze → user is verbalizing for themselves (thinking aloud)
* Late night (22:00+) + HEART mode → maximum attunement; presence matters more than words

**Speak-invitation signals (avatar CAN respond):**
* Direct sustained gaze toward avatar → floor-yield; user awaits response
* Raised brows (AU1+AU2) + direct gaze → question; response invited
* Head tilt toward avatar + pause → "what do you think?" posture

**Milestones:**

* [ ] **`ContextSnapshot` model** in `HCEP.Core.Models`
  * `Timestamp`, `TimeOfDay` (enum), `DayType` (Weekday/Weekend/Holiday)
  * `Season`, `TimezoneId`
  * `Latitude?`, `Longitude?` (Windows Location API, opt-in)
  * `CityName?`, `CountryCode?` (reverse geocoding)
  * `Environment` (enum: Bedroom/LivingRoom/Office/Lab/Outdoor/Public)
  * `UserDefinedLocation` (free-text: "my studio", "dad's kitchen")
  * `Privacy` (Private/SemiPrivate/Public)
  * `SilenceProtocolActive` (bool — computed from HCEP mode + time + context)
  * `CommunicationRegister` (Formal/Informal/Intimate/Professional)

* [ ] **`TimeContextProvider`** — reads system clock, determines `TimeOfDay` category and `Season`

* [ ] **`GeographicContextProvider`** — Windows Location API (opt-in, user consent required), reverse geocodes lat/long to city/country

* [ ] **`SituationContextProvider`** — user-configured environment type + activity description; optional camera-based scene classification (future)

* [ ] **`SilenceProtocolEvaluator`** — real-time rule engine: HCEP mode × time × space × facial cues → `SilenceProtocolActive` bool

* [ ] **LLM Context Injection** — `ContextSnapshot` injected into every LLM system prompt:

  ```
  Current context: [Evening | Friday | Living Room | Private]
  Temporal register: Informal/Personal
  Silence protocol: INACTIVE — user has direct gaze, floor available
  ```

* [ ] **Turn-taking detector** based on Duncan (1972) 6 cues:
  * Gaze toward avatar (from HCEP pipeline)
  * Pause after syntactically complete utterance (from VAD)
  * Sociocentric sequence detection ("you know?", "right?") — text post-processing

* [ ] **Settings UI** — environment type selector and user-defined location field
* [ ] **Dashboard indicator** — show active context (time band, environment, silence state)

---

*HCEP Roadmap — Document last updated: July 2026*  
*© 2026 Kirk LaSalle. All rights reserved.*
