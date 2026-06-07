// ──────────────────────────────────────────────────────────────
// HCEP SDK — Unity Gaze Controller
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace HCEP.Sdk.Unity;

/// <summary>
/// Unity component to drive avatar head and eye rotations in real-time
/// using the HCEP WebSocket stream.
/// </summary>
public sealed class HcepGazeController : MonoBehaviour
{
    [Header("Connection Settings")]
    [Tooltip("HCEP WebSocket server endpoint.")]
    public string serverUrl = "ws://localhost:5000/ws/stream";

    [Header("Avatar Rig Bones")]
    [Tooltip("The avatar's left eye transform.")]
    public Transform leftEyeBone;
    [Tooltip("The avatar's right eye transform.")]
    public Transform rightEyeBone;
    [Tooltip("The avatar's head bone transform.")]
    public Transform headBone;

    [Header("Tracking Constraints")]
    [Range(0f, 90f)]
    public float maxEyeYaw = 35f;
    [Range(0f, 90f)]
    public float maxEyePitch = 25f;
    [Range(0f, 90f)]
    public float maxHeadYaw = 45f;
    [Range(0f, 90f)]
    public float maxHeadPitch = 30f;

    [Header("Smoothing")]
    [Range(0.01f, 1f)]
    public float smoothingFactor = 0.15f;

    private ClientWebSocket _webSocket;
    private CancellationTokenSource _cts;
    private Thread _receiveThread;

    // Thread-safe target values from HCEP
    private Vector3 _targetGazeDirection;
    private Vector3 _targetHeadRotation; // pitch, yaw, roll

    private Vector3 _currentGazeDirection;
    private Vector3 _currentHeadRotation;

    private void Start()
    {
        _cts = new CancellationTokenSource();
        _currentGazeDirection = Vector3.forward;
        
        // Start background connection thread
        _receiveThread = new Thread(async () => await ReceiveLoopAsync(_cts.Token))
        {
            IsBackground = true
        };
        _receiveThread.Start();
    }

    private void Update()
    {
        // Smoothly interpolate rotations on the main Unity thread
        _currentGazeDirection = Vector3.Slerp(_currentGazeDirection, _targetGazeDirection, smoothingFactor);
        _currentHeadRotation = Vector3.Lerp(_currentHeadRotation, _targetHeadRotation, smoothingFactor);

        // Apply Head Rotation (pitch, yaw, roll)
        if (headBone != null)
        {
            float pitch = Mathf.Clamp(_currentHeadRotation.x, -maxHeadPitch, maxHeadPitch);
            float yaw = Mathf.Clamp(_currentHeadRotation.y, -maxHeadYaw, maxHeadYaw);
            float roll = Mathf.Clamp(_currentHeadRotation.z, -20f, 20f);
            headBone.localRotation = Quaternion.Euler(pitch, yaw, roll);
        }

        // Apply Eye Rotation
        if (_currentGazeDirection != Vector3.zero)
        {
            Quaternion gazeRot = Quaternion.LookRotation(_currentGazeDirection, Vector3.up);
            Vector3 euler = gazeRot.eulerAngles;

            // Normalize angles to -180..180
            float eyeYaw = NormalizeAngle(euler.y);
            float eyePitch = NormalizeAngle(euler.x);

            eyeYaw = Mathf.Clamp(eyeYaw, -maxEyeYaw, maxEyeYaw);
            eyePitch = Mathf.Clamp(eyePitch, -maxEyePitch, maxEyePitch);

            Quaternion targetEyeRot = Quaternion.Euler(eyePitch, eyeYaw, 0f);

            if (leftEyeBone != null) leftEyeBone.localRotation = targetEyeRot;
            if (rightEyeBone != null) rightEyeBone.localRotation = targetEyeRot;
        }
    }

    private void OnDestroy()
    {
        _cts?.Cancel();
        _webSocket?.Dispose();
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _webSocket = new ClientWebSocket();
                await _webSocket.ConnectAsync(new Uri(serverUrl), ct);
                Debug.Log($"[HCEP Unity SDK] Connected to {serverUrl}");

                byte[] buffer = new byte[1024 * 8];
                while (_webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close) break;

                    string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    ParseHcepFrame(json);
                }
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    Debug.LogWarning($"[HCEP Unity SDK] Connection dropped or failed: {ex.Message}. Retrying in 3s...");
                    await Task.Delay(3000, ct);
                }
            }
            finally
            {
                _webSocket?.Dispose();
            }
        }
    }

    private void ParseHcepFrame(string json)
    {
        try
        {
            // Lightweight JSON parsing to avoid full framework dependency overhead in Unity
            int primaryPersonIdx = json.IndexOf("\"primaryPerson\"");
            if (primaryPersonIdx == -1 || json.Contains("\"primaryPerson\":null")) return;

            // Extract gazeDirection (x, y, z)
            int gazeIdx = json.IndexOf("\"gazeDirection\"");
            if (gazeIdx != -1)
            {
                float x = ExtractJsonFloat(json, "x", gazeIdx);
                float y = ExtractJsonFloat(json, "y", gazeIdx);
                float z = ExtractJsonFloat(json, "z", gazeIdx);
                // Convert Kinect/OpenCV coordinate system to Unity coordinate system
                // Kinect: +X right, +Y up, +Z forward. Unity matches this, but z rotation needs calibration.
                _targetGazeDirection = new Vector3(x, y, z).normalized;
            }

            // Extract headRotation (pitch, yaw, roll)
            int headRotIdx = json.IndexOf("\"headRotation\"");
            if (headRotIdx != -1)
            {
                float pitch = ExtractJsonFloat(json, "pitch", headRotIdx);
                float yaw = ExtractJsonFloat(json, "yaw", headRotIdx);
                float roll = ExtractJsonFloat(json, "roll", headRotIdx);
                _targetHeadRotation = new Vector3(pitch, yaw, roll);
            }
        }
        catch (Exception)
        {
            // Suppress parse warnings on transient frame corruption
        }
    }

    private static float ExtractJsonFloat(string json, string propertyName, int searchOffset)
    {
        string pattern = $"\"{propertyName}\":";
        int index = json.IndexOf(pattern, searchOffset);
        if (index == -1) return 0f;

        int start = index + pattern.Length;
        int end = json.IndexOfAny(new char[] { ',', '}', ']' }, start);
        if (end == -1) return 0f;

        string valStr = json.Substring(start, end - start).Trim().Replace(":", "");
        float.TryParse(valStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float result);
        return result;
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }
}
