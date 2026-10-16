#if REVIEW
using System;

namespace AuraToggle;

internal static partial class Program
{
    /// <summary>
    /// Engine cases for the data files and the switching logic, run by "-review engine" against
    /// the throwaway data folder.
    /// </summary>
    private static void ReviewCoreCases(Action<string, string, string> check)
    {
    }
}
#endif
