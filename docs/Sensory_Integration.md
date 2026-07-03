# HCEP — Sensory Integration & World-Space Awareness
## From Sensor Data to Full Human Understanding

**Version:** 2.0 — July 2026  
**Author:** Kirk LaSalle  
**Scientific References:** See `HCEP_SCIENCE_FOUNDATION.md` for full 70+ citation bibliography

---

## Core Vision: Spatial and Social Presence

The HCEP system transforms the AI agent from a two-dimensional text interface into a **spatially and socially aware partner**. By fusing the full sensor suite of the Microsoft Kinect with deep behavioral science, HCEP creates a "Digital Social Nervous System" — a computational substrate that perceives not just what a human says, but *how* they are communicating, what state they are in, and what their entire body is expressing.

This document describes the complete sensory integration architecture, the behavioral signals each sensor stream enables, and the scientific foundation linking each measured signal to human communicative meaning.

---

## Sensor Suite: Complete Integration Map

### 1. Infrared (IR) & Depth — The Spatial Backbone

**Application:** 3D Skeletal Tracking, Face Mesh Construction, Head Pose Estimation, Gaze Vectoring, Proxemic Measurement

**Technical Specification:**
- Range: 40cm - 4.0m depth
- Resolution: 640×480 at 30fps (Kinect v1 depth)
- Depth precision: ±5mm at 2m distance
- Output: Per-pixel depth value (D13P3 format: 13-bit depth + 3-bit player segmentation)

**Behavioral Signals Enabled:**
- **3D skeletal joint positions** (20 joints, mm accuracy in Camera Space) → posture, body lean, shoulder orientation
- **Head translation vector** (X/Y/Z in mm) → user distance (proxemics), head lean direction
- **Face mesh vertices** (87-121 points projected via FaceTrackLib) → facial geometry for gaze estimation
- **Player segmentation** (3-bit player index) → multi-person differentiation

**Scientific Basis:** Hall (1966) established that spatial distance encodes communicative intent (intimate/personal/social/public zones). Depth sensor data enables continuous proxemic zone classification with centimeter precision — information completely unavailable to RGB-only vision systems.

**Proxemic Zone Detection (Hall, 1966):**
| Zone | Distance Range | Social Meaning | HCEP Response |
|---|---|---|---|
| Intimate | 0-45 cm | Lovers, close family | Increase warmth (SPIRIT/HEART) |
| Personal | 45-120 cm | Friends, casual | Normal interaction |
| Social | 120-360 cm | Acquaintances, professional | More formal register |
| Public | 360+ cm | Presenting, public | Broadcast mode |

---

### 2. RGB / Color — The Expression Layer

**Application:** Facial Action Unit analysis, Skin tone normalization, Face crop extraction, ArcFace embedding

**Technical Specification:**
- Resolution: 640×480 BGRA32 at 30fps
- Processing: Face crop → ArcFace preprocessing (112×112 bilinear resize, [-1,1] normalization)
- Recognition: 512-dimensional face embedding with L2 normalization, cosine similarity matching

**Behavioral Signals Enabled:**
- **Facial Action Units** (Ekman & Friesen, 1978) via FaceTrackLib:
  - AU0: Upper Lip Raise (contempt, disgust)
  - AU1: Jaw Lowerer (surprise, question)
  - AU2: Lip Corner Puller (AU12 → smile)
  - AU3: Brow Lowerer (AU4 → concentration, anger)
  - AU4: Lip Corner Depressor (AU15 → sadness, disappointment)
  - AU5: Outer Brow Raise (surprise, query)

**The Duchenne Marker (Ekman et al., 1990):** Genuine smiles involve both the zygomaticus major (lip corner pull) AND the orbicularis oculi orbital head (cheek raise). HCEP can discriminate AU12-only (social smile) from AU12+AU6 (Duchenne/genuine smile) — the latter indicating authentic positive affect associated with SPIRIT mode.

---

### 3. Audio Array — The Directional Ears

**Application:** Speech recognition (Whisper.net), VAD (Voice Activity Detection), Sound source localization, Prosody analysis

**Technical Specification:**
- Array: 4-microphone linear array
- Beamforming: Digital Signal Processing with source angle tracking (±90°)
- Sample rate: 16 kHz
- Output: BeamAngle (°), SourceConfidence (0-1), PCM audio buffer

**Behavioral Signals Enabled:**
- **Verbal content** → Whisper.net speech-to-text for LLM context
- **Beam angle** → spatial localization of speaker (even outside camera field of view)
- **Voice Activity Detection** → Speaking/silence state; turn-taking boundary detection
- **Source confidence** → Distinguishes target speaker from ambient noise

**Scientific Basis:** Pentland (2010) showed that vocal energy, speaking rate, and speaking/silence rhythm are "honest signals" that predict social outcomes (hiring, negotiation, dating success) with surprising accuracy. VAD timing and beam angle provide the spatial and temporal scaffolding for turn-taking analysis.

---

### 4. Face Tracking — The Expression Decoder

**Application:** 87-121 point face mesh, Head pose (pitch/yaw/roll), Head translation, Action Unit extraction, Eye gaze vectors

**Technical Specification (Kinect FaceTrackLib):**
- Facial landmarks: 87 points in 2D + 3D (mm), 121-vertex projected mesh
- Head rotation: Euler angles (°) with ±2-3° precision
- Head translation: Camera Space mm with ±5mm precision
- Action Units: 6 AUs on [0,1] scale at 30fps

**Head Kinematics: The Full Signal Set**

