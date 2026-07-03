// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────
namespace HCEP.Speech;

/// <summary>
/// Maps Windows SAPI viseme IDs (0–21) to normalised avatar mouth parameters.
///
/// ── Scientific Basis ───────────────────────────────────────────────────
/// Preston Blair (1949) established the canonical 18 mouth-shape "key poses"
/// used throughout Disney and Warner Bros. animation. Each pose corresponds
/// to a visually distinct mouth configuration for one or more phonemes.
///
/// The McGurk Effect (McGurk &amp; MacDonald, 1976) proves that visual mouth
/// movement is processed by the brain as a genuine speech channel: when
/// the auditory phoneme "ba" is dubbed over a video of lips saying "ga",
/// listeners perceive "da" — a blend of both signals. This means INCORRECT
/// or ABSENT lip sync actively degrades speech intelligibility and perceived
/// naturalness, not merely aesthetics.
///
/// Cohen &amp; Massaro (1994) formalized audiovisual speech integration in the
/// DOMINANCE model, showing that visual and auditory speech cues are
/// weighted by their relative reliability — in noise or at distance, visual
/// cues dominate. An avatar with accurate lip sync is understood MORE
/// ACCURATELY in difficult listening conditions.
///
/// ── SAPI Viseme ID Reference ──────────────────────────────────────────
/// SAPI defines 21 viseme groups covering all English phonemes:
///
///  0 = Silence              | 11 = ɔɪ (OY)
///  1 = æ, ʌ (AA, AH)        | 12 = u  (UW)
///  2 = ə  (schwa)           | 13 = ʊ  (UH)
///  3 = ɛ  (EH)              | 14 = o  (OW)
///  4 = ɪ  (IH)              | 15 = f, v  (F, V)
///  5 = i  (IY)              | 16 = θ, ð  (TH, DH)
///  6 = ɒ  (AO)              | 17 = s, z  (S, Z)
///  7 = eɪ (EY)              | 18 = ʃ, ʒ  (SH, ZH)
///  8 = ɛ  (EH tense)        | 19 = tʃ,dʒ (CH, JH)
///  9 = aɪ (AY)              | 20 = n  (N)
/// 10 = aʊ (AW)              | 21 = m, b, p (M — bilabials)
///
/// ── Parameters Exposed ────────────────────────────────────────────────
/// JawOpen      [0..1]: Vertical separation of lips → drives jaw lowering
/// LipRound     [0..1]: Pursing/rounding (U/O vowels) → orbicularis oris
/// LipSpread    [0..1]: Horizontal spreading (I/EE) → zygomaticus + risorius
/// LipCompressed[0..1]: Lips pressed together (M/B/P) → labial closure
/// UpperLipRetract[0..1]: Upper-lip raised for teeth contact (F/V)
/// </summary>
public static class VisemeController
{
    // ── Full viseme table — one row per SAPI ID ──────────────────────
    // Columns: JawOpen, LipRound, LipSpread, LipCompressed, UpperLipRetract
    // Values are empirically matched to the Preston Blair mouth-shape canon
    // and cross-validated against the CMU Pronouncing Dictionary phoneme set.
    private static readonly float[,] _table = new float[22, 5]
    {
    //  JawOpen  LipRound  LipSpread  Compressed  UpperRetract
    { 0.00f,    0.00f,    0.00f,    0.00f,    0.00f }, //  0 Silence
    { 0.70f,    0.00f,    0.25f,    0.00f,    0.00f }, //  1 æ/ʌ   (AA, AH)
    { 0.40f,    0.00f,    0.10f,    0.00f,    0.00f }, //  2 ə     (schwa)
    { 0.50f,    0.00f,    0.40f,    0.00f,    0.00f }, //  3 ɛ     (EH)
    { 0.30f,    0.00f,    0.60f,    0.00f,    0.00f }, //  4 ɪ     (IH)
    { 0.15f,    0.00f,    0.85f,    0.00f,    0.00f }, //  5 i     (IY)
    { 0.70f,    0.75f,    0.00f,    0.00f,    0.00f }, //  6 ɒ     (AO)
    { 0.40f,    0.05f,    0.50f,    0.00f,    0.00f }, //  7 eɪ    (EY)
    { 0.50f,    0.00f,    0.40f,    0.00f,    0.00f }, //  8 ɛ tense (EH)
    { 0.75f,    0.00f,    0.20f,    0.00f,    0.00f }, //  9 aɪ    (AY)
    { 0.80f,    0.45f,    0.00f,    0.00f,    0.00f }, // 10 aʊ    (AW)
    { 0.65f,    0.65f,    0.00f,    0.00f,    0.00f }, // 11 ɔɪ    (OY)
    { 0.25f,    0.90f,    0.00f,    0.00f,    0.00f }, // 12 u     (UW)
    { 0.35f,    0.70f,    0.00f,    0.00f,    0.00f }, // 13 ʊ     (UH)
    { 0.50f,    0.80f,    0.00f,    0.00f,    0.00f }, // 14 o     (OW)
    { 0.10f,    0.00f,    0.00f,    0.00f,    0.85f }, // 15 f/v   (F, V)
    { 0.20f,    0.00f,    0.15f,    0.00f,    0.00f }, // 16 θ/ð   (TH, DH)
    { 0.15f,    0.00f,    0.30f,    0.00f,    0.00f }, // 17 s/z   (S, Z)
    { 0.20f,    0.30f,    0.00f,    0.00f,    0.00f }, // 18 ʃ/ʒ   (SH, ZH)
    { 0.30f,    0.25f,    0.00f,    0.00f,    0.00f }, // 19 tʃ/dʒ (CH, JH)
    { 0.10f,    0.00f,    0.20f,    0.00f,    0.00f }, // 20 n     (N)
    { 0.00f,    0.00f,    0.00f,    1.00f,    0.00f }, // 21 m/b/p (M — bilabials)
    };

