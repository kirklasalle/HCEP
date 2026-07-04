# HCEP Release Notes — v1.3.0

**Released:** July 4, 2026  
**Build:** 211/211 tests passing · 0 errors · 0 warnings

---

## What's New in v1.3.0

This release ships the three workstreams described in the **HCEP Cryptographic Ethics and Avatar Synchrony** implementation plan, translating the audio-critique findings into production code.

---

### Workstream A — Contextual Prior Inference

**Problem:** `ContextSnapshot` data was used only as a post-classification binary override and was never populating `HybridLlmEngine.CurrentContext` at runtime, meaning every LLM prompt had no contextual header.

**What changed:**

- New `ContextPriorProfile` record carries prior-adjusted thresholds for mode classification.
- New `ContextPriorEngine` translates the current context (time-of-day, environment, privacy, silence state) into a prior profile that adjusts the classifier *before* final mode arbitration — not after.
- `HcepModeAnalyzer` now applies context-driven confidence boosts to Think and Heart candidates, and uses a context-aware hysteresis window. A `ShadowModeOnly` flag allows the prior to be observed without being applied, enabling safe A/B comparison.
- `HybridLlmEngine.CurrentContext` is now properly populated every 100 ms from the snapshot loop — closing a runtime gap where all LLM prompts lacked contextual headers.

**Effect in practice:** At 22:00 in a bedroom, the avatar becomes quieter, the hysteresis window stretches (~233 ms), and Think/Heart modes become easier to confirm without requiring extreme facial evidence. In a laboratory at any hour, the classifier lowers its evidence threshold for reflective states.

---

### Workstream B — PAD-Bound Telemetry Trust

**Problem:** PAD integrity was verified locally at boot, but downstream plugin consumers (WebSocket clients, LangChain agents, Unreal Engine plugins) had no way to verify that the telemetry stream they received was produced under a valid ethical state.

**What changed:**

- New `TelemetryTrustService` bootstraps a per-session HMAC-SHA256 signing key from `SHA256(PAD) XOR RandomBytes(32)`. If PAD verification fails at startup the service enters a permanently invalid state and refuses to sign.
- Every outbound payload from the REST `/api/state` endpoint and the WebSocket `/ws/stream` is now wrapped in a signed trust envelope: `{ "payload": { ...dto... }, "trust": { "signing_state": "valid", "pad_hash": "...", "key_id": "...", "signature": "..." } }`.
- If the PAD is tampered with, `signing_state` becomes `"invalid"` and `signature` is null — any downstream agent can detect and degrade gracefully.

**Effect in practice:** An Unreal Engine plugin or LangChain agent consuming the WebSocket stream can now cryptographically verify that it is talking to an ethically-intact HCEP core and reject or downgrade behavior if it is not.

---

### Workstream C — Avatar Synchrony Upgrade

**Problem:** `BackchannelController` used a fixed 6.5-second nod repeat interval, which produces a metronomic rhythm that contradicts HCEP's biological synchrony goals (Condon & Ogston 1967).

**What changed:**

- New `SpeechCadenceProfile` model carries a rolling estimate of the speaker's syllable rate, derived from Whisper.net segment timing and transcript length.
- `HCEPPipelineOrchestrator` now computes cadence from each final speech result and exposes it via `LatestCadence`.
- `BackchannelController` now scales nod repeat intervals inversely with syllable rate (faster speech → tighter nods; slower speech → wider intervals; range clamped 2.5–12 s). A Gaussian jitter (Box-Muller, ±100 ms by default) is applied so no two repeat nods fire at identical intervals.
- `AvatarWindow` passes `LatestCadence` to the backchannel controller each frame.

**Effect in practice:** If a user speaks rapidly, the avatar nods more frequently to match their cadence. If they slow down late at night, nods breathe and stretch. No two nod intervals are identical, eliminating the metronomic artifact identified in the critique.

---

## Breaking Changes

- **`PluginApiServer` output format changed.** REST and WebSocket responses are now wrapped in a trust envelope. Consumers expecting the flat DTO must unwrap `response.payload`.
- **`PluginApiServer` constructor changed.** Now requires `ITelemetryTrustService` as a second parameter. Existing tests have been updated with a `StubTrustService`.

---

## Migration Guide for Plugin Consumers

### Before (v1.2.x)

```json
{
  "timestamp": "...",
  "frameNumber": 42,
  "personDetected": true,
  "primaryPerson": { ... }
}
```

### After (v1.3.0)

```json
{
  "payload": {
    "timestamp": "...",
    "frameNumber": 42,
    "personDetected": true,
    "primaryPerson": { ... }
  },
  "trust": {
    "signing_state": "valid",
    "pad_hash": "0C1520193240BC7A...",
    "key_id": "DEADBEEF",
    "signature": "base64-hmac-sha256..."
  }
}
```

Access `response.payload` for the original DTO fields.

---

## Test Suite

| Category | Tests |
|---|---|
| Vision (mode analysis) | 31 |
| Intelligence (LLM, prior, trust) | 26 |
| Integration (plugin API) | 4 |
| Spatial (gaze, calibration) | 38 |
| Knowledge (store, capacity) | 22 |
| Telemetry | 8 |
| Audio | 12 |
| Core | 18 |
| Concurrency | 12 |
| Other | 40 |
| **Total** | **211** |

All 211 tests pass. Build: 0 errors, 0 warnings, `TreatWarningsAsErrors` active.

---

*Previous release notes: see [release_notes_v1.0.0.md](release_notes_v1.0.0.md)*
