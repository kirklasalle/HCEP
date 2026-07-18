# HCEP v1.5.0 — Release Notes

**Release date:** 2026-07-18
**Assembly version:** 1.5.0.0
**Package version:** 1.5.0.0

---

## Highlights

HCEP v1.5.0 upgrades the user-facing Avatar App around one canonical 3D wireframe rule: the 3D Wireframe avatar uses the live Candide-3 projected mesh, keeps it persistent, and lets the HCEP eyes own facial alignment.

- **3D Wireframe is now the real Candide-3 projected mesh.** The Avatar App prefers live `GetProjectedShape()` geometry in mirrored and non-mirrored modes, with neutral mesh only as fallback.
- **Eyes are the parent coordinate.** Live eye feature-point contours anchor the 3D wireframe face, and full-mesh rendering no longer applies a second head-pose correction on top of Kinect-projected vertices.
- **New 3D High-Poly Wireframe avatar.** A selectable procedural head-and-shoulders mesh ships with 6,374 vertices, 12,038 wire edges, HCEP eye spheres, brow/viseme/smile/proxemic support, and a human-biased anatomy pass.
- **3D eye-position calibration.** Operators can tune and persist 3D eye offsets through the app UI so the rendered eyes and gaze engine stay aligned.
- **Release packaging hardened.** The package script now derives version metadata from `Directory.Build.props` and reliably creates a verified v1.5.0 ZIP archive.

---

## 1. Avatar App: canonical Candide-3 wireframe

The `3D Wireframe` selection now renders the live Kinect FaceTrackLib Candide-3 projected mesh as the user-facing avatar wireframe. This removes the previous split behavior where one path showed the rich tracked mesh while another path could fall back to a sparse neutral projection.

### What changed

- `AvatarWindow` always prefers live `FaceMeshVertices2D` for `Avatar3DControl` when available.
- Mirroring no longer gates mesh quality or head-pose data flow.
- `Avatar3DControl.ResetGaze()` no longer clears persistent mesh geometry.
- Full-mesh rendering trusts the projected Candide-3 vertices and does not apply an additional corrective head transform.

### Operator effect

The Avatar App should show one persistent 3D wireframe face for the `3D Wireframe` avatar: the real projected Candide-3 mesh, driven by available face data and kept aligned to the HCEP eyes.

---

## 2. Eye-first alignment

The 3D avatar eye positions are no longer derived from the 2D Happy Face's 280×280 proportional layout when live feature points are available.

- Right eye contour: Candide feature-point indices 9–14.
- Left eye contour: Candide feature-point indices 30–35.
- Eye centers are computed from those live contours and drive the screen-space `LeftEyeScreenPos` / `RightEyeScreenPos` values consumed by `GazeVectorEngine`.
- Full-mesh eye anchors are unsmoothed in the authoritative mesh path so the face follows the eyes rather than lagging behind them.

---

## 3. New high-poly procedural avatar

`AvatarHighPolyWireframeControl` adds a third selectable Avatar App implementation: `3D High-Poly Wireframe`.

The new avatar is deterministic and does not require Kinect mesh availability. It provides a production-grade procedural head-and-shoulders wireframe with:

- 6,374 model vertices and 12,038 wire edges.
- Cranium, temple, cheekbone, jaw, chin, neck, trapezius, shoulder, clavicle, and sternocleidomastoid contouring.
- Closed eye contours, brow ridges, nose bridge/tip/nostril structure, lip curves, ears, and cheek planes.
- HCEP eye spheres with convergence, micro-saccades, social gaze offsets, and proxemic pupil dilation.
- `IAvatarComponent` support for gaze, brows, visemes, smile, nod, tilt, social gaze, and proxemic distance.

---

## 4. Calibration and routing

The app now includes an eye-position calibration window for the 3D avatar. Slider changes update `EyePositionCalibration`, persist under local HCEP app data, and flow into the relevant 3D eye-rendering paths.

Avatar routing was expanded so the window forwards gaze, head pose, brows, visemes, smile, proxemics, nods, tilts, and social gaze offsets to the active avatar implementation.

---

## 5. Release packaging

The release script now reads the shared version from `Directory.Build.props`, writes the matching Appx manifest version, and emits a versioned ZIP name:

```powershell
.\scripts\package_release.ps1
```

Expected artifact:

```text
publish\HCEP-win-x64-v1.5.0.zip
```

The ZIP step now uses `System.IO.Compression.ZipFile.CreateFromDirectory()` instead of `Compress-Archive`, then verifies the resulting archive size. This fixes a packaging path where PowerShell could return without leaving the expected ZIP artifact.

---

## 6. Validation

Validated on 2026-07-18:

- Debug build: `dotnet build src\HCEP.App\HCEP.App.csproj -c Debug -v m -nologo`
- Test suite: `dotnet.exe test tests\HCEP.Tests\HCEP.Tests.csproj -c Debug -v m --nologo` — 211/211 passing
- Release package: `scripts\package_release.ps1` — generated verified ZIP with `HCEP.App.exe`
- Diff hygiene: `git diff --check` — clean except Git LF-to-CRLF normalization warnings

---

## 7. Manual smoke test checklist

Before publishing a binary release, perform one visual pass in the WPF Avatar window:

- Select `3D Wireframe` and confirm the Candide-3 mesh persists.
- Confirm the face mesh tracks with the HCEP eyes as the priority anchor.
- Move the 3D eye-position sliders and confirm the eyes respond.
- Select `3D High-Poly Wireframe` and confirm the head, shoulders, eyes, brows, and mouth render cleanly.

---

**© 2026 Kirk LaSalle. All rights reserved.**
