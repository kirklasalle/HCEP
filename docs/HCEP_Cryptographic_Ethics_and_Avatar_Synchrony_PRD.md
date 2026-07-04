# HCEP Cryptographic Ethics and Avatar Synchrony PRD

**Document Type:** Product Requirements Document  
**Program:** HCEP Cryptographic Ethics and Avatar Synchrony  
**Prepared For:** Kirk LaSalle  
**Prepared By:** GitHub Copilot  
**Status:** Draft for Approval  
**Last Updated:** July 4, 2026

---

## 1. Product Summary

This PRD formalizes the implementation program derived from the HCEP Cryptographic Ethics and Avatar Synchrony critique. The program upgrades HCEP in three areas:

1. Context-aware cognitive mode inference
2. Cryptographically trusted telemetry propagation
3. Biologically plausible avatar backchannel synchrony

These upgrades are intended to strengthen HCEP's scientific fidelity, ethical enforcement model, and avatar realism without breaking the current perception, prompt, or plugin surfaces.

---

## 2. Problem Statement

HCEP already implements a five-mode classifier, contextual snapshotting, PAD validation, speech recognition, LLM prompting, and avatar backchanneling. However, the current system has three limitations:

1. Context influences behavior too late and too rigidly.
2. PAD integrity is validated locally, but trust is not propagated to downstream consumers of HCEP telemetry.
3. Avatar backchannel timing is deterministic enough to feel mechanical during sustained interaction.

If these gaps remain, HCEP risks underusing its contextual intelligence, overestimating its ethical trust boundary, and reducing social realism in avatar reciprocation.

---

## 3. Goals

1. Upgrade mode classification so contextual signals act as priors rather than only post-classification overrides.
2. Ensure outbound telemetry can carry machine-verifiable proof of PAD-valid runtime state.
3. Improve avatar nod, smile, and gaze acknowledgment timing so behavior reflects live cadence rather than fixed intervals.
4. Deliver all changes behind safe rollout controls, telemetry instrumentation, and measurable acceptance criteria.

---

## 4. Non-Goals

1. Replacing the existing five-mode classifier with a fully learned probabilistic model in this phase.
2. Re-architecting all SDK/plugin protocols simultaneously.
3. Shipping cross-device identity, PKI, or remote trust federation.
4. Rebuilding avatar rendering or replacing current WPF/3D avatar surfaces.

---

## 5. Users and Stakeholders

| Role | Need |
|---|---|
| Kirk LaSalle / Product Owner | A staged, reviewable implementation path with clear approval gates |
| HCEP Core Engineering | Concrete seams for classification, orchestration, prompt, and API changes |
| Security / Governance | Verifiable propagation of PAD-valid runtime state |
| Avatar / UX Engineering | More natural reciprocation without destabilizing the avatar loop |
| Plugin / SDK Consumers | A trustworthy signal indicating whether HCEP telemetry is valid for actuation |

---

## 6. User Stories

1. As an HCEP operator, I want contextual state to shape interpretation before response logic is chosen so the assistant behaves more appropriately in private, quiet, or high-focus settings.
2. As a downstream plugin consumer, I want outbound telemetry to include verifiable trust metadata so I can reject unsafe or unsigned data automatically.
3. As a user interacting with the avatar, I want nods and micro-responses to feel naturally synchronized with my cadence rather than fixed and repetitive.
4. As the product owner, I want each workstream staged behind flags and measurable acceptance criteria so rollout risk stays controlled.

---

## 7. Functional Requirements

### FR-A: Contextual Prior Inference

1. The system shall compute a structured context-prior profile from the existing `ContextSnapshot`.
2. The system shall apply prior-driven adjustments before final HCEP mode arbitration.
3. The system shall support tunable modifiers for thresholding, hysteresis, silence sensitivity, and backchannel aggressiveness.
4. The system shall preserve the current deterministic path behind a feature flag.
5. The system shall expose telemetry comparing baseline and prior-adjusted outcomes.

### FR-B: PAD-Bound Telemetry Trust

1. The system shall derive runtime trust state from successful PAD verification.
2. The system shall attach signed trust metadata to selected outbound telemetry payloads.
3. The system shall fail closed when PAD validation or signing bootstrap fails.
4. The system shall allow at least one downstream surface to verify signatures end to end.
5. The system shall record tamper, trust, and verification failures in telemetry and logs.

### FR-C: Avatar Synchrony Upgrade

