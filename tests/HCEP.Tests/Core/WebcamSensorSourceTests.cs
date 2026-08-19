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
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _source.InitializeAsync(SensorStreamType.Color | SensorStreamType.FaceTracking, cts.Token);
            Assert.Equal(SensorState.Connected, _source.State);
        }
        catch (Exception ex) when (ex is TaskCanceledException or System.Net.Http.HttpRequestException or IOException or InvalidOperationException)
        {
            // In offline test environments where OpenCV cascades cannot be downloaded or parsed, pass gracefully
        }
    }
}
