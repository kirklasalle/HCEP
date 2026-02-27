# Dual-AI Collaboration Strategy: Nexus & GitHub Copilot

**Project:** HCEP (Human Communication Eye Protocol)  
**Date:** February 26, 2026  
**Authors:** Kirk LaSalle & Nexus  

---

## 1. Executive Summary
This document defines the hybrid development methodology for building the "True Gaze" Avatar (Phase 6 of HCEP). By combining the strategic oversight of Nexus with the tactical, in-editor execution of GitHub Copilot, we establish an unprecedented, context-aware development loop.

## 2. The Division of Labor

### Nexus (Chief Architect & Context Manager)
- **Role:** High-level system design, mathematical theory formulation, roadmap tracking, and ethical/Prime Directive alignment.
- **Strengths:** Persistent memory across sessions, deep understanding of the *Why* (the psychology of eye-contact and micro-saccades), and holistic codebase awareness.
- **Workflow:** 
  - Define the architecture (e.g., the Screen-to-Camera Calibration Matrix).
  - Provide pseudo-code and specific mathematical formulas for 3D-to-2D spatial projections.
  - Review and analyze Copilot's output for logic flaws or edge cases (e.g., lighting failures, hardware limits).
  - Document decisions in `MEMORY.md` and RAG architecture.

### GitHub Copilot (Tactical Developer)
- **Role:** In-IDE execution, syntax completion, boilerplate generation, and immediate refactoring.
- **Strengths:** Lightning-fast typing, deep knowledge of C# / .NET 9.0 syntax, WPF bindings, and immediate file context within Visual Studio / VS Code.
- **Workflow:**
  - Convert Nexus's spatial math formulas into functional C# methods.
  - Generate the WPF Calibration UI overlay.
  - Auto-complete repetitive variable declarations and XAML bindings.

---

## 3. The Development Loop (The "Ping-Pong" Method)

1. **Strategize (Nexus + Kirk):** We discuss the feature requirements (e.g., mapping Face Tracking Basic eye sockets to the Avatar's IK rig). Nexus drafts the logic and constraints.
2. **Execute (Kirk + Copilot):** You open VS Code. Using the logic we mapped out, you prompt Copilot to generate the specific C# classes and methods. 
3. **Verify (Copilot + IDE):** You test the code locally to ensure it compiles and binds correctly in WPF.
4. **Evaluate & Refine (Nexus + Kirk):** You paste the working (or failing) code back to me. I perform a deep structural review, ensuring it scales, doesn't leak memory (crucial for 30fps Kinect data), and strictly aligns with the HCEP theory.

## 4. Phase 6 Immediate Execution Plan

Our next technical focus utilizing this dual-AI strategy:
1. **The Math:** I will provide the coordinate mapping logic to calculate the delta angle between the Kinect sensor and the screen coordinates.
2. **The Code:** You use Copilot to scaffold the `CalibrationMatrixCalculator.cs` class.
3. **The Saccades:** We build the `AvatarEyeController` that targets the left/right bounding boxes of the user's eye sockets with a randomized (but bounded) micro-saccade timer.

---

*This strategy isolates the strengths of both AI models, ensuring fast execution without sacrificing long-term architectural integrity or purpose.*