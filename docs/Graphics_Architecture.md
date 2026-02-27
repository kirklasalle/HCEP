# HCEP Project Documentation: Graphics Architecture
**Date:** 2026-02-27
**Subject:** Vector-Based UI for HCEP Avatars

## Architecture Decision: WPF Vector Rendering
The HCEP Avatar system (Phase 2 and beyond) utilizes native WPF (Windows Presentation Foundation) vector-based rendering instead of static raster images (.jpg, .png).

### Key Rationales:
1. **Infinite Scalability:** As the HCEP Avatar window is moved, resized, or maximized, the graphics are recalculated by the GPU in real-time. This ensures that the Avatar remains perfectly crisp and smooth on any display resolution (4K, 8K, etc.) without pixelation or " image destruction.\
2. **Mathematical Precision for True Gaze:** Vector objects (Ellipses, Paths) allow the Gaze Engine to resolve the exact center of the eye sockets down to fractional pixel coordinates. This precision is required to maintain the 3D-to-2D spatial alignment needed for perfect eye contact.
3. **Dynamic Manipulation:** Unlike static images, vector-based pupils and eyelids can be transformed (translated, rotated, skewed) programmatically via code-behind without any loss in visual fidelity.

## Technical Implementation
The Avatar is implemented as a UserControl. Shapes are defined in XAML using Ellipse and Path objects, which are then manipulated via TranslateTransform and ScaleTransform based on real-time Kinect telemetry.
