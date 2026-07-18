// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace HCEP.App.Updates;

/// <summary>
/// Result of an update check against the HCEP GitHub releases feed.
/// </summary>
public sealed record UpdateCheckResult
{
    /// <summary>Currently running HCEP version (from AssemblyInformationalVersion).</summary>
    public required Version CurrentVersion { get; init; }

    /// <summary>Latest published release version, or null if none/network failure.</summary>
    public Version? LatestVersion { get; init; }

    /// <summary>Tag name (e.g. "v1.4.0") of the latest release.</summary>
    public string? LatestTag { get; init; }

    /// <summary>Human-readable release name.</summary>
    public string? LatestName { get; init; }

    /// <summary>Release notes body (Markdown).</summary>
    public string? ReleaseNotes { get; init; }

    /// <summary>Release publication date.</summary>
    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>Best-guess Windows x64 zip asset download URL, or null.</summary>
    public string? DownloadUrl { get; init; }

    /// <summary>File size in bytes of the download asset.</summary>
    public long? DownloadSizeBytes { get; init; }

    /// <summary>HTML URL to the release page (fallback for manual download).</summary>
    public string? HtmlUrl { get; init; }

    /// <summary>
    /// True when the repository is reachable but no releases have been
    /// published yet. This is NOT an error — HCEP is simply running its
    /// current build and the maintainer hasn't cut a release yet.
    /// </summary>
    public bool NoReleasesPublished { get; init; }

    /// <summary>True when the discovered release is flagged as a pre-release on GitHub.</summary>
    public bool IsPreRelease { get; init; }

    /// <summary>Human-readable error message if the check failed.</summary>
    public string? Error { get; init; }

    /// <summary>True iff a newer release is available.</summary>
    public bool HasUpdate =>
        LatestVersion is not null && LatestVersion > CurrentVersion;
}

/// <summary>
/// GitHub-releases-backed update service.
///
/// <para>
/// Responsibilities:
/// <list type="bullet">
///   <item>Query the public GitHub releases API for the latest tagged release.</item>
///   <item>Compare the tag to the currently running assembly version.</item>
///   <item>Optionally download the release ZIP asset to a staging directory.</item>
///   <item>Emit a non-destructive migration script that unpacks the update
///         WITHOUT touching user configuration or logs.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Non-destructive contract:</b> the service never overwrites
/// <c>%LocalAppData%\HCEP\hcep-settings.json</c>, the
/// <c>overlay-alignment.json</c> file, the <c>Logs\</c> directory, or any
/// Windows Credential Manager entries under the <c>HCEP/*</c> target family.
/// Actual binary replacement runs from a generated PowerShell script after
/// the app has exited; that script skips the protected paths by design.
/// </para>
/// </summary>
public sealed class UpdateService
{
    // GitHub owner/repo — kept as constants so the fallback URLs stay in sync
    // with the API endpoints. If the repo ever moves, change these once here.
    public const string GitHubOwner = "kirklasalle";
    public const string GitHubRepo = "HCEP";

    private const string GitHubApiLatest =
        "https://api.github.com/repos/" + GitHubOwner + "/" + GitHubRepo + "/releases/latest";

    private const string GitHubApiList =
        "https://api.github.com/repos/" + GitHubOwner + "/" + GitHubRepo + "/releases?per_page=10";

    /// <summary>
    /// Human-visible releases page. Always safe to open in a browser and used
    /// as the fallback when the API returns 404 (no releases yet).
    /// </summary>
    public const string GitHubReleasesPage =
        "https://github.com/" + GitHubOwner + "/" + GitHubRepo + "/releases";

    private const string UserAgent = "HCEP-Updater";

    private readonly HttpClient _http;
    private readonly ILogger<UpdateService>? _logger;

