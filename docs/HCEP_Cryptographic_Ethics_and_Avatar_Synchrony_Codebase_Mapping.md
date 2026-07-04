# HCEP Cryptographic Ethics and Avatar Synchrony Codebase Mapping

**Document Type:** Codebase Surface Map  
**Purpose:** Map each approved workstream onto the current HCEP implementation  
**Status:** Draft for Approval  
**Last Updated:** July 4, 2026

---

## 1. Mapping Summary

This document maps the three implementation workstreams to the most likely classes, files, and integration seams in the current HCEP repository. The intent is to reduce discovery time before engineering starts and to distinguish existing capability from planned enhancement.

---

## 2. Workstream A: Contextual Prior Inference

### 2.1 Existing Capability

The repository already contains:

1. A context model in `src/HCEP.Core/Models/ContextSnapshot.cs`
2. A context builder in `src/HCEP.Intelligence/TimeContextProvider.cs`
3. Silence protocol logic in `src/HCEP.Intelligence/SilenceProtocolEvaluator.cs`
4. Five-mode arbitration and hysteresis in `src/HCEP.Vision/HcepModeAnalyzer.cs`
5. Context prompt injection in `src/HCEP.Intelligence/HybridLlmEngine.cs`

### 2.2 Primary Change Surfaces

| File | Current Role | Likely Change |
|---|---|---|
| `src/HCEP.Core/Models/ContextSnapshot.cs` | Canonical time/space/situation model | Add prior-related derived fields or companion model references |
| `src/HCEP.Intelligence/TimeContextProvider.cs` | Builds context snapshots from environment/time | Add richer prior inputs or normalization helpers |
| `src/HCEP.Intelligence/SilenceProtocolEvaluator.cs` | Hard and contextual silence rules | Convert hard rules into thresholdable/context-weighted logic where appropriate |
| `src/HCEP.Vision/HcepModeAnalyzer.cs` | Final mode classification and hysteresis | Insert prior-adjustment seam before final mode commitment |
| `src/HCEP.Intelligence/HybridLlmEngine.cs` | Uses current context in prompt assembly | Optionally expose richer context-prior state in prompts or logs |

### 2.3 New Types Likely Needed

1. `ContextPriorProfile`
2. `IContextPriorEngine`
3. `ContextPriorEngine`
4. Optional feature flag/config model for prior weighting

### 2.4 Integration Path

1. `TimeContextProvider` builds base context.
2. New prior engine derives a prior profile from that context.
3. `HcepModeAnalyzer` applies the prior profile before final mode arbitration.
4. `SilenceProtocolEvaluator` consumes prior-adjusted sensitivity or thresholds.
5. `HybridLlmEngine` can emit context-prior state for observability if approved.

### 2.5 Key Unknowns

1. Where the current `TimeContextProvider` instance is owned and refreshed at runtime.
2. Whether `HcepModeAnalyzer` can accept context directly today or needs an interface change.

---

## 3. Workstream B: PAD-Bound Telemetry Trust

### 3.1 Existing Capability

The repository already contains:

1. PAD integrity verification in `src/HCEP.Intelligence/ActiveDirectivesManager.cs`
2. PAD injection into prompt construction in `src/HCEP.Intelligence/HybridLlmEngine.cs`
3. Outbound REST/WebSocket/gRPC API surfaces in `src/HCEP.Plugin.Api/PluginApiServer.cs`
4. General in-memory metrics/logging in `src/HCEP.Telemetry/HecpTelemetryService.cs`
5. Snapshot event flow from `src/HCEP.App/HecpPipelineOrchestrator.cs` and `src/HCEP.App/HecpPipelineOrchestrator.Snapshot.cs`

### 3.2 Primary Change Surfaces

| File | Current Role | Likely Change |
|---|---|---|
| `src/HCEP.Intelligence/ActiveDirectivesManager.cs` | PAD file/embedded verification | Expose machine-readable trust state, not only directive text |
| `src/HCEP.Intelligence/HybridLlmEngine.cs` | Consumes verified directives in prompts | Possible consumer of trust state, possible fail-closed behavior updates |
| `src/HCEP.Plugin.Api/PluginApiServer.cs` | Emits state via REST/WebSocket/gRPC | Add signed trust envelope to one or more payload paths |
| `src/HCEP.App/HecpPipelineOrchestrator.Snapshot.cs` | Produces `SceneSnapshot` for downstream consumers | Potential place to attach trust metadata before transport serialization |
| `src/HCEP.Telemetry/HecpTelemetryService.cs` | Diagnostics and metrics | Add trust/bootstrap/verification counters and gauges |

### 3.3 New Types Likely Needed

