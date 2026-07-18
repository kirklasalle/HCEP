// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using HCEP.App.Updates;
using Microsoft.Extensions.Logging;

namespace HCEP.App;

/// <summary>
/// Modal update-center window. Runs a check on load, then optionally
/// downloads the latest release ZIP to a staging directory and generates
/// a non-destructive installer script.
/// </summary>
public partial class CheckForUpdatesWindow : Window
{
    private readonly UpdateService _updater;
    private readonly ILogger<CheckForUpdatesWindow>? _logger;
    private UpdateCheckResult? _lastResult;
    private CancellationTokenSource? _cts;

    public CheckForUpdatesWindow(UpdateService updater, ILogger<CheckForUpdatesWindow>? logger = null)
    {
        _updater = updater;
        _logger = logger;
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += (_, _) => _cts?.Cancel();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _cts = new CancellationTokenSource();
            CurrentVersionText.Text = UpdateService.GetCurrentVersion().ToString();
            LatestVersionText.Text = "Checking...";
            StatusText.Text = "";
            SubtitleText.Text = "Contacting GitHub releases API for kirklasalle/HCEP...";

            _lastResult = await _updater.CheckAsync(_cts.Token).ConfigureAwait(true);
            RenderResult(_lastResult);
        }
        catch (OperationCanceledException)
        {
            // Window closed mid-check — ignore.
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Update check failed: {ex.Message}";
            _logger?.LogWarning(ex, "Update check threw");
        }
    }

    private void RenderResult(UpdateCheckResult result)
    {
        CurrentVersionText.Text = result.CurrentVersion.ToString();
        LatestVersionText.Text = result.LatestTag ?? result.LatestVersion?.ToString() ?? "—";
        ReleaseNotesText.Text = string.IsNullOrWhiteSpace(result.ReleaseNotes)
            ? "(No release notes were provided for this release.)"
            : result.ReleaseNotes!;

        // The manual-download button opens the public releases page and is
        // always safe to enable when we have any URL to point at.
        OpenReleasePageButton.IsEnabled = !string.IsNullOrEmpty(result.HtmlUrl);

        if (!string.IsNullOrEmpty(result.Error))
        {
            SubtitleText.Text = "The update service could not read the release feed.";
            StatusText.Text = result.Error;
            DownloadButton.IsEnabled = false;
            return;
        }

        if (result.NoReleasesPublished)
        {
            // Public repo, reachable, but no releases yet. This is the
            // normal state for kirklasalle/HCEP right now and must not be
            // presented as a network error.
            SubtitleText.Text =
                "No releases have been published on GitHub yet. You are running the current build. " +
                "When the maintainer cuts a release, it will appear here automatically.";
            StatusText.Text =
                "Tip: use \"Open Release Page\" to watch the releases page directly. The updater is fully " +
                "wired and will download and stage the first published release non-destructively when it appears.";
            LatestVersionText.Text = "none published yet";
            ReleaseNotesText.Text = "(No release notes — the repository has no published releases yet.)";
            DownloadButton.IsEnabled = false;
            return;
        }

        DownloadButton.IsEnabled = !string.IsNullOrEmpty(result.DownloadUrl) && result.HasUpdate;

        if (result.HasUpdate)
        {
            var preTag = result.IsPreRelease ? " (pre-release)" : "";
            SubtitleText.Text =
                $"A newer HCEP release is available{preTag}. Downloading is fully non-destructive — your settings, " +
                "calibration, credentials, and logs are preserved.";
            StatusText.Text = string.IsNullOrEmpty(result.DownloadUrl)
                ? "This release does not publish a downloadable asset; use Open Release Page instead."
                : $"Ready to download {FormatBytes(result.DownloadSizeBytes)} to a staging folder.";
        }
        else if (result.LatestVersion is not null && result.LatestVersion == result.CurrentVersion)
        {
            SubtitleText.Text = "You are running the current release. No update is required.";
            StatusText.Text = "";
        }
        else if (result.LatestVersion is not null)
        {
            SubtitleText.Text = "You are running a version newer than the latest published release (dev build).";
            StatusText.Text = "";
        }
        else
        {
            SubtitleText.Text = "No published release was found in the repository.";
            StatusText.Text = "";
        }
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastResult?.DownloadUrl is null) return;
        try
        {
            DownloadButton.IsEnabled = false;
            OpenReleasePageButton.IsEnabled = false;
            StatusText.Text = "Downloading update... 0%";

            _cts ??= new CancellationTokenSource();
            var progress = new Progress<(long Received, long Total)>(p =>
            {
                if (p.Total > 0)
                    StatusText.Text = $"Downloading update... {(p.Received * 100.0 / p.Total):F1}% ({FormatBytes(p.Received)} / {FormatBytes(p.Total)})";
                else
                    StatusText.Text = $"Downloading update... {FormatBytes(p.Received)} received";
            });

            var zipPath = await _updater.DownloadAsync(_lastResult, progress, _cts.Token).ConfigureAwait(true);
            var zipSha256 = UpdateService.ComputeSha256Hex(zipPath);

            var installDir = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location ?? Environment.CurrentDirectory)
                             ?? Environment.CurrentDirectory;
            var scriptPath = _updater.GenerateInstallerScript(zipPath, installDir, zipSha256);

            StatusText.Text = "Download complete.";
            SubtitleText.Text =
                "Update staged. Close HCEP and run the generated PowerShell installer script to apply it. " +
                "The installer verifies SHA-256 integrity and can roll back app binaries if update copy fails. " +
                "Your settings, credentials, and logs are preserved.";
            var reveal = MessageBox.Show(
                this,
                "Update downloaded and staged.\n\n" +
                $"ZIP: {zipPath}\n" +
                $"SHA-256: {zipSha256}\n" +
                $"Installer: {scriptPath}\n\n" +
                "Reveal the staging folder in File Explorer?",
                "HCEP Update Ready",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (reveal == MessageBoxResult.Yes)
            {
                try
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{scriptPath}\"") { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to open explorer");
                }
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Download failed: {ex.Message}";
            DownloadButton.IsEnabled = true;
            OpenReleasePageButton.IsEnabled = _lastResult?.HtmlUrl is not null;
            _logger?.LogWarning(ex, "Update download failed");
        }
    }

    private void OpenReleasePageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastResult?.HtmlUrl is null) return;
        try
        {
            Process.Start(new ProcessStartInfo(_lastResult.HtmlUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not open browser: {ex.Message}";
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private static string FormatBytes(long? bytes)
    {
        if (!bytes.HasValue || bytes.Value < 0) return "unknown size";
        double b = bytes.Value;
        string[] units = { "B", "KB", "MB", "GB" };
        int i = 0;
        while (b >= 1024 && i < units.Length - 1) { b /= 1024; i++; }
        return $"{b:F1} {units[i]}";
    }
}
