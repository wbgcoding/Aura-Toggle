using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AuraToggle;

/// <summary>
/// Shown instead of <see cref="MessageBox"/> for anything a switch or the app itself did not
/// expect. Not modal, and - like <see cref="CustomPresetEditor"/> - does not close on an outside
/// click: it carries the one copy of the detail text the user might still want to read or copy
/// after clicking away, e.g. to open the log folder.
/// </summary>
internal sealed class ErrorDialog : PopupForm
{
    private const int PadAt96 = 16;
    private const int DialogWidthAt96 = 380;
    private const int DetailsHeightAt96 = 140;

    private int Pad => this.Scaled(PadAt96);

    private int DialogWidth => this.Scaled(DialogWidthAt96);

    private int DetailsHeight => this.Scaled(DetailsHeightAt96);

    private int _width;

    private readonly Label _heading = new();
    private readonly Label _body = new();
    private readonly LinkLabel _detailsToggle = new();
    private readonly TextBox _details = new();
    private readonly PillButton _copy = new();
    private readonly PillButton _openLog = new();
    private readonly PillButton _close = new();

    private readonly string _detailText;

    private ErrorDialog(string bodyText, string detailText)
    {
        _detailText = detailText;

        AutoScaleMode = AutoScaleMode.Dpi;
        ForeColor = Theme.Text;
        Font = Theme.Ui;

        _heading.AutoSize = true;
        _heading.Text = Strings.ErrorTitle;
        _heading.Font = Theme.Heading;
        _heading.ForeColor = Theme.Text;
        Controls.Add(_heading);

        _body.Text = bodyText;
        _body.ForeColor = Theme.TextMuted;
        _body.AutoSize = true;
        Controls.Add(_body);

        _detailsToggle.AutoSize = true;
        _detailsToggle.Text = "▸ " + Strings.ErrorDetails;
        _detailsToggle.LinkColor = Theme.Accent;
        _detailsToggle.ActiveLinkColor = Theme.Accent;
        _detailsToggle.VisitedLinkColor = Theme.Accent;
        _detailsToggle.LinkBehavior = LinkBehavior.NeverUnderline;
        _detailsToggle.BackColor = Theme.Surface;
        // LinkClicked only. It fires for a mouse click on the link and for Enter on the focused
        // one alike, while Click fires on top of the mouse case - handling both toggled the
        // details open and shut again in the same click. The label is nothing but this one link,
        // so there is no part of it a click could land on that LinkClicked would miss.
        _detailsToggle.LinkClicked += (_, _) => ToggleDetails();
        Controls.Add(_detailsToggle);

        _details.Multiline = true;
        _details.ReadOnly = true;
        _details.ScrollBars = ScrollBars.Vertical;
        _details.BackColor = Theme.NeutralSoft;
        _details.ForeColor = Theme.Text;
        _details.BorderStyle = BorderStyle.FixedSingle;
        // The text already carries \r\n from Environment.NewLine and Exception.ToString() on
        // Windows; normalising through a bare \n first stops that from doubling to \r\r\n.
        _details.Text = detailText.Replace("\r\n", "\n").Replace("\n", "\r\n");
        _details.Visible = false;
        Controls.Add(_details);

        _copy.Text = Strings.ErrorCopyDetails;
        _copy.Fill = Theme.NeutralSoft;
        _copy.ForeColor = Theme.Text;
        _copy.Click += (_, _) => CopyDetails();
        Controls.Add(_copy);

        _openLog.Text = Strings.ErrorOpenLog;
        _openLog.Fill = Theme.NeutralSoft;
        _openLog.ForeColor = Theme.Text;
        _openLog.Click += (_, _) => AuraFiles.OpenFolder();
        Controls.Add(_openLog);

        _close.Text = Strings.ErrorClose;
        _close.Primary = true;
        _close.Click += (_, _) => Close();
        Controls.Add(_close);

        Reflow();
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        // No title bar, and - like CustomPresetEditor - this stays open on an outside click, so
        // it needs its own way to move: by the heading, and by the window's own background.
        WindowDrag.Enable(this, this, _heading);
    }

