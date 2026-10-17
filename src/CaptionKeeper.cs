using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AuraToggle;

/// <summary>
/// Keeps a window's title bar drawn as active while one of this tool's own popups has the focus.
/// Every list, panel and editor here is a window of its own that takes the activation, so Windows
/// dimmed the main window's caption each time one opened - a flicker on every click into a menu.
/// The caption now only turns inactive once the tool as a whole has lost the activation.
/// </summary>
internal sealed class CaptionKeeper : NativeWindow
{
    private const int WmNcActivate = 0x0086;
    private const int WmActivateApp = 0x001C;

    private static readonly List<CaptionKeeper> Keepers = new();

    private readonly Form _form;

    /// <summary>Set while this sends the real inactive state itself, so it is let through.</summary>
    private bool _releasing;

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    private CaptionKeeper(Form form)
    {
        _form = form;
        form.HandleCreated += (_, _) => AssignHandle(form.Handle);
        form.HandleDestroyed += (_, _) => ReleaseHandle();
        form.Disposed += (_, _) => Keepers.Remove(this);

        if (form.IsHandleCreated)
        {
            AssignHandle(form.Handle);
        }
    }

    /// <summary>Keeps <paramref name="form"/>'s caption active while a popup of this tool is.</summary>
    public static void Attach(Form form) => Keepers.Add(new CaptionKeeper(form));

    /// <summary>
    /// A popup lost the activation. When it went to another program rather than back to this
    /// tool, no window kept active here hears about it, so each is told to look again.
    /// </summary>
    public static void Recheck()
    {
        foreach (CaptionKeeper keeper in Keepers)
        {
            keeper.RecheckSoon();
        }
    }

    protected override void WndProc(ref Message m)
    {
        // The tool as a whole lost the foreground. This also covers windows that are no popup
        // of its own, such as the preset editor's file dialogs.
        if (m.Msg == WmActivateApp && m.WParam == IntPtr.Zero)
        {
            Recheck();
        }

        if (m.Msg == WmNcActivate && m.WParam == IntPtr.Zero && !_releasing)
        {
            // Which window takes over is not known yet at this point, so the caption stays as it
            // is and the answer is read once the switch has finished.
            m.WParam = (IntPtr)1;
            base.WndProc(ref m);
            RecheckSoon();
            return;
        }

        base.WndProc(ref m);
    }

    private void RecheckSoon()
    {
        if (_form.IsDisposed || !_form.IsHandleCreated)
        {
            return;
        }

        _form.BeginInvoke(() =>
        {
            // The thread's active window: one of this tool's own while a popup has the focus,
            // none once another program has it.
            if (Handle == IntPtr.Zero || GetActiveWindow() != IntPtr.Zero)
            {
                return;
            }

            _releasing = true;
            try
            {
                SendMessage(Handle, WmNcActivate, IntPtr.Zero, IntPtr.Zero);
            }
            finally
            {
                _releasing = false;
            }
        });
    }
}
