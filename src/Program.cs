using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;

// The tool's own P/Invoke calls load their libraries from System32 only, so a hid.dll or dwmapi.dll
// planted beside a portable exe is never picked up by them.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace AuraToggle;

internal static partial class Program
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private static readonly IntPtr Broadcast = new(0xFFFF);

    /// <summary>Asks an already running window to come back from the notification area.</summary>
    public static readonly uint ShowWindowMessage = RegisterWindowMessage("AuraToggle.Show");

    /// <summary>True when Windows started the tool at logon rather than the user.</summary>
    public static bool LaunchedAtStartup { get; private set; }

    /// <summary>
    /// Set once the main window exists, so a fatal exception on a worker thread - where
    /// <see cref="Application.MessageLoop"/> reads false, that property being per-thread rather
    /// than per-process - still has somewhere to marshal the error dialog to instead of falling
    /// through to a silent <see cref="Environment.Exit"/>.
    /// </summary>
    private static ToggleForm? _mainForm;

    /// <summary>
    /// The version as the release carries it - "1.1.0", not the four part assembly version
    /// "1.1.0.0", so what the tool reports matches the tag and the setup's file name.
    /// </summary>
    internal static string VersionText =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0]
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";

    [STAThread]
    private static int Main(string[] args)
    {
        // First of all, before anything that reads a file: a profile folder that cannot be read
        // makes the two lines below throw, and without the handlers already in place that is a
        // raw .NET crash box instead of this tool's own error dialog.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => HandleFatal(e.Exception, "UI thread");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            HandleFatal(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()), "Unhandled");

#if REVIEW
        // Before anything reads or writes the data folder: the surface tool swaps it for a
        // throwaway one first. Exists only in the review build.
        if (args.Length >= 1 && Normalise(args[0]) is "review" or "shot")
        {
            return RunSurfaceTool(args);
        }
#endif

        // The language choice also governs usage and error output on the command line.
        AuraSettings launchSettings = AuraSettings.Load();
        Strings.Override = launchSettings.Language;
        AuraLog.Info($"Start {VersionText}");

        LaunchedAtStartup = args.Length == 1 && args[0] == AuraSettings.AutoStartArgument;

        // Set the start lighting and leave again, without a window or a tray icon. Done before
        // the single-instance check on purpose: holding that lock while waiting for a controller
        // that is still coming up at logon would make opening the tool by hand in those seconds
        // do nothing at all.
        if (LaunchedAtStartup && launchSettings is { CloseAfterStart: true } &&
            launchSettings.StartAction != AuraSettings.StartActionNone)
        {
            return ApplyStartActionAndExit(launchSettings.StartAction);
        }

        if (args.Length == 0 || LaunchedAtStartup)
        {
            return RunWindow();
        }

        try
        {
            return Run(args);
        }
        catch (AuraNotFoundException ex)
        {
            AuraLog.Error("CLI", ex);
            WriteError(ex.Message);
            return ex.ExitCode;
        }
        catch (IOException ex)
        {
            AuraLog.Error("CLI", ex);
            WriteError(ex.Message);
            return 5;
        }
    }

    /// <summary>The window path: one instance at a time, until the window closes.</summary>
    private static int RunWindow()
    {
        // A second start hands over to the instance already running, which may be sitting
        // in the notification area with no window to click.
        using var single = CreateSingleInstanceMutex(out bool first);
        if (!first)
        {
            PostMessage(Broadcast, ShowWindowMessage, IntPtr.Zero, IntPtr.Zero);
            return 0;
        }

        ApplicationConfiguration.Initialize();
#pragma warning disable WFO5001 // colour mode support is still marked experimental
        Application.SetColorMode(SystemColorMode.System);
#pragma warning restore WFO5001
        var form = new ToggleForm();
        _mainForm = form;
        Application.Run(form);
        GC.KeepAlive(single);
        return 0;
    }

    // A custom preset's name is the user's own text and the log ends up pasted into public
    // issues, so it is logged by kind only.
    private static string LogName(string action) =>
        action.StartsWith(AuraSettings.StartActionCustomPrefix, StringComparison.Ordinal) ? "custom preset" : action;

    /// <summary>
    /// The headless half of autostart. A controller can still be enumerating when the logon
    /// launch arrives, so a missing one is retried for a while before giving up; the outcome is
    /// written to the log, the only place a run without a window can report to.
    /// </summary>
    private static int ApplyStartActionAndExit(string action)
    {
        const int attempts = 20;
        const int pauseMs = 3000;

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                string snapshot;
                using (AuraFiles.HardwareLock())
                {
                    if (ApplyStartAction(action) == null)
                    {
                        AuraLog.Info($"Start action \"{LogName(action)}\" names nothing that exists any more, left the lighting alone.");
                        return 0;
                    }

                    // Taken under the same lock as the apply, so nothing written in between can
                    // end up as the baseline the re-asserting compares against.
                    snapshot = LightingRecords();
                }

                AuraLog.Info($"Start action \"{LogName(action)}\" applied.");
                foreach (int pause in ReassertPausesMs)
                {
                    Thread.Sleep(pause);
                    if (ReassertStartAction(action, ref snapshot) == null)
                    {
                        break;
                    }
                }

                AuraLog.Info("Start action settled, exiting without a window.");
                return 0;
            }
            catch (AuraNotFoundException ex) when (!ex.WorthRetrying)
            {
                // The reason is already in the log, from where it was found out.
                return ex.ExitCode;
            }
            catch (Exception ex) when (ex is AuraNotFoundException or IOException)
            {
                if (attempt == attempts)
                {
                    AuraLog.Error("Start action", ex);
                    return ex is AuraNotFoundException notFound ? notFound.ExitCode : 5;
                }

                Thread.Sleep(pauseMs);
            }
        }
    }

    /// <summary>
    /// How long after the start lighting it is sent again, one pause before each round. The
    /// controller answers and takes the burst as soon as it enumerates, but the board can still
    /// reset its lighting after that (the USB hub settling, a fast-startup resume, another
    /// lighting service starting up) and leave every header dark, so it is sent a few more times
    /// during the first minute after logon.
    /// </summary>
    public static readonly int[] ReassertPausesMs = [5000, 10000, 15000, 30000];

    /// <summary>
    /// One round of sending the start lighting again. Re-sending what is already showing changes
    /// nothing visible. As soon as the lighting was changed from anywhere else since
    /// <paramref name="snapshot"/> (the window, the command line, the hotkey), the records on disk
    /// differ and this returns null without touching anything, so a choice the user just made is
    /// never undone; the caller stops there.
    /// </summary>
    /// <param name="snapshot">The records as last left by the start action, updated after the round.</param>
    /// <returns>The state afterwards, or null once the lighting was changed by someone else.</returns>
    public static AuraState? ReassertStartAction(string action, ref string snapshot)
    {
        using (AuraFiles.HardwareLock())
        {
            if (LightingRecords() != snapshot)
            {
                AuraLog.Info("Lighting changed since the start action, no longer re-asserting it.");
                return null;
            }

            AuraState state = AuraState.Load();
            try
            {
                // The lock is re-entrant on this thread, so the apply below holds it straight
                // through and nothing can slip in between the check and the write.
                state = ApplyStartAction(action) ?? state;
            }
            catch (Exception ex) when (ex is AuraNotFoundException or IOException)
            {
                // The controller dropping out for a moment is exactly what this is for - the
                // next round tries again.
                AuraLog.Warn($"Start action re-assert: {ex.GetType().Name}: {ex.Message}");
            }

            snapshot = LightingRecords();
            return state;
        }
    }

    /// <summary>The stored lighting records as raw text, to tell whether anyone changed them.</summary>
    public static string LightingRecords()
    {
        static string Text(string name)
        {
            try
            {
                return File.ReadAllText(AuraFiles.PathTo(name));
            }
            catch (Exception ex) when (AuraFiles.IsExpected(ex))
            {
                return "";
            }
        }

        return Text(AuraState.FileName) + "\n" + Text(AuraChannelStates.FileName);
    }

    /// <summary>
    /// Puts the lighting into the state a start action names: off, a built-in effect in the
    /// remembered colour, or a custom preset. Shared by the window and the headless autostart.
    /// Returns null when the action names a preset that no longer exists.
    /// </summary>
    public static AuraState? ApplyStartAction(string action)
    {
        if (action == AuraSettings.StartActionOff)
        {
            return Switch(on: false);
        }

        if (AuraSettings.CustomStartPreset(action) is string name)
        {
            return AuraCustomPresets.Load().Find(p => p.Name == name) is CustomPreset custom
                ? ApplyCustomPreset(custom)
                : null;
        }

        return action != AuraSettings.StartActionNone && AuraPresets.Find(action) is AuraPreset preset
            ? ApplyPreset(preset)
            : null;
    }

    /// <summary>
    /// A plain named <see cref="Mutex"/> lives in the caller's Terminal Services session, not the
    /// machine - two sessions of the same account (a physical logon plus a Remote Desktop one)
    /// would each see themselves as the first instance and both open the one physical controller.
    /// The "Global\" prefix makes it machine-wide instead, which is what "one instance" is meant
    /// to guarantee here. Falls back to a session-local name if creating a global object is
    /// denied - a locked-down policy blocking that is rare, and failing open (one instance per
    /// session) beats not starting at all.
    /// </summary>
    private static Mutex CreateSingleInstanceMutex(out bool first)
    {
        try
        {
            return new Mutex(initiallyOwned: true, @"Global\AuraToggle.SingleInstance", out first);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            AuraLog.Info("Global single-instance lock denied, falling back to per-session.");
            return new Mutex(initiallyOwned: true, "AuraToggle.SingleInstance", out first);
        }
    }

    /// <summary>
    /// The last line of defence: an exception nothing more specific caught. Logged always; shown
    /// in <see cref="ErrorDialog"/> whenever there is a UI thread left to paint it on - the
    /// command-line branch never starts one, so it falls through to stderr and exit code 5
    /// instead, matching how <see cref="AuraNotFoundException"/> and <see cref="IOException"/> are
    /// already reported there.
    /// </summary>
    /// <remarks>
    /// <see cref="Application.MessageLoop"/> is per-thread: it reads true on the UI thread (the
    /// common case, <see cref="Application.ThreadException"/>) but false for
    /// <see cref="AppDomain.UnhandledException"/> firing on a worker thread, even with the
    /// window very much still open - that would fall through to <see cref="Environment.Exit"/>
    /// with nothing shown at all. <see cref="_mainForm"/> covers that case by marshalling the
    /// dialog over with <see cref="Control.BeginInvoke(Delegate)"/> instead.
    /// </remarks>
    private static void HandleFatal(Exception ex, string context)
    {
        // Exiting only once the dialog itself closes - Application.Exit tears down every form on
        // the thread, so calling it right away closed the dialog before the user could read or
        // copy the one detail text describing what went wrong.
        if (Application.MessageLoop)
        {
            ErrorDialog.Report(ex, context, owner: null, onClosed: Application.Exit);
            return;
        }

        if (_mainForm is { IsDisposed: false, IsHandleCreated: true } form)
        {
            try
            {
                // BeginInvoke only queues the dialog - it does not wait for it, and this method
                // returning is exactly what AppDomain.UnhandledException treats as "handled",
                // after which the runtime tears the process down right away. Undoing that requires
                // actually blocking this (crashing) thread until the dialog closes, or the queued
                // Report() would never get a turn to run before the process is already gone.
                using var closed = new ManualResetEventSlim(false);
                form.BeginInvoke(() => ErrorDialog.Report(ex, context, owner: null, onClosed: () =>
                {
                    Application.Exit();
                    closed.Set();
                }));
                closed.Wait(TimeSpan.FromSeconds(30));
                return;
            }
            catch (InvalidOperationException)
            {
                // The handle died between the check above and the call - fall through to the
                // console/exit path below like there was never a window to use.
            }
        }

        AuraLog.Error(context, ex);
        WriteError(ex.Message);
        Environment.Exit(5);
    }

    private static int Run(string[] args)
    {
        (List<string> rest, string? deviceArg, string? channelArg) = ExtractTargeting(args);
        if (rest.Count == 0)
        {
            return Usage();
        }

        string command = Normalise(rest[0]);

        // These four never look at deviceArg/channelArg at all - a custom preset names its own
        // channels, and the other three have nothing to target. Silently dropping a -device or
        // -channel typed alongside one of them read as "it worked" when it did nothing.
        if ((deviceArg != null || channelArg != null) &&
            command is "version" or "list" or "status" or "custom" or "help" or "h" or "?")
        {
            return Usage();
        }

        if (command is "help" or "h" or "?" && rest.Count == 1)
        {
            return PrintHelp();
        }

        if (command == "version" && rest.Count == 1)
        {
            WriteLine(VersionText);
            return 0;
        }

        if (command == "list" && rest.Count == 1)
        {
            return PrintList();
        }

        if (command == "status" && rest.Count == 1)
        {
            return PrintStatus(json: false);
        }

        if (command == "status" && rest.Count == 2 && Normalise(rest[1]) == "json")
        {
            return PrintStatus(json: true);
        }

        if (command == "custom" && rest.Count == 2)
        {
            // By name first, since that is unambiguous; a name has to lose to a number that
            // happens to also be a preset's name, but not the other way round - PrintList's own
            // numbering only stays valid for as long as nothing is added, removed or reordered.
            // The command line accepts forgiving spelling like -preset does; the name the program
            // itself remembers (state.CustomPreset) was written by us, so that stays exact-only.
            List<CustomPreset> presets = AuraCustomPresets.Load();
            CustomPreset? custom = presets.Find(p => p.Name == rest[1]) ??
                presets.Find(p => AuraPresets.Normalise(p.Name) == AuraPresets.Normalise(rest[1])) ??
                (int.TryParse(rest[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
                    && index >= 1 && index <= presets.Count ? presets[index - 1] : null);
            if (custom == null)
            {
                return Usage();
            }

            ApplyCustomPreset(custom);
            return 0;
        }

        if (deviceArg == null && channelArg == null)
        {
            return RunApply(command, rest, null, -1, null, null);
        }

        // One discovery serves both the lookup of the named target and the action, under one
        // hardware lock, so the controllers are not handshaken again for every step.
        using IDisposable guard = AuraFiles.HardwareLock();
        if (!AuraDevice.TryDiscoverAll(out List<AuraDevice> devices, out int discoverError))
        {
            WriteError(ListErrorText(discoverError));
            return discoverError;
        }

        try
        {
            List<AuraDeviceSummary> summaries = AuraDevice.Summarize(devices);
            if (!TryResolveTarget(deviceArg, channelArg, summaries, out string? targetDevice,
                    out int targetChannel, out int targetingError))
            {
                return targetingError;
            }

            return RunApply(command, rest, targetDevice, targetChannel, devices, summaries);
        }
        finally
        {
            Close(devices);
        }
    }

    /// <summary>
    /// The on/off/toggle/preset/brightness commands. <paramref name="devices"/> are the controllers
    /// a targeted call already opened (null for the whole board); the caller disposes them.
    /// </summary>
    private static int RunApply(string command, List<string> rest, string? targetDevice, int targetChannel,
        List<AuraDevice>? devices, List<AuraDeviceSummary>? summaries)
    {
        if (rest.Count == 1 && command is "on" or "off")
        {
            Switch(command == "on", targetDevice, targetChannel, devices);
            return 0;
        }

        if (rest.Count == 1 && command == "toggle")
        {
            // A single channel decides by its own remembered state, not the board's - the whole
            // point of targeting one channel is that it can differ from the others. A channel
            // with no record of its own has never been switched on its own, so it still follows
            // whatever the board is doing.
            bool currentlyOn = targetChannel >= 0
                ? AuraChannelStates.Get(AuraChannelStates.All(), targetDevice!, targetChannel)?.On
                    ?? AuraState.Load().On
                : !string.IsNullOrEmpty(targetDevice)
                    ? ControllerIsOn(targetDevice, summaries!)
                    : AuraState.Load().On;

            Switch(!currentlyOn, targetDevice, targetChannel, devices);
            return 0;
        }

        if (command == "preset" && rest.Count is 2 or 3)
        {
            AuraPreset? preset = AuraPresets.Find(rest[1]);
            if (preset == null)
            {
                return Usage();
            }

            Color? colour = null;
            if (rest.Count == 3)
            {
                if (!TryParseColour(rest[2], out Color parsed))
                {
                    return Usage();
                }

                colour = parsed;
            }

            ApplyPreset(preset, colour, targetDevice, targetChannel, devices);
            return 0;
        }

        if (command == "brightness" && rest.Count == 2)
        {
            if (!byte.TryParse(rest[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte percent) ||
                percent > AuraState.MaxBrightness)
            {
                return Usage();
            }

            // Below the floor is clamped rather than rejected: "-brightness 0" reads as "as dim as
            // this tool goes", not a typo worth an error - unlike a value above the ceiling, which
            // is far more likely to be one.
            SetBrightness(Math.Max(percent, AuraState.MinBrightness), targetDevice, targetChannel, devices);
            return 0;
        }

        return Usage();
    }

    /// <summary>
    /// Pulls <c>-device</c> and <c>-channel</c> out of the argument list wherever they appear,
    /// each consuming the token right after it; everything else stays in order for the existing
    /// positional parsing below to see unchanged.
    /// </summary>
    private static (List<string> Positional, string? Device, string? Channel) ExtractTargeting(string[] args)
    {
        var rest = new List<string>();
        string? device = null;
        string? channel = null;

        for (int i = 0; i < args.Length; i++)
        {
            // Unlike -on/-off, which accept a bare word precisely because they take no value of
            // their own, these two consume whatever token follows - so a leading dash (or slash)
            // is required to tell the flag apart from a positional value that happens to spell
            // the same word, such as a channel or preset genuinely named "device" or "channel".
            bool flagLike = args[i].Length > 0 && (args[i][0] == '-' || args[i][0] == '/');
            string normalised = Normalise(args[i]);

            if (flagLike && normalised == "device" && i + 1 < args.Length)
            {
                device = args[++i];
            }
            else if (flagLike && normalised == "channel" && i + 1 < args.Length)
            {
                channel = args[++i];
            }
            else
            {
                rest.Add(args[i]);
            }
        }

        return (rest, device, channel);
    }

    /// <summary>
    /// Turns <c>-device</c>/<c>-channel</c> into the (deviceKey, channel) pair every apply method
    /// already takes, against the controllers the caller already discovered.
    /// </summary>
    private static bool TryResolveTarget(string? deviceArg, string? channelArg,
        List<AuraDeviceSummary> devices, out string? deviceKey, out int channel, out int errorExitCode)
    {
        deviceKey = null;
        channel = -1;
        errorExitCode = 0;

        List<ChannelEntry> entries = FlattenChannels(devices);

        int? deviceNumber = null;
        if (deviceArg != null)
        {
            if (!int.TryParse(deviceArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) ||
                number < 1 || number > devices.Count)
            {
                WriteError(Strings.ErrorDeviceNotFound);
                errorExitCode = 2;
                return false;
            }

            deviceNumber = number;
            deviceKey = devices[number - 1].Key;
        }

        if (channelArg == null)
        {
            return true;
        }

        List<ChannelEntry> pool = deviceNumber == null
            ? entries
            : entries.FindAll(e => e.DeviceNumber == deviceNumber.Value);

        ChannelEntry? resolved = ResolveChannelEntry(channelArg, pool, out List<string> candidates);
        if (resolved == null)
        {
            WriteError(Strings.ErrorChannelNotFound);
            foreach (string candidate in candidates)
            {
                WriteError(candidate);
            }

            errorExitCode = 2;
            return false;
        }

        deviceKey = resolved.DeviceKey;
        channel = resolved.ChannelIndex;
        return true;
    }

    /// <summary>One channel, numbered for <c>-list</c>/<c>-status</c> and <c>-channel</c>.</summary>
    private sealed record ChannelEntry(string DeviceKey, int ChannelIndex, int DeviceNumber, int ChannelNumber,
        int FlatNumber, AuraChannel Channel);

    private static List<ChannelEntry> FlattenChannels(List<AuraDeviceSummary> devices)
    {
        var list = new List<ChannelEntry>();
        int flat = 0;

        for (int d = 0; d < devices.Count; d++)
        {
            for (int c = 0; c < devices[d].Channels.Count; c++)
            {
                flat++;
                list.Add(new ChannelEntry(devices[d].Key, devices[d].Channels[c].Index, d + 1, c + 1, flat,
                    devices[d].Channels[c]));
            }
        }

        return list;
    }

    /// <summary>
    /// Matches, in order: the flat number from <c>-list</c>, the "controller.channel" form, the
    /// default channel name in either language, and a name of the user's own - the same forgiving
    /// compare (case, spaces, hyphens ignored) already used for preset names. Zero or more than
    /// one match is not resolved here; the caller reports it as unknown or ambiguous either way,
    /// with every entry in <paramref name="pool"/> listed as a possible target.
    /// </summary>
    private static ChannelEntry? ResolveChannelEntry(string text, List<ChannelEntry> pool, out List<string> candidates)
    {
        candidates = new List<string>();

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int flat) &&
            pool.Find(e => e.FlatNumber == flat) is ChannelEntry byFlat)
        {
            return byFlat;
        }

        string[] parts = text.Split('.');
        if (parts.Length == 2 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int deviceNumber) &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int channelNumber) &&
            pool.Find(e => e.DeviceNumber == deviceNumber && e.ChannelNumber == channelNumber) is ChannelEntry byDotted)
        {
            return byDotted;
        }

        string wanted = AuraPresets.Normalise(text);
        Dictionary<string, string> customNames = AuraChannelNames.All();
        List<ChannelEntry> matches = pool.FindAll(e => ChannelNameMatches(e, wanted, customNames));

        if (matches.Count == 1)
        {
            return matches[0];
        }

        candidates = pool.ConvertAll(DescribeChannel);
        return null;
    }

    private static bool ChannelNameMatches(ChannelEntry entry, string wanted, Dictionary<string, string> customNames)
    {
        if (AuraChannelNames.Get(customNames, entry.DeviceKey, entry.ChannelIndex) is string own &&
            AuraPresets.Normalise(own) == wanted)
        {
            return true;
        }

        return Strings.Codes.Any(language =>
            AuraPresets.Normalise(DefaultChannelName(entry.Channel, language)) == wanted);
    }

    private static string DefaultChannelName(AuraChannel channel, string language) => channel.Onboard
        ? Strings.InLanguage("ChannelOnboard", language)
        : string.Format(CultureInfo.InvariantCulture, Strings.InLanguage("ChannelHeader", language), channel.Header);

    /// <summary>"&lt;flat&gt;\t&lt;device&gt;.&lt;channel&gt;\t&lt;name&gt;" - the identifiers
    /// <c>-channel</c> accepts, always in English regardless of the display language so a script
    /// reading <c>-list</c> does not break when the user switches it.</summary>
    private static string DescribeChannel(ChannelEntry entry) =>
        $"{entry.FlatNumber}\t{entry.DeviceNumber}.{entry.ChannelNumber}\t{DefaultChannelName(entry.Channel, "en")}";

    /// <summary>One "  &lt;name&gt;   &lt;description&gt;" line of the help, in one column layout.</summary>
    private static string Row(string name, string description) => "  " + name.PadRight(24) + description;

    /// <summary>
    /// The full reference for the command line. Deliberately English whatever the interface
    /// language is, for the same reason <c>-list</c> and <c>-status</c> are: this is what gets
    /// pasted into a script, a batch file or a bug report, and it should read the same for
    /// everyone. The short usage line the error paths print stays translated.
    /// </summary>
    private static int PrintHelp()
    {
        WriteLine($"Aura Toggle {VersionText}");
        WriteLine("Switches ASUS Aura mainboard lighting. Nothing is written to the controller's");
        WriteLine("flash, so a reboot always restores the BIOS lighting.");
        WriteLine("");
        WriteLine("Usage: AuraToggle.exe [command] [options]");
        WriteLine("");
        WriteLine("Starting it with no command at all opens the window.");
        WriteLine("");
        WriteLine("Commands:");
        WriteLine("  -on                     Switch the lighting on, each channel to its own last look.");
        WriteLine("  -off                    Switch the lighting off.");
        WriteLine("  -toggle                 Switch on if it is off, off if it is on.");
        WriteLine("  -preset <effect> [rgb]  Apply an effect, optionally with a colour.");
        WriteLine(Row($"-brightness <{AuraState.MinBrightness}-{AuraState.MaxBrightness}>",
            "Set the brightness in percent. Does nothing for the"));
        WriteLine(Row("", "effects the firmware colours itself."));
        WriteLine("  -custom <name|number>   Apply a custom preset saved in the window, by name or by");
        WriteLine(Row("", "its number from -list."));
        WriteLine("  -list                   List every controller and channel with its identifiers.");
        WriteLine(Row("-status [--json]", "Print the effect, colour, brightness and state per"));
        WriteLine(Row("", "channel, as text or as one line of JSON."));
        WriteLine("  --version               Print the version number.");
        WriteLine("  -help                   Show this text.");
        WriteLine("");
        WriteLine("Options (for -on, -off, -toggle, -preset and -brightness):");
        WriteLine("  -device <n>             Limit to one controller, numbered as in -list.");
        WriteLine("  -channel <id>           Limit to one channel: its number from -list, the");
        WriteLine("                          \"controller.channel\" form, or its name.");
        WriteLine("");
        WriteLine($"Effects: {AuraPresets.Names}");
        WriteLine("Colours: #RRGGBB, RRGGBB or a common colour name such as \"red\".");
        WriteLine("");
        WriteLine("A command may be written with one dash, two, or a slash: -on, --on and /on are");
        WriteLine("the same. -device and -channel always need their leading dash, so a channel or");
        WriteLine("preset genuinely named \"device\" is not mistaken for the option.");
        WriteLine("");
        WriteLine("Exit codes: 0 success, 2 bad arguments, 3 no controller found,");
        WriteLine("            4 controller busy, 5 read or write failed.");
        WriteLine("");
        WriteLine("Examples:");
        WriteLine("  AuraToggle.exe -off");
        WriteLine("  AuraToggle.exe -preset static #FF0000");
        WriteLine("  AuraToggle.exe -preset breathing red -channel 2");
        WriteLine("  AuraToggle.exe -brightness 40 -device 1");
        return 0;
    }

    /// <summary>The message that goes with the exit code <see cref="AuraDevice.ListDevices(out int)"/> reports.</summary>
    internal static string ListErrorText(int exitCode) => exitCode switch
    {
        4 => Strings.ErrorControllerBusy,
        5 => Strings.ErrorWriteGeneric,
        _ => Strings.ErrorControllerNotFound,
    };

    private static int PrintList()
    {
        List<AuraDeviceSummary> devices = AuraDevice.ListDevices(out int errorExitCode);
        if (devices.Count == 0)
        {
            WriteError(ListErrorText(errorExitCode));
            return errorExitCode;
        }

        WriteLine("Devices:");
        for (int d = 0; d < devices.Count; d++)
        {
            WriteLine($"{d + 1}\t{devices[d].Name}");
        }

        WriteLine("");
        WriteLine("Channels:");
        foreach (ChannelEntry entry in FlattenChannels(devices))
        {
            WriteLine(DescribeChannel(entry));
        }

        List<CustomPreset> presets = AuraCustomPresets.Load();
        if (presets.Count > 0)
        {
            WriteLine("");
            WriteLine("Presets:");
            for (int p = 0; p < presets.Count; p++)
            {
                WriteLine($"{p + 1}\t{presets[p].Name}");
            }
        }

        return 0;
    }

    private static int PrintStatus(bool json)
    {
        List<AuraDeviceSummary> devices = AuraDevice.ListDevices(out int errorExitCode);
        if (devices.Count == 0)
        {
            WriteError(ListErrorText(errorExitCode));
            return errorExitCode;
        }

        AuraState state = AuraState.Load();
        Dictionary<string, ChannelLighting> remembered = AuraChannelStates.All();
        List<ChannelEntry> channels = FlattenChannels(devices);

        if (json)
        {
            WriteLine(StatusJson(state, remembered, channels));
            return 0;
        }

        WriteLine(FormatStatusLine("Board", state.On, state.Mode, state.Red, state.Green, state.Blue,
            state.Brightness.ToString(CultureInfo.InvariantCulture)));

        foreach (ChannelEntry entry in channels)
        {
            string label = DescribeChannel(entry);
            ChannelLighting? own = AuraChannelStates.Get(remembered, entry.DeviceKey, entry.ChannelIndex);

            WriteLine(own == null
                ? $"{label}\tfollows board"
                : FormatStatusLine(label, own.On, own.Mode, own.Red, own.Green, own.Blue,
                    own.Brightness == 0 ? "-" : own.Brightness.ToString(CultureInfo.InvariantCulture)));
        }

        return 0;
    }

    /// <summary>
    /// One line of JSON, machine-readable output for <c>-status --json</c>. A channel with no
    /// record of its own reports the board's own effect and colour - what it would actually run -
    /// with brightness 0, the same "follows the board" sentinel <c>channel-state.json</c> itself
    /// uses, rather than a null the caller would have to special-case.
    /// </summary>
    private static string StatusJson(AuraState state, Dictionary<string, ChannelLighting> remembered,
        List<ChannelEntry> channels)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("on", state.On);
            writer.WriteString("effect", AuraPresets.ByMode(state.Mode)?.Key ?? "unknown");
            writer.WriteString("colour", $"#{state.Red:X2}{state.Green:X2}{state.Blue:X2}");
            writer.WriteNumber("brightness", state.Brightness);
            writer.WriteString("customPreset", state.CustomPreset);

            writer.WriteStartArray("channels");
            foreach (ChannelEntry entry in channels)
            {
                ChannelLighting? own = AuraChannelStates.Get(remembered, entry.DeviceKey, entry.ChannelIndex);
                bool on = own?.On ?? state.On;
                byte mode = own?.Mode ?? state.Mode;
                byte red = own?.Red ?? state.Red;
                byte green = own?.Green ?? state.Green;
                byte blue = own?.Blue ?? state.Blue;
                byte brightness = own?.Brightness ?? 0;

                writer.WriteStartObject();
                writer.WriteNumber("device", entry.DeviceNumber);
                writer.WriteNumber("channel", entry.ChannelNumber);
                writer.WriteString("name", DefaultChannelName(entry.Channel, "en"));
                writer.WriteString("effect", AuraPresets.ByMode(mode)?.Key ?? "unknown");
                writer.WriteString("colour", $"#{red:X2}{green:X2}{blue:X2}");
                writer.WriteNumber("brightness", brightness);
                writer.WriteBoolean("on", on);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string FormatStatusLine(string label, bool on, byte mode, byte red, byte green, byte blue,
        string brightness)
    {
        AuraPreset? preset = AuraPresets.ByMode(mode);
        string effectKey = preset?.Key ?? "unknown";
        string colour = preset?.UsesColour == true ? $"#{red:X2}{green:X2}{blue:X2}" : "-";

        return $"{label}\t{(on ? "on" : "off")}\t{effectKey}\t{colour}\tbrightness {brightness}";
    }

    /// <summary>Accepts #RRGGBB, RRGGBB and the common colour names.</summary>
    private static bool TryParseColour(string value, out Color colour)
    {
        if (Theme.TryParseHex(value, out colour))
        {
            return true;
        }

        colour = Color.FromName(value.Trim());
        return colour.IsKnownColor && !colour.IsSystemColor && colour.A == 255;
    }

    private static int Usage()
    {
        WriteError(Strings.UsageLine);
        WriteError(string.Format(CultureInfo.CurrentCulture, Strings.UsagePresets, AuraPresets.Names));
        return 2;
    }

    /// <summary>Accepts -on, --on, /on and on, in any casing.</summary>
    private static string Normalise(string argument) => argument.TrimStart('-', '/').ToLowerInvariant();

    /// <summary>
    /// Applies the stored effect or switches every channel off. Nothing is written to the
    /// controller flash, so a reboot always brings the mainboard lighting back.
    /// </summary>
    /// <param name="deviceKey">
    /// Limits the switch to one controller, matched by <see cref="AuraDevice.Key"/>. Null or
    /// empty means every controller - the default, and the only option on the command line.
    /// A remembered custom preset always targets every controller it names, so it is only
    /// re-applied when switching every device on at once.
    /// </param>
    /// <param name="channel">One channel of that controller, or -1 for all of them.</param>
    public static AuraState Switch(bool on, string? deviceKey = null, int channel = -1) =>
        Switch(on, deviceKey, channel, null);

    /// <param name="open">Controllers the caller already discovered and disposes itself; null discovers here.</param>
    private static AuraState Switch(bool on, string? deviceKey, int channel, List<AuraDevice>? open)
    {
        // Locked around the whole read-modify-write: a command-line switch and one from the
        // window running at the same time would otherwise each save their own copy of
        // state.json and one change would vanish. Re-entrant on the same thread; the calls this
        // makes into AuraChannelStates and AuraChannelNames take the files lock under it.
        using IDisposable guard = AuraFiles.HardwareLock();

        AuraState state = AuraState.Load();
        bool presetUnmatched = false;

        if (on && string.IsNullOrEmpty(deviceKey) && state.CustomPreset.Length > 0)
        {
            CustomPreset? preset = AuraCustomPresets.Load().Find(p => p.Name == state.CustomPreset);
            if (preset != null)
            {
                // Null when the preset names none of the attached controllers: the plain restore
                // below brings the board back instead of failing on a stale device path.
                AuraState? applied = ApplyCustomPresetCore(preset, null, restoreOthers: true);
                if (applied != null)
                {
                    return applied;
                }

                presetUnmatched = true;
            }
        }

        // The board counts as on while any single channel still is, so switching one header off
        // does not make the tray claim the whole board went dark.
        bool anyOn = on
            // Each channel comes back to its own last look, not to one colour smeared across the
            // board - and without rewriting what is remembered, which a plain Send would do.
            ? Restore(state, deviceKey, channel, open)
            : Send(AuraState.ModeOff, state, deviceKey, channel, open);

        // An unmatched preset is dropped from the state: the board runs the plain records, and
        // the window must not claim the preset is active (nor warn again on the next switch).
        AuraState next = state with { On = anyOn, CustomPreset = presetUnmatched ? "" : state.CustomPreset };
        next.Save();
        return next;
    }

    /// <summary>
    /// Puts every channel back to the effect and colour it was last set to, falling back to the
    /// board-wide state for channels that have never been set on their own. Reads the record
    /// rather than writing it, so switching off and on again cannot flatten it.
    /// </summary>
    /// <returns>Whether any channel on the machine is on afterwards.</returns>
    private static bool Restore(AuraState state, string? deviceKey, int channel, List<AuraDevice>? open)
    {
        List<AuraDevice> devices = open ?? AuraDevice.DiscoverAll();

        try
        {
            List<(string, int)> switched = ApplyMix(devices, deviceKey, channel, state, look: null);
            AuraChannelStates.SetPower(switched, on: true, BoardWide(state), Attached(devices));

            // Something was just switched on, so the board is lit without having to re-read.
            return switched.Count > 0;
        }
        finally
        {
            if (open == null)
            {
                Close(devices);
            }
        }
    }

    /// <summary>The lighting the board as a whole is set to, for channels that have no own record.</summary>
    private static ChannelLighting BoardWide(AuraState state) =>
        new(state.Mode, state.Red, state.Green, state.Blue);

    /// <summary>A channel's own brightness, or the board-wide one while it has none of its own.</summary>
    private static byte Brightness(ChannelLighting? look, AuraState state) =>
        look is { Brightness: not 0 } own ? own.Brightness : state.Brightness;

    /// <summary>One header of a controller as the engine sharing sees it.</summary>
    /// <param name="Channel">The channel index.</param>
    /// <param name="Chosen">The caller named this channel.</param>
    /// <param name="Record">What the channel was last set to, or null when it never was.</param>
    internal readonly record struct EngineChannel(int Channel, bool Chosen, ChannelLighting? Record);

    /// <summary>What goes out to one controller, and the records that have to follow it.</summary>
    internal sealed record EngineShare(List<ChannelLook> Looks, List<(int Channel, ChannelLighting Look)> Records);

    /// <summary>
    /// The controller has one engine for the firmware-generated modes: a second channel of the
    /// same burst in a different one would be sent after the chosen channel and win, so the choice
    /// would silently be lost. The first chosen channel (lowest index) on one of those modes
    /// therefore decides, and every other channel of the burst on a firmware mode takes it.
    /// </summary>
    /// <param name="spreadToAll">
    /// A mode was picked: the channels that are on a colour mode, switched off or without a record
    /// follow too. A dark channel stays dark but its record carries the mode; a channel with no
    /// record gets a look from <paramref name="boardWide"/>, the one place channels the user never
    /// set are written, since the hardware runs the engine on them anyway. Off, only the channels
    /// already on a firmware mode follow (a preset, a replay, a brightness change).
    /// </param>
    /// <returns>The looks to send and the records of every channel whose mode was changed.</returns>
    internal static EngineShare ShareEngine(IReadOnlyList<EngineChannel> channels, IReadOnlyList<ChannelLook> looks,
        bool spreadToAll, ChannelLighting boardWide, byte boardBrightness)
    {
        var sent = looks.ToList();
        var records = new List<(int Channel, ChannelLighting Look)>();
        var chosen = channels.Where(c => c.Chosen).Select(c => c.Channel).ToHashSet();

        int engine = -1;
        for (int i = 0; i < sent.Count; i++)
        {
            if (chosen.Contains(sent[i].Channel) && AuraPresets.IsFirmwareMode(sent[i].Mode) &&
                (engine < 0 || sent[i].Channel < sent[engine].Channel))
            {
                engine = i;
            }
        }

        if (engine < 0)
        {
            return new EngineShare(sent, records);
        }

        byte engineMode = sent[engine].Mode;

        for (int i = 0; i < sent.Count; i++)
        {
            ChannelLook current = sent[i];
            if (current.Mode == engineMode || (!spreadToAll && !AuraPresets.IsFirmwareMode(current.Mode)))
            {
                continue;
            }

            // A dark channel is not lit by the engine, but its record still carries the mode so
            // switching it back on joins the others.
            if (current.Mode != AuraState.ModeOff)
            {
                sent[i] = current with { Mode = engineMode };
            }

            ChannelLighting? own = channels.FirstOrDefault(c => c.Channel == current.Channel).Record;
            records.Add((current.Channel, (own ?? boardWide) with { Mode = engineMode }));
        }

        if (spreadToAll)
        {
            foreach (EngineChannel channel in channels)
            {
                if (channel.Record != null || sent.Any(l => l.Channel == channel.Channel))
                {
                    continue;
                }

                (byte red, byte green, byte blue) =
                    AuraState.Dim(boardWide.Red, boardWide.Green, boardWide.Blue, boardBrightness);
                sent.Add(new ChannelLook(channel.Channel, engineMode, red, green, blue));
                records.Add((channel.Channel, boardWide with { Mode = engineMode }));
            }
        }

        return new EngineShare(sent, records);
    }

    /// <summary>What <see cref="MixDevice"/> works out for one controller.</summary>
    internal sealed record DeviceMix(List<ChannelLook> Looks, List<int> Named,
        List<(int Channel, ChannelLighting Look)> Records);

    /// <summary>
    /// The pure half of <see cref="ApplyMix"/> for one controller: the looks to send, the channels
    /// that were named, and the records that follow from sharing the firmware engine. No hardware,
    /// no files, so the review build can run fixed cases against it.
    /// </summary>
    internal static DeviceMix MixDevice(IReadOnlyList<int> channels, Func<int, ChannelLighting?> recordOf,
        int channel, AuraState state, ChannelLighting? look, byte? brightness, bool keepPower)
    {
        var looks = new List<ChannelLook>();
        var named = new List<int>();
        var infos = new List<EngineChannel>();

        foreach (int index in channels)
        {
            ChannelLighting? own = recordOf(index);
            bool chosen = channel < 0 || index == channel;
            infos.Add(new EngineChannel(index, chosen, own));

            ChannelLighting? apply = chosen ? look ?? own ?? BoardWide(state) : own;
            if (apply == null)
            {
                continue;
            }

            // A new effect or colour carries no brightness, so the one this channel was given
            // keeps applying - the same rule the stored record follows.
            (byte red, byte green, byte blue) = AuraState.Dim(apply.Red, apply.Green, apply.Blue,
                chosen && brightness.HasValue ? brightness.Value : Brightness(apply.Brightness != 0 ? apply : own, state));

            // A named channel takes the mode it was given, even when its record says it was
            // off - being named is what switches it back on. An untouched one keeps its own
            // state, off included. A caller that only changes how bright a channel is
            // (keepPower) is not asking for it to come on, so there the record decides for
            // named channels too.
            byte mode = (chosen && !keepPower) || apply.On ? apply.Mode : AuraState.ModeOff;
            looks.Add(new ChannelLook(index, mode, red, green, blue));

            if (chosen)
            {
                named.Add(index);
            }
        }

        // Picking a firmware mode switches the whole controller to it; anything else only keeps
        // the firmware channels in line with each other.
        EngineShare shared = ShareEngine(infos, looks, spreadToAll: look != null, BoardWide(state),
            Brightness(null, state));
        return new DeviceMix(shared.Looks, named, shared.Records);
    }

    /// <summary>
    /// Writes a controller's channels in one go: the channels the caller named get
    /// <paramref name="look"/> - or their own record, when it is null - and every other channel
    /// of the same controller is written again exactly as it already stands.
    /// </summary>
    /// <remarks>
    /// Re-asserting the untouched channels is what makes a mix survive. The controller applies a
    /// new effect across its channels unless every one of them is named in the same burst, which
    /// is why a static header next to an animated one would end up running the same effect as its
    /// neighbour. Channels that have never been set on their own are left alone, since there
    /// is nothing to re-assert and guessing would overwrite the BIOS lighting - except when a
    /// firmware mode is picked, which the hardware runs on every channel anyway
    /// (<see cref="ShareEngine"/>).
    /// </remarks>
    /// <param name="keepPower">
    /// Named channels keep the power their record has instead of being switched on by the naming.
    /// Only the brightness path sets it: a dimmer must never light a header the user switched off.
    /// </param>
    /// <returns>The channels that were named, for the caller to record.</returns>
    private static List<(string DeviceKey, int Channel)> ApplyMix(List<AuraDevice> devices, string? deviceKey,
        int channel, AuraState state, ChannelLighting? look, byte? brightness = null, bool keepPower = false,
        Func<AuraDevice, bool>? only = null)
    {
        List<AuraDevice> targets = ResolveDevices(devices, deviceKey);
        if (only != null)
        {
            targets = targets.FindAll(device => only(device));
        }

        // Read once, then indexed per channel, rather than re-parsing the whole file for every
        // single channel.
        Dictionary<string, ChannelLighting> remembered = AuraChannelStates.All();
        var named = new List<(string, int)>();
        IOException? failure = null;
        bool delivered = false;

        foreach (AuraDevice device in targets)
        {
            DeviceMix mix = MixDevice(device.Channels.Select(c => c.Index).ToList(),
                index => AuraChannelStates.Get(remembered, device.Key, index), channel, state, look, brightness,
                keepPower);
            List<ChannelLook> looks = mix.Looks;
            var deviceNamed = mix.Named.Select(index => (device.Key, index)).ToList();
            var unified = mix.Records.Select(r => (device.Key, r.Channel, r.Look)).ToList();

            try
            {
                // Every channel of this controller in one burst - sending them one at a time
                // would leave the onboard zone lit and settled a dozen reports before the last
                // header caught up, which reads as the board coming on in stages.
                device.Apply(looks);
                named.AddRange(deviceNamed);
                delivered = true;
            }
            catch (IOException ex)
            {
                // One controller dropping a report must not undo what every other controller in
                // this mix already received, and must not leave this device's own channels
                // unrecorded either - the write and the record are the same burst, so part of what
                // was named here may well have reached the hardware before it gave up.
                AuraLog.Error($"Apply: {device.Name} ({looks.Count} channel(s))", ex);
                failure = ex;
                named.AddRange(deviceNamed);
            }

            if (unified.Count > 0)
            {
                AuraChannelStates.Remember(unified, Attached(devices));
            }
        }

        // Whether anything actually got through, which is not the same question as whether any
        // channel was named: the recording above happens either way, so counting names would call
        // a controller that failed on every single report a success and quietly save the state the
        // user asked for over the one the board is really in.
        if (failure != null && !delivered)
        {
            throw failure;
        }

        return named;
    }

    /// <summary>
    /// Whether one controller counts as on, the way the window shows it: any of its channels whose
    /// record is on, or without a record while the board is. The board-wide flag alone stays true
    /// as long as any other controller is lit, which would make a toggle aimed at a dark controller
    /// switch it off again and never on. An unlisted controller falls back to the board-wide flag,
    /// and the switch that follows reports why it cannot be reached.
    /// </summary>
    private static bool ControllerIsOn(string deviceKey, List<AuraDeviceSummary> listed)
    {
        AuraDeviceSummary? summary = listed.Find(d => d.Key == deviceKey);
        AuraState state = AuraState.Load();

        if (summary == null)
        {
            return state.On;
        }

        Dictionary<string, ChannelLighting> remembered = AuraChannelStates.All();

        foreach (AuraChannel own in summary.Channels)
        {
            if (AuraChannelStates.Get(remembered, deviceKey, own.Index)?.On ?? state.On)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>One controller by key, or every controller when none is named. Shared by every
    /// caller that resolves "-device" against an already-open device list.</summary>
    private static List<AuraDevice> ResolveDevices(List<AuraDevice> devices, string? deviceKey)
    {
        List<AuraDevice> targets = string.IsNullOrEmpty(deviceKey)
            ? devices
            : devices.FindAll(device => device.Key == deviceKey);

        if (targets.Count == 0)
        {
            // The selected controller was unplugged since the window last saw it.
            throw new AuraNotFoundException(Strings.ErrorControllerNotFound, 3);
        }

        return targets;
    }

    /// <summary>
    /// The keys of the controllers found this run, handed to the channel records so an attached
    /// controller's look is never aged out.
    /// </summary>
    private static string[] Attached(List<AuraDevice> devices) => devices.Select(d => d.Key).ToArray();

    private static void Close(List<AuraDevice> devices)
    {
        foreach (AuraDevice device in devices)
        {
            device.Dispose();
        }
    }

    /// <summary>
    /// Whether at least one channel of the controllers already open is on. A channel with no
    /// record has never been switched on its own, which means it is following the board.
    /// </summary>
    private static bool AnyChannelOn(List<AuraDevice> devices)
    {
        Dictionary<string, ChannelLighting> remembered = AuraChannelStates.All();

        foreach (AuraDevice device in devices)
        {
            foreach (AuraChannel channel in device.Channels)
            {
                if (AuraChannelStates.Get(remembered, device.Key, channel.Index) is not ChannelLighting look ||
                    look.On)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Switches to a lighting effect and remembers it as the state to restore.</summary>
    public static AuraState ApplyPreset(AuraPreset preset, Color? colour = null, string? deviceKey = null,
        int channel = -1) => ApplyPreset(preset, colour, deviceKey, channel, null);

    /// <param name="open">Controllers the caller already discovered and disposes itself; null discovers here.</param>
    private static AuraState ApplyPreset(AuraPreset preset, Color? colour, string? deviceKey, int channel,
        List<AuraDevice>? open)
    {
        using IDisposable guard = AuraFiles.HardwareLock();

        AuraState state = AuraState.Load();
        bool wholeBoard = string.IsNullOrEmpty(deviceKey) && channel < 0;

        byte red = colour?.R ?? state.Red;
        byte green = colour?.G ?? state.Green;
        byte blue = colour?.B ?? state.Blue;

        Send(preset.Mode, state with { Red = red, Green = green, Blue = blue }, deviceKey, channel, open);

        // Mode and colour only move into the board-wide record when the whole board was the
        // target - a single channel or controller picking its own effect must not silently
        // become what every other, untouched channel falls back to the next time nothing else
        // names them (BoardWide(state) is exactly that fallback).
        AuraState next = wholeBoard
            ? state with { On = true, Mode = preset.Mode, Red = red, Green = green, Blue = blue, CustomPreset = "" }
            : state with { On = true, CustomPreset = "" };

        next.Save();
        return next;
    }

    /// <summary>
    /// Changes the brightness and re-sends the running effect so it takes hold at once.
    /// </summary>
    /// <param name="deviceKey">
    /// One controller, or null for the whole board - which is the only option on the command
    /// line, and also what clears any brightness a single channel was given of its own.
    /// </param>
    /// <param name="channel">One channel of that controller, or -1 for all of them.</param>
    /// <remarks>
    /// The controller has no brightness register: this scales the colour that the effect
    /// command carries. Effects the firmware colours itself - the spectrum and rainbow modes -
    /// therefore cannot be dimmed at all, and the window hides the slider for them.
    /// </remarks>
    public static AuraState SetBrightness(byte percent, string? deviceKey = null, int channel = -1) =>
        SetBrightness(percent, deviceKey, channel, null);

    /// <param name="open">Controllers the caller already discovered and disposes itself; null discovers here.</param>
    private static AuraState SetBrightness(byte percent, string? deviceKey, int channel, List<AuraDevice>? open)
    {
        using IDisposable guard = AuraFiles.HardwareLock();

        percent = Math.Clamp(percent, AuraState.MinBrightness, AuraState.MaxBrightness);

        AuraState state = AuraState.Load();
        // Same formula ApplyPreset uses for the same concept - every real caller currently only
        // ever pairs an empty deviceKey with channel < 0, but the check should say so itself
        // rather than assume it.
        bool wholeBoard = string.IsNullOrEmpty(deviceKey) && channel < 0;

        // The board-wide value is the one every channel without a brightness of its own follows.
        if (wholeBoard)
        {
            state = state with { Brightness = percent };
        }

        if (wholeBoard && state.On && state.CustomPreset.Length > 0 &&
            AuraCustomPresets.Load().Find(p => p.Name == state.CustomPreset) is CustomPreset preset)
        {
            state.Save();

            // The whole-board slider is a blunt "every channel to this value" control - a preset
            // entry's own baked-in brightness must not survive it, or dragging "all" would visibly
            // do nothing to whichever channels the preset happens to dim on its own.
            return ApplyCustomPreset(preset, brightnessOverride: percent);
        }

        List<AuraDevice> devices = open ?? AuraDevice.DiscoverAll();

        try
        {
            // The new value goes to the named channels as an override of what their records say, and
            // is written into the records only once the controller has taken it: a brightness the
            // board never received must not be what the window shows afterwards. Setting it
            // board-wide hands the channels back to the board-wide value instead of leaving each one
            // pinned to what it was given individually.
            if (state.On)
            {
                ApplyMix(devices, deviceKey, channel, state, look: null, brightness: percent, keepPower: true);
            }

            AuraChannelStates.SetBrightness(Channels(devices, deviceKey, channel), wholeBoard ? (byte)0 : percent,
                BoardWide(state), Attached(devices));
        }
        finally
        {
            if (open == null)
            {
                Close(devices);
            }
        }

        state.Save();
        return state;
    }

    /// <summary>Every channel the caller named, as the pairs the stored records are keyed by.</summary>
    private static List<(string DeviceKey, int Channel)> Channels(List<AuraDevice> devices, string? deviceKey,
        int channel)
    {
        List<AuraDevice> targets = ResolveDevices(devices, deviceKey);
        var named = new List<(string, int)>();

        foreach (AuraDevice device in targets)
        {
            foreach (AuraChannel affected in device.Channels)
            {
                if (channel < 0 || affected.Index == channel)
                {
                    named.Add((device.Key, affected.Index));
                }
            }
        }

        return named;
    }

    /// <summary>
    /// The entry of a preset that decides one channel: one written for that channel wins over one
    /// that covers the whole controller, and between two of the same kind the later one wins. The
    /// preview and the real apply both read it from here, so the editor shows what Save sends.
    /// </summary>
    internal static CustomPresetEntry? ResolveEntry(List<CustomPresetEntry> entries, string deviceKey, int channel) =>
        entries.FindLast(e => e.DeviceKey == deviceKey && e.Channel == channel) ??
        entries.FindLast(e => e.DeviceKey == deviceKey && e.Channel < 0);

    /// <summary>
    /// Splits the attached controllers into those a preset names and those it leaves out. A
    /// controller's key is its device path, which changes when the header lands on another port,
    /// so a preset can name none of the controllers that are there.
    /// </summary>
    internal static (List<string> Named, List<string> Left) SplitControllers(
        CustomPreset preset, IEnumerable<string> attached)
    {
        var named = new List<string>();
        var left = new List<string>();

        foreach (string key in attached)
        {
            (preset.Entries.Exists(e => e.DeviceKey == key) ? named : left).Add(key);
        }

        return (named, left);
    }

    /// <summary>
    /// Applies a named bundle of per-device effects. The remembered state mirrors the first
    /// entry, which is what the button and the effect list show while the preset is active.
    /// </summary>
    /// <param name="brightnessOverride">
    /// Set only by the whole-board brightness slider: every channel the preset touches takes
    /// this value regardless of what the preset entry or the channel's own record says, since
    /// "all channels to this brightness" would otherwise visibly do nothing to a channel the
    /// preset dims on its own. Null for every other caller, where each entry's own brightness -
    /// or the channel's own record when the entry carries none - keeps applying.
    /// </param>
    public static AuraState ApplyCustomPreset(CustomPreset preset, byte? brightnessOverride = null) =>
        ApplyCustomPresetCore(preset, brightnessOverride, restoreOthers: false)!;

    /// <param name="restoreOthers">
    /// Switching the board on: the controllers the preset does not name come back to their own
    /// records too, and a preset that names none of the attached ones gives null instead of
    /// throwing, so the caller can restore everything.
    /// </param>
    private static AuraState? ApplyCustomPresetCore(CustomPreset preset, byte? brightnessOverride, bool restoreOthers)
    {
        if (preset.Entries.Count == 0)
        {
            throw new AuraNotFoundException(Strings.ErrorControllerNotFound, 3);
        }

        using IDisposable guard = AuraFiles.HardwareLock();

        AuraState state = AuraState.Load();
        var devices = AuraDevice.DiscoverAll();
        var applied = new List<(string DeviceKey, int Channel, ChannelLighting Look)>();
        var named = new HashSet<(string DeviceKey, int Channel)>();
        IOException? failure = null;
        bool delivered = false;

        // Every entry and every gap-filled channel lands here first, then each controller gets
        // one Apply() call for its whole mix - one call per channel would leave the onboard
        // zone lit and settled a dozen reports before the last header caught up.
        var perDevice = new Dictionary<string, List<ChannelLook>>();

        void AddLook(string deviceKey, int channelIndex, byte mode, byte red, byte green, byte blue)
        {
            if (!perDevice.TryGetValue(deviceKey, out List<ChannelLook>? looks))
            {
                looks = new List<ChannelLook>();
                perDevice[deviceKey] = looks;
            }

            looks.Add(new ChannelLook(channelIndex, mode, red, green, blue));
        }

        try
        {
            Dictionary<string, ChannelLighting> remembered = AuraChannelStates.All();

            foreach (AuraDevice device in devices)
            {
                foreach (AuraChannel affected in device.Channels)
                {
                    if (ResolveEntry(preset.Entries, device.Key, affected.Index) is not CustomPresetEntry entry)
                    {
                        continue;
                    }

                    // A brightness override means "forget every per-channel brightness", so it is
                    // recorded as 0 (follow the board) rather than the transient override value -
                    // the same "0 means follow the board" rule channel-state.json follows everywhere
                    // else.
                    var look = new ChannelLighting(entry.Mode, entry.Red, entry.Green, entry.Blue,
                        On: true, brightnessOverride.HasValue ? (byte)0 : entry.Brightness);

                    // Each channel is dimmed to its own brightness - a preset carries an effect and
                    // a colour, not a brightness, so what the channel was given keeps applying.
                    // The preset's own brightness for that channel, or - when it carries none -
                    // whatever the channel is already dimmed to.
                    byte percent = brightnessOverride ?? (entry.Brightness != 0
                        ? entry.Brightness
                        : Brightness(AuraChannelStates.Get(remembered, device.Key, affected.Index), state));

                    (byte red, byte green, byte blue) =
                        AuraState.Dim(entry.Red, entry.Green, entry.Blue, percent);

                    AddLook(device.Key, affected.Index, entry.Mode, red, green, blue);
                    applied.Add((device.Key, affected.Index, look));
                    named.Add((device.Key, affected.Index));
                }
            }

            // A preset only has to name the channels its own look applies to - the controller
            // still applies an effect across every channel of a controller in one burst unless
            // the whole mix arrives together, so anything left out on a
            // controller the preset touches is re-asserted from its own record here, the same
            // rule ApplyMix enforces for the plain switch path.
            //
            // A brightness override additionally reaches every OTHER controller too, named by
            // the preset or not: "all channels to this brightness" has to mean literally every
            // channel, matching what the plain (non-preset) whole-board slider already does via
            // AuraChannelStates.SetBrightness. Without this, a channel the preset does not
            // mention - on the same controller or a second one entirely - would keep whatever
            // brightness it already had while everything the preset does name jumped to the new
            // value, and the slider would look as if it did nothing.
            var gapFillDevices = brightnessOverride.HasValue
                ? devices
                : devices.FindAll(d => named.Any(n => n.DeviceKey == d.Key));

            foreach (AuraDevice device in gapFillDevices)
            {
                foreach (AuraChannel channel in device.Channels)
                {
                    if (named.Contains((device.Key, channel.Index)) ||
                        AuraChannelStates.Get(remembered, device.Key, channel.Index) is not ChannelLighting own)
                    {
                        continue;
                    }

                    byte percent = brightnessOverride ?? Brightness(own, state);
                    (byte red, byte green, byte blue) = AuraState.Dim(own.Red, own.Green, own.Blue, percent);
                    AddLook(device.Key, channel.Index, own.On ? own.Mode : AuraState.ModeOff, red, green, blue);

                    if (brightnessOverride.HasValue)
                    {
                        // Recorded like the named channels: 0 means follow the board from here
                        // on, so the record does not diverge from what the hardware was just
                        // sent.
                        applied.Add((device.Key, channel.Index, own with { Brightness = 0 }));
                    }
                }
            }

            foreach (AuraDevice device in devices)
            {
                if (!perDevice.TryGetValue(device.Key, out List<ChannelLook>? looks))
                {
                    continue;
                }

                // One engine for the firmware-generated modes per controller, same rule as
                // ApplyMix: within one controller the first entry (lowest channel index) that
                // carries one of those modes decides, and every other channel of the burst that is
                // on a firmware mode of its own takes it - the later of two such entries would
                // otherwise win on the board while the first one's record claims otherwise. Colour
                // entries stay as they are, and the records follow what is sent.
                var infos = device.Channels.Select(c => new EngineChannel(c.Index,
                    named.Contains((device.Key, c.Index)),
                    applied.FindLast(a => a.DeviceKey == device.Key && a.Channel == c.Index).Look ??
                        AuraChannelStates.Get(remembered, device.Key, c.Index))).ToList();
                EngineShare shared = ShareEngine(infos, looks, spreadToAll: false, BoardWide(state),
                    Brightness(null, state));
                looks = shared.Looks;

                foreach ((int index, ChannelLighting follow) in shared.Records)
                {
                    bool recorded = false;
                    for (int r = 0; r < applied.Count; r++)
                    {
                        if (applied[r].DeviceKey == device.Key && applied[r].Channel == index)
                        {
                            applied[r] = (device.Key, index, applied[r].Look with { Mode = follow.Mode });
                            recorded = true;
                        }
                    }

                    if (!recorded)
                    {
                        applied.Add((device.Key, index, follow));
                    }
                }

                try
                {
                    // Every channel of this controller in one burst, same as ApplyMix - and the
                    // same reasoning for catching per device: one controller dropping a report
                    // must not undo what every other controller in this preset already received.
                    device.Apply(looks);
                    delivered = true;
                }
                catch (IOException ex)
                {
                    AuraLog.Error($"Apply: {device.Name} ({looks.Count} channel(s))", ex);
                    failure = ex;
                }
            }

            // Controllers the preset leaves out would stay dark after the board was off.
            if (restoreOthers && !brightnessOverride.HasValue && applied.Count > 0)
            {
                List<string> left = SplitControllers(preset, devices.Select(d => d.Key)).Left;

                if (left.Count > 0)
                {
                    try
                    {
                        List<(string, int)> switched = ApplyMix(devices, null, -1, state, look: null,
                            only: device => left.Contains(device.Key));
                        AuraChannelStates.SetPower(switched, on: true, BoardWide(state), Attached(devices));
                    }
                    catch (IOException ex)
                    {
                        AuraLog.Error("Restore of controllers outside the preset", ex);
                    }
                }
            }
        }
        finally
        {
            Close(devices);
        }

        // Whether anything actually got through, not the same question as whether any channel
        // was named - see ApplyMix for why this has to be its own check.
        if (failure != null && !delivered)
        {
            throw failure;
        }

        if (applied.Count == 0)
        {
            // Every controller the preset names has gone, although others are attached (none at
            // all has already thrown above). A controller's key is its device path, which can
            // change when the header lands on another port, so the preset needs saving again and
            // no amount of waiting at logon will fix that.
            AuraLog.Warn("A custom preset names no attached controller, re-save it in the window.");

            if (restoreOthers)
            {
                return null;
            }

            throw new AuraNotFoundException(Strings.ErrorControllerNotFound, 3, worthRetrying: false);
        }

        // A preset sets each channel individually, so each one remembers its own look - written
        // in one pass instead of once per entry. A brightness override's 0 means "follow the
        // board" and has to land as written, not be read back as "unset" and swapped for the
        // channel's previous value, which would make the whole-board slider do nothing.
        AuraChannelStates.Remember(applied, Attached(devices), keepBrightness: !brightnessOverride.HasValue);

        CustomPresetEntry first = preset.Entries[0];
        AuraState next = state with
        {
            On = true,
            Mode = first.Mode,
            Red = first.Red,
            Green = first.Green,
            Blue = first.Blue,
            CustomPreset = preset.Name,
        };

        next.Save();
        return next;
    }

    /// <returns>Whether any channel on the machine is on afterwards.</returns>
    private static bool Send(byte mode, AuraState state, string? deviceKey, int channel,
        List<AuraDevice>? open = null)
    {
        List<AuraDevice> devices = open ?? AuraDevice.DiscoverAll();

        try
        {
            // The colour is recorded as chosen rather than as dimmed, so that picking one channel
            // in the window shows the colour that was set for it.
            var look = new ChannelLighting(mode, state.Red, state.Green, state.Blue, On: mode != AuraState.ModeOff);
            List<(string, int)> switched = ApplyMix(devices, deviceKey, channel, state, look);

            if (mode == AuraState.ModeOff)
            {
                // Switching off only clears the power flag: the effect and colour stay on record
                // so that switching back on has its own look to return to. A channel with no
                // record yet keeps the board's current look rather than a made-up one.
                AuraChannelStates.SetPower(switched, on: false, BoardWide(state), Attached(devices));
            }
            else
            {
                AuraChannelStates.Remember(switched, look, Attached(devices));
            }

            return AnyChannelOn(devices);
        }
        finally
        {
            if (open == null)
            {
                Close(devices);
            }
        }
    }

    /// <summary>
    /// Lights one channel white at full brightness and takes every other channel on the board -
    /// every controller, not just the one the channel belongs to - to a static black. One lit
    /// header against an otherwise dark board leaves nothing to work out.
    /// <para>
    /// Black here means the static mode carrying the colour 0,0,0, which is what the brightness
    /// model already produces for a channel set to black: the controller has no brightness of its
    /// own, colours are scaled before they are sent. Mode off is not used: on boards whose headers
    /// share one bus, an off neighbour can still catch stray colour. A static black holds the
    /// channel dark. If a board ever does show a glow on a neighbour, the answer is a faint white
    /// on the others - not a blink: toggling a channel in a loop runs into the dynamic-lighting
    /// limit that silently drops the colour command past a certain channel index, so it never
    /// blinks at all.
    /// </para>
    /// Runs until <paramref name="token"/> is cancelled, then puts every channel it touched back
    /// to what <c>channel-state.json</c> already says it should be - nothing is written here, this
    /// only replays what was already on record.
    /// </summary>
    /// <remarks>
    /// Each controller's dark channels and its white one go out in the same burst, not as separate
    /// operations. Sending every black first and the target channel's colour last, as its own later
    /// call, hit the same dropped-command limit on the higher-indexed headers - arriving well
    /// behind a run of other reports is exactly the pattern that trips it.
    /// </remarks>
    public static void Identify(string deviceKey, int channel, CancellationToken token)
    {
        // The controller talks to one caller at a time: the discovery and the lighting burst
        // below each hold the hardware lock, the open-ended wait between them deliberately does not
        // - a rename popup can stay open for minutes, and nothing else may be shut out for that.
        List<AuraDevice> devices;
        using (AuraFiles.HardwareLock())
        {
            devices = AuraDevice.DiscoverAll();
        }

        try
        {
            if (!devices.Exists(d => d.Key == deviceKey))
            {
                // Unplugged since the window last saw it - nothing to light, and no reason to
                // darken the controllers that are still there.
                return;
            }

            // Recorded before each controller is written, not after: a controller that throws
            // half way through its burst has still been changed and has to be put back.
            var touched = new List<AuraDevice>();

            try
            {
                using (AuraFiles.HardwareLock())
                {
                    foreach (AuraDevice device in devices)
                    {
                        var looks = new List<ChannelLook>();

                        foreach (AuraChannel other in device.Channels)
                        {
                            looks.Add(device.Key == deviceKey && other.Index == channel
                                ? new ChannelLook(other.Index, AuraState.ModeStatic, 0xFF, 0xFF, 0xFF)
                                : new ChannelLook(other.Index, AuraState.ModeStatic, 0, 0, 0));
                        }

                        touched.Add(device);
                        device.Apply(looks);
                    }
                }

                token.WaitHandle.WaitOne(Timeout.Infinite);
            }
            finally
            {
                // Even when lighting the target channel itself failed, whatever this did turn dark
                // still needs to go back to its own record - a failure here must not leave a
                // controller stuck dark with channel-state.json still describing it as lit.
                using (AuraFiles.HardwareLock())
                {
                    // Read now, not before the wait: the lock was free all that time, so the
                    // command line or a start action may have changed what the records say.
                    AuraState state = AuraState.Load();
                    Dictionary<string, ChannelLighting> remembered = AuraChannelStates.All();

                    foreach (AuraDevice device in touched)
                    {
                        Replay(device, state, remembered);
                    }
                }
            }
        }
        finally
        {
            Close(devices);
        }
    }

    /// <summary>
    /// Shows a preset on the hardware without recording any of it - for the editor's live
    /// preview, where the lighting has to follow every change but nothing is committed until
    /// Save. Channels the preset does not name are held dark, so what is on the board is exactly
    /// what the preset describes.
    /// </summary>
    public static void Preview(CustomPreset preset)
    {
        using IDisposable guard = AuraFiles.HardwareLock();

        var devices = AuraDevice.DiscoverAll();

        try
        {
            AuraState state = AuraState.Load();

            foreach (AuraDevice device in devices)
            {
                var looks = new List<ChannelLook>();

                foreach (AuraChannel channel in device.Channels)
                {
                    CustomPresetEntry? entry = ResolveEntry(preset.Entries, device.Key, channel.Index);

                    if (entry == null)
                    {
                        looks.Add(new ChannelLook(channel.Index, AuraState.ModeOff, 0, 0, 0));
                        continue;
                    }

                    (byte red, byte green, byte blue) = AuraState.Dim(entry.Red, entry.Green, entry.Blue,
                        entry.Brightness != 0 ? entry.Brightness : state.Brightness);
                    looks.Add(new ChannelLook(channel.Index, entry.Mode, red, green, blue));
                }

                // Shown the way Save would apply it: one firmware engine per controller, the
                // lowest channel on one of those modes decides.
                var infos = device.Channels.Select(c => new EngineChannel(c.Index, true, null)).ToList();
                device.Apply(ShareEngine(infos, looks, spreadToAll: false, BoardWide(state), Brightness(null, state)).Looks);
            }
        }
        finally
        {
            Close(devices);
        }
    }

    /// <summary>
    /// Puts every channel back to what the stored records already say it should be, undoing a
    /// preview without writing anything of its own.
    /// </summary>
    public static void RestoreFromRecords()
    {
        using IDisposable guard = AuraFiles.HardwareLock();

        var devices = AuraDevice.DiscoverAll();

        try
        {
            AuraState state = AuraState.Load();
            Dictionary<string, ChannelLighting> remembered = AuraChannelStates.All();

            foreach (AuraDevice device in devices)
            {
                Replay(device, state, remembered);
            }
        }
        finally
        {
            Close(devices);
        }
    }

    /// <summary>Re-sends every channel of one controller exactly as the records describe it.</summary>
    private static void Replay(AuraDevice device, AuraState state,
        Dictionary<string, ChannelLighting> remembered)
    {
        var looks = new List<ChannelLook>();

        foreach (AuraChannel affected in device.Channels)
        {
            ChannelLighting look = AuraChannelStates.Get(remembered, device.Key, affected.Index) ?? BoardWide(state);
            (byte red, byte green, byte blue) =
                AuraState.Dim(look.Red, look.Green, look.Blue, Brightness(look, state));
            looks.Add(new ChannelLook(affected.Index, look.On ? look.Mode : AuraState.ModeOff, red, green, blue));
        }

        // Records written before the engine was shared can disagree; the board runs one.
        var infos = device.Channels.Select(c => new EngineChannel(c.Index, true,
            AuraChannelStates.Get(remembered, device.Key, c.Index))).ToList();
        device.Apply(ShareEngine(infos, looks, spreadToAll: false, BoardWide(state), Brightness(null, state)).Looks);
    }

    private static bool _consoleAttached;

    /// <summary>
    /// Attaching is idempotent by itself, but repeated calls still cost a kernel round trip -
    /// worth latching since <c>-list</c>/<c>-status</c> on a multi-controller board call this
    /// once per line.
    /// </summary>
    private static void EnsureConsole()
    {
        if (!_consoleAttached)
        {
            AttachConsole(AttachParentProcess);
            _consoleAttached = true;
        }
    }

    /// <summary>Reports to the console of the calling shell - this is a WinExe and owns none.</summary>
    private static void WriteError(string message)
    {
        EnsureConsole();
        Console.Error.WriteLine(message);
    }

    /// <summary>Same reasoning as <see cref="WriteError"/>, for <c>-list</c>/<c>-status</c>/<c>--version</c>.</summary>
    private static void WriteLine(string message)
    {
        EnsureConsole();
        Console.Out.WriteLine(message);
    }
}
