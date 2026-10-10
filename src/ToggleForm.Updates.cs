using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AuraToggle;

/// <summary>
/// The window's side of the update check: the tray entry, the notice, the download and the
/// hand-over to the setup or the portable swap. The checking itself lives in <see cref="AuraUpdate"/>.
/// </summary>
internal sealed partial class ToggleForm
{
    /// <summary>
    /// At most once every 24 hours, and only with the setting on: asks GitHub for a newer
    /// release, entirely in the background - nothing here blocks the window or touches the
    /// controller. A tray balloon and a new tray menu entry are the only visible result, and only
    /// when there actually is a newer version; nothing downloads until that entry is clicked.
    /// </summary>
    private async Task CheckForUpdatesIfDue()
    {
        if (!AuraUpdate.ShouldCheck(_settings))
        {
            return;
        }

        UpdateInfo? found = await AuraUpdate.CheckForUpdateAsync();

        if (IsDisposed)
        {
            return;
        }

        bool due;

        using (AuraFiles.Lock())
        {
            AuraSettings fresh = AuraSettings.Load();
            due = found != null && found.Version != fresh.UpdateNoticeVersion;

            _settings = fresh with
            {
                LastUpdateCheckUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            };
            _settings.Save();
        }

        if (found == null)
        {
            return;
        }

        SetTrayUpdate(string.Format(CultureInfo.CurrentCulture, Strings.TrayUpdateInstall, found.Version),
            async () => await InstallUpdate(found));

        // Kept reachable through the tray icon even after the window is reopened and closed
        // again - RestoreFromTray would otherwise hide the icon (and this entry with it) the
        // moment the window is next shown, the same way it always hides the icon once nothing
        // is minimised to it.
        _updatePending = true;
        _tray.Visible = true;
        _tray.BalloonTipTitle = Strings.WindowTitle;
        _tray.BalloonTipText = string.Format(CultureInfo.CurrentCulture, Strings.TrayUpdateAvailable, found.Version);
        _tray.ShowBalloonTip(10000);

        if (!due)
        {
            return;
        }

        // Only worth showing where it can actually be seen: not while the window is off in the
        // tray or minimised, which is exactly when Program.LaunchedAtStartup - or a manual
        // minimise - routes here without ever un-hiding it. Remembered rather than dropped, so
        // RestoreFromTray can show it the moment there is a window again.
        if (WindowIsVisible)
        {
            ShowUpdateNotice(found);
        }
        else
        {
            _pendingNotice = found;
        }
    }

    private bool WindowIsVisible => Visible && WindowState != FormWindowState.Minimized;

    /// <summary>
    /// Points the tray's update entry at one action. The entry exists once and its click handler
    /// is wired once, so what a click does can change - from "install" to "open the release page"
    /// after a download that could not be verified - without handlers piling up.
    /// </summary>
    private void SetTrayUpdate(string text, Action action)
    {
        _trayUpdate.Text = text;
        _trayUpdateAction = action;
        _trayUpdate.Enabled = true;
        _trayUpdate.Visible = true;
    }

    /// <summary>
    /// Shows the one-time notice popup and records that this version's notice has now been
    /// shown, so it never appears again - called either right away from
    /// <see cref="CheckForUpdatesIfDue"/>, or later from <see cref="RestoreFromTray"/> for a
    /// notice that came due while the window was off in the tray.
    /// </summary>
    private void ShowUpdateNotice(UpdateInfo found)
    {
        using (AuraFiles.Lock())
        {
            _settings = AuraSettings.Load() with { UpdateNoticeVersion = found.Version };
            _settings.Save();
        }

        var popup = new UpdatePopup(found.Version);

        // Same path as the tray entry above - not a second copy of the install logic.
        popup.InstallRequested += async (_, _) => await InstallUpdate(found);

        popup.FormClosed += (_, _) => popup.Dispose();
        popup.Open(this);
    }

    private static void OpenReleasePage(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AuraLog.Warn($"UpdateOpenPage: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Downloads the file, verifies checksum and signature, then installs it and closes this
    /// process: an installed copy runs the new setup (which cannot replace a running exe), a
    /// portable copy swaps its own exe and is started again once it has closed. A failed download
    /// or a declined UAC prompt ends with a balloon instead of a crash, and the tray entry stays
    /// clickable to try again. A file that cannot be shown to be an official release is never
    /// installed: the user is told so and pointed to the release page, and so is a portable swap
    /// that fails on disk.
    /// </summary>
    private async Task InstallUpdate(UpdateInfo info)
    {
        // The notice's button and the tray entry both lead here; one download at a time.
        if (_installingUpdate)
        {
            return;
        }

        _installingUpdate = true;
        try
        {
            await DownloadAndInstall(info);
        }
        finally
        {
            _installingUpdate = false;
        }
    }

    private async Task DownloadAndInstall(UpdateInfo info)
    {
        _trayUpdate.Enabled = false;
        UpdateDownload download = await AuraUpdate.DownloadAndVerifyAsync(info);

        if (IsDisposed)
        {
            return;
        }

        if (download.Result == UpdateResult.NotVerified)
        {
            ShowNotVerified(info);
            return;
        }

        if (download.Result == UpdateResult.Verified)
        {
            bool declined = false;
            if (AuraUpdate.IsInstalled ? AuraUpdate.InstallVerified(download, out declined) : AuraUpdate.SwapPortable(download))
            {
                _exiting = true;
                Close();
                return;
            }

            // A declined UAC prompt is the user's own choice, not a failure; the tray entry stays
            // and offers the install again.
            if (declined)
            {
                _trayUpdate.Enabled = true;
                return;
            }

            // The portable swap failed on disk (a read-only or protected folder): the release page
            // is the way left. An installer that did not start can simply be retried.
            if (!AuraUpdate.IsInstalled && info.HtmlUrl.Length > 0)
            {
                SetTrayUpdate(string.Format(CultureInfo.CurrentCulture, Strings.TrayUpdateOpenPage, info.Version),
                    () => OpenReleasePage(info.HtmlUrl));
            }
        }

        _trayUpdate.Enabled = true;
        ShowBalloon(Strings.TrayUpdateFailed, ToolTipIcon.Warning);
    }

    /// <summary>
    /// The download could not be shown to be an official release, so nothing was installed. Says
    /// so where the user can see it, and turns the tray entry into a link to the release page when
    /// the release has one.
    /// </summary>
    private void ShowNotVerified(UpdateInfo info)
    {
        bool hasPage = info.HtmlUrl.Length > 0;

        if (hasPage)
        {
            SetTrayUpdate(string.Format(CultureInfo.CurrentCulture, Strings.TrayUpdateOpenPage, info.Version),
                () => OpenReleasePage(info.HtmlUrl));
        }
        else
        {
            _trayUpdate.Visible = false;
        }

        if (!WindowIsVisible)
        {
            ShowBalloon(string.Format(CultureInfo.CurrentCulture, Strings.UpdateNotVerified, info.Version),
                ToolTipIcon.Warning);
            return;
        }

        var popup = new UpdatePopup(info.Version, notVerified: true, canOpenPage: hasPage);
        popup.OpenPageRequested += (_, _) => OpenReleasePage(info.HtmlUrl);
        popup.FormClosed += (_, _) => popup.Dispose();
        popup.Open(this);
    }
}
