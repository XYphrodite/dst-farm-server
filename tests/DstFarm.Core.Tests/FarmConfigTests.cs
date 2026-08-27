using DstFarm.Core;
using Xunit;

namespace DstFarm.Core.Tests;

public sealed class FarmConfigTests
{
    [Fact]
    public void DefaultsTargetIdleUptime()
    {
        var config = new FarmConfig();

        Assert.Equal("endless", config.GameMode);
        Assert.True(config.OnlyDay);
        Assert.True(config.DisableThreats);
        Assert.False(config.EnableCaves);
        Assert.True(config.RestartOnExit);
        Assert.Equal(-1, config.DailyRestartHour);
    }

    [Fact]
    public void ShardsFollowCavesFlag()
    {
        Assert.Equal(["Master"], new FarmConfig().Shards);
        Assert.Equal(["Master", "Caves"], new FarmConfig { EnableCaves = true }.Shards);
    }

    [Fact]
    public void RoundTripsThroughJson()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "config.json");
        var config = new FarmConfig
        {
            Cluster = "Idle",
            ClusterName = "Ферма",
            ServerPort = 11111,
            EnableCaves = true,
            DailyRestartHour = 5,
        };

        config.Save(file);
        var loaded = FarmConfig.Load(file);

        Assert.Equal("Idle", loaded.Cluster);
        Assert.Equal("Ферма", loaded.ClusterName);
        Assert.Equal(11111, loaded.ServerPort);
        Assert.True(loaded.EnableCaves);
        Assert.Equal(5, loaded.DailyRestartHour);
    }

    [Fact]
    public void MissingFileFallsBackToDefaults()
    {
        using var temp = new TempDirectory();

        var loaded = FarmConfig.Load(Path.Combine(temp.Path, "нет.json"));

        Assert.Equal("FarmCluster", loaded.Cluster);
    }

    [Fact]
    public void ShortTokenIsNotAcceptedAsValid()
    {
        using var temp = new TempDirectory();
        var config = new FarmConfig { ConfDirectory = temp.Path, Cluster = "T" };
        Directory.CreateDirectory(config.ClusterPath);
        File.WriteAllText(config.ClusterTokenFile, "слишком-коротко");

        Assert.False(config.HasClusterToken());
    }

    [Fact]
    public void ClusterPathLivesUnderConfDirectory()
    {
        var config = new FarmConfig { ConfDirectory = @"C:\klei", Cluster = "Farm" };

        Assert.Equal(Path.Combine(@"C:\klei", "Farm"), config.ClusterPath);
    }
}
