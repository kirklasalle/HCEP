# HCEP Cryptographic Ethics and Avatar Synchrony Engineering Backlog

**Document Type:** Engineering Backlog  
**Source Documents:** Implementation Plan + PRD  
**Status:** Draft for Approval  
**Last Updated:** July 4, 2026

---

## 1. Backlog Structure

This backlog is grouped into epics aligned to the three approved workstreams plus one cross-cutting validation epic. Each item includes a proposed owner role, rough estimate, dependencies, and primary code targets.

---

## 2. Epic A: Contextual Prior Inference

| ID | Task | Owner | Estimate | Depends On | Primary Targets |
|---|---|---|---|---|---|
| A1 | Audit existing context-to-mode flow and document current override behavior | Core Intelligence Engineer | 0.5d | None | `src/HCEP.Core/Models/ContextSnapshot.cs`, `src/HCEP.Intelligence/TimeContextProvider.cs`, `src/HCEP.Intelligence/SilenceProtocolEvaluator.cs`, `src/HCEP.Vision/HcepModeAnalyzer.cs` |
| A2 | Define `ContextPriorProfile` schema and prior scoring rules | Core Intelligence Engineer | 1d | A1 | `src/HCEP.Core/Models/` or `src/HCEP.Intelligence/` |
| A3 | Add `IContextPriorEngine` service and implementation | Core Intelligence Engineer | 1d | A2 | `src/HCEP.Intelligence/` |
| A4 | Insert prior-adjustment seam before final mode arbitration | Core Intelligence Engineer | 1.5d | A3 | `src/HCEP.Vision/HcepModeAnalyzer.cs` |
| A5 | Make silence and hysteresis thresholds context-adjustable | Core Intelligence Engineer | 1d | A4 | `src/HCEP.Intelligence/SilenceProtocolEvaluator.cs`, `src/HCEP.Vision/HcepModeAnalyzer.cs` |
| A6 | Add feature flag and fallback-to-baseline path | Core Platform Engineer | 0.5d | A4 | `src/HCEP.App/`, `src/HCEP.Intelligence/`, config surfaces |
| A7 | Emit comparison telemetry for baseline vs adjusted classification | Telemetry Engineer | 0.5d | A4 | `src/HCEP.Telemetry/HecpTelemetryService.cs`, orchestrator/classifier call sites |
| A8 | Add unit tests for prior application and rollback behavior | Test Engineer | 1d | A4, A6 | `tests/HCEP.Tests/` |

### Epic A Exit Criteria

1. Prior engine runs in shadow mode.
2. Comparison telemetry is visible.
3. Deterministic fallback remains available.

---

## 3. Epic B: PAD-Bound Telemetry Trust

| ID | Task | Owner | Estimate | Depends On | Primary Targets |
|---|---|---|---|---|---|
| B1 | Document current PAD verification lifecycle and identify authoritative trust state | Security / Platform Engineer | 0.5d | None | `src/HCEP.Intelligence/ActiveDirectivesManager.cs`, `src/HCEP.Intelligence/HybridLlmEngine.cs` |
| B2 | Define signed telemetry envelope and trust metadata schema | Security / Platform Engineer | 1d | B1 | `src/HCEP.Core/Models/`, `src/HCEP.Plugin.Api/` |
| B3 | Introduce `TelemetryTrustService` bootstrap/signing abstraction | Security / Platform Engineer | 1.5d | B2 | `src/HCEP.Intelligence/` or `src/HCEP.Telemetry/` |
| B4 | Expose trust state programmatically from PAD verification path | Security / Platform Engineer | 0.5d | B3 | `src/HCEP.Intelligence/ActiveDirectivesManager.cs` |
| B5 | Sign one outbound transport path, starting with WebSocket stream DTOs | Plugin/API Engineer | 1d | B3, B4 | `src/HCEP.Plugin.Api/PluginApiServer.cs` |
| B6 | Add verification helper for one consumer surface | Plugin/API Engineer | 1d | B5 | `sdk/` or `src/HCEP.Plugin.Api/` helper surface |
| B7 | Add safe-mode behavior for unsigned/invalid trust state | Security / Platform Engineer | 1d | B5 | `src/HCEP.Plugin.Api/`, `src/HCEP.Intelligence/` |
| B8 | Add trust-failure logging and metrics | Telemetry Engineer | 0.5d | B5, B7 | `src/HCEP.Telemetry/HecpTelemetryService.cs`, API call sites |
| B9 | Add integration tests for signed payload emission and verification failure | Test Engineer | 1.5d | B5, B6, B7 | `tests/HCEP.Tests/` |

