# HCEP Project Documentation: Graphics Architecture

**Date:** 2026-07-18 (Updated)
**Subject:** Vector-Based UI for HCEP Avatars — Complete Facial Expression System

## Architecture Decision: WPF Vector Rendering

The HCEP Avatar system utilizes native WPF (Windows Presentation Foundation) vector-based rendering. All facial features — eyes, eyelids, eyebrows, mouth — are drawn as parametric geometry rebuilt at 30Hz.

### Key Rationales

1. **Infinite Scalability:** Vector objects (Ellipses, Paths, StreamGeometry) are recalculated by the GPU in real-time, remaining perfectly crisp at any display resolution (4K, 8K) without pixelation.
2. **Mathematical Precision for True Gaze:** Fractional pixel coordinates for eye socket centres drive the GazeVectorEngine. `AvatarCoreControl.UpdateEyeScreenCoordinates()` maps canvas-space socket centres to physical screen pixels via `PointToScreen()`.
3. **Dynamic Manipulation:** Pupils (iris travel), eyelids (blink animation), eyebrows (AU-driven bezier arcs), and mouth (viseme-driven geometry) are all transformed programmatically at runtime.

## Current Avatar Implementations

### 1. AvatarCoreControl (2D Happy Face) — `src/HCEP.App/AvatarCoreControl.xaml(.cs)`

**Canvas layout (280×280 local pixels):**

- Face circle: centre (140,140), radius 120
- Left eye socket: centre (95,112), diameter 44px
- Right eye socket: centre (185,112), diameter 44px
- Left eyebrow: quadratic bezier `M 66,80 Q 95,68 120,76`
- Right eyebrow: quadratic bezier `M 160,76 Q 185,68 214,80`
- Mouth: reshaped SmilePath arc + MouthFill Ellipse (lip sync)

**Animated features:**

| Feature | Mechanism | Update Rate |
|---|---|---|
| Pupil position | `rotYaw`/`rotPitch` × travel fraction | 30 Hz |
| Eyelids | Blink engine EMA (70ms close, 95ms open) | 30 Hz |
| Eyebrows | Quadratic bezier via AU3/AU5 + HCEP mode | 30 Hz |
| Mouth | `SmilePath` + `MouthFill` from `VisemeData` | 30 Hz |
| Head pose | RootCanvas RenderTransform (yaw/pitch/roll) | 30 Hz |
| Micro-saccades | ±1.5% yaw, ±1.0% pitch jitter every 300–900ms | 30 Hz |

### 2. Avatar3DControl (3D Wireframe) — `src/HCEP.App/Avatar3DControl.cs`

**Rendering pipeline:**

- Kinect FaceTrackLib Candide-3 mesh (~121 vertices, ~218 triangles) projected via `GetProjectedShape`
- `OnRender(DrawingContext)` draws all geometry each frame
- Full-mesh Avatar rendering is eye-first: live FP eye contours (indices 9–14 right, 30–35 left) own socket placement regardless of mirroring state, and the projected Candide-3 mesh is not given a second head-pose correction after `GetProjectedShape`
- `_wirePen`: teal (RGBA 220,0,220,190), 1.2px stroke
- `_browPen`: teal 1.8px (slightly thicker for eyebrow visibility)

**Proportional element placement (all relative to eye socket radius `eyeR`):**

| Element | Position | Size |
|---|---|---|
| Eye spheres | `_leftEyeSocketSmoothed`, `_rightEyeSocketSmoothed` | radius = `eyeR` |
| Eyebrows | `eyeR * 1.35` above socket centre | halfW = `eyeR * 1.1` |
| Mouth arc | `eyeR * 2.8` below eye centre line | halfW = `eyeR * 1.3` |

**Eye sphere layers (back to front):**

1. Sclera: RadialGradientBrush (white centre → dark rim)
2. Iris: foreshortened ellipse (yaw/pitch causes perspective narrowing)
3. Pupil: dark centre
4. Specular highlight: upper-left offset white dot

### 3. AvatarHighPolyWireframeControl (3D High-Poly Wireframe) — `src/HCEP.App/AvatarHighPolyWireframeControl.cs`

**Rendering pipeline:**

- Deterministic procedural head-and-shoulders wireframe independent of Kinect `GetProjectedShape` availability
- 6,374 model vertices and 12,038 wire edges across a human-biased cranium, temple, cheekbone, jaw/chin surface, non-cylindrical neck, trapezius/shoulder surface, facial contours, ears, brow ridges, nose/nostrils, lips, clavicles, and neck tendon guide lines
- `OnRender(DrawingContext)` projects all model vertices through the same yaw/pitch/roll/perspective transform, then draws depth-weighted front/back wire pens
- Eye-first design: HCEP eye anchors are model-space points projected through the same transform as the mesh, then exposed through `LeftEyeScreenPos` / `RightEyeScreenPos` for `GazeVectorEngine`

