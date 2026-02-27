// --------------------------------------------------------------
// HCEP — Human Communication Eye Protocol
// Copyright — 2026 Kirk LaSalle. All rights reserved.
// --------------------------------------------------------------

using System.IO;
using System.Windows;
using System.Windows.Threading;
using HCEP.Audio;
using HCEP.Core.Interfaces;
using HCEP.Intelligence;
using HCEP.Kinect;
using HCEP.Knowledge;
using HCEP.Spatial;
using HCEP.Telemetry;
using HCEP.Vision;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HCEP.App;

/// <summary>
/// Application entry point with Microsoft.Extensions.Hosting DI container.
/// </summary>
public partial class App : Application
{
    private IHost? _host;
    private ILogger? _appLogger;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        // ── Global exception handlers to catch and log unhandled crashes ──
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((ctx, services) =>
            {
                // -- Logging ------------------------------------
                var loggerFactory = LoggingConfiguration.CreateLoggerFactory();
                services.AddSingleton(loggerFactory);
                services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));

                // -- Telemetry ----------------------------------
                services.AddSingleton<ITelemetryService, HCEPTelemetryService>();

                // -- Sensor -------------------------------------
                // Use simulated source when Kinect SDK not available
                // Always register SimulatedSensorSource as fallback
                services.AddSingleton<SimulatedSensorSource>();

                if (KinectSdkAvailable())
                    services.AddSingleton<ISensorSource, KinectSensorSource>();
                else
                    services.AddSingleton<ISensorSource>(sp =>
                        sp.GetRequiredService<SimulatedSensorSource>());

                // -- Spatial ------------------------------------
                services.AddSingleton<IGazeEstimator, ThreeStageGazeEstimator>();

                // -- Vision -------------------------------------
                services.AddSingleton<IHcepAnalyzer, HcepModeAnalyzer>();
                services.AddSingleton<IFaceRecognizer, ArcFaceRecognizer>();
                services.AddSingleton<VisionPipeline>();

                // -- Audio --------------------------------------
                services.AddSingleton<ISpeechRecognizer, WhisperSpeechRecognizer>();
                services.AddSingleton<AudioPipeline>();

                // -- Knowledge (Strategy D: UKS Hybrid Adapter) -
                services.AddHCEPKnowledge();

                // -- Intelligence (Agentic LLM) ----------------
                services.AddSingleton<AgenticToolExecutor>();
                services.AddHttpClient<HybridLlmEngine>();
                services.AddSingleton<ILlmEngine, HybridLlmEngine>();

                // -- Pipeline Orchestrator ----------------------
                services.AddSingleton<HCEPPipelineOrchestrator>();
                services.AddSingleton<IPipelineOrchestrator>(sp =>
                    sp.GetRequiredService<HCEPPipelineOrchestrator>());

                // -- UI -----------------------------------------
                services.AddTransient<MainViewModel>();
                services.AddTransient<MainWindow>();
                services.AddTransient<SensorViewViewModel>();
                services.AddTransient<SensorViewWindow>();
                services.AddTransient<KinectVideoViewModel>();
                services.AddTransient<KinectVideoWindow>();
                services.AddTransient<CalibrationWindow>();
                services.AddTransient<AvatarWindow>();
            })
            .Build();

        _appLogger = _host.Services.GetRequiredService<ILogger<App>>();

        // ── Window routing: --window avatar launches Avatar directly ─
        bool avatarMode = e.Args.Length > 0 &&
            e.Args[0].Equals("--window", StringComparison.OrdinalIgnoreCase) &&
            e.Args.Length > 1 &&
            e.Args[1].Equals("avatar", StringComparison.OrdinalIgnoreCase);

        if (avatarMode)
        {
            // Start pipeline so the Avatar gets live gaze data
            var orchestrator = _host.Services.GetRequiredService<HCEPPipelineOrchestrator>();
            _ = orchestrator.StartAsync();

            var avatarWindow = _host.Services.GetRequiredService<AvatarWindow>();
            avatarWindow.Show();
        }
        else
        {
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _appLogger?.LogCritical(e.Exception, "FATAL unhandled UI exception");
        try
        {
            // Flush Serilog so the crash is written before the process dies
            Serilog.Log.CloseAndFlush();
        }
        catch { /* best-effort */ }
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            _appLogger?.LogCritical(ex, "FATAL unhandled AppDomain exception (isTerminating={IsTerminating})", e.IsTerminating);
        else
            _appLogger?.LogCritical("FATAL unhandled AppDomain exception: {Error} (isTerminating={IsTerminating})",
                e.ExceptionObject, e.IsTerminating);
        try { Serilog.Log.CloseAndFlush(); } catch { }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _appLogger?.LogError(e.Exception, "Unobserved task exception (observed={Observed})", e.Observed);
        e.SetObserved(); // Prevent process termination
    }

    private static bool KinectSdkAvailable()
    {
        string? sdkDir = Environment.GetEnvironmentVariable("KINECTSDK10_DIR");
        return !string.IsNullOrEmpty(sdkDir) && Directory.Exists(sdkDir);
    }
}

