using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace AuraToggle;

/// <summary>
/// A newer release found on GitHub, with what is needed to fetch and verify it:
/// the file to install (the setup for an installed copy, the portable exe otherwise), its detached
/// signature (empty when the release carries none) and the release's checksum file.
/// </summary>
internal sealed record UpdateInfo(string Version, string FileUrl, string SignatureUrl, string ChecksumUrl, string HtmlUrl);

/// <summary>How a download ended: only <see cref="Verified"/> hands out any bytes.</summary>
internal enum UpdateResult
{
    /// <summary>The file matched the release's checksum and carries a valid signature.</summary>
    Verified,

    /// <summary>Something answered, but it cannot be shown to be an official release.</summary>
    NotVerified,

    /// <summary>The connection or the disk failed before anything could be judged.</summary>
    Failed,
}

/// <summary>The outcome of <see cref="AuraUpdate.DownloadAndVerifyAsync"/>; <see cref="Data"/> is set
/// only for <see cref="UpdateResult.Verified"/>.</summary>
internal sealed record UpdateDownload(UpdateResult Result, byte[]? Data, string FileName);

/// <summary>
/// The only network code in this tool: checks GitHub for a newer release, and - only once the
/// user clicks to install it - downloads the file and lets it through only when it matches the
/// release's own checksum file and carries a valid signature made with the project's key.
/// Nothing here writes to the controller; this is purely "is there a newer version, and is the
/// file that claims to be it the one the project actually published."
/// </summary>
internal static class AuraUpdate
{
    private const string ReleasesUrl = "https://api.github.com/repos/wbgcoding/Aura-Toggle/releases/latest";
    private const string PortableAssetName = "AuraToggle.exe";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(3);

    /// <summary>Ceiling for anything this downloads. The setup is around 2.3 MB.</summary>
    private const int MaxDownloadBytes = 64 * 1024 * 1024;

    /// <summary>A Base64 ECDSA P-256 signature is 88 characters; anything near this is not one.</summary>
    private const int MaxSignatureBytes = 4 * 1024;

    /// <summary>
    /// Ceiling for the release metadata response. A release's JSON is a few tens of kilobytes -
    /// parsing whatever answers must not be able to decide how much this process allocates.
    /// </summary>
    private const int MaxMetadataBytes = 4 * 1024 * 1024;

    // ERROR_CANCELLED: what starting a process with "runas" throws when the UAC prompt is declined.
    private const int ErrorCancelled = 1223;

    /// <summary>
    /// Whether this process runs from an installed copy rather than a portable one - the
    /// installer always drops its uninstaller, <c>unins000.exe</c>, next to the exe; a portable
    /// copy someone extracted or copied by hand never has one. An installed copy updates by
    /// running the new setup; a portable one swaps its own exe, see <see cref="SwapPortable"/>.
    /// </summary>
    public static bool IsInstalled =>
        Environment.ProcessPath is string exe &&
        Path.GetDirectoryName(exe) is string folder &&
        File.Exists(Path.Combine(folder, "unins000.exe"));