    /// <summary>Dragged onto a display with a different scale: every position and size here was
    /// computed for the one it came from.</summary>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        BeginInvoke(() =>
        {
            if (!IsDisposed)
            {
                Reflow();
            }
        });
    }

    /// <summary>
    /// Logs the exception and, only when a message loop is actually running (or is guaranteed to
    /// start right after, see <paramref name="requireMessageLoop"/>) to show it on, displays this
    /// dialog. A crash before <c>Application.Run</c> - or one on the command line - has nowhere
    /// to paint a window, so it is logged and left at that.
    /// </summary>
    /// <param name="requireMessageLoop">
    /// False only for <see cref="Program"/>'s review mode, which calls this before its own
    /// <c>Application.Run()</c> - showing a form before the loop starts is valid WinForms, the
    /// loop just has to actually follow, which the review path guarantees.
    /// </param>
    /// <remarks>
    /// <see cref="AuraNotFoundException"/> and <see cref="IOException"/> already carry a curated,
    /// localised <see cref="Exception.Message"/> - that is shown as the body directly. Anything
    /// else is a bug, whose raw message means nothing to the user, so the body stays the generic
    /// <see cref="Strings.ErrorUnexpected"/> and the real text goes into the detail area instead.
    /// </remarks>
    public static ErrorDialog? Report(Exception ex, string context, IWin32Window? owner, Action? onClosed = null,
        bool requireMessageLoop = true)
    {
        AuraLog.Error(context, ex);

        if (requireMessageLoop && !Application.MessageLoop)
        {
            return null;
        }

        string body = ex is AuraNotFoundException or IOException ? ex.Message : Strings.ErrorUnexpected;

        string details = AuraFiles.Redact(string.Join(Environment.NewLine,
            $"Aura Toggle {Program.VersionText}",
            $"Channels on record: {SafeChannelCount()}",
            "",
            ex.ToString()));

        var dialog = new ErrorDialog(body, details);
        if (onClosed != null)
        {
            dialog.FormClosed += (_, _) => onClosed();
        }

        dialog.Open(owner);
        return dialog;
    }

    private static int SafeChannelCount()
    {
        try
        {
            return AuraChannelStates.All().Count;
        }
        catch (Exception ex) when (AuraFiles.IsExpected(ex))
        {
            return -1;
        }
    }

    private void ToggleDetails()
    {
        _details.Visible = !_details.Visible;
        _detailsToggle.Text = (_details.Visible ? "▾ " : "▸ ") + Strings.ErrorDetails;
        Reflow();
    }

    private void CopyDetails()
    {
        try
        {
            Clipboard.SetText(_detailText);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another application is holding the clipboard open; not worth surfacing further.
        }
    }

    /// <summary>
    /// The one place every position and size in this dialog is computed, so a display-scale change
    /// only has to run it again: everything here is derived from the current dpi, nothing is left
    /// holding the number it was given when the window was built.
    /// </summary>
    private void Reflow()
    {
        _heading.Location = new Point(Pad, Pad);

        _body.MaximumSize = new Size(DialogWidth - (Pad * 2), 0);
        _body.Location = new Point(Pad, _heading.Bottom + this.Scaled(8));

        _detailsToggle.Location = new Point(Pad, _body.Bottom + this.Scaled(10));

        _details.Location = new Point(Pad, _detailsToggle.Bottom + this.Scaled(6));
        _details.Height = DetailsHeight;

        // Height before width: the buttons fit their own width to the text at the current scale.
        _copy.Height = _openLog.Height = _close.Height = this.Scaled(30);
        _copy.FitToText(14);
        _openLog.FitToText(14);
        _close.FitToText(18);

        // The three buttons are translated text, so their combined width is not known until they
        // are all sized - a fixed dialog width clipped or overlapped them in German. The body
        // still wraps at the plain DialogWidth; that only leaves it narrower than a wider dialog,
        // never overlapping. The heading is folded in the same way, for the same reason.
        int gap = this.Scaled(8);
        _width = Math.Max(DialogWidth, Pad + _copy.Width + gap + _openLog.Width + gap + _close.Width + Pad);
        _width = Math.Max(_width, Pad + _heading.PreferredSize.Width + Pad);

        _details.Width = _width - (Pad * 2);

        int buttonsTop = _detailsToggle.Bottom + this.Scaled(6) +
            (_details.Visible ? _details.Height + this.Scaled(10) : 0);

        _copy.Location = new Point(Pad, buttonsTop);
        _openLog.Location = new Point(_copy.Right + gap, buttonsTop);
        _close.Location = new Point(_width - Pad - _close.Width, buttonsTop);

        ClientSize = new Size(_width, _copy.Bottom + Pad);
        KeepOnScreen();
    }

    private void Open(IWin32Window? owner)
    {
        Rectangle screen = (owner as Form)?.Bounds is Rectangle bounds
            ? Screen.FromRectangle(bounds).WorkingArea
            : Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);

        Location = new Point(screen.Left + ((screen.Width - Width) / 2), screen.Top + ((screen.Height - Height) / 2));

        if (owner == null)
        {
            Show();
        }
        else
        {
            Show(owner);
        }

        Activate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Close();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }
}
