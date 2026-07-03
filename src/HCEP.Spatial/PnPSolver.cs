// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// 
// PROPRIETARY & TRADE SECRET NOTICE:
// This source code and associated documentation (including the HCEP
// Theory, the engineering implementation, the supported mathematical
// formulations, the Permanent Active Directives (PAD), and the Body
// Language Protocols) contain proprietary and trade secret assets
// owned exclusively by Kirk LaSalle. Unauthorized use, copying,
// modification, or distribution is strictly prohibited.
// ──────────────────────────────────────────────────────────────
using System.Numerics;

namespace HCEP.Spatial;

/// <summary>
/// Lightweight SolvePnP implementation for head-pose estimation from
/// 2D-3D point correspondences. Uses iterative DLT (Direct Linear Transform)
/// with Levenberg-Marquardt refinement.
///
/// This avoids the OpenCV dependency by implementing the minimal PnP solver
/// needed for 6-point face landmark pose estimation.
/// </summary>
public static class PnPSolver
{
    /// <summary>
    /// Estimates head pose (rotation + translation) from 2D-3D correspondences.
    /// </summary>
    /// <param name="objectPoints">3D model points (canonical face landmarks, mm).</param>
    /// <param name="imagePoints">Corresponding 2D projections (pixels).</param>
    /// <param name="focalLength">Camera focal length in pixels.</param>
    /// <param name="principalPoint">Camera principal point (cx, cy) in pixels.</param>
    /// <returns>Rotation (pitch, yaw, roll in degrees) and translation vector.</returns>
    public static (Vector3 Rotation, Vector3 Translation) Solve(
        ReadOnlySpan<Vector3> objectPoints,
        ReadOnlySpan<Vector2> imagePoints,
        float focalLength,
        Vector2 principalPoint)
    {
        if (objectPoints.Length < 4 || objectPoints.Length != imagePoints.Length)
            return (Vector3.Zero, Vector3.Zero);

        // ── Normalize image coordinates ────────────────────────
        int n = objectPoints.Length;
        Span<Vector2> normalized = stackalloc Vector2[n];
        for (int i = 0; i < n; i++)
        {
            normalized[i] = new Vector2(
                (imagePoints[i].X - principalPoint.X) / focalLength,
                (imagePoints[i].Y - principalPoint.Y) / focalLength);
        }

        // ── DLT-based initial estimate ─────────────────────────
        // Build the coefficient matrix for the linear system
        var (R, t) = SolveDlt(objectPoints, normalized);

        // ── Extract Euler angles ───────────────────────────────
        var euler = RotationMatrixToEuler(R);

        // ── Levenberg-Marquardt refinement ─────────────────────
        var (refinedRot, refinedT) = RefinePose(objectPoints, normalized, euler, t);

        return (refinedRot, refinedT);
    }

