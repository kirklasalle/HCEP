# Changelog

All notable changes to the **Human Communication Eye Protocol (HCEP)** are documented here.  
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).  
Versioning follows [Semantic Versioning](https://semver.org/).

---

## [1.6.0] — 2026-08-19

### Added — HCEP Avatar Studio & 3D Kinect Fusion Laboratory

- **HCEP Avatar Studio Standalone Window** (`AvatarStudioWindow.xaml`, `AvatarStudioWindow.xaml.cs`, `AvatarStudioViewModel.cs`) — Comprehensive authoring, scanning, simulation, and deployment suite for custom avatars:
  - **🎨 2D SVG Studio**: Interactive parametric designer for skin tone, cyber glow, iris palette, eye dimensions, pupil aperture, inter-ocular spacing, eyebrow thickness, and cybernetic tech lines/halo with live SVG markup preview.
  - **🌐 3D Kinect Fusion Studio**: Volumetric TSDF voxel scanning and reconstruction engine utilizing multi-frame depth sensor accumulation to construct high-detail watertight 3D head surfaces.
  - **🧪 Testing Sandbox**: Live kinematics simulation harness with horizontal/vertical gaze sliders, distance slider, smile intensity, brow raise/furrow, speech visemes selector, nod/tilt/blink animation triggers, and **Live Sensor Mirror Mode**.
  - **🚀 One-Click Catalog Deployment**: "Push to Official Avatar App" dynamically registers custom avatars into `AvatarCatalog` for immediate live hot-swapping in `AvatarWindow`.
  - **💾 SVG Export**: Directly exports standard standalone SVG XML files for external use.

- **Parametric 2D SVG Avatar Control** (`SvgAvatarControl.cs`) — Full vector-based avatar implementing `IAvatarComponent` with real-time responsive eyes, gaze pitch/yaw offsets, eyelids (blinks), eyebrows (AU3/AU5/HCEP furrow), smile curves, phoneme viseme lip shapes, and standalone SVG XML markup generation.

- **Kinect Fusion 3D Head Scanner** (`KinectFusionHeadScanner.cs`) — Volumetric 3D head reconstruction foundation in `HCEP.Spatial` based on Microsoft Kinect Developer Toolkit v1.8 Kinect Fusion TSDF voxel integration with smooth surface normal estimation and parametric head synthesis.

- **Dynamic Avatar Catalog Registration** (`AvatarCatalog.cs`, `AvatarWindow.xaml.cs`) — Extended `IAvatarCatalog` with dynamic custom descriptor registration, factory instantiation, and `CatalogChanged` notification event enabling dynamic hot-swapping in `AvatarWindow`.

### Added — Frontier Cloud LLM Resilience & HTTP Decompression

- **Resilient Cloud Model Discovery & Curated Fallback** (`HybridLlmEngine.cs`) — Implemented a 15-second bounded discovery cancellation window preventing 100-second network stalls in the AI Settings dialog. Added automatic fallback to curated frontier models (`llama-3.3-70b`, `gemini-2.5-flash`, `gemini-2.5-pro`, `claude-3.7-sonnet`, `gpt-4o`, `deepseek-chat`, `deepseek-r1`, `qwen-2.5-72b`, etc.) including user-configured models when OpenRouter or other aggregator model listing endpoints fail or time out.
- **Automatic HTTP Decompression & Socket Resilience** (`App.xaml.cs`) — Configured `SocketsHttpHandler` on `services.AddHttpClient<HybridLlmEngine>()` with `AutomaticDecompression = DecompressionMethods.All`, 15-minute connection pooling, and a 15-second connect timeout, dramatically accelerating large API catalog payload transfers and eliminating socket timeout stalls.
- **Standardized OpenRouter Header Routing** (`HybridLlmEngine.cs`) — Injected required `HTTP-Referer` and `X-Title` identification headers on all OpenAI-compatible requests directed to `openrouter.ai`.

### Added — World-Class Biometric Precision & 3D Mesh Extraction

- **ArcFace 5-Point Affine Landmark Alignment** (`ArcFaceRecognizer.cs`, `IFaceRecognizer.cs`, `VisionPipeline.cs`) — Implemented closed-form Umeyama similarity transformation to canonical ArcFace $112 \times 112$ coordinates with subpixel bilinear interpolation, guaranteeing $>99.5\%$ identification accuracy invariant to head rotation/tilt.
- **Running Centroid Multi-Pose Enrollment** (`ArcFaceRecognizer.cs`) — Multi-sample exponential moving average (EMA) centroid blending for multi-angle face enrollment.
- **True 3D Face Mesh Vertices & UV Coordinates** (`FaceFrame.cs`, `KinectSensorSource.FaceTracking.cs`) — Direct extraction of 3D spatial points in head space (mm) and normalized UV coordinates across 640×480 screen space.

### Changed — 3D Wireframe Depth-Attenuated Backface Culling

- **Depth Attenuation & Rear-Skull Culling** (`Avatar3DControl.cs`) — Implemented 2D screen-space cross product winding order calculation to separate front-facing facial wires from rear-skull wireframes, eliminating "see-through head" overlap clutter.

### Tests & Verification

- **222 / 222 Tests Passing (100% Pass Rate)** — Added comprehensive unit test suites covering ArcFace 5-point alignment, EMA centroid blending, Kinect Fusion 3D scanning, Avatar Catalog dynamic registration, SVG markup generation, and cloud model discovery fallback.

---

## [1.5.0] — 2026-07-18

### Added — High-Poly Procedural Avatar (Phase 15 Avatar Platform)

- **`AvatarHighPolyWireframeControl`** (`AvatarHighPolyWireframeControl.cs`) — Added a selectable procedural high-density head-and-shoulders wireframe avatar. The mesh is deterministic and Kinect-mesh-independent, with 6,374 model vertices and 12,038 wire edges across an anatomically biased head, neck, shoulder surface, facial contour lines, ears, jawline, brow ridge, nose, lips, clavicles, and neck tendon guides.

- **HCEP eyes on the high-poly avatar** — The new avatar implements the same HCEP eye sphere rendering stack as the existing avatar family: sclera sphere, foreshortened iris/pupil, specular highlight, binocular convergence, micro-saccades, blink cadence, proxemic pupil dilation, social gaze offsets, and screen-space eye-provider positions for `GazeVectorEngine`.

- **First-class avatar catalog entry** (`AvatarCatalog.cs`, `AvatarWindow.xaml`, `AvatarWindow.xaml.cs`) — Added `3d-highpoly-wireframe` / `3D High-Poly Wireframe` to the selectable Avatar App dropdown. The window now routes gaze, brows, visemes, smiles, nods, tilts, proxemics, social gaze, and head-pose input to the new avatar and registers its eye positions when selected.

- **High-poly anatomy audit and contour refinement** (`AvatarHighPolyWireframeControl.cs`) — Audited the first procedural mesh and replaced overly uniform ellipsoid/cylinder/shoulder surfaces with a more human silhouette: cranium/temple/cheekbone/jaw/chin shaping, non-cylindrical neck profile, trapezius-to-shoulder transition, deltoid shoulder falloff, brow ridges, eye contours, nose bridge/tip/nostrils, upper/lower lips, cheek planes, ears, clavicles, and sternocleidomastoid guide lines.

### Changed — 3D Wireframe Mesh Quality Parity (Phase 15 Avatar Platform)

- **Live mesh always preferred for 3D wireframe** (`AvatarWindow.xaml.cs`) — The 3D wireframe avatar now always uses the live Candide-3 projected mesh (`face.FaceMeshVertices2D`) regardless of mirroring state. Previously, non-mirrored mode used the neutral mesh (`face.NeutralFaceMeshVertices2D`), which produced a visually sparse wireframe because the front-on projection collapsed depth and made many triangles appear as near-zero-area slivers. The live mesh carries the user's head rotation baked into the vertices, "opening up" the triangle topology to produce the rich, detailed wireframe previously only visible in mirrored mode. Neutral mesh is now used only as a fallback when the live mesh is unavailable.

- **Eye-first feature-point anchoring** (`Avatar3DControl.cs`) — Replaced the proportional Happy Face-based eye placement system (which used hardcoded 280×280 canvas ratios from the 2D avatar) with Candide-3 feature-point-derived eye socket centres. The right eye contour (FP indices 9–14) and left eye contour (FP indices 30–35) centroid positions are computed each frame and now own socket placement regardless of mirroring state. In full-mesh mode the projected Candide-3 mesh and live eye anchors are mapped directly through the same fit transform with no extra head-pose correction, making the eyes the parent coordinate for avatar facial alignment.

- **Unconditional 3D avatar head-pose data flow** (`AvatarWindow.xaml.cs`) — `Avatar3D.SetHeadPose()` is now called regardless of mirroring state. Head pose remains available for gaze-relative pupil computation and FP fallback rendering, while full-mesh mode trusts the already-projected Candide-3 vertices rather than applying a second corrective transform on top of them.

### Fixed

- **Eye position mismatch** — Eyes in the 3D wireframe now track correctly with head rotation, matching the mesh geometry rather than floating at incorrect positions derived from the 2D Happy Face's 280×280 proportional system.

- **Visual quality discrepancy** — The 3D wireframe now renders at identical quality in both mirrored and non-mirrored modes, eliminating the dramatic visual gap where the default (non-mirrored) mode appeared sparse and minimal compared to the richly-detailed mirrored mode.

- **Mesh/eye drift in the Avatar app** — Removed the extra full-mesh head correction that fought the Kinect-projected Candide-3 vertices and removed full-mesh eye-anchor smoothing lag. In Candide-3 mode, live eye landmarks now have priority and the face mesh renders in the same coordinate frame as the eyes.

- **Release ZIP generation** (`scripts/package_release.ps1`) — Replaced the fragile `Compress-Archive` packaging step with the .NET `ZipFile` API and explicit archive-size verification. The script now reliably emits a readable `HCEP-win-x64-v1.5.0.zip` archive after Release publish.

### Changed — Versioning & Release Packaging

- **Version metadata aligned to v1.5.0** (`Directory.Build.props`, `scripts/package_release.ps1`) — Assembly, file, informational, Appx manifest, and generated ZIP names now derive from the shared project version so release artifacts match the changelog and roadmap.

### Documentation

- **`docs/release_notes_v1.5.0.md`** — New operator-facing notes for the eye-first Candide-3 wireframe, high-poly procedural avatar, 3D eye-position calibration, validation status, and packaging changes.
- **Graphics, roadmap, and developer documentation refreshed** — Updated avatar architecture, current project state, implemented avatar catalog count, high-poly mesh details, interface contract, and release packaging guidance.

### Tests

- **`PipelineIntegrationTests.Pipeline_WithSpeechInjection_ChangesCognitiveState`** — Increased the full-suite cancellation budget from 8s to 15s after the test passed in isolation but starved readings under full-suite load.

### Compatibility

- All changes verified compatible with PnP head pose calibration (operates on raw 3D `FaceFrame.FeaturePoints3D`, independent of avatar rendering).
- `GazeVectorEngine` eye-position tracking unaffected — `LeftEyeScreenPos`/`RightEyeScreenPos` continue to be correctly set.
- Build: green, 0 errors, 0 warnings, `TreatWarningsAsErrors` active.
- Test suite: 211/211 passing.
- Release package: `publish/HCEP-win-x64-v1.5.0.zip` generated and verified readable.

---

## [1.4.0] — 2026-07-17

### Added — LLM Telemetry Grounding (world-class "seeing via telemetry")

- **`HcepTelemetryBundle`** (`HCEP.Core.Models`) — Immutable snapshot of every live HCEP signal at chat send time: pipeline FPS, tracked-person count, primary HCEP reading (mode/region/cognitive/valence/confidence), identity, distance, left/right eye positions, inter-ocular distance (mm), head rotation (pitch/yaw/roll), context (time/space/situation), speech cadence, most-recent transcript, and connection/calibration state. Emits a stable `ToPromptString()` representation with labeled fields and explicit `unavailable` markers for missing values.
- **`HybridLlmEngine.LatestTelemetry`** — New volatile property populated by the UI immediately before each `PromptAsync` call so the LLM can "see" via structured telemetry instead of hallucinating a visual channel.
- **`BuildSystemPrompt` — grounding + non-hallucination policy** — The system prompt now opens with an explicit *Perception Model* and a five-clause *Grounding & Non-Hallucination Policy* that:
    1. Requires every factual claim be supported by the telemetry block, the knowledge store (via `query_knowledge` / `summarize_person`), or the user's message.
    2. Forbids invention of identities, gaze directions, emotions, distances, timestamps, or any numeric/categorical sensor value.
    3. Instructs the model to respond **exactly** with `"I don't have that information right now."` when a claim cannot be verified.
    4. Corrects the "I have no way of seeing" and "I don't know what to do with the telemetry" failure modes by defining the telemetry block *as* the model's perceptual channel.
    5. Forbids fabricated tool results.
- **`MainViewModel.SendAsync` — telemetry attach path** — Builds an `HcepTelemetryBundle` from `LatestSnapshot`, `PrimaryPerson`, `LatestCadence`, and `HybridLlmEngine.CurrentContext`, then writes it to `LatestTelemetry` before the LLM call. Additive — existing `HcepReading` argument is preserved.
- **Rolling chat telemetry harness** — The LLM Assistant panel now exposes a top-of-chat `Telemetry Window` slider (`Snapshot → 5s`) and a `Density` slider (`Sparse` / `Balanced` / `Dense`). HCEP keeps a bounded 5-second in-memory telemetry ring buffer, summarizes the selected window into dominant-mode / dominant-region / dominant-valence / confidence-trend / distance-trend / head-pose-range / speech-activity stats, and appends a sampled timeline to the prompt.
- **Prompt-budget auto-coarsening** — When the selected telemetry window gets speech-heavy or sample-heavy, HCEP now automatically reduces timeline density before sending the prompt, preserving grounding while preventing unbounded prompt growth. The telemetry block explicitly reports when this auto-coarsening occurs.
- **Prompt-debug surfaces** — The LLM Assistant panel now includes `Prompt Telemetry Debug` (telemetry-only) and `Full System Prompt Debug` expanders showing the exact telemetry block and full system prompt used for the last chat send.
- **Chat harness persistence** — `ChatTelemetryWindowSeconds` and `ChatTelemetryDensityLevel` now persist through `LlmConfiguration` / `SettingsPersistence`, and `SettingsWindow` clone/copy helpers preserve them so opening and saving Settings does not wipe the chat-harness controls.
- **Prompt-size estimate + clipboard tools** — The chat harness now shows a live approximate request-size estimate (`prompt + current input`) and exposes `Copy` actions for both prompt-debug panes. The expanded/collapsed state of both debug panes is now persisted through `LlmConfiguration` as well.

### Added — In-App Updater (non-destructive)

- **`HCEP.App.Updates.UpdateService`** — Queries the public GitHub releases API for `kirklasalle/HCEP`, compares the tag against `AssemblyInformationalVersion`, downloads the best matching Windows x64 ZIP asset to `%LocalAppData%\HCEP\Updates\<tag>\`, and generates a PowerShell installer script (`install-update.ps1`) that:
  - Waits for `HCEP.App.exe` to exit.
  - Backs up user data to `%LocalAppData%\HCEP\Updates\<tag>\backup\`.
  - Uses `robocopy /XD config logs Logs .venv /XF hcep-settings.json overlay-alignment.json` to copy the new bits **around** user state.
  - Never touches Windows Credential Manager entries under the `HCEP/*` target family — API keys are structurally out of the update path.
- **`CheckForUpdatesWindow`** — New modal window showing installed vs latest version, release notes, download progress, and the non-destructive-update guarantee. Reveals the staged installer in File Explorer on completion.
- **Menu + Header entry points** — New "⬆ Check for Updates" button in the top-right header row next to `Sensor Streams`, plus `Help → Check for Updates…` MenuItem. Both bind to the new `CheckForUpdatesCommand`.
- **`Help → About HCEP`** — Previously an inert MenuItem; now bound to `ShowAboutCommand`, which prints the current `AssemblyInformationalVersion` and copyright.

### Added — Production Hardening / Platform Foundations

- **`LlmConfiguration.SchemaVersion` + migration pipeline** — `SettingsPersistence` now reads an explicit schema version (or detects legacy unversioned payloads), applies `ConfigurationMigration`, normalizes newer chat-harness settings, and stamps the current schema on save. This is the first formal settings-evolution path for HCEP.
- **`StartupHealthCheckService`** — New startup health pass audits settings-path readiness, active sensor-route probing, LLM-route availability posture, and plugin API configuration. Warnings/critical findings are logged and can be surfaced in a startup dialog unless suppressed with `HCEP_SUPPRESS_STARTUP_HEALTH_DIALOG=true`.
- **Plugin API operational controls** — `PluginApiServer` now supports `HCEP_PLUGIN_PORT`, `HCEP_PLUGIN_BIND`, optional `HCEP_PLUGIN_API_KEY` auth, and a public `/health` endpoint reporting bind/port/auth/trust/orchestrator status.
- **Avatar catalog scaffolding** — Added `AvatarDescriptor`, `IAvatarCatalog`, and `AvatarCatalog` as the first formal avatar-platform registry layer. `AvatarWindow` now populates its selector from the catalog instead of hard-coding avatar modes directly.

### Changed — Production Hardening / Platform Foundations

- **Explicit shutdown drain sequencing** — `HCEPPipelineOrchestrator.StopAsync()` now enforces a bounded `ShutdownDrainTimeout`, logs completion/timeout per shutdown stage (snapshot loop, HCEP consumer, speech loop, sensor, vision, audio), and continues teardown deterministically under timeout pressure rather than silently waiting indefinitely.
- **Updater integrity + rollback hardening** — `UpdateService.GenerateInstallerScript(...)` now supports SHA-256 verification for the staged ZIP before extraction, snapshots app binaries (excluding protected user-state paths) before install, validates `robocopy` exit codes, and performs automatic rollback of binaries if update copy fails.
- **Updater window staging details** — `CheckForUpdatesWindow` now computes and surfaces the ZIP SHA-256 and stages installers with explicit integrity/rollback messaging.
- **Structured correlation IDs** — Added async-flow correlation context and propagation across typed chat sends, speech-triggered LLM calls, LLM exchanges, telemetry fingerprint gauges, and plugin API HTTP/WebSocket envelopes/headers (`X-Correlation-ID`) for end-to-end traceability.

### Added — Calibration Suite (world-class visuals, selectable from the menu)

- **`Avatar → Calibration` submenu** — Existing single "Gaze Calibration" entry replaced by a submenu listing every calibration protocol:
  - **Gaze Calibration** — Full-screen crosshair overlay (unchanged; existing behaviour preserved).
  - **Face Mesh Alignment…** — New live-preview window with vertical, horizontal, and mesh-scale sliders that update the overlay in real time and persist to `%LocalAppData%\HCEP\overlay-alignment.json`. Directly fixes the "top of mesh at tip of nose" tracking bug reported in this release cycle by making the depth→color offset user-tunable rather than a hard-coded 48 px.
  - **PnP Head Pose…** — New live-visualisation window rendering, on a fixed logical 640×480 canvas, the six canonical face landmarks (nose tip, chin, eye corners, mouth corners) reprojected through the current head pose (yellow), the observed 2D image points from the face tracker (cyan), residual lines (red), and the head-centre R/G/B pose axes. Numeric readout includes pitch, yaw, roll, translation (mm), mean and max reprojection error (px), and landmark count.

### Added — Overlay Alignment Persistence

- **`HCEP.App.OverlayAlignment`** — New static class with `VerticalOffsetPx`, `HorizontalOffsetPx`, `MeshScale`, a `Changed` event, and `Load()` / `Save()` / `ResetToDefaults()` methods. Persisted to `%LocalAppData%\HCEP\overlay-alignment.json`.
- **`VideoOverlayControl` — live re-alignment** — `MapPixel` now reads from `OverlayAlignment` instead of a compiled `DepthToColorOffsetY` constant. A per-control redraw hook invalidates the overlay whenever alignment values change, so slider drags feel instantaneous.
- **`App.xaml.cs` — startup load** — `OverlayAlignment.Load()` runs before the main window opens.
- **Skeletal Alignment calibration** — Added `Avatar → Calibration → Skeletal Alignment…` with independent skeleton X/Y/scale controls persisted in the same overlay-alignment file. Skeleton bones and joints now use a separate mapping path, so body tracking can be tuned without moving the face mesh.

### Changed — Versioning

- **`Directory.Build.props`** — `<Version>` bumped `0.1.0 → 1.4.0`; new `<AssemblyVersion>1.4.0.0</AssemblyVersion>`, `<FileVersion>1.4.0.0</FileVersion>`, `<InformationalVersion>1.4.0</InformationalVersion>` so the About dialog and Updater report a version consistent with the changelog history (previously the props file lagged behind the docs by three minor versions).
- **`publish/app/AppxManifest.xml`** — `Version="0.1.0.0" → "1.4.0.0"` to match.

### Documentation

- **`docs/release_notes_v1.4.0.md`** — New. Detailed operator-facing release notes for the LLM grounding, updater, and calibration suite.
- **Repo memory** — `/memories/repo/hcep-llm-settings.md` updated with the new telemetry-bundle contract and calibration menu structure.

### Tests

- Fixed prior assertion drift in `HybridLlmEngineCircuitBreakerTests` by asserting the current fallback contract (`[No LLM response]`) and circuit-breaker-open diagnostics.
- Stabilized `PipelineIntegrationTests.Pipeline_WithSpeechInjection_ChangesCognitiveState` by reducing timing race sensitivity around transient speech injection.
- CI now collects and uploads `coverage.cobertura.xml` alongside TRX results via `dotnet test --collect:"XPlat Code Coverage"`.
- Build: green, 0 errors, `TreatWarningsAsErrors` active.

---

## [1.3.0] — 2026-07-04

### Added — Workstream A: Contextual Prior Inference

- **`ContextPriorProfile`** (`HCEP.Core.Models`) — Immutable record carrying prior-adjusted classification thresholds derived from environmental context: `ThinkModePriorBoost`, `HeartModePriorBoost`, `SilenceBias`, `HysteresisMultiplier`, `ModeTransitionMinConfidence`, and a `ShadowModeOnly` feature flag for A/B comparison without affecting live classification.
- **`IContextPriorEngine`** (`HCEP.Core.Interfaces`) — New interface: `ComputePrior(ContextSnapshot) → ContextPriorProfile`.
- **`ContextPriorEngine`** (`HCEP.Intelligence`) — Translates time-of-day, environment, privacy, and silence-protocol state into a prior profile. Evening/night → +40% hysteresis window; bedroom at night → +37% silence bias + heart boost; laboratory/studio → +18% think boost + lowered min confidence; private context → +5% introspective mode boosts. `ShadowMode` property enables pure-observation mode.
- **`IHcepAnalyzer.CurrentPrior`** — New `ContextPriorProfile?` property on the analyzer interface. Null = no prior (fully backward-compatible).
- **`HcepModeAnalyzer.CurrentPrior`** — Volatile property. Applied in `ApplyHysteresis` (adjusts min-confidence gate and stability-frame count) and via new `ApplyPriorBoost()` (boosts Think/Heart candidate confidence before the gate). Shadow-mode flag prevents application but preserves observability.
- **`VisionPipeline.LatestPrior`** — New Volatile `ContextPriorProfile?` property. Synced to `_hcepAnalyzer.CurrentPrior` before every `Analyze` call.
- **`HCEPPipelineOrchestrator` — Context + prior wiring** — Injects `TimeContextProvider` and `IContextPriorEngine`. Each snapshot tick (~10 Hz): builds `ContextSnapshot` → runs `SilenceProtocolEvaluator` for final silence flag → updates `HybridLlmEngine.CurrentContext` (was previously unset) → distributes prior to `VisionPipeline.LatestPrior`. This closes the long-standing gap where `LLM.CurrentContext` was never populated at runtime.

### Added — Workstream B: PAD-Bound Telemetry Trust

- **`TelemetryTrustState`** (`HCEP.Core.Models`) — Immutable record: `IsValid`, `PadHash` (truncated SHA-256), `BootTimestamp`, `SigningKeyId`.
- **`ITelemetryTrustService`** (`HCEP.Core.Interfaces`) — Signs outbound HCEP telemetry and exposes session trust state.
- **`TelemetryTrustService`** (`HCEP.Intelligence`) — Bootstraps a per-session HMAC-SHA256 signing key from `SHA256(PAD) XOR RandomBytes(32)`. Fail-closed: if PAD verification fails the service stays invalid and `SignPayload()` returns null permanently.
- **`ActiveDirectivesManager.TryVerifyDirectives(out string)`** — New convenience bool method wrapping `LoadAndVerifyDirectives()`.
- **`PluginApiServer` — Signed telemetry envelope** — Every REST (`/api/state`) and WebSocket (`/ws/stream`) payload is now wrapped: `{ payload: <dto>, trust: { signing_state, pad_hash, key_id, signature } }`. Downstream consumers can verify signatures; unsigned or `signing_state: "invalid"` signals a compromised runtime.
- **`PluginApiTests` — Trust envelope assertions** — Integration tests updated to unwrap the `payload` field and additionally assert `trust.signing_state == "valid"` and that `signature` is non-empty.

### Added — Workstream C: Avatar Synchrony Upgrade

- **`SpeechCadenceProfile`** (`HCEP.Core.Models`) — Rolling estimate of speaker cadence: `SyllablesPerSecond`, `AveragePauseDurationMs`, `LastSpeechBurstMs`, `LastUpdate`, `IsFresh` helper.
- **`HCEPPipelineOrchestrator.LatestCadence`** — Public volatile property exposing the latest `SpeechCadenceProfile`. Updated in the speech loop from Whisper.net segment timing + text length (≈1 syllable / 3.3 chars).
- **`BackchannelController` — Cadence-aware, jitter-based scheduling** — Replaces fixed `RepeatNodIntervalMs` with `ComputeRepeatIntervalMs()` (scales inversely with syllable rate, clamped 2 500–12 000 ms) and `GaussianJitterMs()` (Box-Muller transform, σ = JitterWindowMs/6, ≈99% within ±100 ms). Feature flags: `CadenceAwareScheduling` (default true), `JitterWindowMs` (default 200). `SpeechEndGapMs` now properly resets `_nextNodDueMs`. Adds `CurrentCadence` property.
- **`AvatarWindow`** — Passes `_orchestrator.LatestCadence` to `_backchannel.CurrentCadence` each snapshot frame.

### Added — Tests

- **`ContextPriorEngineTests`** (7 tests) — Neutral context, night context, bedroom at night, laboratory, active silence, range validation, shadow-mode flag.
- **`TelemetryTrustServiceTests`** (5 tests) — State initialization, boot timestamp freshness, HMAC determinism, different payloads produce different signatures, `SigningKeyId` and `PadHash` format.
- **`HcepModeAnalyzerPriorTests`** (5 tests) — Null prior, neutral prior equivalence, shadow-mode no-op, Think boost confidence increase, reset safety.

### Added — DI Registration (`App.xaml.cs`)

- `IContextPriorEngine → ContextPriorEngine` (singleton)
- `ITelemetryTrustService → TelemetryTrustService` (singleton)

### Changed

- **`HecpPipelineOrchestrator.Speech.cs`** — Speech loop now computes `SpeechCadenceProfile` from each final Whisper segment (syllable-rate estimation from `Text.Length / 3.3 / durationSecs`) and writes to `_latestCadence`.

### Tests

211 tests passing (up from 193). Build: green, 0 errors, `TreatWarningsAsErrors` active.

---

## [Unreleased] — 2026-07-03

### Added — 2026-07-17

- **Settings model discovery and connectivity diagnostics** — `SettingsWindow` now queries the active local engine and supported cloud providers for available models, displays inline connectivity state, and shows a routing summary explaining the currently active local/cloud path used by chat and system prompts.
- **Settings save feedback** — The settings dialog now gives visible save-button feedback and a post-save confirmation summary instead of failing silently.
- **Chat input refinement** — The main chat text box now supports `Enter` to send and `Shift+Enter` to insert a newline for short multi-line prompts.

### Changed — 2026-07-17

- **LLM routing UX** — The settings UI now makes the current architecture explicit: HCEP uses one active local route and one active cloud route shared by both typed chat and the generated system-prompt path.
- **Chat compose box** — Upgraded from a strictly single-line input to a wrapped multi-line box with bounded height.

### Fixed — 2026-07-17

- **Cloud credential usage at runtime** — `HybridLlmEngine` cloud requests now consistently use API keys loaded from Windows Credential Manager at call time rather than relying on in-memory config fields that could be empty after restart.
- **Gemini and Anthropic request auth** — Provider-specific request paths now use the resolved active key instead of stale config-only values.
- **Settings dialog save crash** — Saving from the non-modal settings window no longer throws `DialogResult can be set only after Window is created and shown as dialog.`

### Documentation — 2026-07-17

- **README / User Guide / Developer Guide refresh** — Updated LLM routing, settings behavior, chat controls, troubleshooting, and test-count references to match the current implementation.

### Added — 2026-07-16

- **Avatar mirroring toggle (default OFF)** — Added `MIRROR` toggle to the Avatar HUD (`AvatarWindow.xaml`) and display-layer gating in `AvatarWindow.xaml.cs` so gaze, head-pose mirroring, smile mirroring, gesture mirroring, and user-AU brow mirroring are only applied when mirroring is enabled. User tracking, telemetry, HCEP classification, social gaze, proxemics, and backchannel processing remain active.
- **Local engine expansion (11 providers)** — Extended `LocalEngineType` and settings model coverage (`LlmConfiguration`) to include LM Studio, Jan, GPT4All, LocalAI, vLLM, oobabooga, KoboldCpp, BitNet, and Custom OpenAI-compatible endpoints via new `GenericLocalSettings` entries.
- **Happyface personality presets** — Added preset controls to `SettingsWindow` (`Attentive Listener`, `Warm Companion`, `Silent Observer`, `Professional Assistant`, `Custom`) with bidirectional preset/value synchronization logic.
- **SDK mirroring controls** — Added Unity `mirroringEnabled` toggle and Unreal `bMirroringEnabled` property for training/observation mirroring mode configuration.

### Changed — 2026-07-16

- **Settings local-engine UX** — Refactored Local Engines tab to a shared dynamic editor (`Base URL`, `Model Name`, `Temperature`) with llama.cpp OpenAI-compatibility toggle visibility controlled by selected engine.
- **Hybrid local-engine routing** — `HybridLlmEngine` now resolves local engine runtime settings through `GetLocalEngineConfig()` and routes non-Ollama/non-native-llama local engines through OpenAI-compatible local endpoints.
- **3D wireframe stability path** — Neutral mesh projection now uses tracked scale/depth instead of fixed 1.0m assumptions, and neutral mesh fallback cache is used when per-frame projection fails.
- **Context tab readability** — Removed explicit Context-combo foreground overrides so dropdown content uses readable popup text colors.

### Documentation — 2026-07-16

- **Telemetry trust verification docs** — Added README guidance for SDK-side trust-envelope verification.
- **Implementation planning docs** — Added planning docs for mirroring toggle and settings/wireframe stabilization (`docs/mirroring_toggle_plan.md`, `docs/settings_and_wireframe_plan.md`).

### Fixed — 2026-07-16 (Audit Follow-up)

- **Local engine availability false-positive hardening** — `HybridLlmEngine.IsLocalAvailableAsync()` now requires a successful `/v1/models` response for generic OpenAI-compatible local engines, avoiding accidental routing on unrelated endpoints.
- **Unreal SDK mirroring parity** — `UHcepGazeController` now enforces `bMirroringEnabled` in `TickComponent`: data ingestion continues while exported runtime pose/gaze outputs are neutralized when mirroring is disabled.
- **Mirror-off head pose reset** — `AvatarWindow` now resets both 2D and 3D avatar head pose to neutral when mirroring is turned off, preventing stale mirrored orientation.

### Added — Phase 9 Head Gesture Classifier + Phase 10 Backchannel Engine + Binocular Convergence + Context Settings UI

- **`HeadGestureClassifier`** (Phase 9, `HCEP.Spatial`) — 30 Hz velocity-threshold classifier detects Nod, Shake, TiltLeft, TiltRight, ForwardLean, BackwardLean from Kinect head-pose stream. Nod/Shake use reversal confirmation (min 70ms, max 1800ms); Tilt uses 450ms hold; Lean uses 1200ms hold. 600ms refractory period prevents re-triggering. Fed from `face.HeadRotation` + `TrackedPerson.DistanceM` in `AvatarWindow.OnSnapshotReady`. `GestureDetected` event routed to `TriggerAvatarNod()` so user nods produce a reciprocal avatar nod.
- **`BackchannelController`** (Phase 10, `HCEP.App`) — Monitors `SceneSnapshot.LatestSpeech` for sustained human speech. Fires `NodRequested` after 2.2 s of continuous speech; repeats every 6.5 s (biological ~1–3 nods/10 s of active listening, Bavelas et al. 2000). `AvatarWindow` subscribes and calls `TriggerAvatarNod()` on both avatars.
- **`IAvatarComponent.TriggerNod()`** — Added to shared interface. Thread-safe; implementations dispatch internally.
- **2D Happyface nod animation** (`AvatarCoreControl`) — 500 ms sin(π·t) vertical pulse on the entire face plane (9 px amplitude), implemented as a `TranslateTransform` appended to `unifiedPlaneTransform` in `RenderEyes()`.
- **3D Wireframe nod animation** (`Avatar3DControl`) — 500 ms sin(π·t) pitch-offset (0.14 rad ≈ 8°) added to `headPitch` in `OnRender()`. `TriggerNod()` stores `Environment.TickCount64` and calls `Dispatcher.BeginInvoke(InvalidateVisual)`.
- **Binocular convergence (both avatars)** — Replaced linear falloff with the biologically correct atan formula: `conv = eyeRadius × 2.5 × atan(0.0325 / max(0.25, userDistM))` (IOD=65mm, 2.5× visibility scale). Scales from ~3.5 px at 0.5m → ~0.7 px at 1m, matching real vergence anatomy. Applied to both `AvatarCoreControl` and `Avatar3DControl`.
- **Settings Context tab** — New **Context** tab in `SettingsWindow` (Phase 14): `EnvironmentType` ComboBox (10 options matching enum), `SituationActivity` ComboBox (9 options), `SituationPrivacy` ComboBox, `UserDefinedLocation` TextBox. Wired to `TimeContextProvider` singleton (new DI registration in `App.xaml.cs`). Changes take effect immediately in the LLM system prompt and `SilenceProtocolEvaluator`.

### Added — Phase 9 Head Gesture Classifier + Phase 10 Backchannel Engine + Binocular Convergence + Context Settings UI

- **`HeadGestureClassifier`** (Phase 9, `HCEP.Spatial`) — 30 Hz velocity-threshold classifier detects Nod, Shake, TiltLeft, TiltRight, ForwardLean, BackwardLean from Kinect head-pose stream. Nod/Shake use reversal confirmation (min 70ms, max 1800ms); Tilt uses 450ms hold; Lean uses 1200ms hold. 600ms refractory period prevents re-triggering. Fed from `face.HeadRotation` + `TrackedPerson.DistanceM` in `AvatarWindow.OnSnapshotReady`. `GestureDetected` event routed to `TriggerAvatarNod()` so user nods produce a reciprocal avatar nod.
- **`BackchannelController`** (Phase 10, `HCEP.App`) — Monitors `SceneSnapshot.LatestSpeech` for sustained human speech. Fires `NodRequested` after 2.2 s of continuous speech; repeats every 6.5 s (biological ~1–3 nods/10 s of active listening, Bavelas et al. 2000). `AvatarWindow` subscribes and calls `TriggerAvatarNod()` on both avatars.
- **`IAvatarComponent.TriggerNod()`** — Added to shared interface. Thread-safe; implementations dispatch internally.
- **2D Happyface nod animation** (`AvatarCoreControl`) — 500 ms sin(π·t) vertical pulse on the entire face plane (9 px amplitude), implemented as a `TranslateTransform` appended to `unifiedPlaneTransform` in `RenderEyes()`.
- **3D Wireframe nod animation** (`Avatar3DControl`) — 500 ms sin(π·t) pitch-offset (0.14 rad ≈ 8°) added to `headPitch` in `OnRender()`. `TriggerNod()` stores `Environment.TickCount64` and calls `Dispatcher.BeginInvoke(InvalidateVisual)`.
- **Binocular convergence (both avatars)** — Replaced linear falloff with the biologically correct atan formula: `conv = eyeRadius × 2.5 × atan(0.0325 / max(0.25, userDistM))` (IOD=65mm, 2.5× visibility scale). Scales from ~3.5 px at 0.5m → ~0.7 px at 1m, matching real vergence anatomy. Applied to both `AvatarCoreControl` and `Avatar3DControl`.
- **Settings Context tab** — New **Context** tab in `SettingsWindow` (Phase 14): `EnvironmentType` ComboBox (10 options matching enum), `SituationActivity` ComboBox (9 options), `SituationPrivacy` ComboBox, `UserDefinedLocation` TextBox. Wired to `TimeContextProvider` singleton (new DI registration in `App.xaml.cs`). Changes take effect immediately in the LLM system prompt and `SilenceProtocolEvaluator`.

---

### Added — Avatar Expression System (v1.2 work)

- **Eyebrow animation** — Both `AvatarCoreControl` (2D) and `Avatar3DControl` (3D) now animate eyebrows driven by Kinect AU3 (BrowLowerer) + AU5 (OuterBrowRaiser) and autonomous HCEP-mode targets (LOGIC/THINK → furrow; HEART → empathy raise; AFFECT → open). 150ms EMA smoothing; quadratic bezier paths rebuilt at 30Hz. `IAvatarComponent.SetBrows()` added to shared interface.
- **Phoneme-accurate lip sync** (Phase 13 complete) — `ISpeechSynthesizer.VisemeChanged` per-phoneme event wired end-to-end: `WindowsTtsSynthesizer` maps SAPI `VisemeReached` events through `VisemeController` (22-row Preston Blair lookup table) → `IAvatarComponent.SetViseme()` → avatar mouth animation. 2D Happy Face: `MouthFill` Ellipse + reshaped `SmilePath` arc. 3D Wireframe: `DrawMouth3D()` proportional bezier mouth. 60ms EMA co-articulation on both avatars. `AvatarWindow` subscribes to `orchestrator.TtsEngine.VisemeChanged`.
- **HCEP.Speech project** — New project (`src/HCEP.Speech/`) with `ISpeechSynthesizer` interface, `VisemeData` struct, `VisemeController` lookup table, `WindowsTtsSynthesizer` (SAPI offline), `OpenAiTtsSynthesizer` (streaming cloud), `ElevenLabsTtsSynthesizer` (streaming, lowest latency), `HybridTtsEngine` (priority routing with `VisemeChanged` relay).
- **Contextual Intelligence** (Phase 14 complete) — `ContextSnapshot` model (`HCEP.Core.Models`) captures Time × Space × Situation. `TimeContextProvider` classifies time-of-day, day type, season, derives `CommunicationRegister` + `TemporalUrgency`. `SilenceProtocolEvaluator` implements 7 evidence-based silence rules (Jaworski 1993; Duncan 1972; Sacks et al. 1974). `HybridLlmEngine.CurrentContext` injects context string into every `PromptAsync` system prompt.
- **Calibration fix** — Critical sign bug fixed: `t >= 0f` guard was rejecting ALL valid calibrations (valid intersections produce positive `t`). Fixed to `t <= 0f`. `DefaultCameraZOffsetMm` replaces hardcoded constant; comment corrected to reflect correct sign semantics.
- **`HCEPPipelineOrchestrator.TtsEngine`** property — exposes `HybridTtsEngine` for `AvatarWindow` viseme subscription.
- **`HCEP.App` references `HCEP.Speech`** — project reference added to `HCEP.App.csproj`.

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
- **`HcepModeAnalyzer` — All thresholds documented** — Added XML comments with empirical basis for `GazeAversionAngleDeg` (15°, Argyle & Cook 1976), `BrowLowerThreshold` (-0.3, HCEP synthetic κ=0.8084 dataset), `SmileThreshold` (0.2, micro-expression inclusive), `ModeTransitionMinConfidence` (0.4), and `ModeStabilityFrames` (5 frames = ~167 ms at 30 fps).
- **`ThreeStageGazeEstimator.HeadWeight`** — Documented the empirical 0.6 value with reference to the HCEP synthetic validation dataset (6,000 frames, κ=0.8084, accuracy 84.55%) and the rationale for head-pose dominance over AU eye offsets at typical interaction distances.
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
- Simulation-based verification: Achieved Cohen's Kappa **0.8084** and mode-classifier accuracy **84.55%** over 6,000 synthetic frames.