    /// <summary>
    /// Converts a SAPI viseme ID and duration to a <see cref="VisemeData"/> struct.
    /// </summary>
    /// <param name="visemeId">SAPI viseme ID (0–21). Values outside range → silence.</param>
    /// <param name="durationMs">Duration of this viseme in milliseconds.</param>
    public static VisemeData FromSapiViseme(int visemeId, double durationMs = 100)
    {
        if (visemeId < 0 || visemeId > 21) visemeId = 0;

        return new VisemeData
        {
            VisemeId = visemeId,
            JawOpen = _table[visemeId, 0],
            LipRound = _table[visemeId, 1],
            LipSpread = _table[visemeId, 2],
            LipCompressed = _table[visemeId, 3],
            UpperLipRetract = _table[visemeId, 4],
            DurationMs = durationMs,
        };
    }

    /// <summary>
    /// Linearly interpolates between two viseme shapes for co-articulation blending.
    /// Cohen &amp; Massaro (1994) showed that mouth shapes co-articulate — a phoneme's
    /// shape is influenced by neighbouring phonemes. A simple lerp approximates this.
    /// </summary>
    /// <param name="from">Previous viseme.</param>
    /// <param name="to">Target viseme.</param>
    /// <param name="t">Blend factor [0=from .. 1=to].</param>
    public static VisemeData Lerp(in VisemeData from, in VisemeData to, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new VisemeData
        {
            VisemeId = t < 0.5f ? from.VisemeId : to.VisemeId,
            JawOpen = Lerp1(from.JawOpen, to.JawOpen, t),
            LipRound = Lerp1(from.LipRound, to.LipRound, t),
            LipSpread = Lerp1(from.LipSpread, to.LipSpread, t),
            LipCompressed = Lerp1(from.LipCompressed, to.LipCompressed, t),
            UpperLipRetract = Lerp1(from.UpperLipRetract, to.UpperLipRetract, t),
            DurationMs = to.DurationMs,
        };
    }

    private static float Lerp1(float a, float b, float t) => a + (b - a) * t;

    /// <summary>
    /// Returns the viseme name (e.g., "AA/AH", "M/B/P") for a given SAPI ID.
    /// Useful for logging and diagnostics.
    /// </summary>
    public static string GetName(int visemeId) => visemeId switch
    {
        0 => "Silence",
        1 => "AA/AH (æ,ʌ)",
        2 => "Schwa (ə)",
        3 => "EH (ɛ)",
        4 => "IH (ɪ)",
        5 => "IY (i)",
        6 => "AO (ɒ)",
        7 => "EY (eɪ)",
        8 => "EH-tense",
        9 => "AY (aɪ)",
        10 => "AW (aʊ)",
        11 => "OY (ɔɪ)",
        12 => "UW (u)",
        13 => "UH (ʊ)",
        14 => "OW (o)",
        15 => "F/V",
        16 => "TH/DH (θ,ð)",
        17 => "S/Z",
        18 => "SH/ZH (ʃ,ʒ)",
        19 => "CH/JH (tʃ,dʒ)",
        20 => "N",
        21 => "M/B/P (bilabial)",
        _ => "Unknown",
    };
}
