// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────
using HCEP.Core.Enums;
using HCEP.Core.Models;

namespace HCEP.Intelligence;

/// <summary>
/// Evaluates the Silence Protocol in real-time: determines whether the avatar
/// should initiate speech or remain silently present based on the current
/// HCEP mode, facial cues, and contextual snapshot.
///
/// Scientific basis: Jaworski (1993) The Power of Silence; Sacks, Schegloff &amp;
/// Jefferson (1974) Turn-Taking; Duncan (1972) Speaker Yield Cues.
/// See HCEP_SCIENCE_FOUNDATION.md §11.4.
/// </summary>
public static class SilenceProtocolEvaluator
{
    /// <summary>
    /// Returns true when the avatar should NOT initiate speech.
    /// The facial cues from HCEP perception take precedence over the contextual
    /// defaults — if the user has direct gaze and raised brows, the floor is
    /// yielded regardless of time or environment.
    /// </summary>
    /// <param name="mode">Current HCEP classification (updated at ~30fps).</param>
    /// <param name="context">Time/space/situation snapshot (updated at ~1Hz).</param>
    /// <param name="gazeAversion">
    /// True when gaze is averted from the avatar (>15° from direct gaze).
    /// Signals internal processing — do not interrupt.
    /// </param>
    /// <param name="browFurrowed">
    /// AU4 BrowLowerer > 0.3 sustained for > 2s.
    /// Deep concentration — user has not invited response.
    /// </param>
    /// <param name="directGazeAtAvatar">
    /// True when gaze direction is toward the avatar screen region.
    /// The primary floor-yield signal (Duncan, 1972 cue #6).
    /// </param>
    /// <param name="raisedBrows">
    /// AU1+AU5 > 0.3 — query or invitation to respond.
    /// </param>
    public static bool ShouldBeSilent(
        HcepMode mode,
        ContextSnapshot context,
        bool gazeAversion,
        bool browFurrowed,
        bool directGazeAtAvatar,
        bool raisedBrows)
    {
        // ── Floor-yield overrides (avatar CAN speak) ──────────────────
        // Any of these signals mean the user has handed the floor.
        if (directGazeAtAvatar && !gazeAversion)  return false;
        if (raisedBrows && directGazeAtAvatar)     return false;

        // ── Hard silence signals (avatar must not speak) ──────────────
        // THINK mode is the clearest: gaze aversion + defocus = processing.
        if (mode == HcepMode.Think && gazeAversion)   return true;
        if (mode == HcepMode.Think && browFurrowed)   return true;

        // HEART mode at night: be present, not verbose.
        if (mode == HcepMode.Heart
            && context.TimeOfDay is TimeOfDayCategory.Night or TimeOfDayCategory.Evening
            && !directGazeAtAvatar)
            return true;

        // Bedroom + night → affiliative silence (Jaworski, 1993).
        if (context.Environment == EnvironmentType.Bedroom
            && context.TimeOfDay is TimeOfDayCategory.Night
            && !directGazeAtAvatar)
            return true;

        // Sustained brow furrow without direct gaze → deep work.
        if (browFurrowed && gazeAversion) return true;

        // Context default: silence during deep work environments.
        if (context.Environment is EnvironmentType.Laboratory or EnvironmentType.Studio
            && !directGazeAtAvatar)
            return true;

        return false;
    }

    /// <summary>
    /// Evaluates silence protocol from a <see cref="HcepReading"/> and facial AUs.
    /// Convenience overload that extracts the relevant signals internally.
    /// </summary>
    public static bool ShouldBeSilent(
        HcepReading? reading,
        ContextSnapshot context,
        float[] actionUnits)
    {
        if (reading is null) return false;

        bool gazeAversion = reading.Region is GazeRegion.PeripheralLeft
                                            or GazeRegion.PeripheralRight
                                            or GazeRegion.Above
                                            or GazeRegion.Below
                                            or GazeRegion.Defocused
                         || reading.Mode == HcepMode.Think;

        // AU3 BrowLowerer
        float au3 = actionUnits.Length > (int)ActionUnit.BrowLowerer
            ? actionUnits[(int)ActionUnit.BrowLowerer] : 0f;
        bool browFurrowed = au3 < -0.3f;

        // Direct gaze: SPIRIT or LOGIC with on-face region approximates direct avatar gaze
        bool directGaze = reading.Mode is HcepMode.Spirit or HcepMode.Logic
                       && reading.Region is not (GazeRegion.PeripheralLeft
                           or GazeRegion.PeripheralRight or GazeRegion.Above
                           or GazeRegion.Below or GazeRegion.Defocused);

        // AU5 OuterBrowRaiser
        float au5 = actionUnits.Length > (int)ActionUnit.OuterBrowRaiser
            ? actionUnits[(int)ActionUnit.OuterBrowRaiser] : 0f;
        bool raisedBrows = au5 > 0.3f;

        return ShouldBeSilent(reading.Mode, context, gazeAversion, browFurrowed, directGaze, raisedBrows);
    }
}