### Epic B Exit Criteria

1. One external channel emits signed trust metadata.
2. PAD-invalid runtime cannot emit trusted payloads.
3. Verification failures are observable.

---

## 4. Epic C: Avatar Synchrony Upgrade

| ID | Task | Owner | Estimate | Depends On | Primary Targets |
|---|---|---|---|---|---|
| C1 | Audit current nod, smile, and social gaze timing behavior | Avatar Interaction Engineer | 0.5d | None | `src/HCEP.App/BackchannelController.cs`, `src/HCEP.App/ExpressionMirror.cs`, `src/HCEP.App/SocialGazeController.cs`, `src/HCEP.App/AvatarWindow.xaml.cs` |
| C2 | Define cadence feature payload from audio/speech pipeline | Audio Engineer | 1d | None | `src/HCEP.Audio/AudioPipeline.cs`, `src/HCEP.Core/Models/SpeechResult.cs`, orchestrator speech flow |
| C3 | Extend audio or speech loop with cadence/pause estimates | Audio Engineer | 1.5d | C2 | `src/HCEP.Audio/`, `src/HCEP.App/HecpPipelineOrchestrator.Speech.cs` |
| C4 | Refactor `BackchannelController` to support dynamic delay functions | Avatar Interaction Engineer | 1d | C3 | `src/HCEP.App/BackchannelController.cs` |
| C5 | Add bounded jitter policy and configuration knobs | Avatar Interaction Engineer | 0.5d | C4 | `src/HCEP.App/BackchannelController.cs` or config surface |
| C6 | Use cadence inputs to tune nod scheduling behavior | Avatar Interaction Engineer | 1d | C4, C5 | `src/HCEP.App/BackchannelController.cs`, `src/HCEP.App/AvatarWindow.xaml.cs` |
| C7 | Evaluate whether `ExpressionMirror` and `SocialGazeController` should consume the same rhythm state | Avatar Interaction Engineer | 0.5d | C3 | `src/HCEP.App/ExpressionMirror.cs`, `src/HCEP.App/SocialGazeController.cs` |
| C8 | Add tuning telemetry for actual backchannel intervals and suppression behavior | Telemetry Engineer | 0.5d | C6 | `src/HCEP.Telemetry/HecpTelemetryService.cs`, avatar call sites |
| C9 | Add behavior tests or deterministic timing harness where feasible | Test Engineer | 1d | C4, C5, C6 | `tests/HCEP.Tests/` |

### Epic C Exit Criteria

1. Fixed repeated nod timing is removed as the only timing strategy.
2. Cadence-aware behavior is measurable.
3. Fallback to stable timing remains available.

---

## 5. Epic D: Cross-Cutting Validation and Release

| ID | Task | Owner | Estimate | Depends On | Primary Targets |
|---|---|---|---|---|---|
| D1 | Add feature flags for all three workstreams | Core Platform Engineer | 0.5d | A4, B5, C4 | App/config surfaces |
| D2 | Add benchmark session for latency and trust overhead | Telemetry Engineer | 0.5d | B5 | Telemetry + validation scripts |
| D3 | Run operator review on avatar naturalness | Product Owner + UX | 0.5d | C6 | Manual validation |
| D4 | Update docs and release notes | Technical Writer / Engineer | 0.5d | A8, B9, C9 | `docs/` |
| D5 | Final approval review and rollout recommendation | Kirk LaSalle | 0.5d | All epics | `docs/` |

---

## 6. Delivery Order

1. A1-A7
2. B1-B5
3. C1-C6
4. A8, B6-B9, C7-C9
5. D1-D5

---

## 7. Suggested Sprint Packaging

### Sprint 1

1. A1-A4
2. B1-B3
3. C1-C2

### Sprint 2

1. A5-A8
2. B4-B6
3. C3-C6

### Sprint 3

1. B7-B9
2. C7-C9
3. D1-D5

---

## 8. Approval Checklist

- [ ] Backlog approved as written
- [ ] Owners accepted
- [ ] Estimates accepted
- [ ] Phase order accepted
- [ ] Ready for engineering kickoff
