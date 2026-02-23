// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System.Numerics;
using HCEP.Core.Enums;
using HCEP.Core.Models;

namespace HCEP.Spatial;

/// <summary>
/// Confidence Cone classifier — maps a 3D gaze intersection point
/// to discrete <see cref="GazeRegion"/> by testing whether the gaze
/// falls within overlapping confidence cones centered on facial landmarks.
/// </summary>
public sealed class ConfidenceCone
{
    /// <summary>
    /// Face landmark positions for the interlocutor (camera space).
    /// Set these from the tracked interlocutor's face frame.
    /// </summary>
    public Dictionary<GazeRegion, Vector3> Landmarks { get; } = new();

    /// <summary>Cone radius in centimeters at the target plane.</summary>
    public float ConeRadiusCm { get; set; } = Anthropometrics.DefaultConeRadiusCm;

    /// <summary>
    /// Classifies the gaze intersection into a face region.
    /// Returns the closest region within the confidence cone,
    /// or <see cref="GazeRegion.Unknown"/> if no landmark is within range.
    /// </summary>
    /// <param name="gazeIntersection">3D point where gaze ray hits the interlocutor's face plane.</param>
    /// <returns>Classified region and distance to the nearest landmark.</returns>
    public (GazeRegion Region, float DistanceCm) Classify(Vector3 gazeIntersection)
    {
        if (Landmarks.Count == 0)
            return (GazeRegion.Unknown, float.MaxValue);

        float coneRadiusM = ConeRadiusCm / 100f;
        GazeRegion bestRegion = GazeRegion.Unknown;
        float bestDist = float.MaxValue;

        foreach (var (region, landmark) in Landmarks)
        {
            float dist = Vector3.Distance(gazeIntersection, landmark);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestRegion = region;
            }
        }

        // Only classify if within cone radius
        if (bestDist > coneRadiusM)
            return (GazeRegion.Unknown, bestDist * 100f);

        return (bestRegion, bestDist * 100f);
    }

    /// <summary>
    /// Updates landmark positions from a <see cref="FaceFrame"/>.
    /// Maps key feature point indices to gaze regions.
    /// </summary>
    public void UpdateFromFaceFrame(FaceFrame face)
    {
        if (!face.IsTracked || face.FeaturePoints3D.Length < 74)
            return;

        Landmarks.Clear();

        // Pupil positions → eye regions
        Landmarks[GazeRegion.LeftEye] = face.LeftPupil3D;
        Landmarks[GazeRegion.RightEye] = face.RightPupil3D;

        // Nose bridge (midpoint between eyes)
        Landmarks[GazeRegion.NasalBridge] = face.CyclopeanPoint3D;

        // Face center approximation
        Landmarks[GazeRegion.FaceCenter] = face.HeadTranslation / 1000f; // mm → m

        // Mouth region — approximate from head translation + offset
        Vector3 mouthOffset = new(0, -0.04f, 0); // ~4cm below face center
        Landmarks[GazeRegion.Mouth] = (face.HeadTranslation / 1000f) + mouthOffset;

        // Forehead — approximate
        Vector3 foreheadOffset = new(0, 0.05f, 0);
        Landmarks[GazeRegion.Forehead] = (face.HeadTranslation / 1000f) + foreheadOffset;
    }
}
