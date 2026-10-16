using System.Threading.Tasks;

namespace AuraToggle;

/// <summary>
/// The start lighting of a Windows-logon launch that keeps the window: waiting for a controller
/// that is not up yet, applying the start action and sending it again during the first minute.
/// </summary>
internal sealed partial class ToggleForm
{
    /// <summary>
    /// Autostart only: a controller that is not up yet at logon is looked for again every few
    /// seconds, the way the headless start-lighting path waits for it, so the start lighting
    /// still lands. Stopped as soon as one appears or the budget is spent.
    /// </summary>
    private System.Windows.Forms.Timer? _startRetryTimer;

    private int _startRetries;

    /// <summary>Sends the start lighting again during the first minute, see <see cref="StartReassert"/>.</summary>
    private System.Windows.Forms.Timer? _reassertTimer;

    private void StartDiscoveryRetry()
    {
        const int intervalMs = 3000;
        const int maxRetries = 20;

        _startRetries = 0;

        // The window stays usable while this waits for the controller: opening it from the tray
        // finds the controller too, and whatever the user then picks must not be replaced by the
        // start action a few seconds later.
        string baseline = Program.LightingRecords();

        _startRetryTimer = new System.Windows.Forms.Timer { Interval = intervalMs };
        _startRetryTimer.Tick += async (_, _) =>
        {
            if (_busy || IsDisposed)
            {
                return;
            }

            // Stopped while the discovery runs, so a slow sweep is never started a second time.
            _startRetryTimer?.Stop();
            _startRetries++;

            if (_devices.Count == 0)
            {
                await DiscoverDevices();
            }

            if (IsDisposed)
            {
                return;
            }

            if (_devices.Count > 0)
            {
                StopDiscoveryRetry();

                if (Program.LightingRecords() != baseline)
                {
                    AuraLog.Info("Lighting changed before the controller appeared, start action skipped.");
                    return;
                }

                await ApplyStartAction();
            }
            else if (_startRetries >= maxRetries)
            {
                StopDiscoveryRetry();
                AuraLog.Info("No controller appeared within the autostart retry window.");
            }
            else
            {
                _startRetryTimer?.Start();
            }
        };
        _startRetryTimer.Start();
    }

    private void StopDiscoveryRetry()
    {
        _startRetryTimer?.Stop();
        _startRetryTimer?.Dispose();
        _startRetryTimer = null;
    }

    /// <summary>
    /// Puts the lighting into the state chosen in the settings. Only called for a real
    /// Windows-startup launch (see <see cref="OnShown"/>), never for the user opening the window
    /// by hand later in the same session.
    /// </summary>
    private async Task ApplyStartAction()
    {
        string action = _settings.StartAction;

        if (action == AuraSettings.StartActionNone)
        {
            return;
        }

        // Taken under the same lock as the apply, and also when the apply fails: a controller
        // that dropped the first burst at logon is exactly what the rounds below put right.
        string snapshot = "";
        await Run(() =>
        {
            using (AuraFiles.HardwareLock())
            {
                try
                {
                    return Program.ApplyStartAction(action) ?? _state;
                }
                finally
                {
                    snapshot = Program.LightingRecords();
                }
            }
        }, queue: true);

        StartReassert(action, snapshot);
    }

    /// <summary>
    /// Sends the start lighting again on <see cref="Program.ReassertPausesMs"/>, the same rounds
    /// the headless autostart runs, and stops as soon as anything else changed the lighting.
    /// </summary>
    private void StartReassert(string action, string snapshot)
    {
        StopReassert();

        int round = 0;
        var timer = new System.Windows.Forms.Timer { Interval = Program.ReassertPausesMs[0] };
        _reassertTimer = timer;

        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            if (IsDisposed || !ReferenceEquals(_reassertTimer, timer))
            {
                return;
            }

            // Something the user started is still running; its outcome decides whether this
            // round still applies, so it waits for it rather than being dropped.
            if (_busy)
            {
                timer.Interval = 1000;
                timer.Start();
                return;
            }

            bool changed = false;
            await Run(() =>
            {
                AuraState? next = Program.ReassertStartAction(action, ref snapshot);
                changed = next == null;
                return next ?? AuraState.Load();
            }, queue: true);

            round++;
            if (changed || round >= Program.ReassertPausesMs.Length || IsDisposed)
            {
                StopReassert();
                return;
            }

            timer.Interval = Program.ReassertPausesMs[round];
            timer.Start();
        };

        timer.Start();
    }

    private void StopReassert()
    {
        _reassertTimer?.Stop();
        _reassertTimer?.Dispose();
        _reassertTimer = null;
    }
}
