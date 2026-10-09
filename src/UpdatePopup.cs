using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace AuraToggle;

/// <summary>
/// Shown once per version, the first time the main window is open to see it: the same choice the
/// tray entry already offers, just harder to miss than a balloon. Not modal, and - unlike
/// <see cref="ErrorDialog"/> and <see cref="CustomPresetEditor"/> - closes itself after 30 seconds
/// so a notice nobody acts on does not sit on the screen forever.
/// </summary>
internal sealed class UpdatePopup : PopupForm
{
    private const int PadAt96 = 16;
    private const int BodyWidthAt96 = 280;
    private const int AutoCloseMs = 30000;

    private int Pad => this.Scaled(PadAt96);

    private int BodyWidth => this.Scaled(BodyWidthAt96);

    private readonly Label _heading = new();
    private readonly Label _body = new();
    private readonly PillButton _primary = new();
    private readonly PillButton _later = new();
    private readonly System.Windows.Forms.Timer _autoClose = new() { Interval = AutoCloseMs };

    /// <param name="newVersion">The version the notice is about.</param>
    /// <param name="notVerified">
    /// False for the ordinary notice: the main button reads "Install now" and
    /// <see cref="InstallRequested"/> is what fires. True once a download could not be shown to be
    /// an official release: the text says so and the main button becomes "Open release page"
    /// (<see cref="OpenPageRequested"/>), because nothing is installed from there.
    /// </param>
    /// <param name="canOpenPage">
    /// Whether the release has a page worth linking to; without one the not-verified notice is
    /// just text and "Later".
    /// </param>
    public UpdatePopup(string newVersion, bool notVerified = false, bool canOpenPage = true)
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        ForeColor = Theme.Text;
        Font = Theme.Ui;

        _heading.AutoSize = true;
        _heading.Text = Strings.UpdateNoticeTitle;
        _heading.Font = Theme.Heading;
        _heading.ForeColor = Theme.Text;
        Controls.Add(_heading);

        _body.AutoSize = true;
        _body.Text = notVerified
            ? string.Format(CultureInfo.CurrentCulture, Strings.UpdateNotVerified, newVersion)
            : string.Format(CultureInfo.CurrentCulture, Strings.UpdateNoticeBody, newVersion, Program.VersionText);
        _body.ForeColor = Theme.TextMuted;
        Controls.Add(_body);

        _primary.Text = notVerified ? Strings.UpdateNoticeOpenPage : Strings.UpdateNoticeInstall;
        _primary.Primary = true;
        _primary.Visible = !notVerified || canOpenPage;
        _primary.Click += (_, _) =>
        {
            if (notVerified)
            {
                OpenPageRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                InstallRequested?.Invoke(this, EventArgs.Empty);
            }

            Close();
        };
        Controls.Add(_primary);

        _later.Text = Strings.UpdateNoticeLater;
        _later.Fill = Theme.NeutralSoft;
        _later.ForeColor = Theme.Text;
        _later.Click += (_, _) => Close();
        Controls.Add(_later);

        _autoClose.Tick += (_, _) => Close();
        _autoClose.Start();

        Reflow();

        // No title bar, so it needs its own way to move - by the heading, and by the window's
        // own background, same as ErrorDialog and CustomPresetEditor.
        WindowDrag.Enable(this, this, _heading);
    }

    /// <summary>Fired by the main button of the ordinary notice; <see cref="ToggleForm"/> runs the
    /// same install path the tray entry does, not a second copy of it.</summary>
    public event EventHandler? InstallRequested;

    /// <summary>Fired by the main button of the not-verified notice.</summary>
    public event EventHandler? OpenPageRequested;

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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Close();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _autoClose.Stop();
            _autoClose.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>Every position and size here is computed from the current dpi, so a display-scale
    /// change only has to run this again.</summary>
    private void Reflow()
    {
        // Not read back for layout - the children are still placed by Location, same as
        // ErrorDialog - but set here so a display-scale change is provable the same way the
        // Padding-driven popups already are (-review update 200 doubling this).
        Padding = new Padding(Pad);

        _heading.Location = new Point(Pad, Pad);

        _body.MaximumSize = new Size(BodyWidth, 0);
        _body.Location = new Point(Pad, _heading.Bottom + this.Scaled(8));

        _primary.Height = _later.Height = this.Scaled(30);
        _primary.FitToText(18);
        _later.FitToText(14);

        int gap = this.Scaled(8);
        int primaryWidth = _primary.Visible ? _primary.Width + gap : 0;
        int width = Math.Max(Pad + BodyWidth + Pad, Pad + primaryWidth + _later.Width + Pad);
        width = Math.Max(width, Pad + _heading.PreferredSize.Width + Pad);

        int buttonsTop = _body.Bottom + this.Scaled(14);
        _primary.Location = new Point(width - Pad - _later.Width - primaryWidth, buttonsTop);
        _later.Location = new Point(width - Pad - _later.Width, buttonsTop);

        ClientSize = new Size(width, _primary.Bottom + Pad);
        KeepOnScreen();
    }

    /// <summary>
    /// Centred under <paramref name="owner"/> and clamped onto its own work area by
    /// <see cref="Place"/>, or centred on the primary screen for the ownerless review surface.
    /// </summary>
    public void Open(Form? owner)
    {
        if (owner == null)
        {
            Rectangle screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
            Location = new Point(screen.Left + ((screen.Width - Width) / 2), screen.Top + ((screen.Height - Height) / 2));
            Show();
        }
        else
        {
            Place(new Point(owner.Left + ((owner.Width - Width) / 2), owner.Bottom + this.Scaled(8)));
            Show(owner);
        }

        Activate();
    }
}
