# Captures the README preview screenshots from the real window.
#
# Opens dist\AuraToggle.exe -review preview - the main window against stand-in controllers, on the
# state a first run starts on and in English - and writes it to a PNG with the rounded corners
# punched out, ready for tools\make-preview.py. No controller is touched, nothing stored on
# this machine is read, and the theme is picked by the switch rather than by Windows, so both
# pictures can be taken on one machine without changing its display settings.
#
# The pixels come from PrintWindow(PW_RENDERFULLCONTENT), not from the screen: a screen grab of
# DWM's extended frame bounds takes the window's outermost border pixel while it is still blended
# with whatever sits behind it, which put a one pixel ring of desktop wallpaper around the old
# preview. PrintWindow renders the window on its own, so there is nothing behind it to pick up.
#
#   powershell -ExecutionPolicy Bypass -File tools\capture-preview.ps1            REM both themes
#   powershell -ExecutionPolicy Bypass -File tools\capture-preview.ps1 -Mode dark REM only one

param(
    [ValidateSet("both", "dark", "light")]
    [string] $Mode = "both",
    [string] $OutDir = "docs",
    [int]    $SettleMs = 2500
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class Capture
{
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr pid);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint from, uint to, bool attach);

    /// <summary>Windows refuses SetForegroundWindow to a process that does not already own the
    /// foreground. Borrowing the input queue of the process that does own it lifts that.</summary>
    public static void Activate(IntPtr hwnd)
    {
        uint mine = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
        uint theirs = GetWindowThreadProcessId(hwnd, IntPtr.Zero);
        bool attached = mine != theirs && AttachThreadInput(mine, theirs, true);
        SetForegroundWindow(hwnd);
        BringWindowToTop(hwnd);
        if (attached) { AttachThreadInput(mine, theirs, false); }
    }
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint action, uint param, out RECT value, uint winIni);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    public const uint PW_RENDERFULLCONTENT = 2;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const uint SPI_GETWORKAREA = 0x0030;
    public const uint SWP_NOSIZE_NOZORDER_NOACTIVATE = 0x0001 | 0x0004 | 0x0010;
    public static readonly IntPtr PER_MONITOR_AWARE_V2 = new IntPtr(-4);
}
'@

$repo = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repo "dist\AuraToggle.exe"
if (-not (Test-Path $exe)) { throw "$exe not found - run build.bat portable first" }
if (-not [System.IO.Path]::IsPathRooted($OutDir)) { $OutDir = Join-Path $repo $OutDir }

# Without this the host is dpi unaware and GetWindowRect hands back virtualised coordinates while
# DwmGetWindowAttribute hands back physical ones - on a 150% display the two disagreed by half the
# window, and cropping one against the other is what left the old preview at the wrong scale.
if ([Capture]::SetThreadDpiAwarenessContext([Capture]::PER_MONITOR_AWARE_V2) -eq [IntPtr]::Zero) {
    throw "could not make this thread dpi aware - the capture would be measured at the wrong scale"
}

