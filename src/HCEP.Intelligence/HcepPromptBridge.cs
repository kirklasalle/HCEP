// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using HCEP.Core.Enums;
using HCEP.Core.Models;

namespace HCEP.Intelligence;

/// <summary>
/// Bridges HCEP mode readings into LLM prompt context.
/// Generates rich, contextual preambles that modulate LLM behavior
/// based on the detected cognitive-emotional state of the interlocutor.
/// </summary>
public static class HcepPromptBridge
{
    /// <summary>
    /// Generates a mode-aware context injection string for LLM prompts.
    /// </summary>
    public static string GenerateContext(HcepReading reading, SpeechResult? speech = null)
    {
        var lines = new List<string>
        {
            $"[HCEP Mode: {reading.Mode} | Confidence: {reading.Confidence:P0}]",
            $"[Gaze: {reading.Region} | Cognitive: {reading.Cognitive} | Valence: {reading.Valence}]",
        };

        // Add mode-specific behavioral instructions
        lines.Add(GetModeInstruction(reading.Mode));

        // Add cognitive state context
        lines.Add(GetCognitiveInstruction(reading.Cognitive));

        // Add speech context if available
        if (speech is not null && !string.IsNullOrEmpty(speech.Text))
        {
            lines.Add($"[User said: \"{speech.Text}\"]");
            lines.Add($"[Speech confidence: {speech.Confidence:P0}, angle: {speech.SourceAngleDeg:F0}°]");
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Determines whether a query should be routed to the cloud LLM
    /// based on HCEP mode complexity.
    /// </summary>
    public static bool ShouldUseCloud(HcepReading reading, string query)
    {
        // Spirit mode (deep rapport) and complex queries → cloud for better quality
        if (reading.Mode == HcepMode.Spirit && query.Length > 100)
            return true;

        // High-confidence affect mode → cloud for nuanced emotional responses
        if (reading.Mode == HcepMode.Affect && reading.Confidence > 0.7f)
            return true;

        // Think mode → local (fast, non-intrusive responses)
        if (reading.Mode == HcepMode.Think)
            return false;

        // Logic mode with long queries → cloud
        if (reading.Mode == HcepMode.Logic && query.Length > 200)
            return true;

        return false; // Default to local
    }

    private static string GetModeInstruction(HcepMode mode) => mode switch
    {
        HcepMode.Logic =>
            "[Instruction: User is in analytical mode. Provide structured, factual, detailed responses. Use numbered lists and clear logic.]",
        HcepMode.Affect =>
            "[Instruction: User is emotionally engaged. Mirror their emotional tone. Be warm and empathetic. Acknowledge feelings before facts.]",
        HcepMode.Spirit =>
            "[Instruction: Deep authentic connection detected. Respond personally and genuinely. Drop formal structures. Be present.]",
        HcepMode.Heart =>
            "[Instruction: Empathic resonance state. Use supportive, caring language. Validate their experience. Listen more than instruct.]",
        HcepMode.Think =>
            "[Instruction: User is internally processing. Keep responses brief and non-intrusive. Offer space. Ask if they want to share their thoughts.]",
        _ =>
            "[Instruction: Respond naturally and helpfully.]",
    };

    private static string GetCognitiveInstruction(CognitiveState state) => state switch
    {
        CognitiveState.Recalling =>
            "[Context: User appears to be recalling a memory. Gently prompt if needed.]",
        CognitiveState.Constructing =>
            "[Context: User is constructing an idea or imagining something. Allow creative space.]",
        CognitiveState.Confused =>
            "[Context: User may be confused. Offer clarification proactively.]",
        CognitiveState.PreSpeech =>
            "[Context: User is about to speak. Pause and listen.]",
        CognitiveState.Aligned =>
            "[Context: User shows agreement. Build on the shared understanding.]",
        _ => "",
    };
}
