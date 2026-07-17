# Mirroring Toggle — Implementation Plan

> **Feature**: Enable/Disable toggle for Avatar Mirroring  
> **Goal**: The Avatar's natural state is autonomous — it reacts, emotes, and engages with its own social intelligence. Mirroring is a training/observation mode that can be enabled when needed for calibration, diagnostics, or behavioral study.

---

## Understanding the Problem

The Avatar's default mode should be **autonomous** — a being that listens, processes, and responds with its own expression, just as any human would. It doesn't copy you — it *engages* with you.

**Mirroring** is a specialized training and observation mode. When enabled, the avatar replicates the user's gaze, expressions, brows, and gestures — useful for:
- **Calibration** — verifying sensor fidelity and tracking accuracy
- **Training** — demonstrating what the system sees and how it interprets human signals
- **Observation** — behavioral study and diagnostics

Mirroring defaults to **OFF**. The avatar is itself by default.

---

## What "Mirroring" Actually Is (The Components)

After studying the codebase, I've identified **6 distinct mirroring pathways** that feed the user's physical state directly into the avatar:

| # | Component | What It Does | File |
|---|-----------|-------------|------|
| 1 | **Gaze Mirroring** | Avatar pupils track the user's physical eye position in real-time (`SetGaze`) | [AvatarWindow.xaml.cs](file:///d:/Projects/HCEP/src/HCEP.App/AvatarWindow.xaml.cs#L429-L451) |
| 2 | **Head Pose Mirroring** | Avatar head orientation mirrors user's head rotation (`SetHeadPose`) | [AvatarWindow.xaml.cs](file:///d:/Projects/HCEP/src/HCEP.App/AvatarWindow.xaml.cs#L305-L311) |
| 3 | **Expression Mirror** | Avatar smiles when user smiles (Duchenne detection, reaction delay) | [ExpressionMirror.cs](file:///d:/Projects/HCEP/src/HCEP.App/ExpressionMirror.cs) |
| 4 | **Brow Mirroring** | Avatar eyebrows copy user's Action Units (AU3/AU5) | [AvatarWindow.xaml.cs](file:///d:/Projects/HCEP/src/HCEP.App/AvatarWindow.xaml.cs#L320-L347) |
| 5 | **Gesture Mirroring** | Avatar nods/tilts in response to user's head gestures | [AvatarWindow.xaml.cs](file:///d:/Projects/HCEP/src/HCEP.App/AvatarWindow.xaml.cs#L147-L185) |
| 6 | **Backchannel Nods** | Avatar nods during sustained user speech | [BackchannelController.cs](file:///d:/Projects/HCEP/src/HCEP.App/BackchannelController.cs) |

> [!IMPORTANT]
> **What is NOT mirroring** (and should ALWAYS remain active):
> - **User tracking** (Kinect sensor, face detection, skeleton) — the system must always know where the user is
> - **HCEP mode classification** (LOGIC, THINK, AFFECT, HEART, SPIRIT) — cognitive state analysis continues
> - **Social Gaze Controller** (triangle scanning) — this is the avatar's *own* autonomous gaze pattern, not a mirror. It drives authentic social eye movement based on HCEP mode, not user imitation
> - **Proxemic distance awareness** — the avatar should still know how far away the user is
> - **TTS lip sync** (visemes) — if the avatar is speaking, its mouth should still move
> - **HCEP-mode-driven brow expressions** — the `modeFurrow` and `modeRaise` values from HCEP mode (e.g., furrowed brows in LOGIC mode) are the avatar's *own* expression, not mirroring

---

## Proposed Architecture

### 1. Toggle Location: The Avatar Window HUD

A clean toggle switch in the **AvatarWindow telemetry HUD bar** (the bottom bar), next to the existing 2D/3D mode selector. This keeps it immediately accessible during operation.

```
[MODE] [DIST] [PITCH] [YAW] [MESH] [🪞 MIRROR ⬤] [2D Happy ▾]
```

- **OFF** (default): Avatar operates autonomously — its natural state
- **ON**: Training/observation mode — avatar mirrors user expressions for diagnostics

### 2. State Propagation

A single `bool` property on `AvatarWindow`:

```csharp
/// <summary>
/// When true, avatar mirrors user's gaze, expression, brows, and gestures
/// (training/observation mode). When false (default), avatar operates
/// autonomously using HCEP-mode-driven expressions only.
/// User tracking remains active in both modes.
/// </summary>
public bool IsMirroringEnabled { get; private set; } = false;
```

### 3. Critical Architecture: Data Layer vs Display Layer

> [!IMPORTANT]
> **The mirroring toggle gates ONLY the avatar's visual output.** All sensing, analysis, classification, and telemetry continue at all times, regardless of toggle state. The system never stops knowing — the avatar just chooses whether to show it.

Every mirroring component has two roles:
1. **Data role** — sensing, classifying, and recording human signals into HCEP telemetry (ALWAYS ACTIVE)
2. **Display role** — applying those signals to the avatar's visual appearance (gated by toggle)

| # | Component | Data Layer (always runs) | Display: Mirror ON | Display: Mirror OFF |
|---|-----------|-------------------------|-------------------|--------------------|
| 1 | **Gaze** (`GazeVectorEngine`) | Computes pitch/yaw → telemetry records gaze direction | ✅ Pupils track user | ❌ Avatar uses Social Gaze offsets only |
| 2 | **Head Pose** (`SetHeadPose`) | Head rotation analyzed → informs gesture classifier, HCEP mode | ✅ Head copies user | ❌ Head stays neutral (future: LLM-driven) |
| 3 | **Expression** (`ExpressionMirror`) | Detects smile onset, Duchenne markers → telemetry records | ✅ Avatar smiles back | ❌ Avatar smiles from HCEP mode / LLM only |
| 4 | **Brows** (AU3/AU5) | Action Units read → telemetry, HCEP classification | ✅ Brows copy user | ⚠️ User AUs suppressed; HCEP-mode brows still active |
| 5 | **Gestures** (`HeadGestureClassifier`) | Nods/tilts/shakes classified → telemetry records | ✅ Avatar mirrors gesture | ❌ Avatar generates own gestures |
| 6 | **Backchannel** (`BackchannelController`) | Detects sustained speech → telemetry records | ✅ Avatar nods | ✅ **KEPT** — avatar's own listening behavior |
| — | **Social Gaze** (`SocialGazeController`) | Triangle scanning driven by HCEP mode | ✅ Active | ✅ **KEPT** — autonomous |
| — | **TTS Visemes** | Lip sync from speech synthesis | ✅ Active | ✅ **KEPT** — avatar's own speech |
| — | **Proxemic Distance** | User distance measured | ✅ Active | ✅ **KEPT** — environmental awareness |

> [!NOTE]
> **Key design decision**: Backchannel nods are NOT mirroring. They're the avatar's own listening behavior (it nods while *the user talks*, not when the user nods). These stay active because they're exactly the kind of autonomous social behavior we want when mirroring is off.

### 4. Implementation Changes (3 files)

#### A. `AvatarWindow.xaml` — Add Toggle to HUD
Add a `ToggleButton` or styled `CheckBox` to the telemetry HUD bar, styled to match the existing dark-theme aesthetic.

#### B. `AvatarWindow.xaml.cs` — Guard Display Layer Only
All analysis/detection code stays untouched. Only the **final display calls** get guarded:

```csharp
// In OnGazeVectorReady — analysis already complete, only gate the display:
if (IsMirroringEnabled)
    _activeAvatar.SetGaze(pitch, yaw, distanceM);
// Telemetry HUD (pitch/yaw/distance text) always updates — that's data, not mirroring

// In OnSnapshotReady (head pose) — classifier already fed, only gate avatar:
if (IsMirroringEnabled)
{
    Avatar3D.SetHeadPose(face.HeadRotation);
    Avatar.SetHeadPose(face.HeadRotation);
}
// _gestureClassifier.Update() always runs — that's data

// In OnSnapshotReady (brows) — AU extraction always happens for telemetry:
float auRaise = IsMirroringEnabled ? /* user AU5 */ : 0f;
float auLower = IsMirroringEnabled ? /* user AU3 */ : 0f;
// modeFurrow and modeRaise always pass through — avatar's own expression

// In OnHeadGestureDetected — classifier already ran, only gate avatar response:
if (!IsMirroringEnabled) return;

// In OnSmileRequested — detection already ran, only gate avatar smile:
if (!IsMirroringEnabled) return;
```

#### C. `AvatarWindow.xaml.cs` — Toggle Handler
```csharp
private void MirrorToggle_Changed(object sender, RoutedEventArgs e)
{
    IsMirroringEnabled = MirrorToggle.IsChecked == true;
    // When disabling, reset avatar to neutral pose
    if (!IsMirroringEnabled)
    {
        _activeAvatar.ResetGaze();
        _activeAvatar.SetSmile(0f);
        _activeAvatar.SetBrows(0f, 0f, 0f);
    }
}
```

### 5. Unity SDK (`HcepGazeController.cs`)
Add a corresponding `public bool mirroringEnabled = false;` field so Unity integrators get the same default. When enabled (for training), `Update()` applies head/eye rotations from the HCEP stream.

---

## What This Does NOT Change

- ✅ **User tracking continues** — Kinect, face detection, skeleton tracking all remain active
- ✅ **HCEP analysis continues** — cognitive mode classification keeps running
- ✅ **Data flows normally** — snapshots, telemetry, knowledge store all continue
- ✅ **LLM interactions unaffected** — the AI can still respond to the user
- ✅ **The Avatar is a participant by default** — mirroring is a deliberate training choice

---

## Summary

This is a **surgical, 3-file change** that inserts a single boolean gate at the 5 mirroring pathways in `AvatarWindow.xaml.cs`, adds a toggle to the HUD in `AvatarWindow.xaml`, and extends the Unity SDK with a matching field. The avatar's autonomous behaviors (backchannel nods, social gaze scanning, TTS lip sync, HCEP-mode expressions) continue to operate, giving the avatar its own authentic social presence.

The toggle defaults to **OFF** — the Avatar's natural state is autonomous. Enable mirroring when you need it for training, calibration, or observation. The Avatar is itself first.
