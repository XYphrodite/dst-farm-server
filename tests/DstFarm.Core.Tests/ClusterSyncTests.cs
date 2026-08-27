using DstFarm.Core;
using Xunit;

namespace DstFarm.Core.Tests;

public sealed class ClusterSyncTests
{
    [Fact]
    public void FreshlyWrittenClusterMatchesConfig()
    {
        using var temp = new TempDirectory();
        var config = new FarmConfig { ConfDirectory = temp.Path, Cluster = "T" };

        ClusterWriter.Write(config, overwrite: true);

        Assert.True(ClusterWriter.MatchesDisk(config));
    }

    [Fact]
    public void MissingClusterDoesNotMatch()
    {
        using var temp = new TempDirectory();
        var config = new FarmConfig { ConfDirectory = temp.Path, Cluster = "T" };

        Assert.False(ClusterWriter.MatchesDisk(config));
    }

    [Fact]
    public void RenamingServerWithoutApplyingIsDetected()
    {
        using var temp = new TempDirectory();
        var config = new FarmConfig { ConfDirectory = temp.Path, Cluster = "T", ClusterName = "Farm Idle" };
        ClusterWriter.Write(config, overwrite: true);

        // Ровно то, что было на экране: имя и лимит игроков поменяли, но G не нажали.
        config.ClusterName = "Farm Idle Server";
        config.MaxPlayers = 1;

        Assert.False(ClusterWriter.MatchesDisk(config));

        ClusterWriter.Write(config, overwrite: true);
        Assert.True(ClusterWriter.MatchesDisk(config));
    }

    [Fact]
    public void ChangingWorldSettingsWithoutApplyingIsDetected()
    {
        using var temp = new TempDirectory();
        var config = new FarmConfig { ConfDirectory = temp.Path, Cluster = "T" };
        ClusterWriter.Write(config, overwrite: true);

        config.OnlyDay = false;

        Assert.False(ClusterWriter.MatchesDisk(config));
    }

    [Fact]
    public void EnablingCavesWithoutApplyingIsDetected()
    {
        using var temp = new TempDirectory();
        var config = new FarmConfig { ConfDirectory = temp.Path, Cluster = "T" };
        ClusterWriter.Write(config, overwrite: true);

        config.EnableCaves = true;

        Assert.False(ClusterWriter.MatchesDisk(config));
    }

    [Fact]
    public void HandEditedClusterFileIsDetected()
    {
        using var temp = new TempDirectory();
        var config = new FarmConfig { ConfDirectory = temp.Path, Cluster = "T" };
        ClusterWriter.Write(config, overwrite: true);
        var ini = Path.Combine(config.ClusterPath, "cluster.ini");
        File.WriteAllText(ini, File.ReadAllText(ini).Replace("max_players = 6", "max_players = 4", StringComparison.Ordinal));

        Assert.False(ClusterWriter.MatchesDisk(config));
    }

    [Fact]
    public void LineEndingsDoNotCauseFalseMismatch()
    {
        using var temp = new TempDirectory();
        var config = new FarmConfig { ConfDirectory = temp.Path, Cluster = "T" };
        ClusterWriter.Write(config, overwrite: true);
        var ini = Path.Combine(config.ClusterPath, "cluster.ini");
        File.WriteAllText(ini, File.ReadAllText(ini).ReplaceLineEndings("\n"));

        Assert.True(ClusterWriter.MatchesDisk(config));
    }
}
