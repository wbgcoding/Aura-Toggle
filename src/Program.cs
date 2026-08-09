using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AuraToggle;

internal static class Program
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

        // The language choice also governs usage and error output on the command line.
        Strings.Override = AuraSettings.Load().Language;
        AuraLog.Info($"Start {Assembly.GetExecutingAssembly().GetName().Version}");

        // Opens one popup directly - for visual/interactive review without a real controller or
        // a click-path through the main window, e.g. to screenshot it. Bypasses the single-
        // instance check on purpose: it is independent of whatever tray instance may already be
        // running. Not a documented end-user flag, so it is not in Usage().
        if (args.Length >= 1 && Normalise(args[0]) == "review")
        {
            return RunReview(args.Length >= 2 ? Normalise(args[1]) : "");
        }

        LaunchedAtStartup = args.Length == 1 && args[0] == AuraSettings.AutoStartArgument;

        if (args.Length == 0 || LaunchedAtStartup)
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

    /// <summary>
    /// A plain named <see cref="Mutex"/> lives in the caller's Terminal Services session, not the
    /// machine - two sessions of the same account (a physical logon plus a Remote Desktop one)
    /// would each see themselves as the first instance and both open the one physical controller.
    /// The "Global\" prefix makes it machine-wide instead, which is what "one instance" is meant
    /// to guarantee here. Falls back to the old session-local name if creating a global object is
    /// denied - a locked-down policy blocking that is rare, and failing open (one instance per
    /// session, the previous behaviour) beats not starting at all.
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
    /// Shows one surface standalone: "settings" (the gear panel, hotkey row forced visible
    /// regardless of the real setting) or "error" (a sample <see cref="ErrorDialog"/>, no real
    /// exception involved). Interacting with the settings panel still saves to the real
    /// settings.json, same as it would from the main window - this is a shortcut to the real
    /// control, not a sandboxed mock of it.
    /// </summary>
    private static int RunReview(string surface)
    {
        if (surface is not ("settings" or "error" or "layout"))
        {
            WriteError("Usage: AuraToggle -review settings|error|layout");
            return 2;
        }

        ApplicationConfiguration.Initialize();
#pragma warning disable WFO5001 // colour mode support is still marked experimental
        Application.SetColorMode(SystemColorMode.System);
#pragma warning restore WFO5001

        if (surface == "layout")
        {
            return ReviewLayout();
        }

        if (surface == "error")
        {
            ErrorDialog.Report(new IOException("Sample error for review - the LED controller did not answer in time."),
                "Review", owner: null, onClosed: Application.Exit, requireMessageLoop: false);
        }
        else
        {
            var popup = new SettingsPopup(AuraSettings.Load() with { HotkeyEnabled = true })
            {
                KeepOpenOnDeactivate = true,
            };
            popup.Location = new Point(
                (Screen.PrimaryScreen!.WorkingArea.Width - popup.Width) / 2,
                (Screen.PrimaryScreen.WorkingArea.Height - popup.Height) / 2);
            popup.FormClosed += (_, _) => Application.Exit();
            popup.Show();
            popup.Activate();
        }

        Application.Run();
        return 0;
    }

    /// <summary>
    /// Opens the real window against stand-in controllers and prints what it measured, so a
    /// "cut off on the right" complaint can be reproduced and proved fixed at any display scale
    /// without a controller and without reading pixels off a screenshot. Two controllers with
    /// three channels each: enough to force the channel selector into the top row, which is the
    /// widest the row ever gets and therefore the case that overflows first.
    /// </summary>
    private static int ReviewLayout()
    {
        ToggleForm.ReviewDevices = new List<AuraDeviceSummary>
        {
            new("review-1", "Aura Controller 1", new List<AuraChannel>
            {
                new(0, Onboard: true, Header: 0),
                new(1, Onboard: false, Header: 1),
                new(2, Onboard: false, Header: 2),
            }),
            new("review-2", "Aura Controller 2", new List<AuraChannel>
            {
                new(0, Onboard: true, Header: 0),
                new(1, Onboard: false, Header: 1),
            }),
        };

        var form = new ToggleForm();
        form.Shown += async (_, _) =>
        {
            // The window measures itself after discovery, which OnShown runs asynchronously -
            // reading straight away would report the sizes from before the channel selector
            // appeared, which is exactly the measurement in question.
            await Task.Yield();
            string report = form.DescribeLayout();
            WriteLine(report);

            // Also on disk: this is a WinExe, so when it is started by anything that owns no
            // console - a scheduled task, a test runner - the lines above go nowhere.
            try
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "aura-layout.txt"), report);
            }
            catch (Exception ex) when (AuraFiles.IsExpected(ex))
            {
                // A report that cannot be written is not worth failing the review over.
            }

            // Left open on purpose: the numbers above say whether anything overflows, the window
            // itself is what shows how the result actually looks at this display scale.
        };

        Application.Run(form);
        return 0;
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
    /// window very much still open - that used to fall through to <see cref="Environment.Exit"/>
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
                form.BeginInvoke(() => ErrorDialog.Report(ex, context, owner: null, onClosed: Application.Exit));
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
            WriteLine(Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?");
            return 0;
        }

        if (command == "list" && rest.Count == 1)
        {
            return PrintList();
        }

        if (command == "status" && rest.Count == 1)
        {
            return PrintStatus();
        }

        if (command == "custom" && rest.Count == 2)
        {
            CustomPreset? custom = AuraCustomPresets.Load().Find(p => p.Name == rest[1]);
            if (custom == null)
            {
                return Usage();
            }

            ApplyCustomPreset(custom);
            return 0;
        }

        if (!TryResolveTarget(deviceArg, channelArg, out string? targetDevice, out int targetChannel,
                out int targetingError))
        {
            return targetingError;
        }

        if (rest.Count == 1 && command is "on" or "off")
        {
            Switch(command == "on", targetDevice, targetChannel);
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

            ApplyPreset(preset, colour, targetDevice, targetChannel);
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
            SetBrightness(Math.Max(percent, AuraState.MinBrightness), targetDevice, targetChannel);
            return 0;
        }

        return Usage();
    }

    /// <summary>
    /// Pulls <c>-device</c> and <c>-channel</c> out of the argument list wherever they appear,
    /// each consuming the token right after it; everything else stays in order for the existing
    /// positional parsing below to see exactly as before.
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
    /// already takes. Does no hardware discovery at all when neither flag was given, which is the
    /// overwhelmingly common case - <c>-on</c>/<c>-off</c>/<c>-preset</c>/<c>-brightness</c> stay
    /// exactly as cheap as before for anyone not targeting a single channel or controller.
    /// </summary>
    private static bool TryResolveTarget(string? deviceArg, string? channelArg, out string? deviceKey,
        out int channel, out int errorExitCode)
    {
        deviceKey = null;
        channel = -1;
        errorExitCode = 0;

        if (deviceArg == null && channelArg == null)
        {
            return true;
        }

        List<AuraDeviceSummary> devices = AuraDevice.ListDevices();
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

        return AuraPresets.Normalise(DefaultChannelName(entry.Channel, "en")) == wanted ||
               AuraPresets.Normalise(DefaultChannelName(entry.Channel, "de")) == wanted;
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
        WriteLine($"Aura Toggle {Assembly.GetExecutingAssembly().GetName().Version}");
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
        WriteLine("  -preset <effect> [rgb]  Apply an effect, optionally with a colour.");
        WriteLine(Row($"-brightness <{AuraState.MinBrightness}-{AuraState.MaxBrightness}>",
            "Set the brightness in percent. Does nothing for the"));
        WriteLine(Row("", "effects the firmware colours itself."));
        WriteLine("  -custom <name>          Apply a custom preset saved in the window.");
        WriteLine("  -list                   List every controller and channel with its identifiers.");
        WriteLine("  -status                 Print the effect, colour, brightness and state per channel.");
        WriteLine("  --version               Print the version number.");
        WriteLine("  -help                   Show this text.");
        WriteLine("");
        WriteLine("Options (for -on, -off, -preset and -brightness):");
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

    private static int PrintList()
    {
        List<AuraDeviceSummary> devices = AuraDevice.ListDevices();
        if (devices.Count == 0)
        {
            WriteError(Strings.ErrorControllerNotFound);
            return 3;
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

        return 0;
    }

    private static int PrintStatus()
    {
        List<AuraDeviceSummary> devices = AuraDevice.ListDevices();
        if (devices.Count == 0)
        {
            WriteError(Strings.ErrorControllerNotFound);
            return 3;
        }

        AuraState state = AuraState.Load();
        WriteLine(FormatStatusLine("Board", state.On, state.Mode, state.Red, state.Green, state.Blue,
            state.Brightness.ToString(CultureInfo.InvariantCulture)));

        Dictionary<string, ChannelLighting> remembered = AuraChannelStates.All();
        foreach (ChannelEntry entry in FlattenChannels(devices))
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
        return colour.IsKnownColor;
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
    public static AuraState Switch(bool on, string? deviceKey = null, int channel = -1)
    {
        // Locked around the whole read-modify-write: a command-line switch and one from the
        // window running at the same time would otherwise each save their own copy of
        // state.json and one change would vanish - the same reasoning AuraChannelStates and
        // AuraChannelNames already lock for. Re-entrant on the same thread, so the calls this
        // makes into those (which take the same lock) do not deadlock.
        using IDisposable guard = AuraFiles.Lock();

        AuraState state = AuraState.Load();

        if (on && string.IsNullOrEmpty(deviceKey) && state.CustomPreset.Length > 0)
        {
            CustomPreset? preset = AuraCustomPresets.Load().Find(p => p.Name == state.CustomPreset);
            if (preset != null)
            {
                return ApplyCustomPreset(preset);
            }
        }

        // The board counts as on while any single channel still is, so switching one header off
        // does not make the tray claim the whole board went dark.
        bool anyOn = on
            // Each channel comes back to its own last look, not to one colour smeared across the
            // board - and without rewriting what is remembered, which a plain Send would do.
            ? Restore(state, deviceKey, channel)
            : Send(AuraState.ModeOff, state, deviceKey, channel);

        AuraState next = state with { On = anyOn };
        next.Save();
        return next;
    }

    /// <summary>
    /// Puts every channel back to the effect and colour it was last set to, falling back to the
    /// board-wide state for channels that have never been set on their own. Reads the record
    /// rather than writing it, so switching off and on again cannot flatten it.
    /// </summary>
    /// <returns>Whether any channel on the machine is on afterwards.</returns>
    private static bool Restore(AuraState state, string? deviceKey, int channel)
    {
        var devices = AuraDevice.DiscoverAll();

        try
        {
            List<(string, int)> switched = ApplyMix(devices, deviceKey, channel, state, look: null);
            AuraChannelStates.SetPower(switched, on: true, BoardWide(state));

            // Something was just switched on, so the board is lit without having to re-read.
            return switched.Count > 0;
        }
        finally
        {
            Close(devices);
        }
    }

    /// <summary>The lighting the board as a whole is set to, for channels that have no own record.</summary>
    private static ChannelLighting BoardWide(AuraState state) =>
        new(state.Mode, state.Red, state.Green, state.Blue);

    /// <summary>A channel's own brightness, or the board-wide one while it has none of its own.</summary>
    private static byte Brightness(ChannelLighting? look, AuraState state) =>
        look is { Brightness: not 0 } own ? own.Brightness : state.Brightness;

    /// <summary>
    /// Writes a controller's channels in one go: the channels the caller named get
    /// <paramref name="look"/> - or their own record, when it is null - and every other channel
    /// of the same controller is written again exactly as it already stands.
    /// </summary>
    /// <remarks>
    /// Re-asserting the untouched channels is what makes a mix survive. The controller applies a
    /// new effect across its channels unless every one of them is named in the same burst, which
    /// is why a static header next to an animated one used to end up running the same effect as
    /// its neighbour. Channels that have never been set on their own are left alone, since there
    /// is nothing to re-assert and guessing would overwrite the BIOS lighting.
    /// </remarks>
    /// <returns>The channels that were named, for the caller to record.</returns>
    private static List<(string DeviceKey, int Channel)> ApplyMix(List<AuraDevice> devices, string? deviceKey,
        int channel, AuraState state, ChannelLighting? look)
    {
        List<AuraDevice> targets = ResolveDevices(devices, deviceKey);

        // Read once, then indexed per channel: this used to re-read and re-parse the whole file
        // for every single channel.
        Dictionary<string, ChannelLighting> remembered = AuraChannelStates.All();
        var named = new List<(string, int)>();
        IOException? failure = null;
        bool delivered = false;

        foreach (AuraDevice device in targets)
        {
            var looks = new List<ChannelLook>();
            var deviceNamed = new List<(string, int)>();

            foreach (AuraChannel affected in device.Channels)
            {
                ChannelLighting? own = AuraChannelStates.Get(remembered, device.Key, affected.Index);
                bool chosen = channel < 0 || affected.Index == channel;

                ChannelLighting? apply = chosen ? look ?? own ?? BoardWide(state) : own;
                if (apply == null)
                {
                    continue;
                }

                // A new effect or colour carries no brightness, so the one this channel was given
                // keeps applying - the same rule the stored record follows.
                (byte red, byte green, byte blue) = AuraState.Dim(apply.Red, apply.Green, apply.Blue,
                    Brightness(apply.Brightness != 0 ? apply : own, state));

                // A named channel takes the mode it was given, even when its record says it was
                // off - being named is what switches it back on. An untouched one keeps its own
                // state, off included.
                byte mode = chosen || apply.On ? apply.Mode : AuraState.ModeOff;
                looks.Add(new ChannelLook(affected.Index, mode, red, green, blue));

                if (chosen)
                {
                    deviceNamed.Add((device.Key, affected.Index));
                }
            }

            try
            {
                // Every channel of this controller in one burst - sending them one at a time left
                // the onboard zone lit and settled a dozen reports before the last header caught
                // up, which read as the board coming on in stages.
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
        int channel = -1)
    {
        using IDisposable guard = AuraFiles.Lock();

        AuraState state = AuraState.Load();
        bool wholeBoard = string.IsNullOrEmpty(deviceKey) && channel < 0;

        byte red = colour?.R ?? state.Red;
        byte green = colour?.G ?? state.Green;
        byte blue = colour?.B ?? state.Blue;

        Send(preset.Mode, state with { Red = red, Green = green, Blue = blue }, deviceKey, channel);

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
    public static AuraState SetBrightness(byte percent, string? deviceKey = null, int channel = -1)
    {
        using IDisposable guard = AuraFiles.Lock();

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

        var devices = AuraDevice.DiscoverAll();

        try
        {
            // Recorded before anything is re-sent, so the replay below picks the new value up per
            // channel. Setting it board-wide hands the channels back to the board-wide value
            // instead of leaving each one pinned to what it was given individually.
            AuraChannelStates.SetBrightness(Channels(devices, deviceKey, channel), wholeBoard ? (byte)0 : percent,
                BoardWide(state));

            if (state.On)
            {
                ApplyMix(devices, deviceKey, channel, state, look: null);
            }
        }
        finally
        {
            Close(devices);
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
    public static AuraState ApplyCustomPreset(CustomPreset preset, byte? brightnessOverride = null)
    {
        if (preset.Entries.Count == 0)
        {
            throw new AuraNotFoundException(Strings.ErrorControllerNotFound, 3);
        }

        using IDisposable guard = AuraFiles.Lock();

        AuraState state = AuraState.Load();
        var devices = AuraDevice.DiscoverAll();
        var applied = new List<(string DeviceKey, int Channel, ChannelLighting Look)>();
        var named = new HashSet<(string DeviceKey, int Channel)>();

        // Every entry and every gap-filled channel lands here first, then each controller gets
        // one Apply() call for its whole mix - not one call per channel, which left the onboard
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

            foreach (CustomPresetEntry entry in preset.Entries)
            {
                // A brightness override means "forget every per-channel brightness", so it is
                // recorded as 0 (follow the board) rather than the transient override value -
                // the same "0 means follow the board" rule channel-state.json follows everywhere
                // else.
                var look = new ChannelLighting(entry.Mode, entry.Red, entry.Green, entry.Blue,
                    On: true, brightnessOverride.HasValue ? (byte)0 : entry.Brightness);

                foreach (AuraDevice device in devices.FindAll(d => d.Key == entry.DeviceKey))
                {
                    // Each channel is dimmed to its own brightness - a preset carries an effect and
                    // a colour, not a brightness, so what the channel was given keeps applying.
                    foreach (AuraChannel affected in device.Channels)
                    {
                        if (entry.Channel >= 0 && affected.Index != entry.Channel)
                        {
                            continue;
                        }

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
            }

            // A preset only has to name the channels its own look applies to - the controller
            // still applies an effect across every channel of a controller in one burst unless
            // the whole mix arrives together (see INVARIANTS.md), so anything left out on a
            // controller the preset touches is re-asserted from its own record here, the same
            // rule ApplyMix enforces for the plain switch path. This held only by convention
            // before, because the preset editor always names every channel it finds.
            //
            // A brightness override additionally reaches every OTHER controller too, named by
            // the preset or not: "all channels to this brightness" has to mean literally every
            // channel, matching what the plain (non-preset) whole-board slider already does via
            // AuraChannelStates.SetBrightness. Without this, a channel the preset does not
            // mention - on the same controller or a second one entirely - would keep whatever
            // brightness it already had while everything the preset does name jumped to the new
            // value, which is the same "did the slider even do anything" complaint that started
            // this fix in the first place.
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
                if (perDevice.TryGetValue(device.Key, out List<ChannelLook>? looks))
                {
                    device.Apply(looks);
                }
            }
        }
        finally
        {
            Close(devices);
        }

        if (applied.Count == 0)
        {
            // Every controller the preset names has gone. Saying so beats reporting a look that
            // never reached any hardware.
            throw new AuraNotFoundException(Strings.ErrorControllerNotFound, 3);
        }

        // A preset sets each channel individually, so each one remembers its own look - written
        // in one pass instead of once per entry. A brightness override's 0 means "follow the
        // board" and has to land as written, not be read back as "unset" and swapped for the
        // channel's previous value - which was the whole-board slider's own "did it even do
        // anything" bug re-entering one layer down.
        AuraChannelStates.Remember(applied, keepBrightness: !brightnessOverride.HasValue);

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
    private static bool Send(byte mode, AuraState state, string? deviceKey, int channel)
    {
        var devices = AuraDevice.DiscoverAll();

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
                AuraChannelStates.SetPower(switched, on: false, BoardWide(state));
            }
            else
            {
                AuraChannelStates.Remember(switched, look);
            }

            return AnyChannelOn(devices);
        }
        finally
        {
            Close(devices);
        }
    }

    /// <summary>Every non-target header dims to this instead of going dark - see <see cref="Identify"/>.</summary>
    private const byte IdentifyDim = 0x18;

    /// <summary>
    /// Lights one channel red at full brightness, unmistakably its own colour, and holds every
    /// other channel of the same controller at a faint white so the right header is obvious. Full
    /// off was tried first, but on boards where the RGB headers share one bus (channels 2-4 on
    /// Ben's board) an "off" neighbour can still catch stray colour from whichever header is
    /// actually driving the bus, so a dark header no longer proved it was not the one lit red - a
    /// faint white neighbour stays visibly distinct from the bright red target either way. A
    /// blink was tried before that, but toggling a channel between red and off in a loop ran into
    /// a dynamic-lighting limit on some boards - past a certain channel index the colour command
    /// was silently dropped, so it never blinked at all. A steady colour has no such rate to hit.
    /// Runs until <paramref name="token"/> is cancelled, then puts every channel of the
    /// controller back to what <c>channel-state.json</c> already says it should be - nothing is
    /// written here, this only replays what was already on record.
    /// </summary>
    /// <remarks>
    /// The dim channels and the red one go out in the same burst, not as separate operations.
    /// Sending every "off" first and the target channel's colour last, as its own later call, hit
    /// the same dropped-command limit the blink already ran into on the higher-indexed headers -
    /// arriving well behind a run of other reports is exactly the pattern that trips it.
    /// </remarks>
    public static void Identify(string deviceKey, int channel, CancellationToken token)
    {
        var devices = AuraDevice.DiscoverAll();
        AuraDevice? device = devices.Find(d => d.Key == deviceKey);

        try
        {
            if (device == null)
            {
                // Unplugged since the window last saw it - nothing to light or restore.
                return;
            }

            AuraState state = AuraState.Load();
            Dictionary<string, ChannelLighting> remembered = AuraChannelStates.All();

            var looks = new List<ChannelLook>();
            foreach (AuraChannel other in device.Channels)
            {
                looks.Add(other.Index == channel
                    ? new ChannelLook(channel, AuraState.ModeStatic, 0xFF, 0, 0)
                    : new ChannelLook(other.Index, AuraState.ModeStatic, IdentifyDim, IdentifyDim, IdentifyDim));
            }

            try
            {
                device.Apply(looks);
                token.WaitHandle.WaitOne(Timeout.Infinite);
            }
            finally
            {
                // Even when lighting the target channel itself failed, whatever channels this did
                // turn dark still need to go back to their own record - a failure here must not
                // leave a controller stuck dark with channel-state.json still describing it as lit.
                Replay(device, state, remembered);
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
        var devices = AuraDevice.DiscoverAll();

        try
        {
            AuraState state = AuraState.Load();

            foreach (AuraDevice device in devices)
            {
                var looks = new List<ChannelLook>();

                foreach (AuraChannel channel in device.Channels)
                {
                    CustomPresetEntry? entry = preset.Entries.Find(
                        e => e.DeviceKey == device.Key && (e.Channel == channel.Index || e.Channel < 0));

                    if (entry == null)
                    {
                        looks.Add(new ChannelLook(channel.Index, AuraState.ModeOff, 0, 0, 0));
                        continue;
                    }

                    (byte red, byte green, byte blue) = AuraState.Dim(entry.Red, entry.Green, entry.Blue,
                        entry.Brightness != 0 ? entry.Brightness : state.Brightness);
                    looks.Add(new ChannelLook(channel.Index, entry.Mode, red, green, blue));
                }

                device.Apply(looks);
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

        device.Apply(looks);
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