    public UpdateService(HttpClient http, ILogger<UpdateService>? logger = null)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// Returns the currently running HCEP version, taken from
    /// <c>AssemblyInformationalVersion</c> (falls back to
    /// <c>AssemblyVersion</c>).
    /// </summary>
    public static Version GetCurrentVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? typeof(UpdateService).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            // Strip any "+build.sha" suffix
            var plus = info.IndexOf('+');
            if (plus > 0) info = info.Substring(0, plus);
            if (Version.TryParse(info, out var vInfo)) return vInfo;
        }
        return asm.GetName().Version ?? new Version(0, 0, 0, 0);
    }

    /// <summary>
    /// Root directory under %LocalAppData%\HCEP\Updates where new releases
    /// are staged. This directory is separate from any configuration folder.
    /// </summary>
    public static string GetStagingRoot()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(root, "HCEP", "Updates");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Fetches the latest release metadata from GitHub and compares against
    /// the currently running version. Network / parsing errors are
    /// captured into <see cref="UpdateCheckResult.Error"/> instead of thrown.
    ///
    /// <para>
    /// GitHub's <c>/releases/latest</c> endpoint returns HTTP 404 when the
    /// repository has no non-pre-release releases published yet. In that
    /// case this method falls back to the <c>/releases</c> list endpoint
    /// (which returns <c>[]</c> for a repository with zero releases) and
    /// reports "no releases yet" rather than a hard network error. The
    /// returned <see cref="UpdateCheckResult.HtmlUrl"/> is always populated
    /// with the public releases page so the UI's "Open Release Page"
    /// button is usable even on error.
    /// </para>
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        var current = GetCurrentVersion();

        // Always populate the fallback HTML URL so the UI's manual-download
        // button stays usable even if the API call fails.
        var fallbackHtmlUrl = GitHubReleasesPage;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, GitHubApiLatest);
            req.Headers.UserAgent.ParseAdd(UserAgent);
            req.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // "No latest release" is a documented 404 from GitHub. Fall
                // through to the list endpoint before deciding this is a
                // hard failure — a repo can have only pre-releases.
                _logger?.LogInformation(
                    "GitHub /releases/latest returned 404 — falling back to /releases list");
                return await CheckViaListAsync(current, fallbackHtmlUrl, ct).ConfigureAwait(false);
            }

            if (!resp.IsSuccessStatusCode)
            {
                var rateRemaining = resp.Headers.TryGetValues("x-ratelimit-remaining", out var vals)
                    ? string.Join(",", vals) : null;
                var msg = $"GitHub returned HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}.";
                if (rateRemaining == "0")
                    msg += " GitHub API rate limit exhausted (60 requests/hour for unauthenticated clients).";
                return new UpdateCheckResult
                {
                    CurrentVersion = current,
                    HtmlUrl = fallbackHtmlUrl,
                    Error = msg,
                };
            }

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var release = JsonSerializer.Deserialize<GitHubRelease>(json);
            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            {
                return new UpdateCheckResult
                {
                    CurrentVersion = current,
                    HtmlUrl = fallbackHtmlUrl,
                    Error = "GitHub returned a release payload we could not parse.",
                };
            }

            var latest = ParseTag(release.TagName);
            var (assetUrl, assetSize) = PickBestAsset(release.Assets);

            return new UpdateCheckResult
            {
                CurrentVersion = current,
                LatestVersion = latest,
                LatestTag = release.TagName,
                LatestName = release.Name,
                ReleaseNotes = release.Body,
                PublishedAt = release.PublishedAt,
                DownloadUrl = assetUrl,
                DownloadSizeBytes = assetSize,
                HtmlUrl = release.HtmlUrl ?? fallbackHtmlUrl,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Update check failed");
            return new UpdateCheckResult
            {
                CurrentVersion = current,
                HtmlUrl = fallbackHtmlUrl,
                Error = $"Update check failed: {ex.Message}",
            };
        }
    }

    /// <summary>
    /// Fallback path when <c>/releases/latest</c> returns 404. Lists every
    /// release (including pre-releases) and picks the highest version.
    /// A truly empty repository (no releases at all) returns a friendly
    /// non-error result with <see cref="UpdateCheckResult.NoReleasesPublished"/>
    /// set to <c>true</c>.
    /// </summary>
    private async Task<UpdateCheckResult> CheckViaListAsync(
        Version current,
        string fallbackHtmlUrl,
        CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, GitHubApiList);
            req.Headers.UserAgent.ParseAdd(UserAgent);
            req.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                // A public repo shouldn't 404 here. Anything non-200 is a
                // real error worth surfacing.
                return new UpdateCheckResult
                {
                    CurrentVersion = current,
                    HtmlUrl = fallbackHtmlUrl,
                    Error = $"GitHub /releases returned HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}. " +
                            "The repository may be private, or you may be behind a proxy that blocks api.github.com.",
                };
            }

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var list = JsonSerializer.Deserialize<List<GitHubRelease>>(json);
            if (list is null || list.Count == 0)
            {
                // Repo exists, is reachable, and has zero releases published.
                // This is NOT an error — it's the normal state of a repo that
                // hasn't cut its first release yet. Report it distinctly.
                return new UpdateCheckResult
                {
                    CurrentVersion = current,
                    HtmlUrl = fallbackHtmlUrl,
                    NoReleasesPublished = true,
                };
            }

            // Pick the highest version — favour non-draft, non-prerelease when possible.
            GitHubRelease? best = null;
            Version? bestVer = null;
            foreach (var r in list)
            {
                if (r.Draft) continue;
                var v = ParseTag(r.TagName ?? "");
                if (v is null) continue;
                if (bestVer is null || v > bestVer)
                {
                    best = r;
                    bestVer = v;
                }
            }

            if (best is null || bestVer is null)
            {
                return new UpdateCheckResult
                {
                    CurrentVersion = current,
                    HtmlUrl = fallbackHtmlUrl,
                    NoReleasesPublished = true,
                };
            }

            var (assetUrl, assetSize) = PickBestAsset(best.Assets);
            return new UpdateCheckResult
            {
                CurrentVersion = current,
                LatestVersion = bestVer,
                LatestTag = best.TagName,
                LatestName = best.Name,
                ReleaseNotes = best.Body,
                PublishedAt = best.PublishedAt,
                DownloadUrl = assetUrl,
                DownloadSizeBytes = assetSize,
                HtmlUrl = best.HtmlUrl ?? fallbackHtmlUrl,
                IsPreRelease = best.Prerelease,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Update list-fallback failed");
            return new UpdateCheckResult
            {
                CurrentVersion = current,
                HtmlUrl = fallbackHtmlUrl,
                Error = $"Update check failed: {ex.Message}",
            };
        }
    }

    /// <summary>
    /// Downloads the release asset to the staging directory and returns the
    /// on-disk path. Reports progress via the optional <paramref name="progress"/>
    /// callback (bytesReceived, totalBytes).
    /// </summary>
    public async Task<string> DownloadAsync(
        UpdateCheckResult result,
        IProgress<(long Received, long Total)>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(result.DownloadUrl))
            throw new InvalidOperationException("No downloadable asset available for this release.");

        var versionTag = result.LatestTag ?? result.LatestVersion?.ToString() ?? "unknown";
        var stagingDir = Path.Combine(GetStagingRoot(), versionTag);
        Directory.CreateDirectory(stagingDir);
        var filename = Path.GetFileName(new Uri(result.DownloadUrl).LocalPath);
        if (string.IsNullOrWhiteSpace(filename)) filename = "HCEP-update.zip";
        var target = Path.Combine(stagingDir, filename);

        using var req = new HttpRequestMessage(HttpMethod.Get, result.DownloadUrl);
        req.Headers.UserAgent.ParseAdd(UserAgent);
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? result.DownloadSizeBytes ?? -1L;
        await using var input = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var output = File.Create(target);
        var buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            received += read;
            progress?.Report((received, total));
        }

        _logger?.LogInformation("Downloaded update to {Path} ({Bytes} bytes)", target, received);
        return target;
    }

    /// <summary>
    /// Writes a PowerShell installer script alongside the downloaded ZIP.
    /// The script performs the update AFTER HCEP has closed, and it
    /// explicitly preserves the paths listed in <see cref="PreservedRelativePaths"/>
    /// and every Windows Credential Manager entry.
    /// </summary>
    public string GenerateInstallerScript(string downloadedZipPath, string appInstallDir, string? expectedSha256Hex = null)
    {
        var stagingDir = Path.GetDirectoryName(downloadedZipPath)!;
        var scriptPath = Path.Combine(stagingDir, "install-update.ps1");
        var backupDir = Path.Combine(stagingDir, "backup");

        var script = new System.Text.StringBuilder();
        script.AppendLine("# HCEP non-destructive update installer");
        script.AppendLine("# Generated automatically — safe to inspect before running.");
        script.AppendLine("# ------------------------------------------------------------");
        script.AppendLine("param([switch]$Quiet)");
        script.AppendLine("$ErrorActionPreference = 'Stop'");
        script.AppendLine($"$Zip        = '{downloadedZipPath.Replace("'", "''")}'");
        script.AppendLine($"$InstallDir = '{appInstallDir.Replace("'", "''")}'");
        script.AppendLine($"$Backup     = '{backupDir.Replace("'", "''")}'");
        script.AppendLine($"$ExpectedSha256 = '{(expectedSha256Hex ?? string.Empty).Replace("'", "''")}'");
        script.AppendLine("$LocalAppData = [Environment]::GetFolderPath('LocalApplicationData')");
        script.AppendLine("$HcepData   = Join-Path $LocalAppData 'HCEP'");
        script.AppendLine("if (-not (Test-Path $Backup)) { New-Item -ItemType Directory -Path $Backup -Force | Out-Null }");
        script.AppendLine();
        script.AppendLine("Write-Host 'HCEP — updating in place. User settings and credentials will be preserved.'");
        script.AppendLine();
        script.AppendLine("# Wait until HCEP.App.exe is no longer running (max 30s).");
        script.AppendLine("for ($i = 0; $i -lt 30; $i++) {");
        script.AppendLine("  $running = Get-Process -Name 'HCEP.App' -ErrorAction SilentlyContinue");
        script.AppendLine("  if (-not $running) { break }");
        script.AppendLine("  Start-Sleep -Seconds 1");
        script.AppendLine("}");
        script.AppendLine();
        script.AppendLine("# 1) Snapshot user data (defense-in-depth backup).");
        script.AppendLine("if (Test-Path $HcepData) {");
        script.AppendLine("  Copy-Item -Path $HcepData -Destination (Join-Path $Backup 'HCEP-LocalAppData') -Recurse -Force -ErrorAction SilentlyContinue");
        script.AppendLine("}");
        script.AppendLine("$ConfigDir = Join-Path $InstallDir 'config'");
        script.AppendLine("if (Test-Path $ConfigDir) {");
        script.AppendLine("  Copy-Item -Path $ConfigDir -Destination (Join-Path $Backup 'config') -Recurse -Force -ErrorAction SilentlyContinue");
        script.AppendLine("}");
        script.AppendLine();
        script.AppendLine("# 1b) Integrity check (if a staged hash is present).");
        script.AppendLine("if (-not [string]::IsNullOrWhiteSpace($ExpectedSha256)) {");
        script.AppendLine("  $actual = (Get-FileHash -Path $Zip -Algorithm SHA256).Hash.ToLowerInvariant()");
        script.AppendLine("  if ($actual -ne $ExpectedSha256.ToLowerInvariant()) {");
        script.AppendLine("    throw \"Update archive integrity check failed. Expected SHA256 '$ExpectedSha256' but got '$actual'.\"");
        script.AppendLine("  }");
        script.AppendLine("}");
        script.AppendLine();
        script.AppendLine("# 1c) Snapshot current install (excluding user-state paths) for rollback.");
        script.AppendLine("$AppBackup = Join-Path $Backup 'app-before-update'");
        script.AppendLine("if (Test-Path $AppBackup) { Remove-Item -Recurse -Force $AppBackup }");
        script.AppendLine("New-Item -ItemType Directory -Path $AppBackup -Force | Out-Null");
        script.AppendLine("robocopy $InstallDir $AppBackup /E `");
        script.AppendLine("  /XD config logs Logs .venv `");
        script.AppendLine("  /XF hcep-settings.json overlay-alignment.json | Out-Null");
        script.AppendLine("if ($LASTEXITCODE -ge 8) { throw \"Backup snapshot failed (robocopy exit code $LASTEXITCODE).\" }");
        script.AppendLine();
        script.AppendLine("# 2) Extract update to a temp folder, then robocopy over the app tree,");
        script.AppendLine("#    excluding directories that hold user state.");
        script.AppendLine("$Temp = Join-Path $Backup 'staged'");
        script.AppendLine("if (Test-Path $Temp) { Remove-Item -Recurse -Force $Temp }");
        script.AppendLine("New-Item -ItemType Directory -Path $Temp -Force | Out-Null");
        script.AppendLine("try {");
        script.AppendLine("  Expand-Archive -Path $Zip -DestinationPath $Temp -Force");
        script.AppendLine();
        script.AppendLine("  # robocopy: /E copies subdirs, /XD excludes user-owned dirs, /XF excludes user files.");
        script.AppendLine("  robocopy $Temp $InstallDir /E `");
        script.AppendLine("    /XD config logs Logs .venv `");
        script.AppendLine("    /XF hcep-settings.json overlay-alignment.json | Out-Null");
        script.AppendLine("  if ($LASTEXITCODE -ge 8) { throw \"Update copy failed (robocopy exit code $LASTEXITCODE).\" }");
        script.AppendLine("}");
        script.AppendLine("catch {");
        script.AppendLine("  Write-Warning \"Update failed: $($_.Exception.Message). Attempting rollback from backup...\"");
        script.AppendLine("  if (Test-Path $AppBackup) {");
        script.AppendLine("    robocopy $AppBackup $InstallDir /E `");
        script.AppendLine("      /XD config logs Logs .venv `");
        script.AppendLine("      /XF hcep-settings.json overlay-alignment.json | Out-Null");
        script.AppendLine("  }");
        script.AppendLine("  throw");
        script.AppendLine("}");
        script.AppendLine();
        script.AppendLine("Write-Host 'Update applied with integrity verification and rollback safety. Config, logs and Windows Credential Manager entries were preserved.'");
        script.AppendLine("if (-not $Quiet) { Read-Host 'Press ENTER to close' }");

        File.WriteAllText(scriptPath, script.ToString());
        return scriptPath;
    }

    /// <summary>
    /// Paths (relative to the app install directory) that the installer must
    /// never overwrite. Exposed for tests and documentation.
    /// </summary>
    public static IReadOnlyList<string> PreservedRelativePaths { get; } = new[]
    {
        @"config\",
        @"config\hcep-settings.json",
        @"logs\",
        @".venv\",
    };

    private static Version? ParseTag(string tag)
    {
        var trimmed = tag.TrimStart('v', 'V');
        return Version.TryParse(trimmed, out var v) ? v : null;
    }

    private static (string? Url, long? Size) PickBestAsset(List<GitHubAsset>? assets)
    {
        if (assets is null || assets.Count == 0) return (null, null);

        // Preference: Windows x64 ZIP > any ZIP > first asset.
        GitHubAsset? best = null;
        foreach (var a in assets)
        {
            if (string.IsNullOrEmpty(a.BrowserDownloadUrl)) continue;
            var name = a.Name?.ToLowerInvariant() ?? "";
            bool isZip = name.EndsWith(".zip");
            bool isWinX64 = name.Contains("win") && (name.Contains("x64") || name.Contains("amd64"));
            if (isZip && isWinX64) return (a.BrowserDownloadUrl, a.Size);
            if (isZip && best is null) best = a;
        }
        if (best is not null) return (best.BrowserDownloadUrl, best.Size);
        var first = assets[0];
        return (first.BrowserDownloadUrl, first.Size);
    }

    /// <summary>
    /// Computes the lower-case SHA-256 digest for a file. Used by the updater
    /// to validate release-asset integrity before extraction.
    /// </summary>
    public static string ComputeSha256Hex(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLower(CultureInfo.InvariantCulture);
    }

    // ── DTOs for the GitHub API ────────────────────────────────

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("published_at")] public DateTimeOffset? PublishedAt { get; set; }
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("assets")] public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
    }
}
