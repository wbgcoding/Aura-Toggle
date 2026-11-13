using System;
using System.IO;
using Xunit;

namespace AuraToggle.Tests;

/// <summary>The guard every other test relies on: nothing here may reach the real profile.</summary>
public sealed class IsolationTests
{
    [Fact]
    public void DataFolderIsTheThrowawayOne()
    {
        string real = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "aura-toggle");

        Assert.True(AuraFiles.ReviewStore);
        Assert.Equal(TestData.Folder, AuraFiles.Folder);
        Assert.NotEqual(real, AuraFiles.Folder, StringComparer.OrdinalIgnoreCase);
        Assert.StartsWith(AppContext.BaseDirectory, AuraFiles.PathTo("state.json"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ControllerDiscoveryNeverReachesHardware()
    {
        AuraNotFoundException ex = Assert.Throws<AuraNotFoundException>(() => AuraDevice.DiscoverAll());
        Assert.Equal(3, ex.ExitCode);
    }
}
