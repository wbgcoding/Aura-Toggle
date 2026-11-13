using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Xunit;

namespace AuraToggle.Tests;

public sealed class AuraChannelNamesTests : PersistenceTest
{
    private static Dictionary<string, string> Load(string json)
    {
        TestData.WriteRaw(AuraChannelNames.FileName, json);
        return AuraChannelNames.All();
    }

    private static string? NameOf(string deviceKey, int channel) =>
        AuraChannelNames.Get(AuraChannelNames.All(), deviceKey, channel);

    // ---- constants and the empty states ----

    [Fact]
    public void TheStoredFileNameStaysAsShipped()
    {
        Assert.Equal("channel-names.json", AuraChannelNames.FileName);
    }

    [Fact]
    public void WithoutAFileThereAreNoNames()
    {
        Assert.Empty(AuraChannelNames.All());
        Assert.Null(NameOf("dev#1", 0));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"dev|0\":\"A\"")]
    [InlineData("garbage")]
    [InlineData("[]")]
    [InlineData("[\"dev|0\"]")]
    [InlineData("\"x\"")]
    [InlineData("null")]
    [InlineData("7")]
    [InlineData("true")]
    public void ADamagedFileOrAWrongRootGivesNoNames(string json)
    {
        Assert.Empty(Load(json));
    }

    [Fact]
    public void AnEmptyObjectGivesNoNames()
    {
        Assert.Empty(Load("{}"));
    }

    // ---- reading ----

    [Fact]
    public void AllReadsEveryStoredName()
    {
        Dictionary<string, string> names = Load("""{"dev#1|0":"Case","dev#1|2":"Strip","dev#2|-1":"Fan"}""");

        Assert.Equal(3, names.Count);
        Assert.Equal("Case", AuraChannelNames.Get(names, "dev#1", 0));
        Assert.Equal("Strip", AuraChannelNames.Get(names, "dev#1", 2));
        Assert.Equal("Fan", AuraChannelNames.Get(names, "dev#2", -1));
    }

    [Fact]
    public void GetUsesTheDeviceKeyAndChannelAsOneKey()
    {
        Dictionary<string, string> names = Load("""{"dev#1|0":"Case"}""");

        Assert.Equal("dev#1|0", AuraFiles.ChannelKey("dev#1", 0));
        Assert.Null(AuraChannelNames.Get(names, "dev#1", 1));
        Assert.Null(AuraChannelNames.Get(names, "dev#2", 0));
        Assert.Null(AuraChannelNames.Get(names, "dev#1|0", 0));
        Assert.Null(AuraChannelNames.Get(names, "", 0));
    }

    [Fact]
    public void GetMatchesTheKeyExactlyNotByCase()
    {
        Dictionary<string, string> names = Load("""{"Dev#1|0":"Case"}""");

        Assert.Null(AuraChannelNames.Get(names, "dev#1", 0));
        Assert.Equal("Case", AuraChannelNames.Get(names, "Dev#1", 0));
    }

    [Fact]
    public void GetOfAnEmptyStoredNameIsNoName()
    {
        Dictionary<string, string> names = Load("""{"dev|0":"","dev|1":"   "}""");

        Assert.Null(AuraChannelNames.Get(names, "dev", 0));
        Assert.Null(AuraChannelNames.Get(names, "dev", 1));
    }

    [Fact]
    public void GetOnAnEmptySetIsNoName()
    {
        Assert.Null(AuraChannelNames.Get(new Dictionary<string, string>(), "dev", 0));
    }

    [Fact]
    public void LoadKeepsOnlyTextValues()
    {
        Dictionary<string, string> names = Load(
            """{"a|0":5,"b|0":true,"c|0":null,"d|0":["x"],"e|0":{"n":"x"},"f|0":1.5,"ok|0":"Fine"}""");

        Assert.Equal(new[] { "ok|0" }, names.Keys.ToArray());
        Assert.Equal("Fine", names["ok|0"]);
    }

    [Fact]
    public void LoadCutsALongNameToTheCap()
    {
        Dictionary<string, string> names = Load("{\"d|0\":\"" + new string('n', AuraFiles.MaxChannelName + 40) + "\"}");

        Assert.Equal(new string('n', AuraFiles.MaxChannelName), names["d|0"]);
    }

    [Fact]
    public void LoadKeepsANameExactlyAtTheCap()
    {
        string exact = new string('n', AuraFiles.MaxChannelName);

        Assert.Equal(exact, Load("{\"d|0\":\"" + exact + "\"}")["d|0"]);
    }

    [Fact]
    public void LoadTurnsLineBreaksIntoSpacesAndTrims()
    {
        Dictionary<string, string> names = Load("""{"a|0":"one\ntwo\r\nthree","b|0":"  padded  ","c|0":"\t\r\n"}""");

        Assert.Equal("one two  three", names["a|0"]);
        Assert.Equal("padded", names["b|0"]);
        Assert.Equal("", names["c|0"]);
    }

    [Fact]
    public void LoadDoesNotCutAnEmojiInHalf()
    {
        string name = new string('x', AuraFiles.MaxChannelName - 1) + "\uD83D\uDE00";

        string loaded = Load("{\"d|0\":\"" + name + "\"}")["d|0"];

        Assert.Equal(new string('x', AuraFiles.MaxChannelName - 1), loaded);
        Assert.DoesNotContain(loaded, char.IsSurrogate);
    }

    [Fact]
    public void LoadKeepsUnicodeNames()
    {
        Dictionary<string, string> names = Load("""{"d|0":"Caf\u00E9 \u30B2\u30FC\u30E0 \uD83D\uDE00"}""");

        Assert.Equal("Caf\u00E9 \u30B2\u30FC\u30E0 \uD83D\uDE00", names["d|0"]);
    }

    [Fact]
    public void AKeyThatRepeatsInTheFileTakesTheLastValue()
    {
        Assert.Equal("Second", Load("""{"d|0":"First","d|0":"Second"}""")["d|0"]);
    }

    [Fact]
    public void LoadKeepsAKeyItDoesNotRecogniseForALaterSave()
    {
        // A key that matches no channel today may be a controller that is merely unplugged.
        Dictionary<string, string> names = Load("""{"anything at all":"x","|":"y","":"z"}""");

        Assert.Equal(3, names.Count);
    }

    [Fact]
    public void AllGivesAFreshDictionaryEachCall()
    {
        TestData.WriteRaw(AuraChannelNames.FileName, """{"d|0":"A"}""");

        AuraChannelNames.All().Clear();

        Assert.Single(AuraChannelNames.All());
    }

    [Fact]
    public void ANamesFileOverTheReadCapGivesNoNames()
    {
        Assert.Empty(Load("{\"d|0\":\"" + new string('x', 256 * 1024) + "\"}"));
    }

    // ---- Set ----

    [Fact]
    public void SetThenGetGivesTheName()
    {
        AuraChannelNames.Set("dev#1", 2, "Strip");

        Assert.Equal("Strip", NameOf("dev#1", 2));
    }

    [Fact]
    public void SetWritesOneStringPerChannelKey()
    {
        AuraChannelNames.Set("dev#1", 2, "Strip");
        AuraChannelNames.Set("dev#1", -1, "Whole");

        using JsonDocument document = JsonDocument.Parse(TestData.ReadRaw(AuraChannelNames.FileName)!);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.Equal("Strip", document.RootElement.GetProperty("dev#1|2").GetString());
        Assert.Equal("Whole", document.RootElement.GetProperty("dev#1|-1").GetString());
    }

    [Fact]
    public void SetKeepsEveryOtherName()
    {
        AuraChannelNames.Set("dev#1", 0, "A");
        AuraChannelNames.Set("dev#1", 1, "B");
        AuraChannelNames.Set("dev#2", 0, "C");

        AuraChannelNames.Set("dev#1", 1, "B2");

        Assert.Equal("A", NameOf("dev#1", 0));
        Assert.Equal("B2", NameOf("dev#1", 1));
        Assert.Equal("C", NameOf("dev#2", 0));
        Assert.Equal(3, AuraChannelNames.All().Count);
    }

    [Fact]
    public void SetKeepsNamesOfControllersThatAreNotConnected()
    {
        TestData.WriteRaw(AuraChannelNames.FileName, """{"unplugged|3":"Old strip"}""");

        AuraChannelNames.Set("dev#1", 0, "New");

        Assert.Equal("Old strip", NameOf("unplugged", 3));
        Assert.Equal("New", NameOf("dev#1", 0));
    }

    [Fact]
    public void SetCutsALongNameToTheCap()
    {
        AuraChannelNames.Set("dev", 0, new string('n', AuraFiles.MaxChannelName + 40));

        Assert.Equal(new string('n', AuraFiles.MaxChannelName), NameOf("dev", 0));
    }

    [Fact]
    public void SetTurnsLineBreaksIntoSpacesAndTrims()
    {
        AuraChannelNames.Set("dev", 0, "  one\ntwo  ");

        Assert.Equal("one two", NameOf("dev", 0));
    }

    [Fact]
    public void SetKeepsUnicodeAndDoesNotCutAnEmojiInHalf()
    {
        AuraChannelNames.Set("a", 0, "Caf\u00E9 \u30B2");
        AuraChannelNames.Set("b", 0, new string('x', AuraFiles.MaxChannelName - 1) + "\uD83D\uDE00");

        Assert.Equal("Caf\u00E9 \u30B2", NameOf("a", 0));
        Assert.Equal(new string('x', AuraFiles.MaxChannelName - 1), NameOf("b", 0));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   \t ")]
    [InlineData("\r\n")]
    [InlineData("\u00A0")]
    public void SetOfABlankNameClearsTheChannelBackToItsDefault(string blank)
    {
        AuraChannelNames.Set("dev", 0, "Case");
        AuraChannelNames.Set("dev", 1, "Keep");

        AuraChannelNames.Set("dev", 0, blank);

        Assert.Null(NameOf("dev", 0));
        Assert.Equal("Keep", NameOf("dev", 1));
        Assert.DoesNotContain("dev|0", TestData.ReadRaw(AuraChannelNames.FileName));
    }

    [Fact]
    public void ClearingTheLastNameLeavesAnEmptyObject()
    {
        AuraChannelNames.Set("dev", 0, "Case");

        AuraChannelNames.Set("dev", 0, "");

        Assert.Equal("{}", TestData.ReadRaw(AuraChannelNames.FileName));
        Assert.Empty(AuraChannelNames.All());
    }

    [Fact]
    public void ClearingAChannelThatHasNoNameChangesNothing()
    {
        AuraChannelNames.Set("dev", 1, "Keep");

        AuraChannelNames.Set("dev", 0, "");

        Assert.Equal("Keep", NameOf("dev", 1));
        Assert.Single(AuraChannelNames.All());
    }

    [Fact]
    public void ANameOfOnlyControlCharactersIsNoNameWhenRead()
    {
        // Not blank to Trim, but a control character becomes a space and the cut trims it away.
        AuraChannelNames.Set("dev", 0, "\u0001\u0002");

        Assert.Null(NameOf("dev", 0));
    }

    [Fact]
    public void SetReplacesAnExistingNameCompletely()
    {
        AuraChannelNames.Set("dev", 0, "A long first name");

        AuraChannelNames.Set("dev", 0, "B");

        Assert.Equal("B", NameOf("dev", 0));
    }

    [Fact]
    public void SetOfAnEmptyDeviceKeyStillWorks()
    {
        AuraChannelNames.Set("", 0, "Orphan");

        Assert.Equal("Orphan", NameOf("", 0));
    }

    [Fact]
    public void SetAcceptsAnyChannelNumber()
    {
        foreach (int channel in new[] { -1, 0, 1, 15, int.MaxValue })
        {
            AuraChannelNames.Set("dev", channel, "C" + channel);
        }

        foreach (int channel in new[] { -1, 0, 1, 15, int.MaxValue })
        {
            Assert.Equal("C" + channel, NameOf("dev", channel));
        }
    }

    [Fact]
    public void SetKeepsNamesWithQuotesAndBackslashesWhole()
    {
        AuraChannelNames.Set("dev", 0, "He said \"hi\" \\ and <b>&amp;");

        Assert.Equal("He said \"hi\" \\ and <b>&amp;", NameOf("dev", 0));
    }

    [Fact]
    public void ADeviceKeyWithPathCharactersRoundTrips()
    {
        string key = @"\\?\hid#vid_0b05&pid_19af#7&abc&0&0000#{4d1e55b2}";

        AuraChannelNames.Set(key, 3, "Strip");

        Assert.Equal("Strip", NameOf(key, 3));
        using JsonDocument document = JsonDocument.Parse(TestData.ReadRaw(AuraChannelNames.FileName)!);
        Assert.Equal("Strip", document.RootElement.GetProperty(key + "|3").GetString());
    }

    // ---- persistence behaviour ----

    [Fact]
    public void SetLeavesNoTempFileAndReplacesTheOldContent()
    {
        for (int i = 0; i < 10; i++)
        {
            AuraChannelNames.Set("dev", 0, "N" + i);
        }

        Assert.Empty(TempFiles());
        Assert.Equal("N9", NameOf("dev", 0));
    }

    [Fact]
    public void SetOverADamagedFileKeepsACopyOfIt()
    {
        TestData.WriteRaw(AuraChannelNames.FileName, "{\"dev|0\": oops");

        AuraChannelNames.Set("dev", 1, "New");

        Assert.Equal("{\"dev|0\": oops", File.ReadAllText(Assert.Single(BadCopies("channel-names"))));
        Assert.Equal("New", NameOf("dev", 1));
        Assert.Null(NameOf("dev", 0));
    }

    [Fact]
    public void SetOnAFileThatCannotBeReplacedDoesNotThrow()
    {
        TestData.WriteRaw(AuraChannelNames.FileName, "{}");
        using var hold = new FileStream(AuraFiles.PathTo(AuraChannelNames.FileName), FileMode.Open,
            FileAccess.Read, FileShare.Read);

        AuraChannelNames.Set("dev", 0, "Lost");

        Assert.Empty(TempFiles());
    }

    [Fact]
    public void SetWaitsForTheFilesLockInsteadOfRacingAnotherChange()
    {
        // A read-modify-write without the lock loses whichever of two concurrent changes saved first.
        var holder = new LockHolder(AuraFiles.Lock);
        var setter = new Thread(() => AuraChannelNames.Set("dev", 0, "Waited")) { IsBackground = true };
        setter.Start();

        Assert.True(Poll.Until(() => setter.ThreadState.HasFlag(ThreadState.WaitSleepJoin)));
        Assert.Null(NameOf("dev", 0));

        holder.Dispose();
        setter.Join();

        Assert.Equal("Waited", NameOf("dev", 0));
    }

    [Fact]
    public void ConcurrentSettersEachKeepTheirOwnChannel()
    {
        const int threads = 6;
        Thread[] setters = Enumerable.Range(0, threads).Select(n => new Thread(() =>
        {
            for (int i = 0; i < 8; i++)
            {
                AuraChannelNames.Set("dev", n, "t" + n + "-" + i);
            }
        })).ToArray();

        foreach (Thread setter in setters)
        {
            setter.Start();
        }

        foreach (Thread setter in setters)
        {
            setter.Join();
        }

        Dictionary<string, string> names = AuraChannelNames.All();
        Assert.Equal(threads, names.Count);
        for (int n = 0; n < threads; n++)
        {
            Assert.Equal("t" + n + "-7", AuraChannelNames.Get(names, "dev", n));
        }

        Assert.Empty(TempFiles());
        Assert.Empty(BadCopies("channel-names"));
    }

    [Fact]
    public void AResetDeletesTheNamesAndTheNextReadIsEmpty()
    {
        AuraChannelNames.Set("dev", 0, "Case");

        Assert.Equal(AuraFiles.ResetResult.Done, AuraFiles.ResetAll());

        Assert.Empty(AuraChannelNames.All());
        Assert.False(File.Exists(AuraFiles.PathTo(AuraChannelNames.FileName)));
    }
}