**Anatomy audit refinements:**

| Region | Production contour improvement |
|---|---|
| Head | Cranium vault, temple inset, cheekbone expansion, jaw taper, chin narrowing, front facial plane bias |
| Eyes/brows | Closed anatomical eye contours plus separate brow-ridge arcs |
| Nose | Bridge, lower bridge, protruding tip, nostril wings, nostril arcs, philtrum guide |
| Mouth | Upper/lower lip curves plus neutral mouth seam |
| Ears | Outer and inner ear loops on both sides with antihelix guide strokes |
| Neck | Wider lower neck, front tendon bias, sternocleidomastoid guide lines |
| Shoulders | Trapezius rise near the neck, deltoid falloff toward the shoulders, clavicle arcs |

**Supported HCEP avatar signals:**

| Signal | Behavior |
|---|---|
| Gaze | HCEP eye spheres with convergence, micro-saccades, social gaze offsets |
| Head pose | Smoothed low-influence yaw/pitch/roll for responsive presence |
| Brows | AU/HCEP-mode-driven quadratic brow arcs |
| Visemes | Jaw/rounding mouth geometry from `VisemeData` |
| Smile | Smile-depth blend with viseme co-articulation |
| Proxemics | Close-distance pupil dilation |
| Gestures | Nod and tilt animation hooks via `IAvatarComponent` |

### 4. IAvatarComponent Interface — `src/HCEP.App/IAvatarComponent.cs`

All shipped avatars implement this shared contract:

```csharp
void SetGaze(float pitchRad, float yawRad, float userDistanceM = 1.5f);
void SetViseme(VisemeData viseme);   // lip sync
void SetBrows(float outerBrowRaise, float browLower, float hcepModeFurrow = 0f);
void ResetGaze();
void TriggerNod();
void TriggerTilt(float rollDeg = 6f);
void SetSmile(float intensity);
void SetSocialGazeOffset(float yawRad, float pitchRad);
void SetProxemicDistance(float distanceM);
```

## Eyebrow Animation System

Eyebrows are rendered as quadratic bezier curves (`StreamGeometry`) rebuilt at 30Hz via `ApplyBrows()`:

**2D Happy Face geometry:**

- Neutral: outer=(66,80) → peak=(95,68) → inner=(120,76)
- Raised (AU5): all Y values decrease by up to 9px; deeper arch
- Furrowed (AU3): inner nasal ends drop +7px; creates analytical/concern V-shape

**HCEP Mode → autonomous brow posture:**

| Mode | Brow | Expression |
|---|---|---|
| LOGIC | Furrow 0.30 | Analytical concentration |
| THINK | Furrow 0.50 | Internal processing |
| HEART | Raise 0.35 | Empathy (AU1 inner raise) |
| AFFECT | Raise 0.12 | Open/engaged |
| SPIRIT | Neutral | Relaxed presence |

## Lip Sync System (Phase 13)

Mouth animation is driven by `VisemeData` from `HCEP.Speech.VisemeController`:

| Parameter | Effect |
|---|---|
| `JawOpen` [0..1] | `MouthFill` Ellipse height (0→0, 1→32px) |
| `LipRound` [0..1] | Narrows mouth width (O/U vowels) |
| `LipSpread` [0..1] | Widens mouth (I/EE consonants) |
| `LipCompressed` [0..1] | Overrides to straight line (M/B/P bilabials) |

60ms EMA smoothing produces co-articulation — each phoneme blends into the next rather than snapping.

## True Gaze™ Calibration

The `CalibrationWindow` computes the physical 3D offset between Kinect and screen:

1. User gazes at screen-centre crosshair
2. SPACE captures `HeadTranslation` + `HeadRotation` from Kinect
3. Ray-plane intersection: `t = (screenZ - head.Z) / gazeDir.Z`
4. `t > 0` (positive) for a valid calibration (fixed July 2026 — was incorrectly rejecting valid captures)
5. `KinectOffsetX = -screenCentreX`, `KinectOffsetY = -screenCentreY`
6. Applied to `CalibrationMatrixCalculator` which computes `DeltaYawRad`/`DeltaPitchRad` correction

*Copyright © 2026 Kirk LaSalle. All rights reserved.*
