using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Xunit;

namespace AuraToggle.Tests;

public sealed class AuraLogTests : PersistenceTest
{
    private static string LogText() => TestData.ReadRaw("log.txt") ?? "";

    private static string[] LogLines() =>
        LogText().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void InfoWarnAndErrorEachAddOneLineWithTheirLevel()
    {
        AuraLog.Info("started");
        AuraLog.Warn("odd");
        AuraLog.Error("switch", new InvalidOperationException("boom"));

        string[] lines = LogLines();
        Assert.Equal(3, lines.Length);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}  INFO  started$", lines[0]);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}  WARN  odd$", lines[1]);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}  ERROR  switch: InvalidOperationException: boom$", lines[2]);
    }

    [Fact]
    public void TheStampIsTheLocalTimeOfTheCall()
    {
        DateTime before = DateTime.Now.AddSeconds(-1);
        AuraLog.Info("now");
        DateTime after = DateTime.Now.AddSeconds(1);

        DateTime stamp = DateTime.ParseExact(LogLines()[0][..19], "yyyy-MM-dd HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.InRange(stamp, before, after);
    }

    [Fact]
    public void TheStampIgnoresTheCulture()
    {
        string year = DateTime.Now.Year.ToString(System.Globalization.CultureInfo.InvariantCulture);

        foreach (string culture in new[] { "th-TH", "ar-SA", "de-DE", "ja-JP" })
        {
            TestData.Clear();
            WithCulture(culture, () => AuraLog.Info("x"));

            Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}  INFO  x$", LogLines()[0]);
            Assert.StartsWith(year, LogLines()[0], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void LinesAreAppendedInOrder()
    {
        for (int i = 0; i < 20; i++)
        {
            AuraLog.Info("line " + i);
        }

        string[] lines = LogLines();
        Assert.Equal(20, lines.Length);
        for (int i = 0; i < 20; i++)
        {
            Assert.EndsWith("  INFO  line " + i, lines[i]);
        }
    }

    [Fact]
    public void ALineEndsWithTheLineSeparator()
    {
        AuraLog.Info("x");

        Assert.EndsWith(Environment.NewLine, LogText());
    }

    [Fact]
    public void TheLogHasNoByteOrderMark()
    {
        AuraLog.Info("x");

        byte[] bytes = File.ReadAllBytes(AuraFiles.PathTo("log.txt"));
        Assert.True(bytes.Length > 3);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal((byte)'2', bytes[0]);
    }

    [Fact]
    public void ALogCreatesItsFolderWhenItIsGone()
    {
        Directory.Delete(AuraFiles.Folder, recursive: true);

        AuraLog.Info("first");

        Assert.Single(LogLines());
    }

    [Fact]
    public void AnErrorNamesTheTypeAndEveryInnerCause()
    {
        var ex = new IOException("outer", new UnauthorizedAccessException("middle", new FormatException("root")));

        AuraLog.Error("save", ex);

        Assert.EndsWith(
            "  ERROR  save: IOException: outer <- UnauthorizedAccessException: middle <- FormatException: root",
            LogLines()[0]);
    }

    [Fact]
    public void AnErrorWithoutAnInnerCauseHasNoArrow()
    {
        AuraLog.Error("save", new InvalidOperationException("plain"));

        Assert.DoesNotContain("<-", LogText());
    }

    [Fact]
    public void ATextWithUnicodeAndLongContentIsKeptWhole()
    {
        string text = "Caf\u00E9 \u30B2\u30FC\u30E0 \uD83D\uDE00 " + new string('z', 5000);

        AuraLog.Info(text);

        Assert.EndsWith("  INFO  " + text, LogLines()[0]);
    }

    [Fact]
    public void TheProfilePathNeverReachesTheLog()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length == 0)
        {
            return;
        }

        AuraLog.Info("could not read " + profile + @"\AppData\x.json");
        AuraLog.Error("write " + profile, new IOException("denied: " + profile));

        string text = LogText();
        Assert.DoesNotContain(profile, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%USERPROFILE%", text);
    }

    [Fact]
    public void TheUserNameNeverReachesTheLogAsAPathSegment()
    {
        string user = Environment.UserName;
        if (user.Length < 3)
        {
            return;
        }

        AuraLog.Info(@"portable copy at D:\" + user + @"\tools\x.exe");

        Assert.Contains(@"D:\%USERNAME%\tools\x.exe", LogText());
    }

    [Fact]
    public void ALogOverTheCapRollsOverToTheOldFile()
    {
        string full = new string('o', 200 * 1024 + 1);
        TestData.WriteRaw("log.txt", full);

        AuraLog.Info("fresh");

        Assert.Equal(full, TestData.ReadRaw("log.old.txt"));
        string line = Assert.Single(LogLines());
        Assert.EndsWith("  INFO  fresh", line);
    }

    [Fact]
    public void ARolloverReplacesTheOlderOldFile()
    {
        TestData.WriteRaw("log.old.txt", "from long ago");
        TestData.WriteRaw("log.txt", new string('o', 200 * 1024 + 1));

        AuraLog.Info("fresh");

        Assert.StartsWith("ooo", TestData.ReadRaw("log.old.txt"), StringComparison.Ordinal);
    }

    [Fact]
    public void ALogExactlyAtTheCapKeepsGrowing()
    {
        string exact = new string('o', 200 * 1024);
        TestData.WriteRaw("log.txt", exact);

        AuraLog.Info("more");

        Assert.False(File.Exists(AuraFiles.PathTo("log.old.txt")));
        Assert.StartsWith(exact, LogText(), StringComparison.Ordinal);
        Assert.EndsWith("  INFO  more" + Environment.NewLine, LogText());
    }

    [Fact]
    public void TheLogStaysBoundedOverManyWrites()
    {
        string chunk = new string('c', 4000);
        for (int i = 0; i < 120; i++)
        {
            AuraLog.Info(chunk);
        }

        Assert.True(new FileInfo(AuraFiles.PathTo("log.txt")).Length <= 200 * 1024 + 4100);
        Assert.True(File.Exists(AuraFiles.PathTo("log.old.txt")));
    }

    [Fact]
    public void AFailedWriteIsDroppedWithoutAnException()
    {
        Directory.CreateDirectory(AuraFiles.PathTo("log.txt"));

        AuraLog.Info("nowhere to go");
        AuraLog.Warn("nowhere to go");
        AuraLog.Error("nowhere", new InvalidOperationException("x"));

        Assert.Empty(TempFiles());
    }

    [Fact]
    public void ALogAnotherProgramHoldsIsDroppedWithoutAnException()
    {
        TestData.WriteRaw("log.txt", "held\r\n");

        using (new FileStream(AuraFiles.PathTo("log.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            AuraLog.Info("blocked");
        }

        AuraLog.Info("free again");

        Assert.DoesNotContain("blocked", LogText());
        Assert.Contains("free again", LogText());
    }

    [Fact]
    public void ThreadsWritingAtOnceNeverMixTheirLines()
    {
        const int threads = 8;
        const int each = 25;
        Thread[] writers = Enumerable.Range(0, threads).Select(n => new Thread(() =>
        {
            for (int i = 0; i < each; i++)
            {
                AuraLog.Info($"t{n} #{i} " + new string('w', 300));
            }
        })).ToArray();

        foreach (Thread writer in writers)
        {
            writer.Start();
        }

        foreach (Thread writer in writers)
        {
            writer.Join();
        }

        string[] lines = LogLines();
        Assert.Equal(threads * each, lines.Length);
        Assert.All(lines, line => Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}  INFO  t\d #\d+ w{300}$", line));
        for (int n = 0; n < threads; n++)
        {
            string[] mine = lines.Where(l => l.Contains($"  INFO  t{n} #", StringComparison.Ordinal)).ToArray();
            Assert.Equal(each, mine.Length);
            for (int i = 0; i < each; i++)
            {
                Assert.Contains($"t{n} #{i} ", mine[i], StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void TheLogIsNotOneOfTheFilesAResetDeletes()
    {
        AuraLog.Info("kept");
        TestData.WriteRaw(AuraSettings.FileName, "{}");

        AuraFiles.ResetAll();

        Assert.Contains("kept", LogText());
    }

    [Fact]
    public void TheLogFileIsPlainUtf8Text()
    {
        AuraLog.Info("\u00E4\u00F6\u00FC");

        string text = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(AuraFiles.PathTo("log.txt")));
        Assert.Contains("\u00E4\u00F6\u00FC", text);
    }
}
