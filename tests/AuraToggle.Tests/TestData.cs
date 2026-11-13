using System;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace AuraToggle.Tests;

/// <summary>
/// Points the app at a throwaway data folder before any test touches it, so a test run never
/// reads or writes the real <c>%LOCALAPPDATA%\aura-toggle</c>, the Run key or a controller.
/// </summary>
internal static class TestData
{
    /// <summary>The folder every test's files live in, below the test output folder.</summary>
    public static string Folder { get; private set; } = "";

    [ModuleInitializer]
    internal static void Init()
    {
        Folder = Path.Combine(AppContext.BaseDirectory, "testdata", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        AuraFiles.UseReviewFolder(Folder);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                Directory.Delete(Folder, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        };
    }

    /// <summary>Empties the data folder: every test that reads stored files starts from nothing.</summary>
    public static void Clear()
    {
        foreach (string entry in Directory.GetFileSystemEntries(Folder))
        {
            if (Directory.Exists(entry))
            {
                Directory.Delete(entry, recursive: true);
            }
            else
            {
                File.Delete(entry);
            }
        }
    }

    /// <summary>Writes a raw file into the data folder, as a hand edit or an older version would.</summary>
    public static void WriteRaw(string name, string text) => File.WriteAllText(AuraFiles.PathTo(name), text);

    /// <summary>Reads a stored file back as text, or null when there is none.</summary>
    public static string? ReadRaw(string name)
    {
        string path = AuraFiles.PathTo(name);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}

/// <summary>Base for tests that read or write stored files: each starts from an empty folder.</summary>
public abstract class DataFolderTest : IDisposable
{
    protected DataFolderTest() => TestData.Clear();

    public void Dispose()
    {
        TestData.Clear();
        GC.SuppressFinalize(this);
    }
}
