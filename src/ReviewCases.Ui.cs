#if REVIEW
using System;

namespace AuraToggle;

internal static partial class Program
{
    /// <summary>
    /// Engine cases for the window, the preset editor and the popups, run by "-review engine"
    /// against the throwaway data folder.
    /// </summary>
    private static void ReviewUiCases(Action<string, string, string> check)
    {
    }
}
#endif
