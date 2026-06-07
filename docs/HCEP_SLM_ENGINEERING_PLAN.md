# HCEP Local SLM Agentic Integration & Engineering Plan
**Document Type:** Technical Architecture & Implementation Plan  
**Target Architecture:** Local SLM (Small Language Model) Controller & Kinect v1 Integration  
**Focus:** Privacy-First Local Learning, Ocular-Skeletal Math Foundations  

---

## 1. Architectural Philosophy: Math-Driven, Local-Only

To ensure absolute privacy ("only track me for learning") while achieving high-speed interactive responsiveness, HCEP must shift its intelligence engine from cloud LLMs to **on-device Small Language Models (SLMs)**. The system must operate as a closed feedback loop where raw mathematical sensor data directly drives the SLM's cognitive behavior, and the SLM in turn acts as the active controller for avatar animations and conversational flow.

```
  ┌────────────────────────────────────────────────────────┐
  │                   LOCAL PHYSICAL SENSORS               │
  │     Kinect v1 Skeleton (30Hz)  +  Webcam Face Mesh     │
  └───────────────────────────┬────────────────────────────┘
                              ▼
  ┌────────────────────────────────────────────────────────┐
  │                 OCULO-POSTURAL VECTOR (OPV)            │
  │     Deterministic Geometric & Kinematic Formulations    │
  └───────────────────────────┬────────────────────────────┘
                              ▼
  ┌────────────────────────────────────────────────────────┐
  │               LOCAL SLM CONTROLLER (SLOW LOOP)         │
  │   Phi-3-mini / Llama-3.1-8B (Ollama or ONNX Runtime)   │
  └───────────────────────────┬────────────────────────────┘
                              ▼
  ┌────────────────────────────────────────────────────────┐
  │                 LEARNING & STATE GRAPH                 │
  │   Strategy D InMemoryKnowledgeStore (Local JSON Serialization)│
  └────────────────────────────────────────────────────────┘
```

---

## 2. Sensor Math & Feature Engineering

Before the SLM receives any data, the sensor stream from the Kinect and camera is formalized into a deterministic, normalized **Oculo-Postural Vector (OPV)**. This ensures that the system is driven by explicit physics and geometry, not heuristic guesses.

### 2.1 Postural Physics Formulations
Given the Kinect's 3D joint coordinate outputs (represented as $(x, y, z)$ in meters):
* $J_{head}$: Coordinates of the head center.
* $J_{spine}$: Coordinates of the spine center.
* $J_{shoulder\_c}$: Coordinates of the shoulder center (clavicle).
* $J_{hip\_c}$: Coordinates of the hip center.

We define the **Torso Lean Angle ($\theta_{lean}$)** as the pitch of the torso vector relative to the vertical gravity axis $\vec{u}_{g} = [0, 1, 0]^T$:

$$\vec{V}_{torso} = J_{shoulder\_c} - J_{hip\_c}$$

$$\theta_{lean} = \arccos\left(\frac{\vec{V}_{torso} \cdot \vec{u}_{g}}{\|\vec{V}_{torso}\|}\right)$$

A forward lean ($\theta_{lean} > 0$ relative to rest state) indicates high attention/empathy; a backward lean ($\theta_{lean} < 0$) indicates cognitive deflection or retreat.

### 2.2 Saccadic Metrics & Blink Frequency
* **Peak Saccadic Velocity ($V_{peak}$):** Derived from sequential frame delta displacements of pupil center vectors:
  $$V_{peak} = \max \left( \frac{\|\vec{P}_{pupil}(t) - \vec{P}_{pupil}(t-\Delta t)\|}{\Delta t} \right)$$
* **Blink Rate ($f_{blink}$):** Calculated as blinks per minute over a sliding 60-second window, used as a direct proxy for cognitive load and fatigue.

### 2.3 The Compiled Oculo-Postural Vector (OPV)
Every $100\text{ms}$, the system compiles the normalized vector:

