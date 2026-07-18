# HCEP v1.4.0 — Release Notes

**Release date:** 2026-07-17
**Assembly version:** 1.4.0.0
**Package version:** 1.4.0.0

---

## Highlights

HCEP v1.4.0 turns the HCEP sensor stack into a first-class perceptual channel for the connected LLM, ships an in-app updater with a hard non-destructive guarantee, and expands the calibration workflow from a single gaze protocol into a selectable suite with world-class visualisations.

- **The LLM now "sees" through telemetry.** Every chat turn attaches a full sensory bundle to the system prompt, and a new grounding policy forbids hallucination.
- **In-app updater** with a menu item under `Help`, a header button next to `Sensor Streams`, and a hard preservation contract on your settings, credentials, and logs.
- **Calibration Suite** — the `Avatar → Calibration` submenu now lists every calibration protocol: Gaze, Face Mesh Alignment (fixes the "mesh top at nose tip" tracking bug), Skeletal Alignment (independent body overlay tuning), and PnP Head Pose (world-class reprojection view).

---

## 1. LLM Telemetry Grounding

### The problem it fixes

The configured LLM previously answered questions like *"Can you see me?"* with either:

- *"I have no way of seeing"* — false. HCEP was already tracking a face.
- *"I have access to the telemetry but don't know what to do with it"* — because the system prompt exposed only a five-field `HcepReading` and told the model nothing about how to use it.

### What changed

1. **New `HcepTelemetryBundle`** carries the complete live sensor picture:
   - Pipeline running / FPS / sensor connected / calibration applied
   - Number of tracked persons
   - Primary person's identity (if enrolled), distance, HCEP mode, gaze region, cognitive state, valence, confidence
   - Head pose (pitch/yaw/roll in °), left/right eye 3D positions (m), inter-ocular distance (mm)
   - Time × Space × Situation context
   - Speech cadence + most recent finalized transcript
2. **System prompt rewrite.** A new *Perception Model* block explicitly tells the LLM that the telemetry section IS its sensory feed, and a five-clause *Grounding & Non-Hallucination Policy* forbids invention of any signal.
3. **Exact fallback phrase.** When a question cannot be answered from telemetry, knowledge store, or the current turn, the LLM must respond with the exact sentence:

    > *"I don't have that information right now."*

    followed (optionally) by a concrete suggestion for how the user can make the information available.

### For LLM operators

Nothing in your local/cloud provider selection changes. The grounding policy is injected before the Permanent Active Directives, so it applies to every route (Ollama, LlamaCpp, LM Studio, Jan, GPT4All, LocalAI, vLLM, Oobabooga, KoboldCpp, BitNet, OpenAI, Anthropic, Gemini, Mistral, xAI, Cohere, OpenRouter, DeepSeek, Groq, TogetherAI, FireworksAI, Perplexity, AI21Labs, Replicate, HuggingFace, Azure OpenAI, Amazon Bedrock, NVIDIA NIM, Cerebras, MoonshotAI, Custom).

---

## 2. In-App Updater (Non-Destructive)

### Where to find it

- **Header:** the "⬆ Check for Updates" button appears immediately to the right of `◎ Sensor Streams` in the top bar.
- **Menu:** `Help → Check for Updates…`.

### What it does

