# HCEP — Developer Guide

**Product:** HCEP — Human Communication Eye Protocol  
**Version:** 0.1.0 (Alpha)  
**Author:** Kirk LaSalle  
**Last Updated:** February 23, 2026  

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Project Structure](#2-project-structure)
3. [Building from Source](#3-building-from-source)
4. [Running Tests](#4-running-tests)
5. [Layer-by-Layer Deep Dive](#5-layer-by-layer-deep-dive)
6. [Data Flow & Pipeline](#6-data-flow--pipeline)
7. [Extending HCEP](#7-extending-HCEP)
8. [Key Design Decisions](#8-key-design-decisions)
9. [Coding Conventions](#9-coding-conventions)
10. [Dependency Management](#10-dependency-management)
11. [Debugging Tips](#11-debugging-tips)
12. [Contributing](#12-contributing)

---

## 1. Architecture Overview

HCEP follows a layered architecture with strict dependency flow (bottom → top):

```
┌─────────────────────────────────────────────────────────────────┐
│                          HCEP.App (WPF)                        │
│          Presentation · DI Host · Pipeline Orchestrator        │
├───────────────────────┬─────────────────────────────────────────┤
│   HCEP.Intelligence   │          HCEP.Knowledge                │
│    Hybrid LLM Engine  │   UKS Adapter · In-Memory Store       │
│    Agentic Tool Loop  │   Person Knowledge Manager            │
├───────────────────────┼─────────────────────────────────────────┤
│   HCEP.Vision         │          HCEP.Audio                    │
│    ArcFace · HCEP     │   Whisper · Audio Pipeline            │
│    Mode Analyzer      │                                       │
├───────────────────────┼─────────────────────────────────────────┤
│   HCEP.Spatial        │          HCEP.Kinect                   │
│    Gaze Estimation    │   Kinect v1 · Simulated Source        │
├───────────────────────┴─────────────────────────────────────────┤
│   HCEP.Core (Enums · Models · Interfaces · Channels)           │
├─────────────────────────────────────────────────────────────────┤
│   HCEP.Telemetry (Serilog · Metrics · FPS)                     │
└─────────────────────────────────────────────────────────────────┘
```

### Design Principles

- **Interface-driven:** Every major capability is behind an interface in HCEP.Core
- **Channel-based pipeline:** `System.Threading.Channels` for backpressure-aware async data flow
- **Graceful degradation:** Every component handles missing hardware/models/services
- **Late binding:** UKS adapter uses reflection to avoid hard DLL dependencies
- **DI-first:** All wiring through `Microsoft.Extensions.Hosting`

---

## 2. Project Structure

```
D:\Projects\HCEP\
├── HCEP.sln                         # Root solution (11 projects)
├── Directory.Build.props            # Shared MSBuild properties
├── docs/                            # Documentation
│   ├── PRD.md
│   ├── ROADMAP.md
│   ├── USER_GUIDE.md
│   └── DEVELOPER_GUIDE.md
├── src/
│   ├── HCEP.Core/                   # Enums, models, interfaces, channels
│   │   ├── Enums/                   # HcepMode, GazeRegion, CognitiveState, etc.
│   │   ├── Models/                  # TrackedPerson, FaceFrame, HcepReading, etc.
│   │   ├── Interfaces/             # ISensorSource, IGazeEstimator, ILlmEngine, etc.
│   │   └── Channels/              # HCEPChannels factory
│   ├── HCEP.Telemetry/             # Logging, metrics, FPS
│   ├── HCEP.Spatial/               # Gaze estimation, coordinate mapping, PnP
│   ├── HCEP.Kinect/                # Sensor access (real + simulated)│   ├── HCEP.Kinect.Bridge/         # .NET Framework 4.8.1 bridge for managed Kinect SDK│   ├── HCEP.Vision/                # Face recognition, HCEP mode analysis
│   ├── HCEP.Audio/                 # Speech recognition, audio pipeline
│   ├── HCEP.Knowledge/             # Knowledge store, UKS adapter, person knowledge
│   ├── HCEP.Intelligence/          # LLM engine, agentic tools, prompt bridge
│   └── HCEP.App/                   # WPF application, DI host, orchestrator
└── tests/
    └── HCEP.Tests/                  # xUnit test project (102 tests)
        ├── Spatial/                 # RayPlane, CoordinateMapper, PnP, ConfidenceCone tests
        ├── Knowledge/               # InMemoryStore, UKS adapter, PersonKnowledge tests
        ├── Intelligence/            # AgenticTools, PromptBridge, ToolDefinitions tests
        ├── Vision/                  # HcepModeAnalyzer tests
        └── Core/                    # Model/enum tests
```

---

## 3. Building from Source

### Prerequisites

| Tool | Version | Installation |
|---|---|---|
| .NET SDK | 9.0.311+ | https://dotnet.microsoft.com/download/dotnet/9.0 |
| Kinect SDK v1.8 | 1.8 | https://www.microsoft.com/en-us/download/details.aspx?id=40278 |
| Kinect Developer Toolkit | 1.8 | https://www.microsoft.com/en-us/download/details.aspx?id=40276 |
| Visual Studio 2022 | 17.x | (Optional, for IDE experience) |

### Build Commands

```powershell
# Clone / navigate to project
cd D:\Projects\HCEP

# Restore NuGet packages
dotnet restore HCEP.sln

# Build all projects (Debug, x64)
dotnet build HCEP.sln

# Build Release
dotnet build HCEP.sln -c Release

# Run the application
dotnet run --project src/HCEP.App
```

### Directory.Build.props

All projects inherit shared settings from the root `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net9.0-windows</TargetFramework>
    <Platform>x64</Platform>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <Version>0.1.0</Version>
    <Copyright>Copyright © 2026 Kirk LaSalle</Copyright>
  </PropertyGroup>
</Project>
```

**Important:** The `x64` platform lock is required because Kinect v1 native DLLs are 64-bit only.

---

## 4. Running Tests

```powershell
# Run all tests
dotnet test HCEP.sln

# Run with detailed output
dotnet test HCEP.sln --verbosity normal

# Run specific test class
dotnet test HCEP.sln --filter "FullyQualifiedName~RayPlaneTests"

# Run with coverage (coverlet)
dotnet test HCEP.sln --collect:"XPlat Code Coverage"

# Run tests in a specific category
dotnet test HCEP.sln --filter "FullyQualifiedName~Knowledge"
```

### Test Organization

| Directory | Tests | What's Tested |
|---|---|---|
| `Spatial/` | 23 | Ray-plane math, coordinate mapping, PnP head pose, confidence cone |
| `Knowledge/` | 27 | In-memory store CRUD, UKS fallback, person knowledge, JSON roundtrip |
| `Intelligence/` | 20 | Agentic tool executor, prompt bridge, tool definitions |
| `Vision/` | 8 | HCEP mode analyzer state machine |
| `Core/` | 24 | Models, enums, constants, anthropometrics |
| **Total** | **102** | All passing |

### Writing New Tests

Follow the existing conventions:
- One test class per production class
- Tests organized in directories mirroring `src/` structure
- Use `[Fact]` for single-case tests, `[Theory]` for parameterized tests
- Naming: `MethodName_Scenario_ExpectedResult`
- Tests must be deterministic — no network, no Kinect, no file system (except temp)

```csharp
public class MyNewTests
{
    [Fact]
    public void Solve_WithValidInput_ReturnsExpectedResult()
    {
        // Arrange
        var sut = new MyClass();

        // Act
        var result = sut.Solve(input);

        // Assert
        Assert.Equal(expected, result, precision: 4);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 4)]
    public void Square_ReturnsCorrectValue(double input, double expected)
    {
        Assert.Equal(expected, MyMath.Square(input));
    }
}
```

---

## 5. Layer-by-Layer Deep Dive

### 5.1 HCEP.Core

The foundation layer. Zero external dependencies. Defines:

**Enums:**
- `HcepMode` — LOGIC, AFFECT, SPIRIT, HEART, THINK (the 5 HCEP modes)
- `GazeRegion` — 13 gaze targets (LeftEye, RightEye, NasalBridge, Mouth, Forehead, Chin, PeripheralLeft, PeripheralRight, Above, Below, Defocused, FaceCenter, Unknown)
- `CognitiveState` — 12 cognitive classifications
- `TrackingState`, `SensorState`, `SaccadePhase`, `ActionUnit` (6 AUs)

**Models:**
- `HcepReading` — Sealed record, the primary output of the HCEP analyzer. Contains mode, gaze region, confidence, cognitive state, emotional valence. `HcepReading.Empty` provides a null-safe default.
- `TrackedPerson` — Person identity with `State` property (type `TrackingState`)
- `FaceFrame` — 87+ feature points, 6 AUs, pupil indices 69/73, `CyclopeanPoint3D`
- `Anthropometrics` — Constants: IPD=63mm, Sellion-Menton=118mm, Eye Depth=12mm, canonical face model (6 3D points)
- `SceneSnapshot` — Aggregates all sensor data for one frame
- `LlmExchange` — Input/output/model record for conversation history

**Interfaces (the contracts):**
- `ISensorSource` — `StartAsync()`, `StopAsync()`, sensor state events
- `IGazeEstimator` — `Estimate(FaceFrame, SkeletonFrame) → GazeEstimate`
- `IHcepAnalyzer` — `Analyze(SceneSnapshot) → HcepReading`
- `IFaceRecognizer` — `RecognizeAsync(image) → TrackedPerson`
- `ISpeechRecognizer` — `RecognizeAsync(audio) → SpeechResult`
- `ILlmEngine` — `GenerateAsync(prompt, context) → LlmExchange`
- `IKnowledgeStore` — `AssertAsync`, `QueryAsync`, `RetractAsync`, `SummarizeAsync`, `SaveAsync`, `LoadAsync`
- `IPipelineOrchestrator` — `StartAsync()`, `StopAsync()`
- `ITelemetryService` — `RecordLatency`, `RecordFrame`, `GetFps`

**Channels:**
- `HCEPChannels` — Factory creating `Channel<T>` instances for inter-layer async data flow

### 5.2 HCEP.Telemetry

Cross-cutting logging and metrics. References `HCEP.Core`.

- `LoggingConfiguration` — Serilog setup (console + file sinks, enrichers)
- `HCEPTelemetryService` — Implements `ITelemetryService`, tracks latency and frame rates
- `FpsCounter` — Sliding-window FPS calculation
- `MovingAverage` — Utility for smoothing noisy signals

### 5.3 HCEP.Spatial

Pure math — gaze geometry, coordinate systems, head pose. References `HCEP.Core`.

- `PnPSolver` — Direct Linear Transform (DLT) for head pose from 6 face landmarks. Requires minimum 4 point correspondences. Returns 3×4 projection matrix, extracts Euler angles.
- `RayPlane` — Ray-plane intersection, point-to-ray distance, angle between vectors. All static methods.
- `ConfidenceCone` — Projects a cone from gaze origin along gaze direction. Classifies which `GazeRegion` the gaze falls in (5cm default radius).
- `CoordinateMapper` — Kinect v1 camera intrinsics. Depth focal length=525, color focal=531.15, principal point=(320, 240). Methods: `ProjectToDepth`, `DepthToCamera`, `ProjectToColor`, `Distance`, `DistanceFromSensor`.
- `ThreeStageGazeEstimator` — Implements `IGazeEstimator`. Pipeline: (1) PnP head pose → (2) Eye-in-head rotation from pupil deltas → (3) Hybrid fusion with EMA smoothing.

### 5.4 HCEP.Kinect

Sensor abstraction. References `HCEP.Core`.

- `KinectSensorSource` — Implements `ISensorSource`. Initializes Kinect v1 sensor via native COM interop (Kinect10.dll, FaceTrackLib.dll). Supports full-body (20-joint) and seated (10-joint) tracking modes switchable at runtime via `SeatedMode` property. Pushes `SkeletonFrame`, `FaceFrame`, `ColorFrame`, `DepthFrame` events.
- `SimulatedSensorSource` — Implements `ISensorSource`. Generates synthetic face, skeleton, depth, and audio frames at 30fps. Used for development and testing without hardware.

### 5.5 HCEP.Vision

Computer vision: face recognition and HCEP classification. References `HCEP.Core`, `HCEP.Spatial`.

- `ArcFaceRecognizer` — Implements `IFaceRecognizer`. Loads ONNX model, extracts 512-dimensional face embeddings, computes cosine similarity for identity matching (threshold > 0.6). Uses `SixLabors.ImageSharp` for image preprocessing.
- `HcepModeAnalyzer` — Implements `IHcepAnalyzer`. The core HCEP implementation:
  - Input: `SceneSnapshot` (gaze, face AUs, speech activity)
  - Output: `HcepReading` (mode, confidence, cognitive state, valence)
  - Temporal hysteresis: `ModeStabilityFrames = 5` (~170ms at 30fps)
  - Transition threshold: `ModeTransitionMinConfidence = 0.4`
  - Mode detection logic:
    - THINK: Gaze aversion (PeripheralLeft/Right, Above, Below, Defocused)
    - AFFECT: Social Triangle (alternating eyes + mouth) 
    - SPIRIT: Sustained high-confidence mutual gaze
    - HEART: Lower-face attention + empathic AU markers
    - LOGIC: Default engaged on-face gaze
- `VisionPipeline` — Orchestrates face detection → recognition → HCEP analysis per frame.

### 5.6 HCEP.Audio

Speech recognition pipeline. References `HCEP.Core`.

- `WhisperSpeechRecognizer` — Implements `ISpeechRecognizer`. Uses Whisper.net 1.8.0 for on-device speech-to-text. Configurable model path (`ggml-base.en.bin`). 16kHz mono float32 input.
- `AudioPipeline` — Manages audio buffer, voice activity detection (energy-based), and chunked submission to Whisper. Uses NAudio 2.2.1 for audio format conversion.

### 5.7 HCEP.Knowledge

Knowledge persistence and person memory. References `HCEP.Core`.

- `InMemoryKnowledgeStore` — Implements `IKnowledgeStore`. Triple store (subject → relation → object). Thread-safe via `ConcurrentDictionary`. JSON persistence uses `string[]` arrays (not tuples) for reliable serialization roundtrip.
- `UksKnowledgeAdapter` — **Strategy D hybrid adapter.** Uses reflection to late-bind to BrainSim III's UKS.dll:
  - Searches: app directory → `lib/` subfolder → `HCEP_UKS_PATH` environment variable
  - Resolves `Thing` and `ModuleUKS` types via reflection
  - If UKS not found: transparent fallback to `InMemoryKnowledgeStore`
  - Implements `IDisposable` for cleanup
- `KnowledgeStoreFactory` — DI extension method `AddHCEPKnowledge()` and static `CreateStandalone()`.
- `PersonKnowledgeManager` — Higher-level API for person-specific knowledge:
  - `RecordSighting(personId, name?)` — Tracks when people are seen
  - `RecordUtterance(personId, text)` — Stores what people say
  - `RecordExchange(personId, exchange)` — Stores conversation turns
  - `GetPersonContext(personId)` — Builds LLM-ready summary

### 5.8 HCEP.Intelligence

Hybrid LLM engine with agentic capabilities. References `HCEP.Core`, `HCEP.Knowledge`.

- `HybridLlmEngine` — Implements `ILlmEngine`. Dual-backend:
  - **Local:** Ollama HTTP API (`http://localhost:11434/api/generate`), default model `llama3:8b`, streaming support
  - **Cloud:** OpenAI-compatible API (`https://api.openai.com/v1/chat/completions`), default model `gpt-5-mini`, agentic tool-use
  - Routing: `HcepPromptBridge.ShouldUseCloud()` decides based on HCEP mode
  - Agentic loop: Up to `MaxAgenticSteps = 5` tool-use iterations per query
  - OpenAI DTOs: `OpenAiRequest`, `OpenAiResponse`, `OpenAiToolCallDto`, `OpenAiToolCallFunction`

- `AgenticToolDefinitions` — Defines 5 HCEP tools in OpenAI function-calling format:
  1. `query_knowledge` — Query the knowledge store (subject, optional relation)
  2. `get_hcep_state` — Get current HCEP mode and gaze info
  3. `store_knowledge` — Assert a fact (subject, relation, object)
  4. `summarize_person` — Get accumulated knowledge about a person
  5. `analyze_gaze_pattern` — Analyze recent gaze behavior
  - Uses `System.Text.Json` source generation (`AgenticJsonContext`)

- `AgenticToolExecutor` — Dispatches tool calls to HCEP services:
  - `ExecuteAsync(toolName, arguments)` → `string` result
  - `UpdateState(HcepReading, TrackedPerson)` — Called per-frame to update cached state
  - Returns JSON-formatted results for the LLM

- `HcepPromptBridge` — Builds mode-specific system prompts:
  - `GenerateContext(HcepReading, string? speech)` → system prompt string
  - `ShouldUseCloud(HcepReading)` → `bool` (SPIRIT/AFFECT/HEART → cloud; THINK/LOGIC → local)
  - Mode-specific instructions: LOGIC gets "concise, factual", SPIRIT gets "genuine, personal"

### 5.9 HCEP.App

WPF desktop application. References all other projects.

- `App.xaml.cs` — `IHost` setup with full DI registration:
  - `services.AddHCEPKnowledge()` (Strategy D from KnowledgeStoreFactory)
  - Registers all interfaces → implementations
  - `KinectSdkAvailable()` check → selects real or simulated sensor source
  - Global exception handlers (DispatcherUnhandledException, TaskScheduler, AppDomain)
- `MainWindow.xaml` — Dark-themed 3-column resizable dashboard:
  - Left: Live video with skeleton/face overlays
  - Center: HCEP mode, state grid, gaze visualization, speech log
  - Right: Pipeline metrics, LLM chat
  - Drag-resizable horizontal and vertical GridSplitters with grip styling
- `MainViewModel` — MVVM ViewModel using CommunityToolkit.Mvvm (`ObservableObject`, `RelayCommand`). Properties for ShowFullSkeleton toggle (controls Kinect SeatedMode at runtime), sensor settings, and all UI bindings.
- `VideoOverlayControl` — Custom `FrameworkElement` rendered via `OnRender`. Draws skeleton wireframe (20 joints, 19 bones with tracked/inferred pen styles), face bounding box, 87-point face wireframe with edge chains, pupil markers. Supports ShowFullSkeleton dependency property. Includes automatic sitting/standing detection based on knee-to-hip Y-delta heuristic.
- `GazeVisualizationControl` — Custom `FrameworkElement` rendered via `OnRender`. Vertical layout: face schematic (oval with eyes, nose, mouth, gaze region dots, crosshair targeting) on top, 2-column info panel (tracking, head pose, action unit bars, gaze stats, skeleton info) on bottom.
- `HcepPipelineOrchestrator` — Implements `IPipelineOrchestrator`. Starts/stops the sensor, reads from channels, dispatches to analyzers, updates UI via `Dispatcher`

---

## 6. Data Flow & Pipeline

### Per-Frame Pipeline (30fps target)

```
1. Kinect / Simulated Source
   └── Produces: ColorFrame, DepthFrame, SkeletonFrame, FaceFrame, AudioFrame
       └── Packed into: SceneSnapshot
           └── Written to: Channel<SceneSnapshot>

2. Pipeline Orchestrator (reads from channel)
   ├── Gaze Estimation (ThreeStageGazeEstimator)
   │   ├── Stage 1: PnPSolver → head pose (yaw, pitch, roll)
   │   ├── Stage 2: Pupil delta → eye-in-head rotation
   │   └── Stage 3: Hybrid fusion → GazeEstimate
   │
   ├── HCEP Analysis (HcepModeAnalyzer)
   │   ├── Input: GazeEstimate + FaceFrame AUs + speech activity
   │   ├── Mode classification (LOGIC/AFFECT/SPIRIT/HEART/THINK)
   │   ├── Temporal hysteresis (5-frame stability)
   │   └── Output: HcepReading
   │
   ├── Face Recognition (ArcFaceRecognizer)
   │   ├── 512-d embedding extraction
   │   ├── Cosine similarity matching
   │   └── Output: TrackedPerson
   │
   ├── Speech Recognition (WhisperSpeechRecognizer)
   │   ├── VAD filtering
   │   ├── Whisper transcription
   │   └── Output: SpeechResult
   │
   └── Telemetry recording

3. Intelligence Layer (on speech input or user query)
   ├── HcepPromptBridge builds mode-aware system prompt
   ├── ShouldUseCloud() → routing decision
   ├── HybridLlmEngine.GenerateAsync()
   │   ├── Local path: Ollama llama3:8b
   │   └── Cloud path: GPT-5-mini with agentic tool loop
   │       ├── Tool call → AgenticToolExecutor.ExecuteAsync()
   │       ├── Tool result → appended to conversation
   │       └── Repeat (up to 5 steps)
   └── Output: LlmExchange → UI + PersonKnowledgeManager

4. UI Update (via Dispatcher)
   ├── HCEP mode display
   ├── Gaze region indicator
   ├── Transcript log
   └── Chat response
```

### Channel Architecture

```csharp
// HCEPChannels creates bounded channels with backpressure
Channel<SceneSnapshot> sceneChannel = HCEPChannels.CreateSceneChannel();
Channel<HcepReading>   readingChannel = HCEPChannels.CreateReadingChannel();
Channel<SpeechResult>  speechChannel = HCEPChannels.CreateSpeechChannel();
```

Channels use `BoundedChannelOptions` with `FullMode.DropOldest` to prevent pipeline stalls if a consumer falls behind.

---

## 7. Extending HCEP

### 7.1 Adding a New HCEP Mode

1. Add the mode to `HcepMode` enum in `HCEP.Core/Enums/HcepMode.cs`
2. Add detection logic in `HcepModeAnalyzer.Analyze()` in `HCEP.Vision/HcepModeAnalyzer.cs`
3. Add mode-specific prompt in `HcepPromptBridge.GenerateContext()` in `HCEP.Intelligence/`
4. Update routing in `HcepPromptBridge.ShouldUseCloud()`
5. Add tests in `tests/HCEP.Tests/Vision/HcepModeAnalyzerTests.cs`
6. Update documentation

### 7.2 Adding a New Agentic Tool

1. Define the tool schema in `AgenticToolDefinitions.cs`:
   ```csharp
   new AgenticTool
   {
       Type = "function",
       Function = new AgenticFunction
       {
           Name = "my_new_tool",
           Description = "What the tool does",
           Parameters = new AgenticParameters
           {
               Type = "object",
               Properties = new Dictionary<string, AgenticProperty>
               {
                   ["param1"] = new() { Type = "string", Description = "..." }
               },
               Required = new[] { "param1" }
           }
       }
   }
   ```

2. Add dispatch case in `AgenticToolExecutor.ExecuteAsync()`:
   ```csharp
   "my_new_tool" => HandleMyNewTool(arguments),
   ```

3. Add tests in `Intelligence/AgenticToolExecutorTests.cs`

### 7.3 Adding a New Sensor Source

1. Implement `ISensorSource` interface from `HCEP.Core`
2. Implement `StartAsync()`, `StopAsync()`, and sensor state management
3. Produce `SceneSnapshot` objects and write to the scene channel
4. Register in DI (`App.xaml.cs`) with appropriate condition
5. Example: Azure Kinect, RealSense, webcam

### 7.4 Adding a New LLM Backend

1. The `HybridLlmEngine` currently supports Ollama (local) and OpenAI-compatible (cloud)
2. To add a new backend:
   - Add a new private method: `Task<LlmExchange> CallNewBackendAsync(string prompt, string context)`
   - Update routing logic in `GenerateAsync()`
   - Add configuration for endpoint URL, model name, API key
3. Or implement a standalone `ILlmEngine` and swap in DI

### 7.5 Adding a New Knowledge Backend

1. Implement `IKnowledgeStore` interface
2. Register via `KnowledgeStoreFactory.AddHCEPKnowledge()` or custom DI
3. The Strategy D pattern: try preferred → fallback to InMemoryKnowledgeStore

---

## 8. Key Design Decisions

### Why Kinect v1 (Xbox 360)?

- Uniquely provides face feature points (87+ landmarks) + skeleton + depth + 4-mic array in one device
- Face Tracking SDK v1.8 provides pupil position estimates (indices 69, 73) — critical for eye-in-head gaze
- 6 Action Units from the Kinect face tracker feed directly into HCEP mode classification
- Extremely low cost ($10–30 secondhand)
- Well-documented COM interop

### Why Strategy D for UKS?

BrainSim III's Universal Knowledge Store (UKS) is MIT-licensed and provides rich knowledge graph capabilities, but:
- It's not on NuGet — requires DLL reference
- Its API may change between versions
- Not all users will have it installed

Strategy D: late-bind via reflection, auto-fallback to `InMemoryKnowledgeStore`. This gives us rich knowledge when available, simple operation when not, and zero hard dependencies.

### Why Hybrid LLM (Local + Cloud)?

- **Local (Ollama):** Free, private, fast for simple queries. Good for THINK mode (brief responses) and LOGIC mode (factual).
- **Cloud (GPT-5-mini):** Higher quality for nuanced conversation. Good for SPIRIT mode (deep rapport) and AFFECT mode (emotional intelligence).
- **Agentic tool-use** only available on cloud path (OpenAI function calling).

### Why Channels over Events?

`System.Threading.Channels` provides:
- Backpressure (bounded channels with `DropOldest`)
- Async-native (`ReadAllAsync()`)
- Single-producer/multi-consumer naturally
- Better than events for high-throughput sensor data at 30fps

### Why not MVVM Toolkit Source Generators?

CommunityToolkit.Mvvm 8.4.0 source generators are used (`ObservableProperty`, `RelayCommand`) where appropriate. The architecture prioritizes testability of the pipeline over UI sophistication at this alpha stage.

---

## 9. Coding Conventions

### General

- **Language:** C# 13 (latest)
- **Nullable:** Enabled globally
- **Implicit usings:** Enabled
- **Platform:** x64 only
- **File-scoped namespaces:** Yes
- **Primary constructors:** Used where appropriate
- **Sealed records:** For immutable data (e.g., `HcepReading`)

### Naming

| Element | Convention | Example |
|---|---|---|
| Namespace | `HCEP.{Layer}` | `HCEP.Spatial` |
| Interface | `I{Name}` | `IKnowledgeStore` |
| Class | PascalCase | `ThreeStageGazeEstimator` |
| Method | PascalCase | `EstimateGaze()` |
| Property | PascalCase | `GazeDirection` |
| Private field | `_camelCase` | `_knowledgeStore` |
| Constant | PascalCase | `DefaultIpd` |
| Enum member | PascalCase | `GazeRegion.LeftEye` |
| Test method | `Method_Scenario_Expected` | `Solve_WithValidInput_ReturnsExpected` |

### Async

- All I/O operations are async (`Task`-returning)
- Use `ConfigureAwait(false)` in library code (non-UI projects)
- Channel operations use `async foreach`
- Synchronous methods that return tasks use `Task.FromResult<T>()`

### Error Handling

- Throw `ArgumentException` / `ArgumentNullException` for invalid input
- Use `ILogger<T>` for all logging (via Serilog)
- Graceful degradation: catch, log, continue with defaults rather than crash
- No `catch (Exception)` without logging

---

## 10. Dependency Management

### NuGet Packages

All NuGet dependencies are managed per-project in `.csproj` files. Key packages:

| Package | Version | Project(s) |
|---|---|---|
| Microsoft.ML.OnnxRuntime | 1.20.1 | HCEP.Vision |
| SixLabors.ImageSharp | 3.1.7 | HCEP.Vision |
| Whisper.net | 1.8.0 | HCEP.Audio |
| NAudio | 2.2.1 | HCEP.Audio |
| Serilog | 4.2.0 | HCEP.Telemetry |
| Serilog.Sinks.Console | 6.0.0 | HCEP.Telemetry |
| Serilog.Sinks.File | 6.0.0 | HCEP.Telemetry |
| Serilog.Enrichers.Thread | 4.0.0 | HCEP.Telemetry |
| Microsoft.Extensions.Hosting | 9.0.0 | HCEP.App |
| CommunityToolkit.Mvvm | 8.4.0 | HCEP.App |
| xUnit | 2.9.2 | HCEP.Tests |
| Microsoft.NET.Test.Sdk | 17.12.0 | HCEP.Tests |
| coverlet.collector | 6.0.2 | HCEP.Tests |

### Native Dependencies

| Component | Path Variable | Notes |
|---|---|---|
| Kinect SDK v1.8 | `$(KINECTSDK10_DIR)` | Conditional `Exists` check in .csproj |
| Face Tracking SDK | `$(FTSDK_DIR)` | Conditional `Exists` check in .csproj |

### Model Files (Not in Source Control)

| Model | Filename | Size | Source |
|---|---|---|---|
| Whisper base.en | `ggml-base.en.bin` | ~140 MB | Whisper.net releases |
| ArcFace ResNet100 | `arcfaceresnet100-11-int8.onnx` | ~120 MB | ONNX Model Zoo |

---

## 11. Debugging Tips

### Running Without Kinect

The app auto-detects Kinect availability. If not found, it silently uses `SimulatedSensorSource`, which generates synthetic frames at 30fps. No configuration needed.

### Logs

Serilog writes to:
- Console (when running from terminal)
- `logs/HCEP-{Date}.log` (rolling file)

Enable verbose logging by changing the minimum level in `LoggingConfiguration`:
```csharp
.MinimumLevel.Debug()  // or .Verbose() for maximum detail
```

### Common Build Issues

**Error: Kinect SDK not found**
The `.csproj` files use `Condition="Exists('$(KINECTSDK10_DIR)...')"` for Kinect references. If the SDK isn't installed, these references are silently skipped. The `SimulatedSensorSource` is always available.

**Error: Platform mismatch**
All projects must build as x64. This is enforced in `Directory.Build.props`. If you see `BadImageFormatException`, check that Visual Studio is running the x64 configuration.

**Warning: NU1902 (SixLabors.ImageSharp)**
A moderate vulnerability advisory exists. We use the latest version (3.1.7) at this API surface. Monitor for updates.

### Debugging the Agentic Loop

The agentic LLM loop in `HybridLlmEngine.CallOpenAiAsync()` runs up to 5 steps. To debug:
1. Set breakpoint in `CallOpenAiAsync` at the `while` loop
2. Inspect `response.Choices[0].Message.ToolCalls` to see what tools the LLM wants to call
3. Inspect `AgenticToolExecutor.ExecuteAsync()` results
4. Check the `messages` list to see full conversation history

### Debugging Gaze Estimation

1. Set breakpoint in `ThreeStageGazeEstimator.Estimate()`
2. Inspect Stage 1 output (head pose Euler angles)
3. Inspect Stage 2 output (eye-in-head rotation)
4. Inspect Stage 3 output (fused gaze vector)
5. Check `ConfidenceCone.Classify()` for region classification

---

## 12. Contributing

### Getting Started

1. Clone the repository
2. Install prerequisites (see [Building from Source](#3-building-from-source))
3. Build and run tests to verify your setup
4. Create a feature branch: `git checkout -b feature/my-feature`
5. Make changes, add tests, ensure all 102+ tests pass
6. Submit a pull request

### Code Review Checklist

- [ ] All tests pass (`dotnet test`)
- [ ] New code has corresponding unit tests
- [ ] No new compiler warnings
- [ ] Follows naming conventions (see Section 9)
- [ ] Interfaces in HCEP.Core, implementations in appropriate layer
- [ ] `ILogger<T>` used for all logging
- [ ] Graceful degradation for missing hardware/services
- [ ] XML documentation on public APIs

### Architecture Rules

1. **No upward dependencies.** HCEP.Core depends on nothing. HCEP.Spatial depends only on HCEP.Core. Never reference HCEP.App from a library project.
2. **Interface in Core, implementation in layer.** Every major capability is an interface in HCEP.Core with implementation in the appropriate project.
3. **Test everything testable.** If a class has logic, it has tests. Sensor interaction and UI are excluded.
4. **x64 only.** Don't try to make it AnyCPU — Kinect native DLLs are 64-bit.

---

*Copyright © 2026 Kirk LaSalle. All rights reserved.*
