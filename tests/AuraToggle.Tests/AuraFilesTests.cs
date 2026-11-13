using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Threading;
using Xunit;

namespace AuraToggle.Tests;

public sealed class AuraFilesTests : PersistenceTest
{
    // ---- paths and constants ----

    [Fact]
    public void PathToJoinsTheDataFolderAndTheName()
    {
        Assert.Equal(Path.Combine(AuraFiles.Folder, "state.json"), AuraFiles.PathTo("state.json"));
    }

    [Fact]
    public void StoredFormatLimitsStayAsShipped()
    {
        // These numbers are part of the stored files: a change cuts or rejects what is on disk.
        Assert.Equal(40, AuraFiles.MaxPresetName);
        Assert.Equal(30, AuraFiles.MaxChannelName);
        Assert.Equal(64, AuraFiles.MaxChannelLabel);
        Assert.Equal(256 * 1024, AuraFiles.MaxPresetFileBytes);
        Assert.Equal(15000, AuraFiles.HardwareLockTimeoutMs);
    }

    [Theory]
    [InlineData("dev", 0, "dev|0")]
    [InlineData("a|b", 2, "a|b|2")]
    [InlineData("", -1, "|-1")]
    public void ChannelKeyJoinsDeviceAndChannel(string device, int channel, string expected)
    {
        Assert.Equal(expected, AuraFiles.ChannelKey(device, channel));
    }

    // ---- Caption ----

    [Theory]
    [InlineData("", 5, "")]
    [InlineData("abc", 5, "abc")]
    [InlineData("abcdef", 3, "abc")]
    [InlineData("  padded  ", 20, "padded")]
    [InlineData("a\nb", 10, "a b")]
    [InlineData("a\r\nb", 10, "a  b")]
    [InlineData("\tx\t", 10, "x")]
    [InlineData("ab   cd", 4, "ab")]
    [InlineData("   ", 5, "")]
    [InlineData("x", 0, "")]
    [InlineData("\u00C4\u00F6\u00FC\u20AC", 10, "\u00C4\u00F6\u00FC\u20AC")]
    public void CaptionCleansAndCuts(string text, int max, string expected)
    {
        Assert.Equal(expected, AuraFiles.Caption(text, max));
    }

    [Theory]
    [InlineData("ab\uD83D\uDE00", 3, "ab")]
    [InlineData("ab\uD83D\uDE00", 4, "ab\uD83D\uDE00")]
    public void CaptionNeverLeavesHalfAnEmoji(string text, int max, string expected)
    {
        Assert.Equal(expected, AuraFiles.Caption(text, max));
    }

    [Fact]
    public void CaptionDropsALoneHighSurrogateAtTheEnd()
    {
        // Built at run time: an attribute argument cannot carry a lone surrogate.
        Assert.Equal("ab", AuraFiles.Caption("ab" + (char)0xD83D, 10));
    }

    [Fact]
    public void CaptionCutThroughAnEmojiStillSavesAsJson()
    {
        // A name cut between the two halves of an emoji used to make the next save of its file fail.
        string cut = AuraFiles.Caption(new string('a', 39) + "\uD83D\uDE00", AuraFiles.MaxPresetName);

        Assert.True(AuraFiles.Write("x.json", w => w.WriteStringValue(cut)));
        Assert.Equal(new string('a', 39), JsonSerializer.Deserialize<string>(TestData.ReadRaw("x.json")!));
    }

    [Fact]
    public void CaptionOfAVeryLongTextIsCutToTheLimit()
    {
        Assert.Equal(new string('z', 40), AuraFiles.Caption(new string('z', 100000), 40));
    }

    // ---- Redact ----

    [Fact]
    public void RedactLeavesPlainTextAndTheEmptyStringAlone()
    {
        Assert.Equal("", AuraFiles.Redact(""));
        Assert.Equal(@"D:\tools\x.exe", AuraFiles.Redact(@"D:\tools\x.exe"));
    }

