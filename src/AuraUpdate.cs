using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace AuraToggle;

/// <summary>A newer release found on GitHub, with what is needed to fetch and verify it.</summary>
internal sealed record UpdateInfo(string Version, string InstallerUrl, string ChecksumUrl, string HtmlUrl);

/// <summary>
/// The only network code in this tool: checks GitHub for a newer release, and - only once the
/// user clicks to install it - downloads the setup and verifies it against the release's own
/// checksum file before ever running it. Nothing here writes to the controller; this is purely
/// "is there a newer version, and is the file that claims to be it the one GitHub actually built."
/// </summary>
internal static class AuraUpdate
{
    private const string ReleasesUrl = "https://api.github.com/repos/wbgcoding/aura-toggle/releases/latest";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(3);

    /// <summary>Ceiling for anything this downloads. The setup is around 1.5 MB.</summary>
    private const int MaxDownloadBytes = 64 * 1024 * 1024;

    /// <summary>
    /// Whether this process runs from an installed copy rather than a portable one - the
    /// installer always drops its uninstaller, <c>unins000.exe</c>, next to the exe; a portable
    /// copy someone extracted or copied by hand never has one. Self-replacing only makes sense
    /// for an installed copy: a portable exe cannot delete the file it is currently running as,
    /// so a newer version there is offered as a link to the release page instead.
    /// </summary>
    public static bool IsInstalled =>
        Environment.ProcessPath is string exe &&
        Path.GetDirectoryName(exe) is string folder &&
        File.Exists(Path.Combine(folder, "unins000.exe"));

    /// <summary>
    /// Whether enough time has passed since the last check to run another one - at most once
    /// every 24 hours, so the tool does not phone home on every single start.
    /// <see cref="AuraSettings.CheckUpdates"/> has no switch in the settings panel any more -
    /// turning it off means hand-editing <c>"checkUpdates": false</c> into settings.json.
    /// </summary>
    public static bool ShouldCheck(AuraSettings settings)
    {
        if (!settings.CheckUpdates)
        {
            return false;
        }

        if (!DateTime.TryParse(settings.LastUpdateCheckUtc, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out DateTime last))
        {
            return true;
        }

        return DateTime.UtcNow - last >= CheckInterval;
    }