    private static (Vector3 Rotation, Vector3 Translation) RefinePose(
        ReadOnlySpan<Vector3> obj,
        ReadOnlySpan<Vector2> img,
        Vector3 initialRot,
        Vector3 initialT)
    {
        Vector3 rot = initialRot;
        Vector3 t = initialT;
        float lambda = 0.01f; // LM damping factor
        int maxIterations = 20;
        int n = obj.Length;

        // Move stackalloc allocations out of the loop
        Span<float> r = stackalloc float[n * 2];
        Span<float> J = stackalloc float[n * 2 * 6];
        Span<float> JtJ = stackalloc float[6 * 6];
        Span<float> Jtr = stackalloc float[6];
        Span<float> delta = stackalloc float[6];

        for (int iter = 0; iter < maxIterations; iter++)
        {
            Matrix4x4 R = EulerToRotationMatrix(rot);

            // Compute residuals
            float currentError = 0;
            for (int i = 0; i < n; i++)
            {
                Vector2 proj = Project(obj[i], R, t);
                r[i * 2] = proj.X - img[i].X;
                r[i * 2 + 1] = proj.Y - img[i].Y;
                currentError += r[i * 2] * r[i * 2] + r[i * 2 + 1] * r[i * 2 + 1];
            }

            // Build Jacobian J (size: 2N x 6)
            // Parameters: 0:rx, 1:ry, 2:rz, 3:tx, 4:ty, 5:tz
            // Finite-difference step size: 1e-3 radians (~0.057°) balances numerical
            // precision against floating-point cancellation at 32-bit resolution.
            // Smaller values produce cancellation; larger values reduce gradient accuracy.
            float eps = 1e-3f;

            for (int j = 0; j < 6; j++)
            {
                Vector3 perturbedRot = rot;
                Vector3 perturbedT = t;

                if (j < 3)
                {
                    if (j == 0) perturbedRot.X += eps;
                    else if (j == 1) perturbedRot.Y += eps;
                    else perturbedRot.Z += eps;
                }
                else
                {
                    if (j == 3) perturbedT.X += eps;
                    else if (j == 4) perturbedT.Y += eps;
                    else perturbedT.Z += eps;
                }

                Matrix4x4 Rp = EulerToRotationMatrix(perturbedRot);
                for (int i = 0; i < n; i++)
                {
                    Vector2 projP = Project(obj[i], Rp, perturbedT);
                    float rx = projP.X - img[i].X;
                    float ry = projP.Y - img[i].Y;

                    J[(i * 2) * 6 + j] = (rx - r[i * 2]) / eps;
                    J[(i * 2 + 1) * 6 + j] = (ry - r[i * 2 + 1]) / eps;
                }
            }

            // Compute J^T * J (6x6 matrix) and J^T * r (6x1 vector)
            for (int row = 0; row < 6; row++)
            {
                for (int col = 0; col < 6; col++)
                {
                    float sum = 0;
                    for (int i = 0; i < n * 2; i++)
                    {
                        sum += J[i * 6 + row] * J[i * 6 + col];
                    }
                    JtJ[row * 6 + col] = sum;
                }

                float sumTr = 0;
                for (int i = 0; i < n * 2; i++)
                {
                    sumTr += J[i * 6 + row] * r[i];
                }
                Jtr[row] = sumTr;
            }

            // Apply LM damping to JtJ diagonal
            for (int d = 0; d < 6; d++)
            {
                JtJ[d * 6 + d] += lambda;
            }

            // Solve (JtJ) * delta = -Jtr
            if (!Solve6x6(JtJ, Jtr, delta))
            {
                break; // Singular matrix
            }

            // Propose new parameters
            Vector3 nextRot = rot - new Vector3(delta[0], delta[1], delta[2]);
            Vector3 nextT = t - new Vector3(delta[3], delta[4], delta[5]);

            // Compute new error
            Matrix4x4 Rnext = EulerToRotationMatrix(nextRot);
            float nextError = 0;
            for (int i = 0; i < n; i++)
            {
                Vector2 proj = Project(obj[i], Rnext, nextT);
                float rx = proj.X - img[i].X;
                float ry = proj.Y - img[i].Y;
                nextError += rx * rx + ry * ry;
            }

            if (nextError < currentError)
            {
                rot = nextRot;
                t = nextT;
                lambda *= 0.1f; // Accept step, decrease damping
                if (currentError - nextError < 1e-4f)
                {
                    break; // Converged
                }
            }
            else
            {
                lambda *= 10f; // Reject step, increase damping
            }
        }

        return (rot, t);
    }

    private static Vector2 Project(Vector3 objPt, Matrix4x4 R, Vector3 t)
    {
        Vector3 pPrime = Vector3.Transform(objPt, R) + t;
        if (pPrime.Z < 0.001f) pPrime = new Vector3(pPrime.X, pPrime.Y, 0.001f);
        return new Vector2(pPrime.X / pPrime.Z, pPrime.Y / pPrime.Z);
    }

    private static Matrix4x4 EulerToRotationMatrix(Vector3 euler)
    {
        float pitch = euler.X * MathF.PI / 180f;
        float yaw = euler.Y * MathF.PI / 180f;
        float roll = euler.Z * MathF.PI / 180f;

        Matrix4x4 rx = Matrix4x4.CreateRotationX(pitch);
        Matrix4x4 ry = Matrix4x4.CreateRotationY(yaw);
        Matrix4x4 rz = Matrix4x4.CreateRotationZ(roll);

        return rz * rx * ry;
    }

    private static bool Solve6x6(ReadOnlySpan<float> A, ReadOnlySpan<float> b, Span<float> x)
    {
        Span<float> m = stackalloc float[6 * 7];
        for (int r = 0; r < 6; r++)
        {
            for (int c = 0; c < 6; c++)
            {
                m[r * 7 + c] = A[r * 6 + c];
            }
            m[r * 7 + 6] = b[r];
        }

        for (int i = 0; i < 6; i++)
        {
            int pivotRow = i;
            float maxVal = Math.Abs(m[i * 7 + i]);
            for (int r = i + 1; r < 6; r++)
            {
                float val = Math.Abs(m[r * 7 + i]);
                if (val > maxVal)
                {
                    maxVal = val;
                    pivotRow = r;
                }
            }

            if (maxVal < 1e-9f)
            {
                return false;
            }

            if (pivotRow != i)
            {
                for (int col = 0; col < 7; col++)
                {
                    float temp = m[i * 7 + col];
                    m[i * 7 + col] = m[pivotRow * 7 + col];
                    m[pivotRow * 7 + col] = temp;
                }
            }

            for (int r = i + 1; r < 6; r++)
            {
                float factor = m[r * 7 + i] / m[i * 7 + i];
                for (int c = i; c < 7; c++)
                {
                    m[r * 7 + c] -= factor * m[i * 7 + c];
                }
            }
        }

        for (int i = 5; i >= 0; i--)
        {
            float sum = m[i * 7 + 6];
            for (int col = i + 1; col < 6; col++)
            {
                sum -= m[i * 7 + col] * x[col];
            }
            x[i] = sum / m[i * 7 + i];
        }

        return true;
    }