$$\mathbf{v}_{OPV} = \left[ \theta_{lean}, \text{yaw}_{head}, \text{pitch}_{head}, \text{dist}_{user}, f_{blink}, \text{dwell}_{zone}, \Delta_{vergence} \right]$$

---

## 3. Local SLM Driver Loop Integration

Instead of sending the raw coordinates to the SLM, we serialize $\mathbf{v}_{OPV}$ into a structured semantic format and inject it directly into the local model's system prompt context. 

### 3.1 Targeted SLM Selection
We leverage **Microsoft Phi-3-mini (3.8B parameters)** or **Llama-3.1-8B-Instruct**, quantized to 4-bit (GGUF or ONNX Runtime). These models:
* Run at $>45$ tokens/second on standard local hardware (RTX 3060/4060 or Apple Silicon).
* Native support for structured JSON schema outputs and function calling.
* Extremely low context-switch latency.

### 3.2 System Prompt Integration Pattern
On every conversational step, the prompt builder compiles the system context as:

```json
{
  "system_directives": "You are the cognitive driver of the HCEP avatar. You must adjust your tone, vocabulary, and avatar animation triggers based on the user's current physical state.",
  "user_postural_state": {
    "torso_pitch_deg": 8.4,
    "head_orientation": { "yaw": -2.1, "pitch": 5.0 },
    "dwell_mode": "Logic",
    "gaze_stability_score": 0.85,
    "blink_frequency_bpm": 12.0
  }
}
```

### 3.3 Agentic Output Model (JSON Driving Format)
The local SLM is constrained to output responses in JSON format, containing both the **verbal response** and the **avatar behavioral drivers**:

```json
{
  "thought_process": "User is leaning forward (engaged) and dwelling on LOGIC zone (Left Eye). Factual, structured response required.",
  "verbal_response": "The current calibration parameters are locked to x64. Here is the mathematical formula...",
  "avatar_behavior": {
    "target_gaze_mode": "Logic",
    "saccadic_variance": 0.15,
    "blink_sync_trigger": true,
    "micro_saccade_frequency_hz": 1.2
  }
}
```

---

## 4. Local-Only Learning & Adaptation Loop

To satisfy "only track me for learning", no data must leave the machine. Adaptation occurs inside the local **Strategy D** storage framework.

```
                     ┌───────────────────────┐
                     │   Oculo-Postural      │
                     │    Vector (OPV)       │
                     └───────────┬───────────┘
                                 ▼
                     ┌───────────────────────┐
                     │   Session Clustering  │
                     │    (Locally Saved)    │
                     └───────────┬───────────┘
                                 ▼
                     ┌───────────────────────┐
                     │  Ollama SLM Prompt    │
                     │    Weights Update     │
                     └───────────────────────┘
```

