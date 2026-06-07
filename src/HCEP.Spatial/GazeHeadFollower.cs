using System;

namespace HCEP.Spatial
{
    /// <summary>
    /// Computes automatic head rotation driven by extreme eye gaze.
    /// When eyes look too far in any direction, the head rotates proportionally to create
    /// the illusion of the avatar turning its head while maintaining gaze continuity.
    /// 
    /// Uses the same pitch/yaw/roll coordinate system as the gaze estimation pipeline.
    /// </summary>
    public class GazeHeadFollower
    {
        // Configuration: Trigger threshold (as percentage of max gaze angle)
        private const float GazeTriggerThreshold = 0.0f;  // 0% of max angle (always follow/track)

        // Configuration: Maximum head rotation induced by gaze
        private float _maxGazeInducedYawRad;      // ~30 degrees (Math.PI / 6) - subtle, not extreme
        private float _maxGazeInducedPitchRad;    // ~25 degrees (~0.44 rad) - subtle, graceful

        // Configuration: How much eye movement translates to head movement (proportional factor)
        // 0.3 means if eye moves 40° right, head rotates ~12° right (more conservative)
        private const float ProportionalFactor = 0.3f;

        // Configuration: Exponential smoothing time constant (seconds)
        // Increased to 0.7s for smoother, more graceful animation
        private const float SmoothingTimeConstantSec = 0.7f;

        // Current head pose state (targets that we're smoothing towards)
        private float _targetHeadYawRad = 0.0f;
        private float _targetHeadPitchRad = 0.0f;
        private float _targetHeadRollRad = 0.0f;

        // Smoothed output (what we actually apply)
        private float _smoothedHeadYawRad = 0.0f;
        private float _smoothedHeadPitchRad = 0.0f;
        private float _smoothedHeadRollRad = 0.0f;

        // Whether gaze follower is actively driving head (for override behavior)
        private bool _isActive = false;

        // Max gaze angles for threshold detection
        private float _maxGazeAngleRad;

        // Threshold value (80% of max gaze angle, in radians)
        private float _gazeThresholdRad;

        // Head rotation limits (clamped after gaze computation)
        private float _maxHeadYawRad = (float)Math.PI / 3.0f;     // ±60°
        private float _maxHeadPitchRad = (float)Math.PI / 4.0f;   // ±45°
        private float _maxHeadRollRad = (float)Math.PI / 4.0f;    // ±45°

        /// <summary>
        /// Initialize the gaze head follower with max gaze angle limits.
        /// </summary>
        /// <param name="maxGazeAngleRad">Maximum gaze angle (e.g., Math.PI/9 = 20° for mesh, Math.PI/4 = 45° for 2D)</param>
        public GazeHeadFollower(float maxGazeAngleRad)
        {
            _maxGazeAngleRad = maxGazeAngleRad;
            _gazeThresholdRad = maxGazeAngleRad * GazeTriggerThreshold;

            // Configure max induced head rotation (gentle, graceful limits)
            _maxGazeInducedYawRad = (float)Math.PI / 6.0f;     // ~30°
            _maxGazeInducedPitchRad = (float)(Math.PI * 0.14);  // ~25°
        }

