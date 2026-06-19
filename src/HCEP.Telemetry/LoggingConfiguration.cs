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
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace HCEP.Telemetry;

/// <summary>
/// Configures Serilog-based structured logging for the HCEP platform.
/// </summary>
public static class LoggingConfiguration
{
    /// <summary>
    /// Creates an <see cref="ILoggerFactory"/> with Serilog sinks for
    /// console and rolling file output.
    /// </summary>
    /// <param name="logDirectory">Directory for log files.</param>
    /// <param name="minimumLevel">Minimum log level.</param>
    public static ILoggerFactory CreateLoggerFactory(
        string? logDirectory = null,
        LogLevel minimumLevel = LogLevel.Information)
    {
        logDirectory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HCEP", "Logs");

        Directory.CreateDirectory(logDirectory);

        var serilogLevel = minimumLevel switch
        {
            LogLevel.Trace => Serilog.Events.LogEventLevel.Verbose,
            LogLevel.Debug => Serilog.Events.LogEventLevel.Debug,
            LogLevel.Information => Serilog.Events.LogEventLevel.Information,
            LogLevel.Warning => Serilog.Events.LogEventLevel.Warning,
            LogLevel.Error => Serilog.Events.LogEventLevel.Error,
            LogLevel.Critical => Serilog.Events.LogEventLevel.Fatal,
            _ => Serilog.Events.LogEventLevel.Information,
        };

        var serilogLogger = new LoggerConfiguration()
            .MinimumLevel.Is(serilogLevel)
            .Enrich.WithThreadId()
            .Enrich.WithProperty("Application", "HCEP")
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: Path.Combine(logDirectory, "HCEP-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] [{ThreadId}] {Message:lj}{NewLine}{Exception}",
                fileSizeLimitBytes: 50 * 1024 * 1024,
                rollOnFileSizeLimit: true)
            .CreateLogger();

        return new SerilogLoggerFactory(serilogLogger, dispose: true);
    }
}