function Save-Preview([string] $theme, [string] $Out) {
$app = Start-Process $exe -ArgumentList "-review", "preview", $theme -PassThru
try {
    $deadline = (Get-Date).AddSeconds(20)
    while ($app.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 200
        $app.Refresh()
    }
    if ($app.MainWindowHandle -eq 0) { throw "the review window never appeared" }
    $hwnd = $app.MainWindowHandle

    # Windows greys a title bar out while its window is not the active one, so without this the two
    # captures came out with different title bars depending on what was active at the time.
    [Capture]::Activate($hwnd)

    # Centred on the primary display, whatever the window opened on. The app sizes its text from
    # the system dpi and the rest of the window from the display it sits on, so on a secondary
    # monitor of a different scale the two disagree - its own layout review calls that out as
    # "off the system scale", and it is what made the old preview look wrongly scaled.
    $work = New-Object Capture+RECT
    [void][Capture]::SystemParametersInfo([Capture]::SPI_GETWORKAREA, 0, [ref] $work, 0)
    $opened = New-Object Capture+RECT
    [void][Capture]::GetWindowRect($hwnd, [ref] $opened)
    [void][Capture]::SetWindowPos($hwnd, [IntPtr]::Zero,
        [int](($work.Left + $work.Right - ($opened.Right - $opened.Left)) / 2),
        [int](($work.Top + $work.Bottom - ($opened.Bottom - $opened.Top)) / 2),
        0, 0, [Capture]::SWP_NOSIZE_NOZORDER_NOACTIVATE)

    # Moving between displays of different scales makes the window re-fit itself; the capture must
    # not read the pixels from before that.
    Start-Sleep -Milliseconds 1200

    # A pointer resting over the window leaves whatever it touches drawn in its hover state, which
    # is how one of the two screenshots ended up with a highlighted gear. Parked clear of the
    # window for the capture and put back straight afterwards.
    $cursor = New-Object Capture+POINT
    [void][Capture]::GetCursorPos([ref] $cursor)
    $bounds = New-Object Capture+RECT
    [void][Capture]::GetWindowRect($hwnd, [ref] $bounds)
    $parked = $cursor.X -ge $bounds.Left -and $cursor.X -le $bounds.Right -and
              $cursor.Y -ge $bounds.Top -and $cursor.Y -le $bounds.Bottom
    if ($parked) { [void][Capture]::SetCursorPos($bounds.Left - 40, $bounds.Top + 20) }

    # The window measures itself after device discovery and the button animates in - both have to
    # be done before the pixels mean anything.
    Start-Sleep -Milliseconds $SettleMs

    $dpi = [Capture]::GetDpiForWindow($hwnd)
    $window = New-Object Capture+RECT
    if (-not [Capture]::GetWindowRect($hwnd, [ref] $window)) { throw "GetWindowRect failed" }
    $frame = New-Object Capture+RECT
    $hr = [Capture]::DwmGetWindowAttribute($hwnd, [Capture]::DWMWA_EXTENDED_FRAME_BOUNDS, [ref] $frame, 16)
    if ($hr -ne 0) { throw ("DwmGetWindowAttribute failed: 0x{0:X}" -f $hr) }
    Write-Verbose ("window {0},{1} {2}x{3} | frame {4},{5} {6}x{7}" -f `
        $window.Left, $window.Top, ($window.Right - $window.Left), ($window.Bottom - $window.Top),
        $frame.Left, $frame.Top, ($frame.Right - $frame.Left), ($frame.Bottom - $frame.Top)) -Verbose

    $full = New-Object System.Drawing.Bitmap(($window.Right - $window.Left), ($window.Bottom - $window.Top),
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($full)
    $hdc = $g.GetHdc()
    $ok = [Capture]::PrintWindow($hwnd, $hdc, [Capture]::PW_RENDERFULLCONTENT)
    $g.ReleaseHdc($hdc)
    $g.Dispose()
    if (-not $ok) { throw "PrintWindow refused to render the window" }

    # GetWindowRect includes DWM's invisible resize border; the extended frame bounds are what a
    # user sees, so everything outside them is cropped away.
    $crop = New-Object System.Drawing.Rectangle(
        ($frame.Left - $window.Left), ($frame.Top - $window.Top),
        ($frame.Right - $frame.Left), ($frame.Bottom - $frame.Top))
    $edged = $full.Clone($crop, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $full.Dispose()

    # The outermost ring of those bounds is the border DWM composites, which PrintWindow does not
    # draw at all and leaves pure black - two pixels of it on a 150% display, one on a 100% one.
    # Measured rather than assumed: a line is border while nearly all of it is black, and no theme
    # of this window paints anything in pure black.
    function Test-BorderLine([System.Drawing.Bitmap] $bmp, [int] $index, [string] $side) {
        $length = if ($side -eq "top" -or $side -eq "bottom") { $bmp.Width } else { $bmp.Height }
        $black = 0
        for ($i = 0; $i -lt $length; $i += 1) {
            $p = switch ($side) {
                "top"    { $bmp.GetPixel($i, $index) }
                "bottom" { $bmp.GetPixel($i, $bmp.Height - 1 - $index) }
                "left"   { $bmp.GetPixel($index, $i) }
                "right"  { $bmp.GetPixel($bmp.Width - 1 - $index, $i) }
            }
            if ($p.R -eq 0 -and $p.G -eq 0 -and $p.B -eq 0) { $black += 1 }
        }
        # Half is plenty: a border line measures around 90% black, the first real line of window
        # under it around 3%, and the rounded corners are what keeps a border line off 100%.
        return ($black * 2 -ge $length)
    }

    $inset = @{ top = 0; bottom = 0; left = 0; right = 0 }
    foreach ($side in @("top", "bottom", "left", "right")) {
        while ($inset[$side] -lt 4 -and (Test-BorderLine $edged $inset[$side] $side)) { $inset[$side] += 1 }
    }

    $shot = $edged.Clone((New-Object System.Drawing.Rectangle(
        $inset.left, $inset.top,
        ($edged.Width - $inset.left - $inset.right), ($edged.Height - $inset.top - $inset.bottom))),
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $edged.Dispose()

    # Windows 11 rounds a window by 8 device independent pixels. PrintWindow draws it square, so
    # the corners are punched here - without it the README preview sits in a rectangular box.
    $radius = [int][Math]::Round(8.0 * $dpi / 96.0)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $w = $shot.Width
    $h = $shot.Height
    # The arcs span the bitmap exactly: pulled in by a pixel, the clip drops the last column and
    # the last row, which showed up as a transparent hairline down the right edge.
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($w - $d, 0, $d, $d, 270, 90)
    $path.AddArc($w - $d, $h - $d, $d, $d, 0, 90)
    $path.AddArc(0, $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $rounded = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $rg = [System.Drawing.Graphics]::FromImage($rounded)
    $rg.Clear([System.Drawing.Color]::Transparent)
    $rg.SetClip($path)
    $rg.DrawImage($shot, 0, 0)
    $rg.Dispose()
    $path.Dispose()
    $shot.Dispose()

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Out) | Out-Null
    $rounded.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
    $rounded.Dispose()

    Write-Output ("{0}: {1}x{2} at {3} dpi ({4}%), corner radius {5}, border trimmed t{6} b{7} l{8} r{9}" -f `
        (Split-Path -Leaf $Out), $w, $h, $dpi, [int]($dpi * 100 / 96), $radius,
        $inset.top, $inset.bottom, $inset.left, $inset.right)
}
finally {
    if ($parked) { [void][Capture]::SetCursorPos($cursor.X, $cursor.Y) }
    if (-not $app.HasExited) { $app.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 500 }
    if (-not $app.HasExited) { $app.Kill() }
}
}

foreach ($theme in @("dark", "light")) {
    if ($Mode -eq "both" -or $Mode -eq $theme) {
        Save-Preview $theme (Join-Path $OutDir "preview-$theme.png")
    }
}