1. The system shall derive speech cadence features from the audio path.
2. The system shall replace fixed-only nod timing with cadence-aware scheduling.
3. The system shall support bounded stochastic timing variance for selected backchannel actions.
4. The system shall expose tunable synchrony parameters for UX iteration.
5. The system shall preserve stable behavior under silence, noisy input, and speaker pauses.

---

## 8. Non-Functional Requirements

1. Added trust and synchrony logic shall not cause visible UI stalls.
2. Telemetry signing overhead for the first outbound channel should target a median added latency below 10 ms.
3. Context-prior logic shall be observable in logs/metrics before full enablement.
4. All new behavior shall be feature-flagged for rollback.
5. Default behavior after deployment shall remain safe under missing context, failed signing, or weak cadence detection.

---

## 9. Success Metrics

| Area | Metric | Target |
|---|---|---|
| Contextual inference | Reduction in hard silence overrides | 30% reduction in cases handled purely by override rules |
| Contextual inference | Observable prior-adjusted mode transitions | 100% of flagged runs emit comparison telemetry |
| Telemetry trust | Signed outbound payload coverage | 1 production outbound channel in milestone 1 |
| Telemetry trust | Verification success rate in signed path | >99% in local validation sessions |
| Avatar synchrony | Repeated identical nod timing sequences | Reduced to near-zero in cadence-enabled sessions |
| Avatar synchrony | Subjective naturalness score | Improve operator-rated naturalness versus baseline |

---

## 10. Scope by Release Phase

### Phase 1: Design and Instrumentation

1. Finalize architecture and flags
2. Add comparison telemetry
3. Confirm trust and cadence data availability

### Phase 2: Contextual Prior Prototype

1. Introduce context-prior model
2. Run in shadow mode
3. Review threshold tuning

### Phase 3: Signed Telemetry Prototype

1. Add trust bootstrap service
2. Sign one outbound path
3. Validate verification semantics

### Phase 4: Avatar Synchrony Prototype

1. Add cadence features
2. Add jitter-based scheduling
3. Tune naturalness parameters

### Phase 5: Integrated Rollout

1. Run full-system validation
2. Approve rollout order
3. Promote to internal default where approved

---

## 11. Proposed Owners

| Workstream | Proposed Owner | Supporting Roles |
|---|---|---|
| Contextual Prior Inference | Core Intelligence Engineer | Vision, Telemetry |
| PAD-Bound Telemetry Trust | Security / Platform Engineer | Plugin API, Intelligence, Telemetry |
| Avatar Synchrony Upgrade | Avatar Interaction Engineer | Audio, UX, Orchestration |
| Program Coordination | Kirk LaSalle | All leads |

---

## 12. Estimates

| Workstream | Effort Estimate | Notes |
|---|---|---|
| Contextual Prior Inference | 1.5 to 2.5 engineering weeks | Includes shadow mode and instrumentation |
| PAD-Bound Telemetry Trust | 2 to 3.5 engineering weeks | Depends on trust envelope design and first consumer validation |
| Avatar Synchrony Upgrade | 1.5 to 2.5 engineering weeks | Depends on usable cadence feature extraction |
| End-to-end validation and rollout | 1 engineering week | Integration, tuning, documentation |

**Total Program Estimate:** 6 to 9.5 engineering weeks, assuming sequential delivery with some parallel design work.

---

## 13. Dependencies

1. Existing `ContextSnapshot` and `TimeContextProvider` flow remains stable.
2. PAD verification logic remains authoritative in `ActiveDirectivesManager` or a successor trust provider.
3. Outbound API surfaces remain anchored in `PluginApiServer` and related transport DTO paths.
4. Audio cadence features can be extracted from the existing speech/audio pipeline without invasive redesign.
5. Product owner approval is available at the end of each milestone.

---

## 14. Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Priors overpower observed signals | Incorrect mode classification | Launch in shadow mode and compare against baseline |
| Signed trust path adds latency | API responsiveness degradation | Start with one outbound channel and benchmark before expansion |
| Cadence estimation is noisy | Unnatural avatar behavior | Gate on confidence and fall back to existing timing policy |
| Partial rollout creates false trust assumptions | Security ambiguity | Make signed vs. unsigned surfaces explicit in docs and logs |

---

## 15. Approval Gates

1. Architecture approval for context-prior insertion point
2. Security approval for trust envelope and fail-closed semantics
3. UX approval for cadence/jitter defaults
4. Final rollout approval after integrated validation

---

## 16. Approval Status

- [ ] Approved as written
- [ ] Approved with revisions
- [ ] Requires architecture review
- [ ] Requires security review
- [ ] Deferred
