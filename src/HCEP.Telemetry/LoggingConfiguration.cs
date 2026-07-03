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
using Serilog.Events;
using Serilog.Extensions.Logging;
using Serilog.Formatting.Compact;

namespace HCEP.Telemetry;

/// <summary>
/// Configures Serilog-based structured logging for the HCEP pipeline.
///
/// Three sinks write to the log directory:
///   HCEP-{date}.log       Human-readable rolling daily (Debug+)
///   HCEP-trace-{date}.log Verbose trace log - every event (Verbose+)
///   HCEP-json-{date}.jsonl CLEF structured JSON for Seq/analysis
///
/// Log directory resolution (first match wins):
///   1. HCEP_LOG_DIR environment variable
///   2. logs/ directory adjacent to HCEP.sln (dev tree)
///   3. %LocalAppData%\HCEP\Logs (release/installed)
/// </summary>
public static class LoggingConfiguration
{
    private const string ConsoleTemplate =
        "[{Timestamp:HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";

    private const string FileTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] [T{ThreadId}] {Message:lj}{NewLine}{Exception}";

    private const string TraceTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.ffff zzz} [{Level:u3}] [{SourceContext}] [T{ThreadId}] [PID{ProcessId}] {Message:lj}{NewLine}{Properties:j}{NewLine}{Exception}";

    private const long MainFileSizeBytes  = 50  * 1024 * 1024;
    private const long TraceFileSizeBytes = 100 * 1024 * 1024;
    private const int  RetainedDays       = 30;

    /// <summary>
    /// Creates an ILoggerFactory with Serilog providing console, rolling-file,
    /// trace-file, and JSON structured output.
    /// </summary>
    public static ILoggerFactory CreateLoggerFactory(
        string? logDirectory = null,
        LogLevel minimumLevel = LogLevel.Debug)
    {
        string logDir = logDirectory ?? ResolveLogDirectory();
        Directory.CreateDirectory(logDir);

        var mainLevel  = ToSerilog(minimumLevel);
        var traceLevel = LogEventLevel.Verbose;

        var serilogLogger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
            .Enrich.WithThreadId()
            .Enrich.WithProcessId()
            .Enrich.WithMachineName()
            .Enrich.WithEnvironmentUserName()
            .Enrich.WithProperty("Application", "HCEP")
            .Enrich.WithProperty("Version", GetAssemblyVersion())
            .WriteTo.Console(
                restrictedToMinimumLevel: mainLevel,
                outputTemplate: ConsoleTemplate)
            .WriteTo.File(
                path: Path.Combine(logDir, "HCEP-.log"),
                restrictedToMinimumLevel: mainLevel,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedDays,
                outputTemplate: FileTemplate,
                fileSizeLimitBytes: MainFileSizeBytes,
                rollOnFileSizeLimit: true,
                shared: false,
                flushToDiskInterval: TimeSpan.FromSeconds(2))
            .WriteTo.File(
                path: Path.Combine(logDir, "HCEP-trace-.log"),
                restrictedToMinimumLevel: traceLevel,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedDays,
                outputTemplate: TraceTemplate,
                fileSizeLimitBytes: TraceFileSizeBytes,
                rollOnFileSizeLimit: true,
                shared: false,
                flushToDiskInterval: TimeSpan.FromSeconds(1))
            .WriteTo.File(
                formatter: new CompactJsonFormatter(),
                path: Path.Combine(logDir, "HCEP-json-.jsonl"),
                restrictedToMinimumLevel: traceLevel,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedDays,
                fileSizeLimitBytes: TraceFileSizeBytes,
                rollOnFileSizeLimit: true,
                shared: false,
                flushToDiskInterval: TimeSpan.FromSeconds(1))
            .CreateLogger();

        Log.Logger = serilogLogger;
        serilogLogger.Information(
            "HCEP logging started — dir={LogDir} level={MinLevel}",
            logDir, mainLevel);

        return new SerilogLoggerFactory(serilogLogger, dispose: true);
    }

    /// <summary>
    /// Walks up from AppContext.BaseDirectory looking for HCEP.sln.
    /// Returns the adjacent logs/ folder when found (dev tree detection).
    /// Falls back to %LocalAppData%\HCEP\Logs.
    /// </summary>
    internal static string ResolveLogDirectory()
    {
        string? dir = AppContext.BaseDirectory;
        for (int depth = 0; depth < 10 && dir is not null; depth++)
        {
            if (File.Exists(Path.Combine(dir, "HCEP.sln")))
                return Path.Combine(dir, "logs");
            dir = Path.GetDirectoryName(dir);
        }
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HCEP", "Logs");
    }

    private static LogEventLevel ToSerilog(LogLevel level) => level switch
    {
        LogLevel.Trace       => LogEventLevel.Verbose,
        LogLevel.Debug       => LogEventLevel.Debug,
        LogLevel.Information => LogEventLevel.Information,
        LogLevel.Warning     => LogEventLevel.Warning,
        LogLevel.Error       => LogEventLevel.Error,
        LogLevel.Critical    => LogEventLevel.Fatal,
        _                    => LogEventLevel.Debug,
    };

    private static string GetAssemblyVersion()
    {
        var v = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
        return v is null ? "unknown" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