    /// <summary>
    /// Asks GitHub for the latest release and compares it against this build's own version.
    /// Returns null on anything short of "a newer version exists and both files this needs are
    /// there" - a network hiccup, a malformed response and "already on the latest" all look the
    /// same to the caller: nothing to offer right now.
    /// </summary>
    public static async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = CheckTimeout };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AuraToggle", Program.VersionText));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using HttpResponseMessage response = await client.GetAsync(ReleasesUrl);
            if (!response.IsSuccessStatusCode || !EndedOnTrustedHost(response))
            {
                return null;
            }

            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
            JsonElement root = document.RootElement;

            string tag = Text(root, "tag_name");
            string remoteVersion = tag.TrimStart('v', 'V');

            if (!Version.TryParse(remoteVersion, out Version? remote) ||
                !Version.TryParse(Program.VersionText, out Version? local) || remote <= local)
            {
                return null;
            }

            string? installerUrl = null;
            string? checksumUrl = null;

            if (root.TryGetProperty("assets", out JsonElement assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement asset in assets.EnumerateArray())
                {
                    string name = Text(asset, "name");
                    string url = Text(asset, "browser_download_url");

                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                        name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                    {
                        installerUrl = url;
                    }
                    else if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        checksumUrl = url;
                    }
                }
            }

            // The checksum only proves the download matches what GitHub published - it says
            // nothing about which server actually answered. Both URLs have to be plain https to
            // GitHub's own asset hosts, or a hostile redirect/proxy could hand back a setup and a
            // matching checksum file for it that never came from the real release.
            if (installerUrl == null || checksumUrl == null ||
                !IsTrustedDownloadUrl(installerUrl) || !IsTrustedDownloadUrl(checksumUrl))
            {
                return null;
            }

            return new UpdateInfo(remoteVersion, installerUrl, checksumUrl, Text(root, "html_url"));
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            AuraLog.Warn($"UpdateCheck: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Downloads the setup and the release's checksum file, and only returns a path at all when
    /// the downloaded bytes hash to exactly the entry the release itself published for that file
    /// name - the one and only gate <see cref="LaunchInstaller"/> trusts. No Authenticode
    /// signature exists to check yet (see docs/INVARIANTS.md); the day one is added to the
    /// release, verifying it belongs here too, not replacing this check.
    /// </summary>
    public static async Task<string?> DownloadAndVerifyAsync(UpdateInfo info)
    {
        try
        {
            using var client = new HttpClient
            {
                Timeout = DownloadTimeout,
                // The setup is a couple of megabytes. Reading a response into memory without a
                // ceiling would let whatever answers decide how much this process allocates.
                MaxResponseContentBufferSize = MaxDownloadBytes,
            };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AuraToggle", Program.VersionText));

            using HttpResponseMessage checksumResponse = await client.GetAsync(info.ChecksumUrl);
            if (!checksumResponse.IsSuccessStatusCode || !EndedOnTrustedHost(checksumResponse))
            {
                return null;
            }

            string checksums = await checksumResponse.Content.ReadAsStringAsync();
            string fileName = Path.GetFileName(new Uri(info.InstallerUrl).LocalPath);

            string? expected = FindChecksum(checksums, fileName);
            if (expected == null)
            {
                AuraLog.Warn($"UpdateDownload: no checksum entry for {fileName}");
                return null;
            }

            using HttpResponseMessage installerResponse = await client.GetAsync(info.InstallerUrl);
            if (!installerResponse.IsSuccessStatusCode || !EndedOnTrustedHost(installerResponse))
            {
                return null;
            }

            byte[] data = await installerResponse.Content.ReadAsByteArrayAsync();
            string actual = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                AuraLog.Warn("UpdateDownload: checksum did not match, download rejected");
                return null;
            }

            string path = Path.Combine(Path.GetTempPath(), fileName);
            await File.WriteAllBytesAsync(path, data);
            return path;
        }
        catch (Exception ex) when (IsExpected(ex) || ex is UnauthorizedAccessException)
        {
            AuraLog.Error("UpdateDownload", ex);
            return null;
        }
    }

    /// <summary>
    /// Runs the downloaded, already-verified setup silently and lets the caller close this
    /// process - the installer replaces the running exe, which cannot happen while it is still
    /// open. The setup is <c>PrivilegesRequired=admin</c>, so this still raises a UAC prompt
    /// under <c>/SILENT</c> - declining it throws (Win32Exception 1223, "cancelled by the
    /// user"), which is a normal choice here, not a failure worth a crash dialog for.
    /// </summary>
    public static bool LaunchInstaller(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path, "/SILENT")
            {
                UseShellExecute = true,
            });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AuraLog.Warn($"UpdateInstall: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Matches <c>build.bat</c>'s own "&lt;hash&gt;  &lt;filename&gt;" format, two spaces
    /// between the two fields, one entry per line.</summary>
    private static string? FindChecksum(string sumsFile, string fileName)
    {
        foreach (string rawLine in sumsFile.Split('\n'))
        {
            string line = rawLine.Trim();
            int gap = line.IndexOf("  ", StringComparison.Ordinal);
            if (gap < 0)
            {
                continue;
            }

            if (string.Equals(line[(gap + 2)..].Trim(), fileName, StringComparison.OrdinalIgnoreCase))
            {
                return line[..gap].Trim().ToLowerInvariant();
            }
        }

        return null;
    }

    private static bool IsTrustedDownloadUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) && IsTrustedHost(parsed);

    private static bool IsTrustedHost(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Where the request actually ended up. A release asset answers with a redirect to GitHub's
    /// own object storage, so redirects have to be followed - but then the URL the release named
    /// is no longer the host the bytes came from, and checking only that URL would miss a redirect
    /// off to somewhere else entirely. That matters more here than anywhere: a setup and a
    /// checksum file handed over by the same foreign server verify against each other perfectly.
    /// </summary>
    private static bool EndedOnTrustedHost(HttpResponseMessage response) =>
        response.RequestMessage?.RequestUri is Uri final && IsTrustedHost(final);

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    /// <summary>Everything a flaky connection, a malformed response or a denied request can
    /// throw - none of it is worth more than a log line, since checking for an update is
    /// never allowed to get in the way of switching the lights.</summary>
    private static bool IsExpected(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException
            or InvalidOperationException or NotSupportedException or IOException;
}
