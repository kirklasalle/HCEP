# Nexus-Copilot Bridge Context
**Project:** HCEP (Human Communication Eye Protocol) v1.0
**Current Goal:** Implement the "True Gaze" Avatar parallax fix and micro-saccades.

## Architecture
- **Camera:** Offset from the screen (e.g., top of monitor).
- **Target:** User's eye sockets (Face Tracking Basic), avoiding heavy pupil mesh processing.
- **Goal:** Avatar's IK rig needs to look at the user's screen-relative eyes, requiring a 3D-to-2D Calibration Matrix to account for the camera offset.

## Core Rules for Copilot
1. Follow Nexus's architecture directives implicitly.
2. Focus on C# syntax, WPF bindings, and memory-safe code (preventing memory leaks at 30fps).
3. Do not alter the core HCEP psychological logic (the 5 Modes).