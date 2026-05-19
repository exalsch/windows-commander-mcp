using System;
using WindowsCommander.McpServer;

namespace WindowsCommander.Tests;

public class ServerInfoTests
{
    [Fact]
    public void CleanVersion_ReturnsPlainSemVerUnchanged()
    {
        Assert.Equal("0.2.0", ServerInfo.CleanVersion("0.2.0", null));
    }

    [Fact]
    public void CleanVersion_StripsBuildMetadata()
    {
        Assert.Equal("0.2.0", ServerInfo.CleanVersion("0.2.0+abc1234", null));
    }

    [Fact]
    public void CleanVersion_KeepsPrereleaseSuffix()
    {
        Assert.Equal("0.2.0-rc1", ServerInfo.CleanVersion("0.2.0-rc1+abc1234", null));
    }

    [Fact]
    public void CleanVersion_FallsBackToThreePartAssemblyVersion()
    {
        Assert.Equal("1.2.3", ServerInfo.CleanVersion(null, new Version(1, 2, 3, 0)));
    }

    [Fact]
    public void CleanVersion_FallsBackWhenInformationalVersionIsBlank()
    {
        Assert.Equal("0.1.0", ServerInfo.CleanVersion("", new Version(0, 1, 0, 0)));
    }

    [Fact]
    public void Version_IsNonEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(ServerInfo.Version));
    }
}
