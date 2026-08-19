// ──────────────────────────────────────────────────────────────
// HCEP — Unit Tests: Avatar Catalog & SvgAvatar Generation
// ──────────────────────────────────────────────────────────────
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using HCEP.App;
using HCEP.Speech;
using Xunit;

namespace HCEP.Tests.App;

public sealed class AvatarCatalogAndStudioTests
{
    private sealed class TestAvatarComponent : IAvatarComponent
    {
        public string Name { get; set; } = "Test";
        public void SetGaze(float pitchRad, float yawRad, float userDistanceM = 1.5f) { }
        public void SetViseme(VisemeData viseme) { }
        public void SetBrows(float outerBrowRaise, float browLower, float hcepModeFurrow = 0) { }
        public void ResetGaze() { }
        public void TriggerNod() { }
        public void TriggerTilt(float rollDeg = 6) { }
        public void SetSmile(float intensity) { }
        public void SetSocialGazeOffset(float yawRad, float pitchRad) { }
        public void SetProxemicDistance(float distanceM) { }
    }

    private static void RunInStaThread(Action action)
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current is null)
                {
                    _ = new Application();
                }
                action();
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (ex is not null) throw new TargetInvocationException(ex);
    }

    [Fact]
    public void AvatarCatalog_BuiltInAvatars_Contains2DAnd3DOptions()
    {
        var catalog = new AvatarCatalog();
        var selectable = catalog.GetSelectableAvatars();

        Assert.NotEmpty(selectable);
        Assert.Contains(selectable, a => a.Key == "2d-happy");
        Assert.Contains(selectable, a => a.Key == "3d-wireframe");
        Assert.Contains(selectable, a => a.Key == "3d-highpoly-wireframe");
    }

    [Fact]
    public void AvatarCatalog_RegisterCustomAvatar_TriggersCatalogChangedAndEnablesFactory()
    {
        var catalog = new AvatarCatalog();
        bool changedFired = false;
        catalog.CatalogChanged += () => changedFired = true;

        var customDesc = new AvatarDescriptor(
            Key: "custom-neon-bot",
            DisplayName: "⭐ Neon Bot",
            Use3DMode: false,
            IsImplemented: true,
            Summary: "Custom studio avatar test");

        catalog.RegisterCustomAvatar(customDesc, () => new TestAvatarComponent
        {
            Name = "Neon Bot"
        });

        Assert.True(changedFired);
        var selectable = catalog.GetSelectableAvatars();
        Assert.Contains(selectable, a => a.Key == "custom-neon-bot");

        var instance = catalog.CreateAvatarInstance("custom-neon-bot");
        Assert.NotNull(instance);
        Assert.IsType<TestAvatarComponent>(instance);
        var custom = (TestAvatarComponent)instance;
        Assert.Equal("Neon Bot", custom.Name);
    }

    [Fact]
    public void SvgAvatarControl_GenerateSvgMarkup_ProducesValidSvgXml()
    {
        RunInStaThread(() =>
        {
            var ctrl = new SvgAvatarControl
            {
                AvatarName = "Test Cyber",
                SkinColor = Color.FromRgb(10, 20, 30),
                AccentGlowColor = Color.FromRgb(0, 220, 255),
                IrisColor = Color.FromRgb(0, 255, 180),
                EyeRadiusX = 30,
                EyeRadiusY = 40,
                PupilRadius = 14,
                EyeSpacing = 75,
                BrowThickness = 4.0,
                ShowCyberneticAccents = true
            };

            string svg = ctrl.GenerateSvgMarkup(512, 512);

            Assert.NotNull(svg);
            Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\"", svg.TrimStart());
            Assert.EndsWith("</svg>", svg.TrimEnd());
            Assert.Contains("viewBox=\"0 0 512 512\"", svg);
            Assert.Contains("filter id=\"glow\"", svg);
            Assert.Contains("rx=\"120\" ry=\"140\"", svg); // Head rect
        });
    }
}
