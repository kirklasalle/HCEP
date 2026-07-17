# Settings & Wireframe Avatar — Analysis & Plan

Kirk, here is the updated plan reflecting the completed improvements across settings, local engines support, and the emulation tab presets.

---

## 1. ✅ DONE — Context Tab Color Readability

Removed `Foreground="{StaticResource TextBrush}"` overrides from all 3 Context tab ComboBoxes. Global style (`Foreground="Black"`) now applies correctly to dropdown items.

**Root cause**: The `ComboBox` elements on the Context tab explicitly set `Background="#252540"` and `Foreground="{StaticResource TextBrush}"` (light text), but the **dropdown popup** uses the system theme — a white/light background. The light text on a light dropdown = invisible.

**Fix**: Remove the custom `Foreground` on the Context tab ComboBoxes. The global popup brush will use high-contrast dark text.

---

## 2. ✅ DONE — Local Engines (Expanded to 11 Solutions)

Successfully refactored the backend and UI settings to support the top local SLM inference platforms. We converted the Local Engines tab to use a dynamic shared selection style (mirroring the Frontier Cloud tab), which prevents UI clutter while enabling full control of each engine.

### Supported Local Engines:
1. **Ollama** (OpenAI-compatible / `/api/generate`)
2. **Llama.cpp** (native / OpenAI-compatible endpoint toggle)
3. **LM Studio** (OpenAI-compatible)
4. **Jan** (OpenAI-compatible)
5. **GPT4All** (OpenAI-compatible)
6. **LocalAI** (OpenAI-compatible)
7. **vLLM** (OpenAI-compatible)
8. **Text Generation WebUI (oobabooga)** (OpenAI-compatible)
9. **KoboldCpp** (OpenAI-compatible)
10. **BitNet** (1-bit LLM local server, OpenAI-compatible)
11. **Custom (OpenAI-compatible)** (User-configurable URL/Model)

### Architecture updates:
- Updated `LlmConfiguration` to add `GenericLocalSettings` models for Jan, GPT4All, LM Studio, vLLM, BitNet, etc.
- Added `GetLocalEngineConfig()` helper to `HybridLlmEngine.cs` to resolve engine URL / Model / Temp configurations at runtime.
- Updated `IsLocalAvailableAsync` to query `/health`, `/api/tags`, or generic `/v1/models` to verify health.
- Implemented state copy/clone logic in `SettingsWindow.xaml.cs` to correctly save/load all local engine configurations.

---

## 3. ✅ DONE — Happyface / Emulation Tab Presets & Audit

Audited and enhanced the Emulation controls by adding **Personality Presets** that automatically control pacing, mirroring, and somatic cues.

### Personality Profiles Implemented:
*   **Attentive Listener** (Responsive blend)
    *   Emulation Weight: `0.70`
    *   Reflection Delay: `200 ms`
    *   Blink Sync: `True`
*   **Warm Companion** (High empathy/contagion)
    *   Emulation Weight: `0.90`
    *   Reflection Delay: `350 ms`
    *   Blink Sync: `True`
*   **Silent Observer** (Quiet reflection)
    *   Emulation Weight: `0.15`
    *   Reflection Delay: `700 ms`
    *   Blink Sync: `False`
*   **Professional Assistant** (Direct and prompt)
    *   Emulation Weight: `0.40`
    *   Reflection Delay: `150 ms`
    *   Blink Sync: `True`
*   **Custom** (User-controlled overrides)

### Interactive Logic:
- When a Preset is selected from the `PresetCombo` dropdown, the sliders and blinks checkbox auto-update.
- If a user manually adjusts any of the sliders or the blinks checkbox, the Preset selection switches automatically to **Custom**.
- Values are loaded on window open and saved successfully via `Save_Click` into configuration persistence.

---

## 4. ✅ DONE — 3D Wireframe Avatar Stabilization

Stabilization features implemented in the Kinect face tracking bridge and the AvatarWindow event handler to eliminate mesh warping, flickering, and pupils displacement.

### Fixes:
1. **Neutral mesh scale matching**: Neutral face projection now reads actual tracked scale/depth to project the model accurately.
2. **Neutral mesh caching**: Caches successful projections. If a single frame's COM call fails, uses the cached shape instead of falling back to raw rotated mesh data.
3. **Mirroring-gated mesh selection**: When `IsMirroringEnabled` is disabled, the system forces the stable neutral mesh, isolating the face wireframe from erratic user rotations while keeping eye/pupil tracking perfectly active.