        /// <summary>
        /// Update the gaze head follower with current gaze and head pose.
        /// Called once per frame from the avatar rendering pipeline.
        /// </summary>
        /// <param name="elapsedMs">Elapsed time since last frame (milliseconds)</param>
        /// <param name="gazeYawRad">Current gaze yaw angle (radians)</param>
        /// <param name="gazePitchRad">Current gaze pitch angle (radians)</param>
        /// <param name="headYawRad">Current head yaw angle (radians)</param>
        /// <param name="headPitchRad">Current head pitch angle (radians)</param>
        public void Update(float elapsedMs, float gazeYawRad, float gazePitchRad, float headYawRad, float headPitchRad)
        {
            // Compute eye-relative gaze angles (what the eyes are doing relative to the head)
            // This accounts for head position as a baseline
            float eyeRelativeYaw = gazeYawRad - (headYawRad * 0.75f);
            float eyeRelativePitch = gazePitchRad - (headPitchRad * 0.55f);

            // Compute magnitude of eye gaze away from center
            float eyeGazeMagnitude = (float)Math.Sqrt(eyeRelativeYaw * eyeRelativeYaw + eyeRelativePitch * eyeRelativePitch);

            // Determine if eyes have exceeded the threshold for head turning
            _isActive = eyeGazeMagnitude >= _gazeThresholdRad;

            if (_isActive)
            {
                // Compute target head rotation based on eye position
                // Proportional to eye displacement, clamped to max induced angles
                _targetHeadYawRad = Clamp(eyeRelativeYaw * ProportionalFactor, -_maxGazeInducedYawRad, _maxGazeInducedYawRad);
                _targetHeadPitchRad = Clamp(eyeRelativePitch * ProportionalFactor, -_maxGazeInducedPitchRad, _maxGazeInducedPitchRad);
                _targetHeadRollRad = 0.0f;
            }
            else
            {
                // Below threshold, smoothly return to neutral
                _targetHeadYawRad = 0.0f;
                _targetHeadPitchRad = 0.0f;
                _targetHeadRollRad = 0.0f;
            }

            // Apply exponential moving average smoothing to head targets
            // This creates smooth, proportional head following instead of snappy jumping
            float elapsedSec = elapsedMs / 1000.0f;
            float alpha = ComputeEmaAlpha(elapsedSec, SmoothingTimeConstantSec);

            _smoothedHeadYawRad = Lerp(_smoothedHeadYawRad, _targetHeadYawRad, alpha);
            _smoothedHeadPitchRad = Lerp(_smoothedHeadPitchRad, _targetHeadPitchRad, alpha);
            _smoothedHeadRollRad = Lerp(_smoothedHeadRollRad, _targetHeadRollRad, alpha);
        }

        /// <summary>
        /// Get the current target head pose computed from gaze.
        /// </summary>
        public HeadPoseResult GetTargetHeadPose()
        {
            return new HeadPoseResult
            {
                IsActive = _isActive,
                YawRad = Clamp(_smoothedHeadYawRad, -_maxHeadYawRad, _maxHeadYawRad),
                PitchRad = Clamp(_smoothedHeadPitchRad, -_maxHeadPitchRad, _maxHeadPitchRad),
                RollRad = Clamp(_smoothedHeadRollRad, -_maxHeadRollRad, _maxHeadRollRad)
            };
        }

        /// <summary>
        /// Reset the follower to neutral state (no gaze-induced head movement).
        /// </summary>
        public void Reset()
        {
            _targetHeadYawRad = 0.0f;
            _targetHeadPitchRad = 0.0f;
            _targetHeadRollRad = 0.0f;
            _smoothedHeadYawRad = 0.0f;
            _smoothedHeadPitchRad = 0.0f;
            _smoothedHeadRollRad = 0.0f;
            _isActive = false;
        }

        /// <summary>
        /// Compute exponential moving average alpha for a given time constant.
        /// Based on standard EMA formula: alpha = 1 - exp(-2.0 / (N + 1))
        /// where time constant = N sample periods.
        /// </summary>
        private static float ComputeEmaAlpha(float deltaTimeSec, float timeConstantSec)
        {
            if (timeConstantSec <= 0.0f)
                return 1.0f;

            return 1.0f - (float)Math.Exp(-deltaTimeSec / timeConstantSec);
        }

        /// <summary>
        /// Linear interpolation between two values.
        /// </summary>
        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        /// <summary>
        /// Clamp a value between min and max.
        /// </summary>
        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>
        /// Result of head pose computation from gaze following.
        /// </summary>
        public struct HeadPoseResult
        {
            /// <summary>Whether the gaze follower is actively driving head rotation.</summary>
            public bool IsActive { get; set; }

            /// <summary>Target head yaw angle (radians, positive = right).</summary>
            public float YawRad { get; set; }

            /// <summary>Target head pitch angle (radians, positive = up).</summary>
            public float PitchRad { get; set; }

            /// <summary>Target head roll angle (radians, positive = clockwise when viewed from front).</summary>
            public float RollRad { get; set; }
        }
    }
}