1. `TelemetryTrustState`
2. `TelemetryTrustEnvelope<T>`
3. `TelemetryTrustService`
4. Signature verification helper for SDK/consumer use

### 3.4 Integration Path

1. `ActiveDirectivesManager` verifies PAD and emits trust state.
2. `TelemetryTrustService` boots a signed session only if trust state is valid.
3. `PluginApiServer` wraps DTO payloads in a signed envelope.
4. Consumers verify signature/trust metadata before acting.
5. `HecpTelemetryService` records trust-related metrics.

### 3.5 Best First Milestone

The lowest-risk first milestone is the WebSocket path in `PluginApiServer`, because it already serializes a single DTO per snapshot and is the clearest proof point for end-to-end trust wrapping.

---

## 4. Workstream C: Avatar Synchrony Upgrade

### 4.1 Existing Capability

The repository already contains:

1. A fixed-interval nod scheduler in `src/HCEP.App/BackchannelController.cs`
2. Snapshot-driven avatar behavior wiring in `src/HCEP.App/AvatarWindow.xaml.cs`
3. Expression mirroring in `src/HCEP.App/ExpressionMirror.cs`
4. Social gaze timing/orientation behavior in `src/HCEP.App/SocialGazeController.cs`
5. Live speech ingestion in `src/HCEP.App/HecpPipelineOrchestrator.Speech.cs`
6. Audio pipeline infrastructure in `src/HCEP.Audio/AudioPipeline.cs`

### 4.2 Primary Change Surfaces

| File | Current Role | Likely Change |
|---|---|---|
| `src/HCEP.App/BackchannelController.cs` | Fixed nod timing based on speech duration | Replace constant delay logic with cadence-aware scheduling |
| `src/HCEP.App/AvatarWindow.xaml.cs` | Consumes `BackchannelController` events | Possibly pass richer rhythm state or tune action routing |
| `src/HCEP.App/ExpressionMirror.cs` | Smile reciprocation from snapshots | Optional rhythm coupling for expression timing |
| `src/HCEP.App/SocialGazeController.cs` | Social gaze behavior | Optional rhythm or pause-sensitive adjustments |
| `src/HCEP.App/HecpPipelineOrchestrator.Speech.cs` | Receives `SpeechResult` and triggers LLM flow | Potential place to compute or cache cadence features |
| `src/HCEP.Audio/AudioPipeline.cs` | Audio frame processing and speech result emission | Best seam for low-level cadence or pause metrics |

### 4.3 New Types Likely Needed

1. `SpeechCadenceProfile`
2. `BackchannelTimingPolicy`
3. `BackchannelJitterSettings`
4. Optional timing strategy abstraction if deterministic fallback is preserved cleanly

### 4.4 Integration Path

1. Audio path computes cadence/pause features.
2. Orchestrator or speech loop caches rhythm state.
3. `BackchannelController` consumes rhythm state to compute nod timing dynamically.
4. `AvatarWindow` continues dispatching resulting actions onto the UI thread.
5. `ExpressionMirror` and `SocialGazeController` may optionally consume the same timing context.

### 4.5 Best First Milestone

Refactor `BackchannelController` first, because it is the most isolated current timing controller and already owns the strongest fixed-delay behavior identified by the transcript.

---

## 5. Cross-Cutting Validation Surfaces

| Area | Likely Files |
|---|---|
| Telemetry and diagnostics | `src/HCEP.Telemetry/HecpTelemetryService.cs` |
| Snapshot distribution | `src/HCEP.App/HecpPipelineOrchestrator.Snapshot.cs`, `src/HCEP.App/HecpPipelineOrchestrator.Events.cs` |
| LLM prompt behavior | `src/HCEP.Intelligence/HybridLlmEngine.cs`, `src/HCEP.Intelligence/HcepPromptBridge.cs` |
| External transport | `src/HCEP.Plugin.Api/PluginApiServer.cs` |
| Tests | `tests/HCEP.Tests/` |

---

## 6. Recommended Engineering Entry Points

If implementation starts immediately, the recommended sequence is:

1. `src/HCEP.Vision/HcepModeAnalyzer.cs`
2. `src/HCEP.Intelligence/SilenceProtocolEvaluator.cs`
3. `src/HCEP.Intelligence/ActiveDirectivesManager.cs`
4. `src/HCEP.Plugin.Api/PluginApiServer.cs`
5. `src/HCEP.App/BackchannelController.cs`

This order reflects the smallest set of high-leverage files that directly control the criticized behavior.

---

## 7. Approval Notes

This map is intentionally conservative. It identifies the most likely owning files and classes based on the current repository, but it should be validated during kickoff before any large refactor crosses project boundaries.

- [ ] Code-surface mapping approved
- [ ] Entry points approved
- [ ] Ready for implementation discovery
