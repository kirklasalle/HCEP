# HCEP Project Documentation: Graphics Architecture

**Date:** 2026-07-03 (Updated)  
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

### 3. IAvatarComponent Interface — `src/HCEP.App/IAvatarComponent.cs`

Both avatars implement this shared contract:

```csharp
void SetGaze(float pitchRad, float yawRad, float userDistanceM = 1.5f);
void SetViseme(VisemeData viseme);   // lip sync
void SetBrows(float outerBrowRaise, float browLower, float hcepModeFurrow = 0f);
void ResetGaze();
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

## Architecture Decision: WPF Vector Rendering

The HCEP Avatar system (Phase 2 and beyond) utilizes native WPF (Windows Presentation Foundation) vector-based rendering instead of static raster images (.jpg, .png).

### Key Rationales

1. **Infinite Scalability:** As the HCEP Avatar window is moved, resized, or maximized, the graphics are recalculated by the GPU in real-time. This ensures that the Avatar remains perfectly crisp and smooth on any display resolution (4K, 8K, etc.) without pixelation or " image destruction.\
2. **Mathematical Precision for True Gaze:** Vector objects (Ellipses, Paths) allow the Gaze Engine to resolve the exact center of the eye sockets down to fractional pixel coordinates. This precision is required to maintain the 3D-to-2D spatial alignment needed for perfect eye contact.
3. **Dynamic Manipulation:** Unlike static images, vector-based pupils and eyelids can be transformed (translated, rotated, skewed) programmatically via code-behind without any loss in visual fidelity.

## Technical Implementation

The Avatar is implemented as a UserControl. Shapes are defined in XAML using Ellipse and Path objects, which are then manipulated via TranslateTransform and ScaleTransform based on real-time Kinect telemetry.