    /// <summary>
    /// Whether enough time has passed since the last check to run another one - at most once
    /// every 24 hours, so the tool does not phone home on every single start.
    /// The switch for it sits in the settings panel (<see cref="AuraSettings.CheckUpdates"/>).
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
    /// Returns null on anything short of "a newer version exists and the files this needs are
    /// there" - a network hiccup, a malformed response and "already on the latest" all look the
    /// same to the caller: nothing to offer right now. A release without a signature file still
    /// counts as found (with an empty signature URL): it is offered, and installing it ends in
    /// "not verified" instead of silently vanishing.
    /// </summary>
    public static async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = CheckTimeout, MaxResponseContentBufferSize = MaxMetadataBytes };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AuraToggle", Program.VersionText));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using HttpResponseMessage response = await client.GetAsync(ReleasesUrl);
            if (!response.IsSuccessStatusCode || !EndedOnTrustedHost(response))
            {
                return null;
            }

            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
            JsonElement root = document.RootElement;

            string tag = AuraFiles.JsonText(root, "tag_name");
            string remoteVersion = tag.TrimStart('v', 'V');

            if (!Version.TryParse(remoteVersion, out Version? remote) ||
                !Version.TryParse(Program.VersionText, out Version? local) || remote <= local)
            {
                return null;
            }

            var assetUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (root.TryGetProperty("assets", out JsonElement assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement asset in assets.EnumerateArray())
                {
                    string name = AuraFiles.JsonText(asset, "name");
                    if (name.Length > 0)
                    {
                        assetUrls[name] = AuraFiles.JsonText(asset, "browser_download_url");
                    }
                }
            }

            string? fileName = IsInstalled ? FindSetupName(assetUrls.Keys) : PortableAssetName;
            if (fileName == null || !assetUrls.TryGetValue(fileName, out string? fileUrl) ||
                !assetUrls.TryGetValue("SHA256SUMS.txt", out string? checksumUrl))
            {
                return null;
            }

            // The checksum only proves the download matches what GitHub published - it says
            // nothing about which server actually answered. Both URLs have to be plain https to
            // GitHub's own asset hosts, or a hostile redirect/proxy could hand back a file and a
            // matching checksum for it that never came from the real release.
            if (!IsTrustedDownloadUrl(fileUrl) || !IsTrustedDownloadUrl(checksumUrl))
            {
                return null;
            }

            string signatureUrl = assetUrls.TryGetValue(fileName + ".sig", out string? sigUrl) &&
                IsTrustedDownloadUrl(sigUrl)
                ? sigUrl
                : "";

            // The download URLs above are checked before anything trusts them; html_url ends up
            // in a shell-executed Process.Start (ToggleForm.OpenReleasePage), so it gets the same gate.
            string htmlUrl = AuraFiles.JsonText(root, "html_url");
            return new UpdateInfo(remoteVersion, fileUrl, signatureUrl, checksumUrl,
                IsTrustedDownloadUrl(htmlUrl) ? htmlUrl : "");
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            AuraLog.Warn($"UpdateCheck: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Downloads the file, its signature and the release's checksum file, and only hands out bytes
    /// when all of it holds together: the bytes hash to exactly the entry the release published for
    /// that file name, and the detached signature verifies against the key built into this program
    /// - the gate the install paths trust. Nothing is written to disk here. Every reason to doubt
    /// the file (no signature published, a redirect off GitHub, a mismatching hash, a bad
    /// signature) ends as <see cref="UpdateResult.NotVerified"/>; only a connection or disk problem
    /// is <see cref="UpdateResult.Failed"/>.
    /// </summary>
    public static async Task<UpdateDownload> DownloadAndVerifyAsync(UpdateInfo info)
    {
        string fileName = "";

        try
        {
            fileName = Path.GetFileName(new Uri(info.FileUrl).LocalPath);

            if (info.SignatureUrl.Length == 0)
            {
                AuraLog.Warn($"UpdateDownload: the release has no signature for {fileName}, not installed");
                return Rejected(fileName);
            }

            using var client = new HttpClient
            {
                Timeout = DownloadTimeout,
                // The file is a couple of megabytes. Reading a response into memory without a
                // ceiling would let whatever answers decide how much this process allocates.
                MaxResponseContentBufferSize = MaxDownloadBytes,
            };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AuraToggle", Program.VersionText));

            using HttpResponseMessage checksumResponse = await client.GetAsync(info.ChecksumUrl);
            if (!checksumResponse.IsSuccessStatusCode)
            {
                return new UpdateDownload(UpdateResult.Failed, null, fileName);
            }

            if (!EndedOnTrustedHost(checksumResponse))
            {
                return Rejected(fileName);
            }

            string checksums = await checksumResponse.Content.ReadAsStringAsync();

            string? expected = FindChecksum(checksums, fileName);
            if (expected == null)
            {
                AuraLog.Warn($"UpdateDownload: no checksum entry for {fileName}");
                return Rejected(fileName);
            }

            using HttpResponseMessage fileResponse = await client.GetAsync(info.FileUrl);
            if (!fileResponse.IsSuccessStatusCode)
            {
                return new UpdateDownload(UpdateResult.Failed, null, fileName);
            }

            if (!EndedOnTrustedHost(fileResponse))
            {
                return Rejected(fileName);
            }

            byte[] data = await fileResponse.Content.ReadAsByteArrayAsync();
            string actual = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                AuraLog.Warn("UpdateDownload: checksum did not match, download rejected");
                return Rejected(fileName);
            }

            using HttpResponseMessage sigResponse =
                await client.GetAsync(info.SignatureUrl, HttpCompletionOption.ResponseHeadersRead);
            if (!sigResponse.IsSuccessStatusCode)
            {
                return new UpdateDownload(UpdateResult.Failed, null, fileName);
            }

            if (!EndedOnTrustedHost(sigResponse))
            {
                return Rejected(fileName);
            }

            string? signature = await ReadSignatureAsync(sigResponse.Content);
            if (signature == null || !UpdateSignature.Verify(data, signature, UpdatePublicKey.Value))
            {
                AuraLog.Warn("UpdateDownload: signature did not verify, download rejected");
                return Rejected(fileName);
            }

            // The signature covers the bytes, not the release they hang on. The version written into
            // the signed file itself has to be the one the release claims, so an older signed build
            // re-published under a newer tag is refused rather than installed as a downgrade.
            if (!CarriesVersion(data, info.Version))
            {
                AuraLog.Warn($"UpdateDownload: the signed file is not version {info.Version}, download rejected");
                return Rejected(fileName);
            }

            return new UpdateDownload(UpdateResult.Verified, data, fileName);
        }
        catch (Exception ex) when (IsExpected(ex) || ex is UnauthorizedAccessException)
        {
            AuraLog.Error("UpdateDownload", ex);
            return new UpdateDownload(UpdateResult.Failed, null, fileName);
        }
    }

    /// <summary>
    /// Whether the setup registered this copy for the current user only. Inno Setup writes the
    /// uninstall entry under HKCU for a per-user install and under HKLM for one for all users, and
    /// that holds wherever the user chose to put the folder.
    /// </summary>
    private static bool InstalledForCurrentUser()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(UninstallKey);
        return key != null;
    }

    private const string UninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{8E5C1F42-6A1D-4A0B-9C3F-2B7E4D9A1C55}_is1";

    /// <summary>
    /// Writes the verified setup to a fresh folder under the temp directory and runs it silently;
    /// the caller then lets this process close - the installer replaces the running exe, which
    /// cannot happen while it is still open. An install for all users starts the setup elevated
    /// from here, so the UAC prompt belongs to this call: declining it sets
    /// <paramref name="declined"/> and the app simply stays open. <c>/update=1</c> tells the setup
    /// to start the app again after a silent install, which its ordinary finish-page launch skips.
    /// </summary>
    public static bool InstallVerified(UpdateDownload verified, out bool declined)
    {
        declined = false;

        if (verified.Result != UpdateResult.Verified || verified.Data == null || verified.FileName.Length == 0)
        {
            return false;
        }

        try
        {
            string folder = Path.Combine(Path.GetTempPath(), "AuraToggle-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, verified.FileName);
            File.WriteAllBytes(path, verified.Data);

            // The folder is under the user's temp directory, so another process of this user could
            // swap the file between the write and the elevated run. Reopened for reading with no
            // write or delete sharing, what is on disk is hashed through that very handle and the
            // handle is kept until the process has started, and Process.Start still opens it for
            // execution with the handle held. An install for all users is started elevated right
            // here rather than letting the setup reopen itself for elevation from the same path
            // after this handle is gone; once the process runs, its image cannot be replaced.
            using var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(hold), SHA256.HashData(verified.Data)))
            {
                AuraLog.Warn("UpdateInstall: the setup on disk no longer matches the verified download, not run.");
                return false;
            }

            bool perUser = InstalledForCurrentUser();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path,
                perUser ? "/SILENT /CURRENTUSER /update=1" : "/SILENT /ALLUSERS /update=1")
            {
                UseShellExecute = true,
                Verb = perUser ? "" : "runas",
            });
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            AuraLog.Info("UpdateInstall: the UAC prompt was declined, the app stays open.");
            declined = true;
            return false;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException
            or IOException or UnauthorizedAccessException)
        {
            AuraLog.Warn($"UpdateInstall: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Replaces the running portable exe with the verified one: the new bytes go to
    /// <c>&lt;exe&gt;.new</c> next to it, the running exe is renamed to <c>&lt;exe&gt;.old</c> (a
    /// running exe can be renamed, just not deleted or overwritten) and the new one takes its name.
    /// Any IO failure puts the original back and returns false, so the caller can fall back to the
    /// release page. On success <see cref="Program.RestartPath"/> is set; the caller closes the
    /// window and the new copy is started once this process has let go of the single-instance lock.
    /// </summary>
    public static bool SwapPortable(UpdateDownload verified)
    {
        if (verified.Result != UpdateResult.Verified || verified.Data == null ||
            Environment.ProcessPath is not string exe)
        {
            return false;
        }

        string fresh = exe + ".new";
        string old = exe + ".old";
        bool renamed = false;

        try
        {
            File.WriteAllBytes(fresh, verified.Data);

            if (File.Exists(old))
            {
                File.Delete(old);
            }

            File.Move(exe, old);
            renamed = true;
            File.Move(fresh, exe);

            Program.RestartPath = exe;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AuraLog.Warn($"UpdateSwap: {ex.GetType().Name}: {ex.Message}");

            if (renamed && !File.Exists(exe))
            {
                TryMove(old, exe);
            }

            TryDelete(fresh);
            return false;
        }
    }

    /// <summary>
    /// Removes what a portable swap left next to the exe. Silent on purpose: right after a restart
    /// the previous copy may still be shutting down and hold its file, in which case the next start
    /// gets it.
    /// </summary>
    public static void DeleteLeftovers()
    {
        if (Environment.ProcessPath is string exe)
        {
            TryDelete(exe + ".old");
            TryDelete(exe + ".new");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the next start.
        }
    }

    private static void TryMove(string from, string to)
    {
        try
        {
            File.Move(from, to);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AuraLog.Warn($"UpdateSwap: could not put the original back: {ex.Message}");
        }
    }

    /// <summary>
    /// Whether the file version resource of <paramref name="data"/> names
    /// <paramref name="version"/> (major.minor.build). Both the exe and the setup carry one, written
    /// from the same project version the release tag is made from.
    /// </summary>
    internal static bool CarriesVersion(byte[] data, string version)
    {
        if (!Version.TryParse(version, out Version? wanted))
        {
            return false;
        }

        string path = Path.Combine(Path.GetTempPath(), "AuraToggle-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(path, data);
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
            return info.FileMajorPart == wanted.Major && info.FileMinorPart == wanted.Minor &&
                info.FileBuildPart == Math.Max(wanted.Build, 0);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A scanner holding the file a moment longer leaves a stray temp file, nothing more.
            }
        }
    }

    private static UpdateDownload Rejected(string fileName) => new(UpdateResult.NotVerified, null, fileName);

    /// <summary>The setup's asset name: an <c>.exe</c> with "Setup" in it, the one the installer build publishes.</summary>
    private static string? FindSetupName(IEnumerable<string> names)
    {
        foreach (string name in names)
        {
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>The signature text, or null when the response is larger than any real signature.</summary>
    private static async Task<string?> ReadSignatureAsync(HttpContent content)
    {
        await using Stream stream = await content.ReadAsStreamAsync();
        var buffer = new byte[MaxSignatureBytes + 1];
        int length = 0;

        while (length < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(length));
            if (read == 0)
            {
                break;
            }

            length += read;
        }

        return length > MaxSignatureBytes ? null : System.Text.Encoding.ASCII.GetString(buffer, 0, length);
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
    /// off to somewhere else entirely. That matters more here than anywhere: a file and a
    /// checksum handed over by the same foreign server verify against each other perfectly.
    /// </summary>
    private static bool EndedOnTrustedHost(HttpResponseMessage response) =>
        response.RequestMessage?.RequestUri is Uri final && IsTrustedHost(final);

    /// <summary>Everything a flaky connection, a malformed response or a denied request can
    /// throw - none of it is worth more than a log line, since checking for an update is
    /// never allowed to get in the way of switching the lights.</summary>
    private static bool IsExpected(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException
            or InvalidOperationException or NotSupportedException or IOException;
}