1. **User Profile Clustering:** The system logs the history of $\mathbf{v}_{OPV}$ centroids during sessions. Using a lightweight, local clustering algorithm (e.g., K-Means in C#), the system defines a baseline "rest state" and "focused state" unique to Kirk LaSalle.
2. **Local Session Knowledge Storage:** The learning parameters (e.g., typical blink rates, favorite eye point zones, and vocabulary preferences) are serialized and written directly to local JSON storage via `InMemoryKnowledgeStore`.
3. **Adaptive Prompt Weighting:** During initialization, `InMemoryKnowledgeStore` resolves past interaction logs and dynamically compiles a personalized prefix containing user preference vectors.

---

## 5. Somatic Emulation & Mirroring Loop (Autonomous AI Reflection)

To make the AI avatar fully autonomous and decoupled while allowing it to actively mirror or emulate the user for rapport, HCEP introduces the **Somatic Emulation & Mirroring Loop**. Instead of the user's movements simply acting as a passive input, the local SLM uses the user's oculo-postural vector (OPV) to drive the avatar's physical animation parameters—mimicking, echoing, or playfully "mocking" the user's states in real-time.

### 5.1 Mathematical Mimicry Formulation
The avatar's target physical posture vector $\mathbf{v}_{avatar}(t)$ is calculated as a blend between its autonomous baseline posture $\mathbf{v}_{baseline}$ and the user's postural vector $\mathbf{v}_{user}$ at a specific delay offset $\tau$:

$$\mathbf{v}_{avatar}(t) = (1 - w_{emu}) \cdot \mathbf{v}_{baseline} + w_{emu} \cdot \mathbf{v}_{user}(t - \tau)$$

Where:
* **$w_{emu} \in [0, 1]$ (Emulation Blend Weight):** Controls the scale of the mirroring. A weight of `0.0` represents a completely decoupled, autonomous AI posture; a weight of `1.0` is an absolute real-time shadow mirror of the user's physical joints.
* **$\tau$ (Temporal Offset):** Defines the reflection lag (latency). 
  * *Empathy/Rapport Mode ($\tau \approx 300\text{ms} - 500\text{ms}$):* Subtle, delayed mirroring of posture (leaning, tilting) which is proven to build trust in human psychology.
  * *Mimicry/Mocking Mode ($\tau \le 100\text{ms}$):* Rapid, synchronized copying of the user's head tilts, blinks, and gaze aversion zones.

### 5.2 SLM Control Interface
The local SLM updates the mirroring dynamics dynamically via its JSON control blocks:

```json
"avatar_behavior": {
  "target_gaze_mode": "Affect",
  "emulation_blend_weight": 0.70,
  "reflection_delay_ms": 350,
  "sync_blinks_to_user": true
}
```

This ensures the AI remains the **sole driver** of its physical presence, actively deciding when to remain decoupled and when to use the user's actions for fast physical reflection.

---

## 6. Phase-by-Phase Engineering Work Plan

### Phase 1: Local SLM Engine Setup (Q3 2026)
* **Goal:** Configure local SLM execution to bypass external API networks.
* **Actions:**
  1. Add a local controller option inside `HybridLlmEngine` to force routing through `Ollama` (`llama3:8b` or `phi3:mini`).
  2. Implement a JSON parsing pipeline that extracts the `verbal_response` and `avatar_behavior` properties from model outputs.
  3. Write fallback handlers if local LLM latency spikes above $2000\text{ms}$.

### Phase 2: Kinect Mathematical Vectorization (Q3 2026)
* **Goal:** Implement the physical coordinate calculations from the active Kinect stream.
* **Actions:**
  1. Update `HCEP.Kinect` to read 3D joints (`JointType.Spine`, `JointType.ShoulderCenter`, `JointType.HipCenter`).
  2. Implement the pitch, yaw, and distance equations in `HCEP.Spatial` to generate the normalized `Oculo-Postural Vector (OPV)`.
  3. Build a debug view overlay inside `VideoOverlayControl` showing torso angles and real-time head yaw/pitch metrics.

### Phase 3: Somatic Mirroring and Animation Blending (Q4 2026)
* **Goal:** Implement the real-time posture replication equations.
* **Actions:**
  1. Update `AvatarCoreControl.xaml.cs` to store a history buffer of user skeletal and eye positions (holding the last $1000\text{ms}$ of frames).
  2. Implement the linear interpolation equation to blend the avatar's target coordinates using the SLM's `emulation_blend_weight` and `reflection_delay_ms`.
  3. Validate that blink events are mirrored or synchronized when `sync_blinks_to_user` is flagged.

### Phase 4: Local Learning & Adaptation Loop (Q4 2026)
* **Goal:** Implement the local storage and learning heuristics.
* **Actions:**
  1. Extend `UksKnowledgeAdapter` (or `InMemoryKnowledgeStore`) to serialize user baseline profiles (averages of blink frequency, lean offsets, and preferred HCEP modes).
  2. Write an active optimizer in `HCEP.Core` that reads these baselines on startup to dynamically adjust the spatial confidence cones (e.g., widening targets if the user exhibits higher natural drift).
  3. Validate that no network requests are dispatched during learning profiling.

