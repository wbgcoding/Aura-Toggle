using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace AuraToggle.Tests;

public sealed class ReviewCoreCasesTests : PersistenceTest
{
    [Fact]
    public void EveryEngineCaseOfTheReviewBuildMatches()
    {
        var all = new List<(string Name, string Expected, string Actual)>();

        Program.ReviewCoreCases((name, expected, actual) => all.Add((name, expected, actual)));

        Assert.NotEmpty(all);
        string[] failed = all
            .Where(c => !string.Equals(c.Expected, c.Actual, StringComparison.Ordinal))
            .Select(c => $"{c.Name}: expected \"{c.Expected}\" but got \"{c.Actual}\"")
            .ToArray();
        Assert.True(failed.Length == 0, string.Join(Environment.NewLine, failed));
    }

    [Fact]
    public void TheEngineCasesCoverEveryGroup()
    {
        var names = new List<string>();

        Program.ReviewCoreCases((name, _, _) => names.Add(name));

        foreach (string group in new[] { "damaged presets", "entry", "switch on", "duplicate names", "bad stamp" })
        {
            Assert.Contains(names, n => n.StartsWith(group + ":", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void TheEngineCasesLeaveTheDataFolderWithoutLeftovers()
    {
        Program.ReviewCoreCases((_, _, _) => { });

        Assert.Empty(TempFiles());
        Assert.Empty(BadCopies("presets"));
    }

    [Fact]
    public void TheEngineCasesRestoreTheCurrentCulture()
    {
        System.Globalization.CultureInfo before = System.Globalization.CultureInfo.CurrentCulture;

        Program.ReviewCoreCases((_, _, _) => { });

        Assert.Same(before, System.Globalization.CultureInfo.CurrentCulture);
    }
}
