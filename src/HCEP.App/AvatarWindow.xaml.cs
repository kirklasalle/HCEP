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
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using HCEP.Core.Models;
using HCEP.Spatial;

namespace HCEP.App;

/// <summary>
/// Hosts the <see cref="AvatarCoreControl"/> and wires it into the
/// <see cref="HCEPPipelineOrchestrator"/> gaze pipeline.
///
/// ── Startup sequence ──────────────────────────────────────────
/// 1. <c>Window_Loaded</c> resolves physical screen dimensions via
///    <c>PresentationSource.CompositionTarget.TransformToDevice</c>
///    (DPI-safe; returns device pixels, not WPF DIPs).
/// 2. Calls <see cref="HCEPPipelineOrchestrator.SetAvatarEyeProvider"/> to
///    register a thread-safe delegate that queries the Avatar's eye screen
///    positions and passes the physical screen size to the math engine.
/// 3. Subscribes to <see cref="HCEPPipelineOrchestrator.GazeVectorReady"/>
///    which fires from the background pipeline loop with (pitch, yaw).
/// 4. <see cref="OnGazeVectorReady"/> dispatches to the UI thread and calls
///    <c>Avatar.SetGaze(pitch, yaw)</c> — completing the pipeline.
///
/// ── Thread safety ─────────────────────────────────────────────
/// • <c>GazeVectorReady</c> fires from a background async task.
/// • <c>AvatarCoreControl.LeftEyeScreenPos</c> / <c>RightEyeScreenPos</c>
///   are plain properties updated on the UI thread via <c>LayoutUpdated</c>.
///   They are accessed from the pipeline thread via the provider delegate —
///   the positions are <c>System.Windows.Point</c> value types, so reads are
///   atomic and no explicit locking is required for this MVP scenario.
/// </summary>
public partial class AvatarWindow : Window
{
    private readonly HCEPPipelineOrchestrator _orchestrator;
    private readonly IAvatarCatalog _avatarCatalog;
    private bool _is3DMode;
    private string _activeAvatarKey = "2d-happy";
    private IAvatarComponent _activeAvatar = null!;
    private float _screenWidthPx;
    private float _screenHeightPx;
    private Vector2[]? _lastMeshVerts;
    private (int First, int Second, int Third)[]? _lastMeshTris;
    private Vector3 _lastMeshBakedRot;

    /// <summary>
    /// When true, avatar mirrors user's gaze, expression, brows, and gestures
    /// (training/observation mode). When false (default), avatar operates
    /// autonomously using HCEP-mode-driven expressions only.
    /// User tracking and telemetry remain active in both modes.
    /// </summary>
    public bool IsMirroringEnabled { get; private set; }

    // ── TTS viseme subscription (Phase 13) ────────────────────────
    // Wired in Window_Loaded to HybridLlmEngine's TTS engine if available.
    private HCEP.Speech.HybridTtsEngine? _ttsEngine;
    // ── Phase 9 — Head gesture classifier ─────────────────────────────
    private readonly HeadGestureClassifier _gestureClassifier = new();

    // ── Phase 10 — Backchannel engine ──────────────────────────────────
    private readonly BackchannelController _backchannel = new();
    // ── Phase 10 — Expression Mirror (smile reciprocation) ────────────────────
    private readonly ExpressionMirror _expressionMirror = new();

    // ── Phase 10 — Social Gaze Controller (triangle scanning) ─────────────────
    private readonly SocialGazeController _socialGaze = new();
    private IReadOnlyList<AvatarDescriptor> _selectableAvatars = Array.Empty<AvatarDescriptor>();

    public AvatarWindow(HCEPPipelineOrchestrator orchestrator, IAvatarCatalog avatarCatalog)
    {
        _orchestrator = orchestrator;
        _avatarCatalog = avatarCatalog;
        InitializeComponent();

        Loaded += Window_Loaded;
        PreviewKeyDown += Window_PreviewKeyDown;
        Closed += (_, _) =>
        {
            _avatarCatalog.CatalogChanged -= OnCatalogChanged;
            _orchestrator.GazeVectorReady -= OnGazeVectorReady;
            _orchestrator.SnapshotReady -= OnSnapshotReady;
            _backchannel.NodRequested -= OnBackchannelNodRequested;
            _expressionMirror.SmileRequested -= OnSmileRequested;
            _socialGaze.GazeOffsetChanged -= OnSocialGazeOffsetChanged;
            if (_ttsEngine is not null)
                _ttsEngine.VisemeChanged -= OnVisemeChanged;
        };
    }