    /// <summary>
    /// Direct Linear Transform for PnP — provides initial pose estimate.
    /// </summary>
    private static (Matrix4x4 R, Vector3 t) SolveDlt(
        ReadOnlySpan<Vector3> obj,
        ReadOnlySpan<Vector2> img)
    {
        int n = obj.Length;

        // Compute centroid of 3D points
        Vector3 centroid3D = Vector3.Zero;
        for (int i = 0; i < n; i++)
            centroid3D += obj[i];
        centroid3D /= n;

        // Simple pose estimation using the first 3 non-collinear points
        // with cross-ratio constraints
        Vector3 p0 = obj[0] - centroid3D;
        Vector3 p1 = obj[1] - centroid3D;
        Vector3 p2 = obj[2] - centroid3D;

        Vector2 q0 = img[0];
        Vector2 q1 = img[1];
        Vector2 q2 = img[2];

        // Estimate depth from inter-point distances
        float modelDist01 = Vector3.Distance(obj[0], obj[1]);
        float imageDist01 = Vector2.Distance(img[0], img[1]);
        float estimatedZ = modelDist01 / Math.Max(imageDist01, 0.001f);

        // Build approximate rotation from face plane normal
        Vector3 v01 = Vector3.Normalize(p1 - p0);
        Vector3 v02 = Vector3.Normalize(p2 - p0);
        Vector3 faceNormal = Vector3.Normalize(Vector3.Cross(v01, v02));

        // Image-space direction vectors
        Vector2 d01 = Vector2.Normalize(q1 - q0);
        Vector2 d02 = Vector2.Normalize(q2 - q0);

        // Build rotation matrix columns
        Vector3 xAxis = new(d01.X, d01.Y, 0);
        Vector3 yAxis = new(d02.X, d02.Y, 0);
        Vector3 zAxis = Vector3.Normalize(Vector3.Cross(xAxis, yAxis));
        yAxis = Vector3.Cross(zAxis, xAxis);

        xAxis = Vector3.Normalize(xAxis);
        yAxis = Vector3.Normalize(yAxis);

        var R = new Matrix4x4(
            xAxis.X, yAxis.X, zAxis.X, 0,
            xAxis.Y, yAxis.Y, zAxis.Y, 0,
            xAxis.Z, yAxis.Z, zAxis.Z, 0,
            0, 0, 0, 1);

        // Translation: center of 2D projection at estimated depth
        Vector2 centroid2D = Vector2.Zero;
        for (int i = 0; i < n; i++)
            centroid2D += img[i];
        centroid2D /= n;

        Vector3 t = new(centroid2D.X * estimatedZ, centroid2D.Y * estimatedZ, estimatedZ);

        return (R, t);
    }

    /// <summary>
    /// Extracts Euler angles (pitch, yaw, roll) in degrees from a rotation matrix.
    /// Uses ZYX convention.
    /// </summary>
    public static Vector3 RotationMatrixToEuler(Matrix4x4 R)
    {
        float sy = MathF.Sqrt(R.M11 * R.M11 + R.M21 * R.M21);
        bool singular = sy < 1e-6f;

        float pitch, yaw, roll;
        if (!singular)
        {
            pitch = MathF.Atan2(R.M32, R.M33);  // X rotation
            yaw = MathF.Atan2(-R.M31, sy);       // Y rotation
            roll = MathF.Atan2(R.M21, R.M11);    // Z rotation
        }
        else
        {
            pitch = MathF.Atan2(-R.M23, R.M22);
            yaw = MathF.Atan2(-R.M31, sy);
            roll = 0;
        }

        // Convert to degrees
        const float rad2deg = 180.0f / MathF.PI;
        return new Vector3(pitch * rad2deg, yaw * rad2deg, roll * rad2deg);
    }
}
