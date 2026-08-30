using DstFarm.Core;
using Xunit;

namespace DstFarm.Core.Tests;

public sealed class ServerInstallTests
{
    private static FarmConfig Prepared(TempDirectory temp, bool withServer = true, bool withSteamCmd = true)
    {
        var config = new FarmConfig { Root = temp.Path, ConfDirectory = Path.Combine(temp.Path, "klei"), Cluster = "T" };

        if (withServer)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(config.ServerExe)!);
            File.WriteAllText(config.ServerExe, new string('x', 2048));
            File.WriteAllText(Path.Combine(config.ServerPath, "data.bin"), new string('y', 1024));
        }

        if (withSteamCmd)
        {
            Directory.CreateDirectory(config.SteamCmdPath);
            File.WriteAllText(config.SteamCmdExe, new string('z', 512));
        }

        return config;
    }

    [Fact]
    public void SeesAnInstalledServer()
    {
        using var temp = new TempDirectory();

        Assert.True(ServerInstall.IsInstalled(Prepared(temp)));
        Assert.False(ServerInstall.IsInstalled(Prepared(new TempDirectory(), withServer: false)));
    }

    [Fact]
    public void ReportsWhatWouldBeRemovedWithSizes()
    {
        using var temp = new TempDirectory();
        var config = Prepared(temp);

        var targets = ServerInstall.Removable(config, includeSteamCmd: false);

        var target = Assert.Single(targets);
        Assert.Equal(config.ServerPath, target.Path);
        Assert.Equal(2048 + 1024, target.Bytes);
    }

    [Fact]
    public void SteamCmdOnlyGoesWhenAsked()
    {
        using var temp = new TempDirectory();
        var config = Prepared(temp);

        Assert.Single(ServerInstall.Removable(config, includeSteamCmd: false));
        Assert.Equal(2, ServerInstall.Removable(config, includeSteamCmd: true).Count);
    }

    /// <summary>Мир и токен переустановку переживают — ради этого всё и затевалось.</summary>
    [Fact]
    public void LeavesTheClusterAlone()
    {
        using var temp = new TempDirectory();
        var config = Prepared(temp);
        config.ClusterToken = new string('t', 40);
        ClusterWriter.Write(config, overwrite: true);
        Directory.CreateDirectory(Path.Combine(config.ClusterPath, "Master", "save"));

        ServerInstall.Remove(config, includeSteamCmd: true);

        Assert.False(ServerInstall.IsInstalled(config));
        Assert.True(File.Exists(Path.Combine(config.ClusterPath, "cluster.ini")));
        Assert.True(config.HasClusterToken());
        Assert.True(Directory.Exists(Path.Combine(config.ClusterPath, "Master", "save")));
    }

    [Fact]
    public void RemovingTwiceIsNotAnError()
    {
        using var temp = new TempDirectory();
        var config = Prepared(temp);

        Assert.NotEmpty(ServerInstall.Remove(config, includeSteamCmd: true));
        Assert.Empty(ServerInstall.Remove(config, includeSteamCmd: true));
    }

    [Fact]
    public void NothingToRemoveOnACleanInstall()
    {
        using var temp = new TempDirectory();
        var config = Prepared(temp, withServer: false, withSteamCmd: false);

        Assert.Empty(ServerInstall.Removable(config, includeSteamCmd: true));
    }

    [Fact]
    public void SizeOfAMissingDirectoryIsZeroNotAnException()
    {
        using var temp = new TempDirectory();

        Assert.Equal(0, ServerInstall.DirectorySize(Path.Combine(temp.Path, "нет-такого")));
    }
}
