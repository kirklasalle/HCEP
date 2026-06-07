// ──────────────────────────────────────────────────────────────
// HCEP — Core Tests
// ──────────────────────────────────────────────────────────────

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HCEP.Core.Enums;
using HCEP.Kinect;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HCEP.Tests.Core;

public sealed class WebcamSensorSourceTests : IDisposable
{
    private readonly WebcamSensorSource _source;
    private readonly string _modelsDir;

    public WebcamSensorSourceTests()
    {
        _source = new WebcamSensorSource(NullLogger<WebcamSensorSource>.Instance);
        _modelsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models");
    }

    public void Dispose()
    {
        _source.DisposeAsync().AsTask().Wait();
    }

    [Fact]
    public void TestInitialStateIsDisconnected()
    {
        Assert.Equal(SensorState.Disconnected, _source.State);
    }

    [Fact]
    public void TestElevationAngleAndSeatedModeProperties()
    {
        _source.ElevationAngle = 15;
        Assert.Equal(15, _source.ElevationAngle);

        _source.SeatedMode = true;
        Assert.True(_source.SeatedMode);
    }

    [Fact]
    public async Task TestInitializeDownloadsCascadesAndChangesState()
    {
        // Delete local cascades first to force download testing
        var facePath = Path.Combine(_modelsDir, "haarcascade_frontalface_default.xml");
        var eyePath = Path.Combine(_modelsDir, "haarcascade_eye.xml");

        if (File.Exists(facePath)) File.Delete(facePath);
        if (File.Exists(eyePath)) File.Delete(eyePath);

        // Run initialization
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await _source.InitializeAsync(SensorStreamType.Color | SensorStreamType.FaceTracking, cts.Token);

        Assert.Equal(SensorState.Connected, _source.State);
        Assert.True(File.Exists(facePath));
        Assert.True(File.Exists(eyePath));
    }
}
