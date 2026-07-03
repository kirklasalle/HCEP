# Changelog

All notable changes to the **Human Communication Eye Protocol (HCEP)** are documented here.  
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).  
Versioning follows [Semantic Versioning](https://semver.org/).

---

## [Unreleased] — 2026-07-03

### Security

- **[CRITICAL]** Replaced incorrect `Interlocked.CompareExchange(ref obj, null!, null!)` pattern (which is *not* a volatile read) with `Volatile.Read` / `Volatile.Write` on all four cross-thread shared-state properties in `VisionPipeline`: `LatestSpeech`, `LatestColor`, `LatestRecognition`, `PendingEnrollmentName`. Eliminates a race condition where written values were silently invisible to reader threads, causing dropped speech and recognition results.
- **`WindowsCredentialStore`** — New class (`HCEP.Intelligence.WindowsCredentialStore`) wrapping Windows Credential Manager (`advapi32.dll`) via P/Invoke. API keys are now read from the WCM vault first, falling back to environment variables, then `LlmConfiguration` properties. Keys stored in WCM are never visible in process listings or environment dumps. Well-known target names: `HCEP/OpenAI`, `HCEP/Anthropic`, `HCEP/Gemini`, `HCEP/Mistral`, `HCEP/xAI`, `HCEP/Cohere`. Includes a `LoadWithFallback()` helper for zero-friction adoption.
- **`EncryptedStorageProvider`** — Documented the DPAPI `CurrentUser` scope limitation with explicit upgrade-path notes pointing to `WindowsCredentialStore` for API keys and a future per-session AES-256-GCM key for biometric embeddings.

### Fixed

- **`ArcFaceRecognizer.LoadModel()`** — Wrapped `new InferenceSession()` in `try/catch`. A corrupted or incompatible ONNX file previously crashed the entire pipeline unhandled; it now logs `LogError` and leaves `IsModelLoaded = false`.
- **`HybridLlmEngine.PromptAsync()`** — Added explicit `LogError` before the no-LLM fallback return so the silent `[No LLM available]` path is visible in production logs.
- **`KinectSensorSource.InitializeFaceTracking()`** — Split the generic `catch (Exception)` into a dedicated `catch (DllNotFoundException)` handler with a specific "Kinect SDK not installed" message.
- **`AudioPipeline.StopAsync()`** — Elevated speech-recognizer flush failure from `LogDebug` to `LogWarning` + `_telemetry.Increment("audio.flush_error")`.
- **`InMemoryKnowledgeStore.Query()`** — Snapshots the key list inside the lock then releases before LINQ execution, preventing excessive lock hold time.
- **`PluginApiServer` WebSocket handler** — Closes with `WebSocketCloseStatus.InternalServerError` on exception paths rather than always `NormalClosure`.
- **`HecpPipelineOrchestrator.OnFaceFrameReady()`** — Emits `LogWarning` when `TryWrite` returns `false` (channel back-pressure drop).
- **`HecpPipelineOrchestrator.OnAudioFrameReady()`** — Emits `LogWarning` when audio frame is dropped due to back-pressure.

### Added

- **`HybridLlmEngine` — Cloud Circuit Breaker** — Configurable circuit-breaker (`CircuitBreakerThreshold = 3`, `CircuitBreakerCoolDown = 30s`). After threshold consecutive failures the breaker opens; calls are short-circuited with a `LogWarning` until cool-down expires. Resets on success.
- **`InMemoryKnowledgeStore` — Capacity Limits** — `MaxSubjects` (500), `MaxTriplesPerSubject` (1000), string length bounds (`subject ≤ 255`, `relation ≤ 100`, `object ≤ 10,000` chars), and LRU eviction of oldest triple when per-subject cap is reached.
- **`HecpPipelineOrchestrator.AutoFallbackSeconds`** — Replaced hardcoded `const double AutoFallbackSeconds = 5.0` with a configurable public property (default: 5.0 s). Set to `double.MaxValue` to disable auto-fallback entirely.
- **`PnPSolver` — epsilon documented** — Added inline comment explaining the `1e-3f` finite-difference step size choice (numerical precision vs. float cancellation trade-off at 32-bit resolution).
- **`HcepModeAnalyzer` — All thresholds documented** — Added XML comments with empirical basis for `GazeAversionAngleDeg` (15°, Argyle & Cook 1976), `BrowLowerThreshold` (-0.3, HCEP κ=0.8084 dataset), `SmileThreshold` (0.2, micro-expression inclusive), `ModeTransitionMinConfidence` (0.4), and `ModeStabilityFrames` (5 frames = ~167 ms at 30 fps).
- **`ThreeStageGazeEstimator.HeadWeight`** — Documented the empirical 0.6 value with reference to the HCEP validation dataset (6,000 frames, κ=0.8084, accuracy 84.55%) and the rationale for head-pose dominance over AU eye offsets at typical interaction distances.
- **Tests — Concurrency stress suite** (`VisionPipelineConcurrencyTests`): Four async tests validating `Volatile.Read/Write` correctness under concurrent load for all four shared-state properties.
- **Tests — Knowledge store stress suite** (`InMemoryKnowledgeStoreStressTests`): Concurrent assert/query deadlock tests, capacity limit tests, LRU eviction verification, and input validation negative-path tests.
- **Tests — ArcFaceRecognizer negative paths** (`ArcFaceRecognizerNegativePathTests`): Tests for missing file, corrupted ONNX, zero-byte file, embedding with no model loaded, and match with no model loaded.
- **Tests — Circuit breaker negative paths** (`HybridLlmEngineCircuitBreakerTests`): Tests for both-providers-down fallback, breaker opening after threshold failures, breaker reset after cool-down, and no-API-key fast path.

### Verified

- **`PnPSolver` stackalloc** — Confirmed already fixed in prior session; all `stackalloc` allocations are outside the Levenberg-Marquardt iteration loop.
- **`HCEP-SDK` sync** — All 6 SDK files (`csharp/`, `python/`, `unity/`, `unreal/`) are byte-identical between `HCEP/sdk/` and `HCEP-SDK/sdk/`. No sync needed.

---

## [1.0.0] — 2026-06-19

### Added

- **Documentation** — Aligned all definitions and terminology to *Human Communication Eye Protocol* branding throughout all docs.
- **README** — Added Advanced Use Cases & Human-Avatar Applications section covering human performance, acting, and cloning use cases.
- **PRD** — Expanded advanced use cases section.
- **README** — Added dashboard screenshot, Cones of Vision image, animated SVG diagrams (Gaze Geometry, Cones of Vision & 13 Regions, True Gaze Parallax Calibration).
- **Dual Licensing** — Updated README with HCEP-SDK integration overview and dual-licensing (MIT SDK / proprietary core) explanation.

### Changed

- **Asset Protection** — Applied proprietary trade-secret notices to all core engine source files; SDK files updated to MIT license headers.
- **README** — Fixed SVG syntax errors and added cache-busting version query strings for correct GitHub rendering.

### Fixed

- **SVG rendering** — Corrected broken inline SVG paths that failed to render on GitHub.

---

## [0.9.0] — 2026-06-06

### Added

- **Project Audit & Security Hardening** — Full internal security audit completed; hardened plugin API, input validation, and DPAPI encryption scope documented.
- **Autonomous Avatar Responsiveness** — Avatar transitions smoothly to neutral idle state during tracking gaps without freezing on last known pose.
- **Decoupled Head Rotation** — Avatar head rotation axis decoupled from face-mesh FK to prevent pole-lock artefacts at extreme angles.
- **Plugin API** — Embedded Kestrel server exposing REST (`/api/state`, `/api/tools/openai`), WebSocket (`/ws/stream`), MCP (`/mcp`), and gRPC (`HcepPluginService`) endpoints on port 5000.
- **Multi-Language SDK** — C# Semantic Kernel plugin, Python LangChain tool, Python LlamaIndex tool, Unity `HcepGazeController`, and Unreal Engine `UHcepGazeController` released in the companion `HCEP-SDK` repository.

---

## [0.8.0] — 2026-02-27

### Added

- **Avatar 3D (Phase 6)** — Full 3D Candide-3 wireframe avatar with gaze-driven pupils locked to live eye sockets. Supports screen-position tracking and 1.2 px wire rendering.
- **Dual-Eye Socket Lock** — Persistent per-eye lock with pitch/yaw eyeball rotation; pupil seating stabilized under extreme yaw via reseeding logic.
- **Near Mode Depth** — Kinect v1 depth stream with near-mode flag (`NUI_SKELETON_TRACKING_FLAG_ENABLE_IN_NEAR_RANGE`) for sub-1-metre tracking down to ~40 cm.
- **Avatar 2D / 3D Toggle** — Runtime switch between `AvatarCoreControl` (2D) and `Avatar3DControl` (3D wireframe) via `IAvatarComponent` factory + ComboBox selector.
- **`avatar.bat`** — Dedicated launch shortcut that opens the Avatar window directly.
- **HUD Mesh Diagnostics** — MESH HUD indicator showing live `GetProjectedShape` success/fail rate and HRESULT codes.

### Fixed

- **FaceTrackLib COM Interop** — Bypassed broken `QueryInterface` (E_NOINTERFACE) on `IFTResult`, `IFTFaceTracker`, and `IFTImage` by calling all methods via raw vtable helpers. Matches the C++ FaceTracking reference implementation.
- **Depth stream format mismatch** — Corrected `NuiImageStreamOpen` to use `DepthAndPlayerIndex` type consistent with the `NUI_INITIALIZE_FLAG_USES_DEPTH_AND_PLAYER_INDEX` init flag, eliminating `E_INVALIDARG` at startup.
- **FaceTrackLib depth focal length** — Corrected depth camera focal length to 571.26 px (640×480) matching `NUI_CAMERA_DEPTH_NOMINAL_FOCAL_LENGTH_IN_PIXELS × 2`.
- **Pitch drift** — Fixed `FeaturePoints3D` to produce head-relative mm coordinates using `HeadTranslation` as origin; added bounding-box fallback path.
- **Avatar high-poly mesh** — Kept mesh visible across transient tracking misses (frame gaps ≤ 500 ms) rather than falling back to dot-cloud prematurely.
- **Near Mode crash** — Fixed startup crash when Near Mode was enabled before skeleton tracking was initialized.

---

## [0.6.0] — 2026-02-27 (Phase 6 — True Gaze Architecture)

### Added

- **`GazeVectorEngine`** — Computes real-time pitch/yaw for the avatar to look at the user's eyes using world-space geometry and EMA smoothing.
- **`CalibrationMatrixCalculator`** — Builds a Kinect-to-screen-centre transform from measured physical offsets (X/Y/Z mm) for true parallax correction.
- **`MicroSaccadeController`** — Alternates fixation between user's left and right eye on a configurable 1–3 s interval, simulating natural biological saccades.
- **`CalibrationWindow`** — Step-through WPF UI for measuring and applying the physical Kinect mounting offset.
- **`AvatarWindow`** — Standalone floating window hosting the avatar face; registers its eye pixel positions with the orchestrator for gaze calculation.
- **Phase 6 Milestone** — Verified real-time window-agnostic gaze tracking; avatar eyes follow user eyes across monitor positions.

### Fixed

- **Vertical gaze correction** — Applied −5° empirical pitch correction.
- **Binocular convergence** — Unified left/right eye vector into single convergence point.
- **gitignore** — Broadened tracking rules to prevent `models/` folder from shadowing `C# Models/` namespaces.

---

## [0.3.0] — 2026-02-27 (Phase 3 — World-Space Gaze Engine)

### Added

- **`GazeVectorEngine.AvatarEyeScreenToWorldMm()`** — Maps avatar eye pixel coordinates to 3D world-space mm for the gaze calculation pipeline.
- **`AvatarWindow` / `AvatarCoreControl`** — WPF avatar face UserControl with self-aware eye screen coordinates and `SetGaze(pitch, yaw)`.

---

## [0.2.0] — 2026-02-26 (Phase 2 — Avatar & Calibration)

### Added

- **`Phase 1` Calibration Engine** — `CalibrationWindow`, `LatestFaceFrame` property on orchestrator, `ApplyCalibration()` method.
- **`Phase 2` AvatarCoreControl** — 2D happy-face UserControl with pupil-offset gaze animation.

---

## [0.1.0] — 2026-02-23 (Alpha)

### Added

- Initial commit: HCEP v0.1.0 Alpha.
- Core pipeline: Kinect v1 native COM interop via `Kinect10.dll` (bypasses managed `Microsoft.Kinect.dll` incompatible with .NET 9).
- Sensor abstraction: `ISensorSource` with `KinectSensorSource`, `WebcamSensorSource`, `SimulatedSensorSource`.
- Vision pipeline: `VisionPipeline` → `ThreeStageGazeEstimator` → `HcepModeAnalyzer` → `HcepReading`.
- Audio pipeline: `AudioPipeline` → `WhisperSpeechRecognizer` → `SpeechResult`.
- Hybrid LLM engine: `HybridLlmEngine` routing between local (Ollama / Llama.cpp) and frontier cloud providers (OpenAI, Anthropic, Gemini, Mistral, xAI, Cohere).
- Knowledge store: `InMemoryKnowledgeStore` triple-store graph with DPAPI-encrypted persistence.
- Telemetry: `TelemetryService` with counters, gauges, timers, and JSON export.
- ArcFace face recognition: ONNX-based 512-d embedding with cosine-similarity matching.
- `PnPSolver`: Levenberg-Marquardt iterative pose solver for sub-millimetre head coordinate stability.
- `ConfidenceCone`: 3D gaze region classifier mapping to the 13 HCEP spatial regions.
- Plugin API server: Embedded Kestrel REST + WebSocket + gRPC + MCP endpoints.
- WPF dashboard: `MainViewModel`, `KinectVideoWindow`, live HUD overlays.
- Empirical validation: Achieved Cohen's Kappa **0.8084** and mode-classifier accuracy **84.55%** over 6,000 frames.