1. Calls `https://api.github.com/repos/kirklasalle/HCEP/releases/latest`.
2. Compares the tag against the currently running `AssemblyInformationalVersion` (from `Directory.Build.props`).
3. Displays release notes and the download size.
4. On **Download & Stage**, saves the release ZIP into `%LocalAppData%\HCEP\Updates\<tag>\` and writes a PowerShell installer script alongside it.

### The non-destructive contract

The generated installer:

- Waits for `HCEP.App.exe` to exit before touching any files.
- Snapshots `%LocalAppData%\HCEP\` and `<app>\config\` into `<staging>\backup\` first.
- Verifies the staged ZIP SHA-256 before extraction.
- Snapshots the current app binary tree, validates `robocopy` exit codes, and attempts binary rollback if the update copy fails.
- Applies the update using **`robocopy`** with `/XD config logs Logs .venv` and `/XF hcep-settings.json overlay-alignment.json` — the excluded paths are physically skipped.
- **Never** touches Windows Credential Manager entries under the `HCEP/*` target family. API keys are structurally out of the update path.

If any step fails, the backup remains in place and the previous binary tree is untouched.

### If GitHub is unreachable

The window still opens, reports the network error, and offers the "Open Release Page" fallback. Downloads are optional and gated behind an explicit button click.

---

## 3. Calibration Suite

The `Avatar` menu now contains a **Calibration** submenu with every protocol listed for direct selection:

| Menu path | Purpose | Visualisation |
|---|---|---|
| `Avatar → Calibration → Gaze Calibration` | Full-screen crosshair capture — computes the Kinect-to-screen-centre offset (unchanged). | Full-screen crosshair + live face-track readout + preview offset. |
| `Avatar → Calibration → Face Mesh Alignment…` | Fix the depth-to-color pixel offset so the face mesh sits on your actual face. | Live sliders (vertical, horizontal, mesh scale) with the main window's face overlay updating in real time. Save persists to `overlay-alignment.json`. |
| `Avatar → Calibration → Skeletal Alignment…` | Adjust only the green skeleton bones and joint dots. | Live sliders (vertical, horizontal, skeleton scale) using an independent skeleton mapping path, so body overlay tuning does not move the face mesh. |
| `Avatar → Calibration → PnP Head Pose…` | Visualise the Perspective-n-Point head-pose solve. | 640×480 canvas — yellow reprojected model landmarks, cyan observed image points, red residual lines, R/G/B pose axes at head centre, plus pitch/yaw/roll/translation/mean+max reprojection-error readout. |

### The face-mesh tracking bug this fixes

Before v1.4.0, `VideoOverlayControl.MapPixel` applied a hard-coded `DepthToColorOffsetY = 48` px. On some Kinect v1 units and mounting positions this over-shifts the mesh downward — the reported symptom was *"top of the mesh head is right at the tip of my actual nose"*. That constant is now:

- Sourced from `OverlayAlignment.VerticalOffsetPx` (default 48 px, backward-compatible).
- User-tunable in real time via the Face Mesh Alignment slider.
- Persisted between runs.
- Broadcast to every overlay renderer via `OverlayAlignment.Changed` so the video overlay redraws on the next dispatcher pass.

Skeleton alignment is intentionally separate. `VideoOverlayControl` maps bones and joints through `MapSkeletonPixel()` using `OverlayAlignment.SkeletonHorizontalOffsetPx`, `SkeletonVerticalOffsetPx`, and `SkeletonScale`, while face mesh, face rectangle, eye markers, and labels keep the face/mesh mapping.

---

## 4. Operability and traceability

- `LlmConfiguration.SchemaVersion` and `ConfigurationMigration` provide a non-destructive settings evolution path.
- `StartupHealthCheckService` audits settings path, sensor route, LLM route, updater posture, and plugin API configuration at startup.
- Plugin API `/health` reports bind/port/auth/trust/orchestrator state.
- REST and WebSocket plugin API responses include `X-Correlation-ID` / `correlation_id` so telemetry, chat, and external tool calls can be traced together.
- CI collects TRX test output and XPlat Code Coverage artifacts.

---

## 5. Version alignment

| Location | Before | After |
|---|---|---|
| `Directory.Build.props` `<Version>` | `0.1.0` | `1.4.0` |
| `Directory.Build.props` `<AssemblyVersion>` | *(implicit)* | `1.4.0.0` |
| `Directory.Build.props` `<FileVersion>` | *(implicit)* | `1.4.0.0` |
| `Directory.Build.props` `<InformationalVersion>` | *(implicit)* | `1.4.0` |
| `publish/app/AppxManifest.xml` `Version` | `0.1.0.0` | `1.4.0.0` |

The About dialog, the Updater, and any downstream telemetry that reads assembly version metadata will now report `1.4.0` consistently.

---

## 6. Known issues

- The installer script is generated but not auto-executed; the user must run `install-update.ps1` after HCEP exits. This is intentional — auto-elevation would violate the "no destructive mutation to user data" constraint if a bad update slipped through.

---

## 7. Upgrade path

- **From v1.3.0:** run the updater, click *Download & Stage*, close HCEP, run the generated `install-update.ps1`. Settings and credentials are preserved automatically.
- **From v0.1.0 dev branches:** rebuild from source (`dotnet build ./HCEP.sln`); the new version metadata will flow in automatically.

---

**© 2026 Kirk LaSalle. All rights reserved.**