    // ── Startup ───────────────────────────────────────────────

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        float dpiScaleX = 1f, dpiScaleY = 1f;
        var src = PresentationSource.FromVisual(this);
        if (src is not null)
        {
            dpiScaleX = (float)src.CompositionTarget.TransformToDevice.M11;
            dpiScaleY = (float)src.CompositionTarget.TransformToDevice.M22;
        }
        float screenWidthPx = (float)(SystemParameters.PrimaryScreenWidth * dpiScaleX);
        float screenHeightPx = (float)(SystemParameters.PrimaryScreenHeight * dpiScaleY);

        _screenWidthPx = screenWidthPx;
        _screenHeightPx = screenHeightPx;
        RegisterEyeProvider();

        _orchestrator.GazeVectorReady += OnGazeVectorReady;
        _orchestrator.SnapshotReady += OnSnapshotReady;

        // ── Phase 9/10: gesture classifier + backchannel ──────────────────────
        _gestureClassifier.GestureDetected += OnHeadGestureDetected;
        _backchannel.NodRequested += OnBackchannelNodRequested;
        _expressionMirror.SmileRequested += OnSmileRequested;
        _socialGaze.GazeOffsetChanged += OnSocialGazeOffsetChanged;

        // ── Wire TTS viseme events for lip sync ───────────────────────
        // The TTS engine is exposed by the orchestrator when HCEP.Speech is wired in.
        // Viseme events drive SetViseme() on both avatar controls at 30fps.
        if (_orchestrator.TtsEngine is HCEP.Speech.HybridTtsEngine tts)
        {
            _ttsEngine = tts;
            _ttsEngine.VisemeChanged += OnVisemeChanged;
            _ttsEngine.SpeechCompleted += () =>
                Dispatcher.BeginInvoke(() =>
                {
                    Avatar.SetViseme(HCEP.Speech.VisemeData.Silence);
                    Avatar3D.SetViseme(HCEP.Speech.VisemeData.Silence);
                    AvatarHighPoly.SetViseme(HCEP.Speech.VisemeData.Silence);
                });
        }

