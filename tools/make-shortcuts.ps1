# Creates the two Windows shortcuts next to the executable.
#
# The shortcuts store a relative path in addition to the absolute one, so the whole folder
# can be moved or copied to another machine and the shortcuts still find the executable.
#
#   powershell -ExecutionPolicy Bypass -File tools\make-shortcuts.ps1 -Directory dist

param(
    [string]$Directory = (Join-Path $PSScriptRoot "..\dist"),
    [string]$ExeName = "Aura Toggle.exe"
)

$ErrorActionPreference = "Stop"

$Directory = (Resolve-Path $Directory).Path
$exe = Join-Path $Directory $ExeName
if (-not (Test-Path $exe)) {
    Write-Host "$ExeName not found in $Directory"
    exit 1
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AuraShortcut
{
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath,
            IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxArgs);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder icon, int maxPath,
            out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string icon, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pathRel, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName,
            [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
        void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder fileName);
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    public class ShellLink
    {
    }

    public static class Writer
    {
        public static void Create(string linkPath, string target, string arguments, string description)
        {
            var link = (IShellLinkW)new ShellLink();
            link.SetPath(target);
            link.SetArguments(arguments);
            link.SetDescription(description);
            link.SetIconLocation(target, 0);
            link.SetRelativePath(linkPath, 0);
            ((IPersistFile)link).Save(linkPath, true);
        }
    }
}
'@

$shortcuts = @(
    @{ Name = "Aura An.lnk";  Arguments = "-on";  Description = "Mainboard-Beleuchtung einschalten" },
    @{ Name = "Aura Aus.lnk"; Arguments = "-off"; Description = "Mainboard-Beleuchtung ausschalten" }
)

foreach ($shortcut in $shortcuts) {
    $path = Join-Path $Directory $shortcut.Name
    if (Test-Path $path) {
        Remove-Item $path -Force
    }

    [AuraShortcut.Writer]::Create($path, $exe, $shortcut.Arguments, $shortcut.Description)
    Write-Host "  $($shortcut.Name) -> $ExeName $($shortcut.Arguments)"
}