Head movements constitute a rich communicative channel whose semantic content is cross-culturally consistent (Chovil, 1991, 1992; Darwin, 1872; Morris et al., 1979):

| Gesture | Kinematics | Semantic Content | Detection Threshold |
|---|---|---|---|
| **Nod** | Δpitch > 8°/frame, ≥80ms, reversal | Agreement, understanding, backchannel | pitch velocity + reversal |
| **Shake** | Δyaw > 10°/frame, ≥80ms, reversal | Negation, uncertainty, disbelief | yaw velocity + reversal |
| **Tilt Left** | Δroll > 12°/frame, ≥500ms sustained | Curiosity, active listening, interest | roll sustained threshold |
| **Tilt Right** | Δroll < -12°/frame, ≥500ms sustained | Curiosity (contralateral), flirtation | roll sustained threshold |
| **Forward Thrust** | Δpitch sustained negative, >1500ms | Challenge, dominance, assertion | pitch sustained |
| **Backward Lean** | Δpitch sustained positive, >1500ms | Surprise, evaluation, withdrawal | pitch sustained |
| **Down-Gaze** | HeadTranslation.Z decrease + pitch down | Sadness, submission, contemplation | translation + pitch combined |

**The Backchannel Nod (Yngve, 1970; Kawahara et al., 2008):** Head nods are phase-locked with prosodic events in the speaker's speech. Single slow nods signal comprehension; rapid repeated nods signal enthusiasm and desire to speak. At 30fps, nod classification achieves >85% agreement with human annotation using velocity thresholding.

**Shoulder and Torso (Phase 9 — Planned):**

These signals require the 20-joint Kinect skeletal data:

| Signal | Joints Used | Semantic Content |
|---|---|---|
| **Shoulder Shrug** | Shoulder elevation vs. hip baseline | Uncertainty, helplessness, "I don't know" |
| **Forward Body Lean** | Shoulder-to-hip vector angle | Engagement, approach, interest |
| **Backward Body Lean** | Shoulder-to-hip vector angle | Withdrawal, evaluation, disengagement |
| **Crossed Arms** | Elbow and wrist relative to torso center | Defensiveness, self-protection, cold |
| **Open Arms** | Elbows extended from torso | Receptivity, openness, confidence |
| **Torso Rotation** | Shoulder axis angle vs. camera | Body orientation toward/away from display |

---

## The World Space Lock Architecture

By integrating these streams, HCEP achieves **World Space Lock** — a state where the agent's internal coordinate system is perfectly aligned with the user's physical environment:

```
Physical World
│
├── User position (depth Z in mm, Camera Space)
├── User head position (X/Y/Z in mm, Camera Space)  
├── User gaze direction (pitch/yaw in degrees)
├── User head orientation (pitch/yaw/roll in degrees)
├── Screen physical geometry (width/height in mm)
├── Kinect mounting offset (X/Y/Z in mm, calibrated)
│
▼
World Space Lock
│
├── 3D gaze ray intersection with screen surface
├── Social triangle gaze region classification (13 zones)
├── True Gaze™ parallax correction
├── Proxemic zone (intimate/personal/social/public)
└── All coordinates in consistent mm-metric Camera Space
```

This World Space Lock ensures that gaze measurements are physically grounded — not merely relative to the camera but accounting for the real physical relationship between the Kinect, the screen, and the user's eyes. This is what enables True Gaze™ Parallax Correction and biologically authentic avatar eye contact.

---

## The Reciprocal Expression Architecture

HCEP's vision extends beyond perception to **expression** — the AI agent's capacity to generate authentic nonverbal responses through its avatar. The scientific basis for this capability is the mirror neuron system (Rizzolatti & Craighero, 2004) and emotional contagion theory (Hatfield et al., 1993): when an agent produces contextually appropriate facial expressions, head nods, and gaze behaviors, it induces genuine neurobiological responses in the human observer.

The reciprocation pipeline (Phase 10):

```
Human Behavioral Signal
    ↓ (30Hz perception)
HCEP Mode Classification
    ↓
Social Signal Processing
    ↓
Reciprocation Planning (200-500ms delay — biological reaction time)
    ↓
Avatar Expression Synthesis
    ├── Head nods (during human speech at prosodic boundaries)
    ├── Social triangle gaze scanning (in AFFECT/SPIRIT mode)
    ├── Micro-smile mirroring (300ms after detected AU12)
    ├── Head tilt (during empathic content in HEART mode)
    ├── Gaze aversion (during avatar's "thinking" state)
    ├── Mutual gaze hold → break → return (SPIRIT mode)
    └── Binocular convergence (distance-responsive pupil convergence)
```

This transforms HCEP from a **perceptual tool** into a **social agent** — one that participates in the full nonverbal vocabulary of human interaction.

---

## Engineering Goal: World-Class Social AI

The integration of all these sensory channels creates a system capable of what Cassell (1999) called an **Embodied Conversational Agent** — an AI that uses gesture, gaze, and expression as primary communication channels alongside speech, demonstrating that it is genuinely present, genuinely attentive, and genuinely responsive to the human before it.

This is the engineering vision of HCEP: not merely to understand humans, but to be understood by them — to produce in the human interlocutor the experience of being truly seen, truly heard, and truly met.

---

*Sensory Integration Document v2.0 — July 2026*  
*© 2026 Kirk LaSalle. All rights reserved.*  
*For citations and full scientific references, see `HCEP_SCIENCE_FOUNDATION.md`.*