    [Fact]
    public void RedactReplacesTheProfileFolderInAnyCase()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length == 0)
        {
            return;
        }

        Assert.Equal(@"%USERPROFILE%\AppData\x.json", AuraFiles.Redact(profile + @"\AppData\x.json"));
        Assert.Equal(@"%USERPROFILE%\AppData\x.json", AuraFiles.Redact(profile.ToUpperInvariant() + @"\AppData\x.json"));
        Assert.Equal(@"%USERPROFILE%\a and %USERPROFILE%\b", AuraFiles.Redact(profile + @"\a and " + profile + @"\b"));
    }

    [Fact]
    public void RedactHidesTheUserNameAsAPathSegment()
    {
        string user = Environment.UserName;
        if (user.Length < 3)
        {
            return;
        }

        string Leaks(string text) => AuraFiles.Redact(text).Replace("%USERPROFILE%", "").Replace("%USERNAME%", "")
            .Contains(user, StringComparison.OrdinalIgnoreCase)
            ? "leaks"
            : "clean";

        Assert.Equal(@"D:\%USERNAME%\zz-data\q.bin", AuraFiles.Redact(@"D:\" + user + @"\zz-data\q.bin"));
        Assert.Equal("clean", Leaks(@"d:\" + user.ToUpperInvariant() + @"\tools"));
        Assert.Equal("clean", Leaks("Access denied: E:/" + user));
        Assert.Equal("clean", Leaks("denied: \"E:\\" + user + "\" (code 5)"));
        Assert.Equal("clean", Leaks("two: D:\\" + user + "\\a;E:\\" + user + "\\b"));
    }

    [Fact]
    public void RedactKeepsTextThatOnlyContainsTheUserName()
    {
        string user = Environment.UserName;
        if (user.Length < 3)
        {
            return;
        }

        // A word equal to the name is not a path segment: it must survive, or half of any text would go.
        Assert.StartsWith(user, AuraFiles.Redact(user + ": a switch was running"), StringComparison.Ordinal);
        Assert.Equal(@"D:\" + user + @"x\a", AuraFiles.Redact(@"D:\" + user + @"x\a"));
        Assert.Equal(@"D:\x" + user + @"\a", AuraFiles.Redact(@"D:\x" + user + @"\a"));
    }

    // ---- Json readers ----

    [Fact]
    public void JsonTextFallsBackForMissingAndWrongKinds()
    {
        JsonElement root = Json("""{"s":"hi","e":"","n":5,"z":null,"b":true,"o":{},"a":[]}""");

        Assert.Equal("hi", AuraFiles.JsonText(root, "s"));
        Assert.Equal("", AuraFiles.JsonText(root, "e", "fb"));
        Assert.Equal("", AuraFiles.JsonText(root, "missing"));
        Assert.Equal("fb", AuraFiles.JsonText(root, "missing", "fb"));

        foreach (string name in new[] { "n", "z", "b", "o", "a" })
        {
            Assert.Equal("fb", AuraFiles.JsonText(root, name, "fb"));
        }
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("7", 7)]
    [InlineData("255", 255)]
    [InlineData("256", 9)]
    [InlineData("1000000", 9)]
    [InlineData("-1", 9)]
    [InlineData("1.5", 9)]
    [InlineData("1e400", 9)]
    [InlineData("\"7\"", 9)]
    [InlineData("null", 9)]
    [InlineData("true", 9)]
    [InlineData("[]", 9)]
    [InlineData("{}", 9)]
    public void JsonByteTakesOnlyAWholeNumberInRange(string literal, int expected)
    {
        // A wrong type in a hand-edited file has to fall back, not throw.
        JsonElement root = Json("{\"v\":" + literal + "}");

        Assert.Equal((byte)expected, AuraFiles.JsonByte(root, "v", 9));
    }

    [Fact]
    public void JsonByteDefaultsAndMissing()
    {
        JsonElement root = Json("{}");

        Assert.Equal(0, AuraFiles.JsonByte(root, "v"));
        Assert.Equal(200, AuraFiles.JsonByte(root, "v", 200));
    }

    [Theory]
    [InlineData("true", false, true)]
    [InlineData("false", true, false)]
    [InlineData("1", false, false)]
    [InlineData("1", true, true)]
    [InlineData("0", true, true)]
    [InlineData("\"true\"", false, false)]
    [InlineData("null", true, true)]
    [InlineData("[]", false, false)]
    [InlineData("{}", true, true)]
    public void JsonFlagTakesOnlyABoolean(string literal, bool fallback, bool expected)
    {
        JsonElement root = Json("{\"v\":" + literal + "}");

        Assert.Equal(expected, AuraFiles.JsonFlag(root, "v", fallback));
    }

    [Fact]
    public void JsonFlagMissingUsesTheFallback()
    {
        JsonElement root = Json("{}");

        Assert.False(AuraFiles.JsonFlag(root, "v"));
        Assert.True(AuraFiles.JsonFlag(root, "v", true));
    }

    [Theory]
    [InlineData("0", 0, 0)]
    [InlineData("-5", -5, -5)]
    [InlineData("2147483647", int.MaxValue, int.MaxValue)]
    [InlineData("-2147483648", int.MinValue, int.MinValue)]
    [InlineData("2147483648", 77, null)]
    [InlineData("1.5", 77, null)]
    [InlineData("\"3\"", 77, null)]
    [InlineData("null", 77, null)]
    [InlineData("true", 77, null)]
    [InlineData("[1]", 77, null)]
    public void JsonNumberTakesOnlyAnInt32(string literal, int expected, int? expectedOrNull)
    {
        JsonElement root = Json("{\"v\":" + literal + "}");

        Assert.Equal(expected, AuraFiles.JsonNumber(root, "v", 77));
        Assert.Equal(expectedOrNull, AuraFiles.JsonNumberOrNull(root, "v"));
    }

    [Fact]
    public void JsonNumberMissingUsesTheFallbackOrNull()
    {
        JsonElement root = Json("{}");

        Assert.Equal(0, AuraFiles.JsonNumber(root, "v"));
        Assert.Equal(-3, AuraFiles.JsonNumber(root, "v", -3));
        Assert.Null(AuraFiles.JsonNumberOrNull(root, "v"));
    }

    [Fact]
    public void JsonNumberOrNullKeepsAGenuineZero()
    {
        Assert.Equal(0, AuraFiles.JsonNumberOrNull(Json("""{"v":0}"""), "v"));
    }

    // ---- IsExpected ----

    [Fact]
    public void IsExpectedCoversEverythingAFileCanThrow()
    {
        Exception[] expected =
        {
            new JsonException(), new IOException(), new FileNotFoundException(), new DirectoryNotFoundException(),
            new PathTooLongException(), new UnauthorizedAccessException(), new InvalidOperationException(),
            new NotSupportedException(), new SecurityException(), new ArgumentException(),
            new ArgumentNullException("x"), new ArgumentOutOfRangeException("x"),
        };

        foreach (Exception ex in expected)
        {
            Assert.True(AuraFiles.IsExpected(ex), ex.GetType().Name);
        }
    }

    [Fact]
    public void IsExpectedRefusesBugsAndResourceFailures()
    {
        Exception[] unexpected =
        {
            new NullReferenceException(), new OutOfMemoryException(), new InvalidCastException(),
            new IndexOutOfRangeException(), new Exception(), new TimeoutException(), new StackOverflowException(),
        };

        foreach (Exception ex in unexpected)
        {
            Assert.False(AuraFiles.IsExpected(ex), ex.GetType().Name);
        }
    }

    // ---- Read ----

    [Fact]
    public void ReadOfAMissingFileGivesNull()
    {
        Assert.Null(AuraFiles.Read("nothing.json", JsonValueKind.Object));
    }

    [Theory]
    [InlineData("{\"a\":1}", JsonValueKind.Object)]
    [InlineData("[1,2]", JsonValueKind.Array)]
    [InlineData("  \n{}\n ", JsonValueKind.Object)]
    public void ReadGivesTheDocumentWhenTheRootKindMatches(string text, JsonValueKind kind)
    {
        TestData.WriteRaw("f.json", text);

        using JsonDocument? document = AuraFiles.Read("f.json", kind);

        Assert.NotNull(document);
        Assert.Equal(kind, document.RootElement.ValueKind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("{\"a\":1")]
    [InlineData("{\"a\":1,}")]
    [InlineData("{\"a\":1} trailing")]
    [InlineData("{/* comment */}")]
    [InlineData("not json at all")]
    [InlineData("\u0000\u0001\u0002")]
    [InlineData("[1,2]")]
    [InlineData("\"text\"")]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("true")]
    public void ReadOfAnythingButAnObjectGivesNull(string text)
    {
        TestData.WriteRaw("f.json", text);

        Assert.Null(AuraFiles.Read("f.json", JsonValueKind.Object));
    }

    [Fact]
    public void ReadOfAnObjectWhereAnArrayIsExpectedGivesNull()
    {
        TestData.WriteRaw("f.json", "{}");

        Assert.Null(AuraFiles.Read("f.json", JsonValueKind.Array));
    }

    [Fact]
    public void ReadOfBinaryGarbageGivesNull()
    {
        File.WriteAllBytes(AuraFiles.PathTo("f.json"), new byte[] { 0xFF, 0xFE, 0x00, 0x80, 0xC3, 0x28, 0x7B });

        Assert.Null(AuraFiles.Read("f.json", JsonValueKind.Object));
    }

    [Fact]
    public void ReadAcceptsAByteOrderMark()
    {
        File.WriteAllBytes(AuraFiles.PathTo("f.json"), new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("{\"a\":1}")).ToArray());

        using JsonDocument? document = AuraFiles.Read("f.json", JsonValueKind.Object);

        Assert.NotNull(document);
    }

    [Fact]
    public void ReadOfAnAbsurdlyDeepFileGivesNull()
    {
        TestData.WriteRaw("f.json", new string('[', 5000) + new string(']', 5000));

        Assert.Null(AuraFiles.Read("f.json", JsonValueKind.Array));
    }

    [Fact]
    public void ReadRefusesAFileOverTheSizeCeiling()
    {
        const string head = "{\"a\":\"";
        const string tail = "\"}";
        string Pad(long total) => head + new string('x', (int)(total - head.Length - tail.Length)) + tail;

        TestData.WriteRaw("big.json", Pad(AuraFiles.MaxPresetFileBytes));
        using (JsonDocument? atLimit = AuraFiles.Read("big.json", JsonValueKind.Object))
        {
            Assert.NotNull(atLimit);
        }

        TestData.WriteRaw("big.json", Pad(AuraFiles.MaxPresetFileBytes + 1));
        Assert.Null(AuraFiles.Read("big.json", JsonValueKind.Object));
    }

    [Fact]
    public void ReadOfAFileAnotherProgramHoldsExclusivelyGivesNull()
    {
        TestData.WriteRaw("f.json", "{}");

        using (new FileStream(AuraFiles.PathTo("f.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Null(AuraFiles.Read("f.json", JsonValueKind.Object));
        }

        using JsonDocument? again = AuraFiles.Read("f.json", JsonValueKind.Object);
        Assert.NotNull(again);
    }

    // ---- damaged copies ----

    [Fact]
    public void AFileThatCouldNotBeReadIsCopiedAsideBeforeTheFirstWrite()
    {
        byte[] broken = Encoding.UTF8.GetBytes("{\"keep\": me");
        File.WriteAllBytes(AuraFiles.PathTo("thing.json"), broken);
        Assert.Null(AuraFiles.Read("thing.json", JsonValueKind.Object));
        Assert.Empty(BadCopies("thing"));

        Assert.True(AuraFiles.Write("thing.json", w => { w.WriteStartObject(); w.WriteEndObject(); }));

        string[] copies = BadCopies("thing");
        Assert.Single(copies);
        Assert.Equal(broken, File.ReadAllBytes(copies[0]));
        Assert.Equal("{}", TestData.ReadRaw("thing.json"));
        Assert.Matches(@"^thing\.bad-\d{8}-\d{6}\.json$", Path.GetFileName(copies[0]));
    }

    [Fact]
    public void ASecondWriteKeepsNoSecondCopy()
    {
        TestData.WriteRaw("thing.json", "[broken");
        Assert.Null(AuraFiles.Read("thing.json", JsonValueKind.Object));

        AuraFiles.Write("thing.json", w => { w.WriteStartObject(); w.WriteEndObject(); });
        AuraFiles.Write("thing.json", w => { w.WriteStartObject(); w.WriteEndObject(); });

        Assert.Single(BadCopies("thing"));
    }

    [Fact]
    public void AWrongRootKindCountsAsDamaged()
    {
        TestData.WriteRaw("thing.json", "[1,2,3]");
        Assert.Null(AuraFiles.Read("thing.json", JsonValueKind.Object));

        AuraFiles.Write("thing.json", w => { w.WriteStartObject(); w.WriteEndObject(); });

        string[] copies = BadCopies("thing");
        Assert.Single(copies);
        Assert.Equal("[1,2,3]", File.ReadAllText(copies[0]));
    }

    [Fact]
    public void AnOversizedFileCountsAsDamagedAndIsKept()
    {
        string big = "{\"a\":\"" + new string('x', (int)AuraFiles.MaxPresetFileBytes) + "\"}";
        TestData.WriteRaw("thing.json", big);
        Assert.Null(AuraFiles.Read("thing.json", JsonValueKind.Object));

        AuraFiles.Write("thing.json", w => { w.WriteStartObject(); w.WriteEndObject(); });

        Assert.Equal(big, File.ReadAllText(Assert.Single(BadCopies("thing"))));
    }

    [Fact]
    public void AnEmptyDamagedFileLeavesNoCopy()
    {
        TestData.WriteRaw("thing.json", "");
        Assert.Null(AuraFiles.Read("thing.json", JsonValueKind.Object));

        Assert.True(AuraFiles.Write("thing.json", w => { w.WriteStartObject(); w.WriteEndObject(); }));

        Assert.Empty(BadCopies("thing"));
    }

    [Fact]
    public void AFileThatReadsFineAgainIsNoLongerKept()
    {
        TestData.WriteRaw("thing.json", "{broken");
        Assert.Null(AuraFiles.Read("thing.json", JsonValueKind.Object));
        TestData.WriteRaw("thing.json", "{}");
        AuraFiles.Read("thing.json", JsonValueKind.Object)?.Dispose();

        AuraFiles.Write("thing.json", w => { w.WriteStartObject(); w.WriteEndObject(); });

        Assert.Empty(BadCopies("thing"));
    }

    [Fact]
    public void AMissingFileClearsTheDamagedMark()
    {
        TestData.WriteRaw("thing.json", "{broken");
        Assert.Null(AuraFiles.Read("thing.json", JsonValueKind.Object));
        File.Delete(AuraFiles.PathTo("thing.json"));
        Assert.Null(AuraFiles.Read("thing.json", JsonValueKind.Object));

        TestData.WriteRaw("thing.json", "{\"fresh\":true}");
        AuraFiles.Write("thing.json", w => { w.WriteStartObject(); w.WriteEndObject(); });

        Assert.Empty(BadCopies("thing"));
    }

    [Fact]
    public void WritingAFileNobodyReadAsDamagedKeepsNoCopy()
    {
        TestData.WriteRaw("thing.json", "{broken");

        AuraFiles.Write("thing.json", w => { w.WriteStartObject(); w.WriteEndObject(); });

        Assert.Empty(BadCopies("thing"));
    }

    [Fact]
    public void OnlyTheThreeNewestDamagedCopiesStay()
    {
        foreach (string stamp in new[] { "20200101-000001", "20200101-000002", "20200101-000003", "20200101-000004" })
        {
            TestData.WriteRaw($"thing.bad-{stamp}.json", stamp);
        }

        TestData.WriteRaw("other.bad-20200101-000000.json", "other file");
        TestData.WriteRaw("thing.json", "{broken");
        Assert.Null(AuraFiles.Read("thing.json", JsonValueKind.Object));

        AuraFiles.Write("thing.json", w => { w.WriteStartObject(); w.WriteEndObject(); });

        string[] kept = BadCopies("thing").Select(Path.GetFileName).ToArray()!;
        Assert.Equal(3, kept.Length);
        Assert.DoesNotContain("thing.bad-20200101-000001.json", kept);
        Assert.DoesNotContain("thing.bad-20200101-000002.json", kept);
        Assert.Contains("thing.bad-20200101-000004.json", kept);
        Assert.Single(BadCopies("other"));
    }

    [Fact]
    public void AWriteIsRefusedWhenTheDamagedCopyCannotBeMade()
    {
        File.WriteAllText(AuraFiles.PathTo("thing.json"), "{broken");
        Assert.Null(AuraFiles.Read("thing.json", JsonValueKind.Object));

        using (new FileStream(AuraFiles.PathTo("thing.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(AuraFiles.Write("thing.json", w => { w.WriteStartObject(); w.WriteEndObject(); }));
        }

        Assert.Equal("{broken", TestData.ReadRaw("thing.json"));
        Assert.Empty(TempFiles());

        // Still marked: once the file is free the copy is made and the write goes through.
        Assert.True(AuraFiles.Write("thing.json", w => { w.WriteStartObject(); w.WriteEndObject(); }));
        Assert.Equal("{broken", File.ReadAllText(Assert.Single(BadCopies("thing"))));
    }

    [Fact]
    public void BadStampIsSortableAndIgnoresTheCulture()
    {
        Assert.Equal("20261016-080405", AuraFiles.BadStamp(new DateTime(2026, 10, 16, 8, 4, 5)));
        Assert.Equal("20000101-000000", AuraFiles.BadStamp(new DateTime(2000, 1, 1)));
        Assert.Equal("20261231-235959", AuraFiles.BadStamp(new DateTime(2026, 12, 31, 23, 59, 59)));

        foreach (string culture in new[] { "th-TH", "ar-SA", "fa-IR", "de-DE", "ja-JP" })
        {
            WithCulture(culture, () =>
                Assert.Equal("20261016-080405", AuraFiles.BadStamp(new DateTime(2026, 10, 16, 8, 4, 5))));
        }

        Assert.True(string.CompareOrdinal(AuraFiles.BadStamp(new DateTime(2026, 1, 2)),
            AuraFiles.BadStamp(new DateTime(2026, 1, 10))) < 0);
    }

    // ---- Write ----

    [Fact]
    public void WriteStoresTheFileWithoutAByteOrderMarkOrATempFile()
    {
        Assert.True(AuraFiles.Write("w.json", w =>
        {
            w.WriteStartObject();
            w.WriteString("name", "caf\u00E9 \u20AC \uD83D\uDE00");
            w.WriteEndObject();
        }));

        byte[] bytes = File.ReadAllBytes(AuraFiles.PathTo("w.json"));
        Assert.Equal((byte)'{', bytes[0]);
        Assert.Equal("caf\u00E9 \u20AC \uD83D\uDE00", JsonDocument.Parse(bytes).RootElement.GetProperty("name").GetString());
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void WriteReplacesAnExistingFileWholly()
    {
        TestData.WriteRaw("w.json", "{\"old\":\"" + new string('o', 500) + "\"}");

        Assert.True(AuraFiles.Write("w.json", w => { w.WriteStartObject(); w.WriteEndObject(); }));

        Assert.Equal("{}", TestData.ReadRaw("w.json"));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void WriteCreatesTheFolderWhenItIsGone()
    {
        Directory.Delete(AuraFiles.Folder, recursive: true);

        Assert.True(AuraFiles.Write("w.json", w => { w.WriteStartObject(); w.WriteEndObject(); }));

        Assert.True(File.Exists(AuraFiles.PathTo("w.json")));
    }

    [Fact]
    public void WriteOverAStaleTempFileOfThisProcessStillWorks()
    {
        File.WriteAllText(AuraFiles.PathTo($"w.json.{Environment.ProcessId}.tmp"), "half a write");

        Assert.True(AuraFiles.Write("w.json", w => { w.WriteStartObject(); w.WriteEndObject(); }));

        Assert.Equal("{}", TestData.ReadRaw("w.json"));
        Assert.Empty(TempFiles());
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(JsonException))]
    public void AFailedWriteKeepsThePreviousFileAndLeavesNoTempFile(Type failure)
    {
        TestData.WriteRaw("w.json", "{\"keep\":1}");

        Assert.False(AuraFiles.Write("w.json", w =>
        {
            w.WriteStartObject();
            w.WriteString("half", "written");
            throw (Exception)Activator.CreateInstance(failure)!;
        }));

        Assert.Equal("{\"keep\":1}", TestData.ReadRaw("w.json"));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void AWriterMisuseIsAFailedWriteNotACrash()
    {
        TestData.WriteRaw("w.json", "{\"keep\":1}");

        Assert.False(AuraFiles.Write("w.json", w => w.WriteEndObject()));

        Assert.Equal("{\"keep\":1}", TestData.ReadRaw("w.json"));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void AWriteThatCannotReplaceTheTargetLeavesNoTempFile()
    {
        Directory.CreateDirectory(AuraFiles.PathTo("w.json"));
        File.WriteAllText(Path.Combine(AuraFiles.PathTo("w.json"), "inside.txt"), "x");

        Assert.False(AuraFiles.Write("w.json", w => { w.WriteStartObject(); w.WriteEndObject(); }));

        Assert.Empty(TempFiles());
    }

    [Fact]
    public void ManyWritesLeaveNoTempFiles()
    {
        for (int i = 0; i < 50; i++)
        {
            int value = i;
            Assert.True(AuraFiles.Write("w.json", w => { w.WriteStartObject(); w.WriteNumber("i", value); w.WriteEndObject(); }));
        }

        Assert.Equal(49, JsonDocument.Parse(TestData.ReadRaw("w.json")!).RootElement.GetProperty("i").GetInt32());
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void ConcurrentWritersNeverLeaveAHalfWrittenFile()
    {
        bool stop = false;
        string? torn = null;
        Thread[] writers = Enumerable.Range(0, 4).Select(n => new Thread(() =>
        {
            for (int i = 0; i < 30; i++)
            {
                using IDisposable guard = AuraFiles.Lock();
                AuraFiles.Write("w.json", w =>
                {
                    w.WriteStartObject();
                    w.WriteString("by", new string('a', 2000) + n);
                    w.WriteEndObject();
                });
            }
        })).ToArray();

        var reader = new Thread(() =>
        {
            while (!Volatile.Read(ref stop))
            {
                try
                {
                    string? text = TestData.ReadRaw("w.json");
                    if (text != null)
                    {
                        using JsonDocument document = JsonDocument.Parse(text);
                    }
                }
                catch (IOException)
                {
                    // The move can hold the file for an instant.
                }
                catch (JsonException ex)
                {
                    torn = ex.Message;
                }
            }
        });

        reader.Start();
        foreach (Thread writer in writers)
        {
            writer.Start();
        }

        foreach (Thread writer in writers)
        {
            writer.Join();
        }

        Volatile.Write(ref stop, true);
        reader.Join();

        Assert.Null(torn);
        Assert.Empty(TempFiles());
    }

    // ---- ResetAll ----

    private static void SeedEveryStoredFile()
    {
        TestData.WriteRaw(AuraState.FileName, "{\"a\":1}");
        TestData.WriteRaw(AuraSettings.FileName, "{\"b\":2}");
        TestData.WriteRaw(AuraChannelNames.FileName, "{\"c\":3}");
        TestData.WriteRaw(AuraChannelStates.FileName, "{\"d\":4}");
        TestData.WriteRaw(AuraCustomPresets.FileName, "[5]");
    }

    [Fact]
    public void ResetAllWithNothingStoredDoesNothing()
    {
        Assert.Equal(AuraFiles.ResetResult.Done, AuraFiles.ResetAll());

        Assert.False(File.Exists(AuraFiles.PathTo("reset-backup.zip")));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void ResetAllDeletesTheFiveFilesAndBacksThemUpFirst()
    {
        SeedEveryStoredFile();
        TestData.WriteRaw("log.txt", "a log line");
        TestData.WriteRaw("presets.bad-20200101-000000.json", "old copy");

        Assert.Equal(AuraFiles.ResetResult.Done, AuraFiles.ResetAll());

        foreach (string name in new[]
        {
            AuraState.FileName, AuraSettings.FileName, AuraChannelNames.FileName,
            AuraChannelStates.FileName, AuraCustomPresets.FileName,
        })
        {
            Assert.False(File.Exists(AuraFiles.PathTo(name)), name);
        }

        // The log and the damaged copies are not part of a reset.
        Assert.Equal("a log line", TestData.ReadRaw("log.txt"));
        Assert.Equal("old copy", TestData.ReadRaw("presets.bad-20200101-000000.json"));

        using ZipArchive zip = ZipFile.OpenRead(AuraFiles.PathTo("reset-backup.zip"));
        Assert.Equal(
            new[] { "channel-names.json", "channel-state.json", "presets.json", "settings.json", "state.json" },
            zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.Equal("[5]", new StreamReader(zip.GetEntry("presets.json")!.Open()).ReadToEnd());
        Assert.Equal("{\"a\":1}", new StreamReader(zip.GetEntry("state.json")!.Open()).ReadToEnd());
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void ResetAllBacksUpOnlyTheFilesThatExist()
    {
        TestData.WriteRaw(AuraSettings.FileName, "{}");

        Assert.Equal(AuraFiles.ResetResult.Done, AuraFiles.ResetAll());

        using ZipArchive zip = ZipFile.OpenRead(AuraFiles.PathTo("reset-backup.zip"));
        Assert.Equal("settings.json", Assert.Single(zip.Entries).FullName);
    }

    [Fact]
    public void ASecondResetReplacesTheBackupOfTheFirst()
    {
        TestData.WriteRaw(AuraSettings.FileName, "{\"first\":true}");
        AuraFiles.ResetAll();

        TestData.WriteRaw(AuraState.FileName, "{\"second\":true}");
        AuraFiles.ResetAll();

        using ZipArchive zip = ZipFile.OpenRead(AuraFiles.PathTo("reset-backup.zip"));
        Assert.Equal("state.json", Assert.Single(zip.Entries).FullName);
    }

    [Fact]
    public void AResetWithNothingToLoseKeepsTheEarlierBackup()
    {
        TestData.WriteRaw(AuraSettings.FileName, "{\"first\":true}");
        AuraFiles.ResetAll();
        byte[] backup = File.ReadAllBytes(AuraFiles.PathTo("reset-backup.zip"));

        Assert.Equal(AuraFiles.ResetResult.Done, AuraFiles.ResetAll());

        Assert.Equal(backup, File.ReadAllBytes(AuraFiles.PathTo("reset-backup.zip")));
    }

    [Fact]
    public void ResetAllRemovesTheRetiredBakFile()
    {
        TestData.WriteRaw("reset-backup.bak", "old format");
        TestData.WriteRaw(AuraSettings.FileName, "{}");

        AuraFiles.ResetAll();

        Assert.False(File.Exists(AuraFiles.PathTo("reset-backup.bak")));
    }

    [Fact]
    public void ResetAllDeletesNothingWhenTheBackupCannotBeWritten()
    {
        SeedEveryStoredFile();
        Directory.CreateDirectory(AuraFiles.PathTo("reset-backup.zip"));
        File.WriteAllText(Path.Combine(AuraFiles.PathTo("reset-backup.zip"), "block.txt"), "x");

        Assert.Equal(AuraFiles.ResetResult.BackupFailed, AuraFiles.ResetAll());

        Assert.Equal("{\"a\":1}", TestData.ReadRaw(AuraState.FileName));
        Assert.Equal("{\"b\":2}", TestData.ReadRaw(AuraSettings.FileName));
        Assert.Equal("{\"c\":3}", TestData.ReadRaw(AuraChannelNames.FileName));
        Assert.Equal("{\"d\":4}", TestData.ReadRaw(AuraChannelStates.FileName));
        Assert.Equal("[5]", TestData.ReadRaw(AuraCustomPresets.FileName));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void AFailedBackupKeepsTheBackupFromBefore()
    {
        TestData.WriteRaw(AuraSettings.FileName, "{\"first\":true}");
        AuraFiles.ResetAll();
        byte[] backup = File.ReadAllBytes(AuraFiles.PathTo("reset-backup.zip"));

        // A file that cannot be read stops the new archive half way.
        TestData.WriteRaw(AuraSettings.FileName, "{\"second\":true}");
        using (new FileStream(AuraFiles.PathTo(AuraSettings.FileName), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal(AuraFiles.ResetResult.BackupFailed, AuraFiles.ResetAll());
        }

        Assert.Equal(backup, File.ReadAllBytes(AuraFiles.PathTo("reset-backup.zip")));
        Assert.Equal("{\"second\":true}", TestData.ReadRaw(AuraSettings.FileName));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void ResetAllReportsAFileAnotherProgramHolds()
    {
        SeedEveryStoredFile();

        // Readable for the backup, but not deletable.
        using (new FileStream(AuraFiles.PathTo(AuraChannelNames.FileName), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(AuraFiles.ResetResult.Incomplete, AuraFiles.ResetAll());
        }

        Assert.True(File.Exists(AuraFiles.PathTo(AuraChannelNames.FileName)));
        Assert.False(File.Exists(AuraFiles.PathTo(AuraState.FileName)));
        Assert.False(File.Exists(AuraFiles.PathTo(AuraSettings.FileName)));
        Assert.False(File.Exists(AuraFiles.PathTo(AuraChannelStates.FileName)));
        Assert.False(File.Exists(AuraFiles.PathTo(AuraCustomPresets.FileName)));
        Assert.True(File.Exists(AuraFiles.PathTo("reset-backup.zip")));
    }

    [Fact]
    public void AfterAResetTheStoresReadAsFirstRun()
    {
        new AuraState(false, AuraState.ModeStatic, 1, 2, 3, "x", 50).Save();
        AuraSettings.Default.Save();
        AuraFiles.ResetAll();

        Assert.Equal(AuraState.Default, AuraState.Load());
        Assert.Equal(AuraSettings.Default, AuraSettings.Load());
        Assert.Empty(AuraCustomPresets.Load());
    }

    [Fact]
    public void ResetAllDeletesNothingWhileASwitchHoldsTheHardwareLock()
    {
        SeedEveryStoredFile();

        using var holder = new LockHolder(AuraFiles.TryHardwareLock);
        IDisposable? secondAcquire = null;
        var another = new Thread(() => secondAcquire = AuraFiles.TryHardwareLock()) { IsBackground = true };
        another.Start();

        // Both wait out the full hardware timeout while the holder keeps the lock.
        AuraFiles.ResetResult result = AuraFiles.ResetAll();
        another.Join();
        secondAcquire?.Dispose();

        Assert.Equal(AuraFiles.ResetResult.Busy, result);
        Assert.Null(secondAcquire);
        Assert.Equal("{\"a\":1}", TestData.ReadRaw(AuraState.FileName));
        Assert.Equal("[5]", TestData.ReadRaw(AuraCustomPresets.FileName));
        Assert.False(File.Exists(AuraFiles.PathTo("reset-backup.zip")));
        Assert.Contains("nothing was deleted", TestData.ReadRaw("log.txt"));
    }

    // ---- locks ----

    [Fact]
    public void EachLockCanBeTakenAndGivenBack()
    {
        AuraFiles.Lock().Dispose();
        AuraFiles.HardwareLock().Dispose();
        AuraFiles.LogLock().Dispose();

        using IDisposable? hardware = AuraFiles.TryHardwareLock();
        Assert.NotNull(hardware);
    }

    [Fact]
    public void ALockIsReEntrantOnTheThreadThatHoldsIt()
    {
        using IDisposable outer = AuraFiles.Lock();
        using IDisposable inner = AuraFiles.Lock();
        using IDisposable? hardware = AuraFiles.TryHardwareLock();
        using IDisposable? again = AuraFiles.TryHardwareLock();

        Assert.NotNull(hardware);
        Assert.NotNull(again);
    }

    [Fact]
    public void DisposingTheLockLetsAnotherThreadIn()
    {
        var holder = new LockHolder(AuraFiles.TryHardwareLock);
        IDisposable? taken = null;
        var other = new Thread(() => taken = AuraFiles.TryHardwareLock()) { IsBackground = true };
        other.Start();

        // The second caller is parked in the wait, not through.
        Assert.True(Poll.Until(() => other.ThreadState.HasFlag(ThreadState.WaitSleepJoin)));
        Assert.Null(taken);

        holder.Dispose();
        other.Join();

        Assert.NotNull(taken);
        taken.Dispose();
    }

    [Fact]
    public void AReEnteredLockStaysHeldUntilEveryHoldIsGivenBack()
    {
        var holder = new LockHolder(AuraFiles.TryHardwareLock, times: 2);
        IDisposable? taken = null;
        var other = new Thread(() => taken = AuraFiles.TryHardwareLock()) { IsBackground = true };
        other.Start();

        Assert.True(Poll.Until(() => other.ThreadState.HasFlag(ThreadState.WaitSleepJoin)));
        Assert.Null(taken);

        holder.Dispose();
        other.Join();

        Assert.NotNull(taken);
        taken.Dispose();
    }

    [Fact]
    public void TheFilesLockMakesAnotherThreadWaitForIt()
    {
        var holder = new LockHolder(AuraFiles.Lock);
        bool done = false;
        var other = new Thread(() =>
        {
            using IDisposable guard = AuraFiles.Lock();
            done = true;
        })
        { IsBackground = true };
        other.Start();

        Assert.True(Poll.Until(() => other.ThreadState.HasFlag(ThreadState.WaitSleepJoin)));
        Assert.False(done);

        holder.Dispose();
        other.Join();

        Assert.True(done);
    }

    [Fact]
    public void AWaitThatRunsOutCarriesOnWithoutTheFilesLock()
    {
        // The files lock gives up after two seconds rather than hanging a switch for good.
        using var holder = new LockHolder(AuraFiles.Lock);
        bool done = false;
        var other = new Thread(() =>
        {
            using IDisposable guard = AuraFiles.Lock();
            done = true;
        })
        { IsBackground = true };

        other.Start();

        Assert.True(other.Join(TimeSpan.FromSeconds(30)));
        Assert.True(done);
    }

    // ---- a target held open by someone else ----

    private static bool WriteMarker(string value) =>
        AuraFiles.Write("held.json", w => { w.WriteStartObject(); w.WriteString("v", value); w.WriteEndObject(); });

    // A virus scanner or a second copy of the app reading the file holds it open for a moment, and
    // Windows refuses to replace a file that is open without delete sharing. That used to lose the
    // write at once; it now waits the moment out.
    [Fact]
    public void AWriteRidesOutATargetHeldOpenForAMoment()
    {
        Assert.True(WriteMarker("old"));
        string path = AuraFiles.PathTo("held.json");
        var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        bool sawTemp = false;

        // Lets go of the file only once the write has created its temp file, so the first move
        // always meets the held target and only a retry can succeed.
        var releaser = new Thread(() =>
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!sawTemp && clock.Elapsed < TimeSpan.FromSeconds(10))
            {
                sawTemp = TempFiles().Length > 0;
            }

            Thread.Sleep(40);
            held.Dispose();
        }) { IsBackground = true };
        releaser.Start();

        bool written = WriteMarker("new");

        Assert.True(releaser.Join(TimeSpan.FromSeconds(15)));
        Assert.True(sawTemp);
        Assert.True(written);
        Assert.Contains("\"new\"", File.ReadAllText(path));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void AWriteGivesUpOnATargetThatStaysHeldAndKeepsTheOldFile()
    {
        Assert.True(WriteMarker("old"));
        string path = AuraFiles.PathTo("held.json");

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            Assert.False(WriteMarker("new"));
        }

        Assert.Contains("\"old\"", File.ReadAllText(path));
        Assert.Empty(TempFiles());
    }

    // ---- folder helpers ----

    [Fact]
    public void ReviewStoreIsOnSoNothingTouchesTheRealProfile()
    {
        Assert.True(AuraFiles.ReviewStore);
    }
}