        TrackingModeText.Text = "waiting";
        _activeAvatar = Avatar;
        _avatarCatalog.CatalogChanged += OnCatalogChanged;
        InitializeAvatarCatalog();
        Avatar3D.IsMirroringEnabled = IsMirroringEnabled;
        AvatarHighPoly.IsMirroringEnabled = IsMirroringEnabled;
    }

    private void OnCatalogChanged()
    {
        Dispatcher.BeginInvoke(() =>
        {
            string currentKey = _activeAvatarKey;
            InitializeAvatarCatalog();
            // Try to keep previous selection if still in catalog
            for (int i = 0; i < AvatarModeCombo.Items.Count; i++)
            {
                if (AvatarModeCombo.Items[i] is System.Windows.Controls.ComboBoxItem item
                    && item.Tag is AvatarDescriptor d && d.Key == currentKey)
                {
                    AvatarModeCombo.SelectedIndex = i;
                    break;
                }
            }
        });
    }

    private void InitializeAvatarCatalog()
    {
        _selectableAvatars = _avatarCatalog.GetSelectableAvatars();
        AvatarModeCombo.Items.Clear();
        foreach (var avatar in _selectableAvatars)
        {
            AvatarModeCombo.Items.Add(new System.Windows.Controls.ComboBoxItem
            {
                Content = avatar.DisplayName,
                Tag = avatar,
            });
        }

        AvatarModeCombo.SelectedIndex = _selectableAvatars.Count > 1 ? 0 : 0;
    }

    private void OnVisemeChanged(HCEP.Speech.VisemeData viseme)
    {
        // Viseme events fire from the TTS thread — dispatch to UI for WPF element updates.
        Dispatcher.BeginInvoke(() =>
        {
            Avatar.SetViseme(viseme);
            Avatar3D.SetViseme(viseme);
            AvatarHighPoly.SetViseme(viseme);
        });
    }

    // ── Phase 9: head gesture handler — classifier always runs (data layer). ──────
    // The avatar only mirrors the detected gesture when mirroring is enabled.

    private void OnHeadGestureDetected(HeadGestureType gesture)
    {
        // Gate: display-layer mirroring only
        if (!IsMirroringEnabled) return;

        // Called from the pipeline thread — route to UI for avatar controls.
        Dispatcher.BeginInvoke(() =>
        {
            switch (gesture)
            {
                case HeadGestureType.Nod:
                    // Phase 10: reciprocal nod
                    TriggerAvatarNod();
                    break;
                case HeadGestureType.TiltLeft:
                    // Phase 10: curiosity/interest posture — mirror the tilt
                    Avatar.TriggerTilt(-5f);
                    Avatar3D.TriggerTilt(-5f);
                    AvatarHighPoly.TriggerTilt(-5f);
                    break;
                case HeadGestureType.TiltRight:
                    Avatar.TriggerTilt(5f);
                    Avatar3D.TriggerTilt(5f);
                    AvatarHighPoly.TriggerTilt(5f);
                    break;
                case HeadGestureType.Shake:
                    // Phase 10: brief look-away (gaze aversion) in response to head-shake
                    Avatar.SetSocialGazeOffset(-0.12f, 0.005f);
                    Avatar3D.SetSocialGazeOffset(-0.12f, 0.005f);
                    AvatarHighPoly.SetSocialGazeOffset(-0.12f, 0.005f);
                    // Clear the aversion after 700ms
                    Dispatcher.BeginInvoke(async () =>
                    {
                        await System.Threading.Tasks.Task.Delay(700);
                        Avatar.SetSocialGazeOffset(0f, 0f);
                        Avatar3D.SetSocialGazeOffset(0f, 0f);
                        AvatarHighPoly.SetSocialGazeOffset(0f, 0f);
                    });
                    break;
                case HeadGestureType.ForwardLean:
                    // Phase 10: lean in → subtle forward engagement nod
                    TriggerAvatarNod();
                    break;
            }
        });
    }

    // ── Phase 10: backchannel nod handler ──────────────────────────────────

    private void OnBackchannelNodRequested()
    {
        // BackchannelController fires from the pipeline thread — dispatch to UI.
        Dispatcher.BeginInvoke(TriggerAvatarNod);
    }

    private void TriggerAvatarNod()
    {
        Avatar.TriggerNod();
        Avatar3D.TriggerNod();
        AvatarHighPoly.TriggerNod();
    }

    // ── Phase 10: expression mirror handler (display-layer gate) ───────────

    private void OnSmileRequested(float intensity)
    {
        // ExpressionMirror always detects smiles (data layer).
        // Only apply to avatar display when mirroring is enabled.
        if (!IsMirroringEnabled) return;

        // Dispatch to UI thread.
        Dispatcher.BeginInvoke(() =>
        {
            Avatar.SetSmile(intensity);
            Avatar3D.SetSmile(intensity);
            AvatarHighPoly.SetSmile(intensity);
        });
    }

    // ── Phase 10: social gaze handler ──────────────────────────────────────

    private void OnSocialGazeOffsetChanged(float yawRad, float pitchRad)
    {
        // SocialGazeController fires from the pipeline thread — dispatch to UI.
        Dispatcher.BeginInvoke(() =>
        {
            Avatar.SetSocialGazeOffset(yawRad, pitchRad);
            Avatar3D.SetSocialGazeOffset(yawRad, pitchRad);
            AvatarHighPoly.SetSocialGazeOffset(yawRad, pitchRad);
        });
    }

    // ── Mode switch ────────────────────────────────────

    private void AvatarMode_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (AvatarModeCombo.SelectedIndex < 0) return;
        var descriptor = GetSelectedAvatarDescriptor();
        if (descriptor is not null)
            ApplyAvatarDescriptor(descriptor);
    }

    /// <summary>Externally switch the avatar mode (called from MainViewModel).</summary>
    public void SetAvatarMode(bool use3D)
    {
        // Guard: called from VM before the window may be fully loaded
        if (!IsLoaded) return;
        Dispatcher.BeginInvoke(() =>
        {
            int index = _selectableAvatars
                .Select((avatar, idx) => new { avatar, idx })
                .FirstOrDefault(entry => entry.avatar.Use3DMode == use3D)?.idx ?? 0;
            AvatarModeCombo.SelectedIndex = index;
            ApplyMode(use3D);
        });
    }

    private void ApplyMode(bool use3D)
    {
        var descriptor = _selectableAvatars.FirstOrDefault(a => a.Use3DMode == use3D)
            ?? _selectableAvatars.FirstOrDefault()
            ?? new AvatarDescriptor("2d-happy", "2D Happy", false, true, string.Empty);
        ApplyAvatarDescriptor(descriptor);
    }

    private void ApplyAvatarDescriptor(AvatarDescriptor descriptor)
    {
        _activeAvatarKey = descriptor.Key;
        _is3DMode = descriptor.Use3DMode;

        bool isHappy = descriptor.Key == "2d-happy";
        bool is3DWire = descriptor.Key == "3d-wireframe";
        bool is3DHighPoly = descriptor.Key == "3d-highpoly-wireframe";
        bool isCustom = !isHappy && !is3DWire && !is3DHighPoly;

        Avatar.Visibility = isHappy ? Visibility.Visible : Visibility.Collapsed;
        Avatar3D.Visibility = is3DWire ? Visibility.Visible : Visibility.Collapsed;
        AvatarHighPoly.Visibility = is3DHighPoly ? Visibility.Visible : Visibility.Collapsed;
        CustomAvatarHost.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;

        if (isCustom)
        {
            var customComponent = _avatarCatalog.CreateAvatarInstance(descriptor.Key);
            if (customComponent is FrameworkElement elem)
            {
                CustomAvatarHost.Content = elem;
                _activeAvatar = customComponent;
            }
            else
            {
                _activeAvatar = Avatar;
            }
        }
        else
        {
            _activeAvatar = descriptor.Key switch
            {
                "3d-wireframe" => Avatar3D,
                "3d-highpoly-wireframe" => AvatarHighPoly,
                _ => Avatar,
            };
        }

        Title = descriptor.Key switch
        {
            "3d-wireframe" => "HCEP — True Gaze Avatar (3D Wireframe)",
            "3d-highpoly-wireframe" => "HCEP — True Gaze Avatar (3D High-Poly Wireframe)",
            _ => $"HCEP — {descriptor.DisplayName}",
        };

        if (descriptor.Key == "3d-highpoly-wireframe")
        {
            MeshStatusText.Text = $"HP {AvatarHighPoly.MeshVertexCount}V";
            MeshStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
        }
        else if (isCustom)
        {
            MeshStatusText.Text = "CUSTOM SVG";
            MeshStatusText.Foreground = System.Windows.Media.Brushes.Cyan;
        }

        // Re-register provider so GazeVectorEngine reads the active control's eye positions.
        if (_screenWidthPx > 0) RegisterEyeProvider();
    }

    private AvatarDescriptor? GetSelectedAvatarDescriptor()
    {
        if (AvatarModeCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item
            && item.Tag is AvatarDescriptor descriptor)
            return descriptor;
        return null;
    }

    /// <summary>
    /// Registers the eye-socket screen-position provider with the orchestrator.
    /// The delegate evaluates <c>_is3DMode</c> at call time so it automatically
    /// returns positions from whichever avatar control is currently active.
    /// </summary>
    private void RegisterEyeProvider()
    {
        _orchestrator.SetAvatarEyeProvider(
            provider: () => _activeAvatarKey switch
            {
                "3d-wireframe" => (
                    new Vector2((float)Avatar3D.LeftEyeScreenPos.X, (float)Avatar3D.LeftEyeScreenPos.Y),
                    new Vector2((float)Avatar3D.RightEyeScreenPos.X, (float)Avatar3D.RightEyeScreenPos.Y)),
                "3d-highpoly-wireframe" => (
                    new Vector2((float)AvatarHighPoly.LeftEyeScreenPos.X, (float)AvatarHighPoly.LeftEyeScreenPos.Y),
                    new Vector2((float)AvatarHighPoly.RightEyeScreenPos.X, (float)AvatarHighPoly.RightEyeScreenPos.Y)),
                _ => (
                    new Vector2((float)Avatar.LeftEyeScreenPos.X, (float)Avatar.LeftEyeScreenPos.Y),
                    new Vector2((float)Avatar.RightEyeScreenPos.X, (float)Avatar.RightEyeScreenPos.Y)),
            },
            screenWidthPhysicalPx: _screenWidthPx,
            screenHeightPhysicalPx: _screenHeightPx);
    }

    // ── Mesh data callback (from SnapshotReady, background thread) ─

    private void OnSnapshotReady(SceneSnapshot snapshot)
    {
        var face = snapshot.PrimaryPerson?.Face;

        // When no FaceFrame exists, GazeVectorReady never fires — update status here so
        // the HUD doesn't stay frozen on "waiting" while the pipeline is alive.
        if (face is null)
        {
            Dispatcher.BeginInvoke(() =>
            {
                TrackingModeText.Text = snapshot.PrimaryPerson is null ? "SEARCHING" : "NO FACE";
                TrackingModeText.Foreground = System.Windows.Media.Brushes.Gray;
                MeshStatusText.Text = "—";
            });
        }
        else if (!face.IsTracked)
        {
            // Face detected but tracking quality below threshold — show distinct LOST state
            // so the operator can tell this apart from FALLBACK (tracked, low precision).
            Dispatcher.BeginInvoke(() =>
            {
                TrackingModeText.Text = "LOST";
                TrackingModeText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            });
        }

        // Always push feature points — Avatar3D needs them for eye socket gaze tracking
        // regardless of whether the full mesh or edge-chain fallback is active.
        // Also set head pose on both avatars for gaze-driven head turning and
        // for Avatar3D's correction math against the projected Candide mesh.
        if (face is { IsTracked: true, FeaturePoints2D.Length: > 0 })
        {
            Avatar3D.UpdateEyeData(face.FeaturePoints2D);
            // Head pose is data, not mirroring. Avatar3D needs it every tracked
            // frame so the live projected mesh and the display-pose correction
            // stay in the same reference frame.
            Avatar3D.SetHeadPose(face.HeadRotation);
            AvatarHighPoly.SetHeadPose(face.HeadRotation);

            if (IsMirroringEnabled)
                Avatar.SetHeadPose(face.HeadRotation);

            // ── Phase 9: feed head pose to gesture classifier ────────────────
            _gestureClassifier.Update(
                face.HeadRotation.X,
                face.HeadRotation.Y,
                face.HeadRotation.Z,
                snapshot.PrimaryPerson?.DistanceM ?? 1.5f);

            // ── Eyebrow animation ──────────────────────────────────────────────
            // Extract AU3 (BrowLowerer) and AU5 (OuterBrowRaiser) from Kinect AUs.
            // Data layer: AUs are always read for telemetry and HCEP classification.
            // Display layer: user AU values are only applied to the avatar when mirroring.
            var aus = face.ActionUnits;
            float rawAuRaise = aus.Length > (int)HCEP.Core.Enums.ActionUnit.OuterBrowRaiser
                ? aus[(int)HCEP.Core.Enums.ActionUnit.OuterBrowRaiser] : 0f;
            float rawAuLower = aus.Length > (int)HCEP.Core.Enums.ActionUnit.BrowLowerer
                ? aus[(int)HCEP.Core.Enums.ActionUnit.BrowLowerer] : 0f;
            float auRaise = IsMirroringEnabled ? rawAuRaise : 0f;
            float auLower = IsMirroringEnabled ? rawAuLower : 0f;

            var hcep = snapshot.PrimaryPerson?.LatestHcep;
            float modeFurrow = hcep?.Mode switch
            {
                HCEP.Core.Enums.HcepMode.Logic => 0.30f,
                HCEP.Core.Enums.HcepMode.Think => 0.50f,
                HCEP.Core.Enums.HcepMode.Heart => 0.00f,
                HCEP.Core.Enums.HcepMode.Affect => 0.00f,
                HCEP.Core.Enums.HcepMode.Spirit => 0.00f,
                _ => 0.10f,
            };
            float modeRaise = hcep?.Mode switch
            {
                HCEP.Core.Enums.HcepMode.Heart => 0.35f,  // inner empathy raise (AU1)
                HCEP.Core.Enums.HcepMode.Affect => 0.12f,  // open/engaged
                _ => 0.00f,
            };
            float blendedRaise = Math.Max(auRaise, modeRaise);

            Avatar3D.SetBrows(blendedRaise, auLower, modeFurrow);
            AvatarHighPoly.SetBrows(blendedRaise, auLower, modeFurrow);
            Avatar.SetBrows(blendedRaise, auLower, modeFurrow);
        }
        else if (face is not null && !face.IsTracked)
        {
            _gestureClassifier.Reset();
        }

        // ── Phase 10: feed snapshot to backchannel engine ────────────────────
        // Workstream C: pass latest cadence so nod intervals are speech-rate aware.
        _backchannel.CurrentCadence = _orchestrator.LatestCadence;
        _backchannel.OnSnapshot(snapshot);

        // ── Phase 10: feed snapshot to expression mirror ─────────────────────
        _expressionMirror.OnSnapshot(snapshot);

        // ── Phase 10: update social gaze + proxemic state ────────────────────
        var hcepMode = snapshot.PrimaryPerson?.LatestHcep?.Mode ?? HCEP.Core.Enums.HcepMode.Unknown;
        float distM = snapshot.PrimaryPerson?.DistanceM ?? 1.5f;
        _socialGaze.Update(hcepMode, distM);

        // Push proxemic distance to both avatars
        Dispatcher.BeginInvoke(() =>
        {
            Avatar.SetProxemicDistance(distM);
            Avatar3D.SetProxemicDistance(distM);
            AvatarHighPoly.SetProxemicDistance(distM);
        });

        // 3D wireframe avatar: always prefer the same live Candide-3 projected
        // mesh the dashboard renders. Mirroring controls expression/gaze display
        // behavior, not whether the avatar is allowed to receive the real mesh.
        if (_activeAvatarKey == "3d-highpoly-wireframe")
        {
            Dispatcher.BeginInvoke(() =>
            {
                MeshStatusText.Text = $"HP {AvatarHighPoly.MeshVertexCount}V";
                MeshStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
            });
            return;
        }

        if (_activeAvatarKey != "3d-wireframe") return;
        if (face is null) return;

        Vector2[]? meshToUse;
        Vector3 bakedRotation;

        if (face.FaceMeshVertices2D is { Length: > 0 })
        {
            meshToUse = face.FaceMeshVertices2D;
            bakedRotation = face.HeadRotation;
        }
        else if (face.NeutralFaceMeshVertices2D is { Length: > 0 })
        {
            meshToUse = face.NeutralFaceMeshVertices2D;
            bakedRotation = System.Numerics.Vector3.Zero;
        }
        else
        {
            meshToUse = null;
            bakedRotation = System.Numerics.Vector3.Zero;
        }

        if (meshToUse is { Length: > 0 } && face.FaceMeshTriangles is { Length: > 0 })
        {
            var tris = face.FaceMeshTriangles;
            _lastMeshVerts = meshToUse;
            _lastMeshTris = tris;
            _lastMeshBakedRot = bakedRotation;
            Dispatcher.BeginInvoke(() =>
            {
                Avatar3D.SetMesh(meshToUse, tris, bakedRotation);
                MeshStatusText.Text = $"MESH {Avatar3D.MeshVertexCount}V";
                MeshStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
            });
        }
        else if (_lastMeshVerts is { Length: > 0 } && _lastMeshTris is { Length: > 0 })
        {
            // Keep rendering the last known good high-poly mesh if a frame misses.
            // This prevents fallback FP overwrite/blanking while tracking reacquires.
            var verts = _lastMeshVerts;
            var tris = _lastMeshTris;
            var cachedRot = _lastMeshBakedRot;
            Dispatcher.BeginInvoke(() =>
            {
                Avatar3D.SetMesh(verts, tris, cachedRot);
                MeshStatusText.Text = $"MESH {Avatar3D.MeshVertexCount}V";
                MeshStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
            });
        }
        else if (face.FeaturePoints2D is { Length: > 0 })
        {
            // Full mesh call failed AND no cached mesh exists yet.
            // Feed FP data into Avatar3D — SetFeaturePoints honours the
            // persistent-mesh contract: if a mesh has ever been acquired,
            // it stays drawn and only eye anchoring is refreshed here.
            // Only in the cold-start case (no mesh ever received) does the
            // FP data seed a first-frame visual.
            var pts = face.FeaturePoints2D;
            var bakedRot = face.HeadRotation;
            // Distinguish states in the HUD:
            //   • Mesh persisted, live FP driving pose/eyes → "MESH+FP" (green)
            //   • Cold start, no mesh ever received         → HRESULT or "FP" (red/orange)
            bool haveMesh = _lastMeshVerts is { Length: > 0 } && _lastMeshTris is { Length: > 0 };
            string hrLabel = haveMesh
                ? "MESH+FP"
                : (face.MeshHr != 0 ? $"0x{face.MeshHr:X8}" : "FP");
            Dispatcher.BeginInvoke(() =>
            {
                Avatar3D.SetFeaturePoints(pts, bakedRot);
                MeshStatusText.Text = hrLabel;
                MeshStatusText.Foreground = haveMesh
                    ? System.Windows.Media.Brushes.LightGreen
                    : (face.MeshHr != 0
                        ? System.Windows.Media.Brushes.Red
                        : System.Windows.Media.Brushes.Orange);
            });
        }
    }

    // ── Gaze callback (arrives from background thread) ────────

    private void OnGazeVectorReady(float pitch, float yaw, float distanceM, bool isPrecision)
    {
        // Marshal to UI thread, update Avatar pupils and HUD.
        Dispatcher.BeginInvoke(() =>
        {
            // Display-layer gate: only drive avatar pupils when mirroring is enabled.
            // Telemetry HUD always updates — that's data, not mirroring.
            if (IsMirroringEnabled)
                _activeAvatar.SetGaze(pitch, yaw, distanceM);

            PitchText.Text = $"{pitch * 180f / MathF.PI:+0.0;-0.0;+0.0}°";
            YawText.Text = $"{yaw * 180f / MathF.PI:+0.0;-0.0;+0.0}°";
            DistanceText.Text = $"{distanceM:F2} m";

            if (isPrecision)
            {
                TrackingModeText.Text = "PRECISION";
                TrackingModeText.Foreground = Brushes.LightGreen;
            }
            else
            {
                TrackingModeText.Text = "FALLBACK";
                TrackingModeText.Foreground = Brushes.Orange;
            }
        });
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Manual blink test: press B while 2D Happy avatar is active.
        if (e.Key != Key.B || _is3DMode) return;

        Avatar.TriggerBlink();
        TrackingModeText.Text = "BLINK";
        TrackingModeText.Foreground = Brushes.LightBlue;
        e.Handled = true;
    }

    // ── Mirror toggle handler ─────────────────────────────────────────────

    private void MirrorToggle_Changed(object sender, RoutedEventArgs e)
    {
        IsMirroringEnabled = MirrorToggle.IsChecked == true;
        Avatar3D.IsMirroringEnabled = IsMirroringEnabled;
        AvatarHighPoly.IsMirroringEnabled = IsMirroringEnabled;

        // Update visual state indicator
        MirrorStateText.Text = IsMirroringEnabled ? "ON" : "OFF";
        MirrorStateText.Foreground = IsMirroringEnabled
            ? System.Windows.Media.Brushes.Cyan
            : new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0x88, 0x88, 0x88, 0x88));

        // When disabling mirroring, reset avatar to neutral pose so it doesn't
        // freeze on the last mirrored state.
        if (!IsMirroringEnabled)
        {
            _activeAvatar.ResetGaze();
            _activeAvatar.SetSmile(0f);
            _activeAvatar.SetBrows(0f, 0f, 0f);
            Avatar.SetHeadPose(Vector3.Zero);
            Avatar3D.SetHeadPose(Vector3.Zero);
            AvatarHighPoly.SetHeadPose(Vector3.Zero);
        }
    }
}
