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
using HCEP.Plugin.Api;
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
                var minLogLevel = LogLevel.Information;
                string? logLevelEnv = Environment.GetEnvironmentVariable("HCEP_LOG_LEVEL");
                if (!string.IsNullOrEmpty(logLevelEnv) && Enum.TryParse<LogLevel>(logLevelEnv, true, out var parsedLevel))
                {
                    minLogLevel = parsedLevel;
                }
                string? logDirEnv = Environment.GetEnvironmentVariable("HCEP_LOG_DIR");
                var loggerFactory = LoggingConfiguration.CreateLoggerFactory(logDirectory: logDirEnv, minimumLevel: minLogLevel);
                services.AddSingleton(loggerFactory);
                services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));

                // -- Telemetry ----------------------------------
                services.AddSingleton<ITelemetryService, HCEPTelemetryService>();

                // -- Sensor -------------------------------------
                // Register all available sources
                services.AddSingleton<SimulatedSensorSource>();
                services.AddSingleton<WebcamSensorSource>();
                if (KinectSdkAvailable())
                {
                    services.AddSingleton<KinectSensorSource>();
                }

                services.AddSingleton<ISensorSource>(sp =>
                {
                    string? sensorTypeEnv = Environment.GetEnvironmentVariable("HCEP_SENSOR_TYPE");
                    if (!string.IsNullOrEmpty(sensorTypeEnv))
                    {
                        if (sensorTypeEnv.Equals("Kinect", StringComparison.OrdinalIgnoreCase))
                        {
                            return KinectSdkAvailable()
                                ? sp.GetRequiredService<KinectSensorSource>()
                                : sp.GetRequiredService<WebcamSensorSource>(); // Fallback if Kinect SDK not installed
                        }
                        if (sensorTypeEnv.Equals("Webcam", StringComparison.OrdinalIgnoreCase))
                        {
                            return sp.GetRequiredService<WebcamSensorSource>();
                        }
                        return sp.GetRequiredService<SimulatedSensorSource>();
                    }

                    // Default auto-detection path: Kinect -> Webcam -> Simulated
                    if (KinectSdkAvailable())
                    {
                        return sp.GetRequiredService<KinectSensorSource>();
                    }
                    return sp.GetRequiredService<WebcamSensorSource>();
                });

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

                // -- Plugin API Server --------------------------
                services.AddHostedService<PluginApiServer>();

                // -- UI -----------------------------------------
                services.AddTransient<MainViewModel>();
                services.AddTransient<MainWindow>();
                services.AddTransient<SensorViewViewModel>();
                services.AddTransient<SensorViewWindow>();
                services.AddTransient<KinectVideoViewModel>();
                services.AddTransient<KinectVideoWindow>();
                services.AddTransient<CalibrationWindow>();
                services.AddTransient<AvatarWindow>();
                services.AddTransient<SettingsWindow>();
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

