# HCEP — User Guide

**Product:** HCEP — Human Communication Eye Points
**Version:** 1.0.0 (Stable)  
**Author:** Kirk LaSalle  
**Last Updated:** June 6, 2026  

---

## Table of Contents

1. [Introduction](#1-introduction)
2. [System Requirements](#2-system-requirements)
3. [Installation](#3-installation)
4. [Quick Start](#4-quick-start)
5. [Understanding HCEP Modes](#5-understanding-hcep-modes)
6. [Dashboard Overview](#6-dashboard-overview)
7. [Having a Conversation](#7-having-a-conversation)
8. [Knowledge & Memory](#8-knowledge--memory)
9. [Configuration](#9-configuration)
10. [Troubleshooting](#10-troubleshooting)
11. [Glossary](#11-glossary)

---

## 1. Introduction

HCEP (Human Communication Eye Points) is a desktop application that watches your face through an Xbox 360 Kinect sensor or standard USB webcam and understands *how* you're communicating — not just what you say, but what your eyes reveal about your cognitive and emotional state.

HCEP implements Kirk LaSalle's HCEP (Human Communication Eye Points) theory: a classification of five fundamental communication modes that people naturally cycle through during face-to-face conversation — **Logic, Affect, Spirit, Heart,** and **Think**.

The application:
- Tracks your face, eyes, and body in real-time at 30 frames per second
- Classifies which of the 5 HCEP modes you're in right now
- Recognizes who you are across sessions
- Listens to what you say via speech recognition
- Responds through an AI assistant that adapts its style to your current mode

---

## 2. System Requirements

### Development Reference System

HCEP is developed and tested on the following system:

| Component | Specification |
|---|---|
| **Processor** | Intel Core i5 |
| **System RAM** | 32 GB |
| **GPU** | NVIDIA GeForce GTX 1050 Ti (4 GB VRAM) |
| **Kinect Sensor** | Xbox 360 Kinect (v1) via USB 2.0 + power adapter |
| **Display** | 1920 × 1080 |

### Storage Layout

HCEP uses a multi-drive architecture with separate SSDs for isolation and performance:

| Drive | Purpose | Contents |
|---|---|---|
| **System SSD** | Windows OS | Operating system, drivers, Kinect SDK |
| **Programs SSD** | Installed applications | .NET 9.0 SDK/Runtime, Ollama, Visual Studio, development tools |
| **Projects SSD** | Source code & build output | `D:\Projects\HCEP\` — solution, source, tests, build artifacts |
| **Remote / Network Drive** | Data & models | LLM models (Ollama library), Whisper model (`ggml-base.en.bin`), ArcFace ONNX model, datasets, logs archive |

> **Tip:** Keeping models on a remote/network drive saves SSD space. HCEP loads models into RAM/VRAM at startup, so network latency only affects initial load time, not runtime performance.

### Minimum Hardware Requirements

| Component | Minimum | Notes |
|---|---|---|
| **Kinect Sensor** | Xbox 360 Kinect (v1) | Required for live sensor input |
| **USB** | USB 2.0 port | Avoid USB hubs; dedicated controller preferred |
| **Processor** | Intel i5 / AMD Ryzen 5 | Multi-core required for parallel pipeline |
| **Memory** | 8 GB RAM | 16 GB+ recommended for Ollama + ONNX models |
| **GPU** | Any DirectX 11 GPU | NVIDIA with 4 GB+ VRAM recommended for Ollama GPU inference |
| **Disk** | 2 GB free (source + build) | + 10 GB for models if stored locally |

### Software

| Component | Requirement |
|---|---|
| **OS** | Windows 10 (64-bit) or Windows 11 |
| **.NET Runtime** | .NET 9.0 Desktop Runtime (x64) |
| **Kinect SDK** | Kinect for Windows SDK v1.8 |
| **Kinect Toolkit** | Kinect for Windows Developer Toolkit v1.8 |

### Optional (for AI features)

| Component | Purpose |
|---|---|
| **Ollama** | Local AI inference (free, private) — GPU accelerated on GTX 1050 Ti |
| **OpenAI API key** | Cloud AI inference (GPT-5-mini) |
| **Whisper model** | Speech recognition (`ggml-base.en.bin`, ~140 MB) |
| **ArcFace model** | Face recognition (`arcfaceresnet100-11-int8.onnx`, ~120 MB) |

---

## 3. Installation

### Step 1: Install Prerequisites

1. **Install .NET 9.0 Desktop Runtime (x64)**
   - Download from: https://dotnet.microsoft.com/download/dotnet/9.0
   - Select: ".NET Desktop Runtime 9.0.x — Windows x64"
   - Run the installer

2. **Install Kinect for Windows SDK v1.8**
   - Download from: https://www.microsoft.com/en-us/download/details.aspx?id=40278
   - Run `KinectSDK-v1.8-Setup.exe`
   - Restart your computer if prompted

3. **Install Kinect Developer Toolkit v1.8**
   - Download from: https://www.microsoft.com/en-us/download/details.aspx?id=40276
   - Run the installer

### Step 2: Install HCEP

1. Extract `HCEP-v0.1.0.zip` to a folder (e.g., `C:\HCEP`)
2. Or, if building from source:
   ```
   cd D:\Projects\HCEP
   dotnet build HCEP.sln -c Release
   ```

### Step 3: Connect Kinect

1. Plug the Kinect sensor into the Xbox 360 Kinect USB + power adapter
2. Connect the USB cable to your PC
3. Wait for Windows to recognize the device (green light on Kinect)
4. Verify in Device Manager: "Kinect for Windows" entries appear under:
   - Audio devices
   - Cameras
   - Kinect for Windows

### Step 4: Install AI Models (Optional)

**For local AI (Ollama):**
```powershell
# Install Ollama from https://ollama.com
# Then pull the default model:
ollama pull llama3:8b
```

**For cloud AI (GPT-5-mini):**
- Set environment variable: `OPENAI_API_KEY=sk-your-key-here`

**For speech recognition:**
- Download `ggml-base.en.bin` from the Whisper.net releases
- Place in: `models/ggml-base.en.bin` (next to the executable)

**For face recognition:**
- Download `arcfaceresnet100-11-int8.onnx` from the ONNX Model Zoo
- Place in: `models/arcfaceresnet100-11-int8.onnx`

---

## 4. Quick Start

### With Kinect

1. Ensure Kinect is connected (green light)
2. Launch `HCEP.App.exe` (or run `dotnet run --project src/HCEP.App`)
3. Stand 1.5–3 meters in front of the Kinect
4. Look at the sensor — the dashboard shows your HCEP mode
5. Speak — your words appear in the transcript
6. The AI responds based on your current communication mode

### Without Kinect (Simulated Mode)

1. Launch the app without Kinect connected
2. HCEP auto-detects the absence and uses `SimulatedSensorSource`
3. Synthetic data flows through the pipeline at 30fps
4. Use this mode for testing the AI and dashboard features

---

## 5. Understanding HCEP Modes

### What is HCEP?

HCEP (Human Conversation Eye Points) is Kirk LaSalle's theory that eye contact patterns during conversation reveal five distinct communication modes. The system classifies which mode you're in based on where you look, how you move your eyes, and your facial expressions.

### The 5 Modes

#### LOGIC Mode
- **Eye Pattern:** Structured, engaged gaze staying on the face
- **What it means:** You're thinking analytically, processing facts, working through a problem
- **AI responds with:** Clear, precise, factual information — numbered lists, direct answers
- **Example:** "How does the gaze estimation algorithm work?"

#### AFFECT Mode
- **Eye Pattern:** Social Triangle — eyes cycle between the other person's eyes and mouth
- **What it means:** You're emotionally engaged, feeling something about the conversation
- **AI responds with:** Warm, empathetic language — acknowledges feelings first, then content
- **Example:** "I'm really excited about this project!"

#### SPIRIT Mode
- **Eye Pattern:** Sustained deep mutual gaze, high confidence, minimal saccades
- **What it means:** Deep authentic connection — genuine rapport, vulnerability
- **AI responds with:** Personal, genuine, unstructured conversation — no bullet points, just real talk
- **Example:** Sharing something meaningful during deep conversation

#### HEART Mode
- **Eye Pattern:** Lower-face attention, empathic facial markers
- **What it means:** You're in an empathic space — caring, supportive, nurturing
- **AI responds with:** Supportive, validating language — "I hear you," "That makes sense"
- **Example:** Listening to someone share a difficult experience

#### THINK Mode
- **Eye Pattern:** Gaze aversion, defocused eyes, looking away
- **What it means:** Internal processing — you're thinking, remembering, constructing ideas
- **AI responds with:** Brief, non-intrusive responses — gives you space to think
- **Example:** Pausing to recall a memory or work through a complex thought

### Mode Transitions

Modes change naturally as conversation flows. You might start in LOGIC (discussing a problem), shift to AFFECT (feeling frustrated), pass through THINK (pondering a solution), and arrive at SPIRIT (sharing a breakthrough moment).

HCEP uses temporal hysteresis (a 5-frame stability window) to prevent noisy flickering between modes. A new mode must be detected consistently for approximately 170ms before the system transitions.

---

## 6. Dashboard Overview

The HCEP dashboard is a dark-themed WPF window with a 3-column resizable layout. All panel boundaries can be dragged to resize using the visible grip handles between columns and rows.

### Header Bar
- **Application title:** "◉ HCEP — Human Communication Eye Protocol"
- **Window launchers:** Sensor Streams, Kinect Video
- **Full Body toggle:** Switches between 20-joint full-body and 10-joint seated skeleton tracking
- **Sensor status:** Green dot = connected, Red = disconnected

### Left Column — Sensor Feed
- Live RGB video from the Kinect camera (640×480 @ 30fps)
- **Skeleton wireframe overlay:** Green lines connecting 20 joints (solid = tracked, dashed = inferred)
- **Posture label:** "STANDING" / "SITTING (full)" / "UPPER BODY" near hip center
- **Face bounding box:** Yellow rectangle around detected face
- **Face wireframe:** 87-point facial feature mesh (eyes, eyebrows, lips, jawline, nose)
- **Pupil markers:** Magenta dots marking pupil positions

### Center Column — HCEP Analysis
- **HCEP Mode:** Current mode name (LOGIC / AFFECT / SPIRIT / HEART / THINK) with confidence percentage bar
- **State Grid:** Gaze Region, Cognitive State, and Emotional Valence indicators
- **Gaze Visualization:** Interactive face schematic panel showing:
  - Face oval with eyes, nose, mouth, and gaze region dots
  - Active region highlighted in accent color
  - Gaze crosshair/target indicator
  - Mode bar showing current HCEP mode with color coding
- **Info Panel (2-column):**
  - Tracking: identity, state, distance
  - Head Pose: pitch, yaw, roll in degrees
  - Action Units: 6 bar charts (lip raise, jaw lower, lip corner, brow lower, brow raise, outer brow raise)
  - Gaze: region, confidence, direction vector
  - Skeleton: joint count, body state
- **Speech Log:** Real-time transcript of recognized speech

### Right Column — Metrics & AI
- **Pipeline Metrics:** FPS, vision latency (ms), tracked persons, audio beam angle
- **LLM Assistant:** Chat interface for conversing with the AI; responses adapt to your current HCEP mode

### Status Bar
- Pipeline status message
- Kinect tilt angle
- Copyright notice

---

## 7. Having a Conversation

### Natural Conversation
Simply talk naturally while facing the Kinect. HCEP listens through Whisper speech recognition and responds through the AI engine. The AI adapts its conversation style in real-time to match your HCEP mode.

### Typed Input
You can also type in the chat panel. The AI still uses your current HCEP mode (from gaze analysis) to modulate its response style.

### Multi-Person
Kinect v1 supports tracking up to 2 people simultaneously. Each person gets their own identity, knowledge store, and HCEP mode tracking.

---

## 8. Knowledge & Memory

### How HCEP Remembers

HCEP maintains a knowledge store for each recognized person:

- **Sightings:** When and how often you've been seen
- **Utterances:** What you've said (with timestamps)
- **Exchanges:** Full conversation turns (your input + AI response)
- **Custom facts:** Information the AI learns about you (name, preferences, interests)

### Persistence

Knowledge is saved to JSON files and persists across sessions. When you return, HCEP recognizes your face and loads your history, allowing the AI to reference previous conversations.

### Strategy D (UKS Integration)

If BrainSim III's Universal Knowledge Store (UKS) is available, HCEP uses it for richer knowledge graph capabilities. Otherwise, it seamlessly falls back to an in-memory store with JSON persistence. This is transparent — you don't need to configure anything.

---

## 9. Configuration

### Environment Variables

| Variable | Purpose | Default |
|---|---|---|
| `OPENAI_API_KEY` | GPT-5-mini API key | (none — cloud AI disabled) |
| `HCEP_UKS_PATH` | Path to UKS.dll | (auto-detect) |
| `OLLAMA_HOST` | Ollama server URL | `http://localhost:11434` |

### LLM Settings

| Setting | Default | Description |
|---|---|---|
| Local model | `llama3:8b` | Ollama model for THINK/LOGIC modes |
| Cloud model | `gpt-5-mini` | OpenAI model for SPIRIT/AFFECT modes |
| Agentic steps | 5 | Maximum reasoning steps per query |
| Local timeout | 3 seconds | Failover threshold to cloud |

### Gaze Settings

| Setting | Default | Description |
|---|---|---|
| Cone radius | 5 cm | Confidence cone size for gaze target |
| Stability frames | 5 | Frames required to confirm mode transition |
| Min confidence | 0.4 | Minimum confidence for mode transition |

---

## 10. Troubleshooting

### Kinect Not Detected

**Symptoms:** App shows "Simulated" mode, no live video

**Solutions:**
1. Check USB connection — green light should be on
2. Check Device Manager for Kinect entries
3. Reinstall Kinect SDK v1.8
4. Try a different USB port (avoid USB hubs)
5. Ensure the Kinect power supply is connected

### No Speech Recognition

**Symptoms:** You speak but nothing appears in transcript

**Solutions:**
1. Verify `ggml-base.en.bin` is in the `models/` directory
2. Check that the Kinect audio array is not muted in Windows Sound settings
3. Speak clearly at a normal volume, 1.5–3 meters from the sensor
4. Check logs for Whisper initialization errors

### Face Not Recognized

**Symptoms:** Face tracked but shows "Unknown"

**Solutions:**
1. Verify `arcfaceresnet100-11-int8.onnx` is in the `models/` directory
2. Ensure good lighting (avoid backlight)
3. Face the Kinect directly
4. The first time you're seen, you're enrolled as "Unknown" — speak your name for the AI to learn it

### LLM Not Responding

**Symptoms:** Chat input accepted but no AI response

**Solutions:**
1. **For local AI:** Verify Ollama is running (`ollama list` in terminal)
2. **For cloud AI:** Verify `OPENAI_API_KEY` environment variable is set
3. Check logs for HTTP errors or timeouts
4. Verify internet connectivity (for cloud AI)

### App Crashes on Startup

**Solutions:**
1. Verify .NET 9.0 Desktop Runtime (x64) is installed
2. Run from command line to see error output: `dotnet run --project src/HCEP.App`
3. Check `logs/` directory for Serilog output
4. Ensure running on Windows 10/11 x64

### High Latency / Low FPS

**Solutions:**
1. Close other applications using the Kinect
2. Ensure adequate lighting (IR sensor works better with some ambient light)
3. For local AI: ensure Ollama has GPU access
4. Check Windows Task Manager for CPU/memory pressure

---

## 11. Glossary

| Term | Definition |
|---|---|
| **HCEP** | Human Conversation Eye Points — Kirk LaSalle's theory of eye contact communication modes |
| **HCEP** | Human Communication Eye Protocol — this application |
| **Gaze Region** | One of 13 classified areas where a person is looking (LeftEye, RightEye, NasalBridge, Mouth, Forehead, Chin, PeripheralLeft, PeripheralRight, Above, Below, Defocused, FaceCenter, Unknown) |
| **Social Triangle** | Gaze pattern cycling between both eyes and mouth, associated with AFFECT mode |
| **Saccade** | Rapid eye movement between fixation points |
| **Confidence Cone** | Geometric cone projected from the eyes used to classify gaze targets |
| **PnP** | Perspective-n-Point — algorithm for estimating head pose from face landmarks |
| **Agentic** | LLM capability to use tools and reason in multiple steps |
| **UKS** | Universal Knowledge Store — BrainSim III's knowledge graph component |
| **Strategy D** | HCEP's hybrid approach: try UKS, fallback to in-memory store |
| **Temporal Hysteresis** | Requiring consistent detection over multiple frames before changing state |
| **Action Unit (AU)** | Facial muscle movement unit from the Facial Action Coding System |
| **Ollama** | Open-source local LLM inference engine |
| **ArcFace** | ONNX-based deep learning face recognition model |
| **Whisper** | OpenAI's speech-to-text model, run locally via Whisper.net |

---

*Copyright © 2026 Kirk LaSalle. All rights reserved.*
