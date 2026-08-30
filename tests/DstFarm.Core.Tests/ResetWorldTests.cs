using DstFarm.Core;
using Xunit;

namespace DstFarm.Core.Tests;

public sealed class ResetWorldTests
{
    [Fact]
    public void RemovesSaveAndBackupButKeepsTheConfiguration()
    {
        using var temp = new TempDirectory();
        var config = new FarmConfig { ConfDirectory = temp.Path, Cluster = "T" };
        ClusterWriter.Write(config, overwrite: true);
        var master = Path.Combine(config.ClusterPath, "Master");
        Directory.CreateDirectory(Path.Combine(master, "save"));
        Directory.CreateDirectory(Path.Combine(master, "backup"));
        File.WriteAllText(Path.Combine(master, "save", "session.dat"), "мир");

        var removed = ClusterWriter.ResetWorld(config);

        Assert.Equal(2, removed.Count);
        Assert.False(Directory.Exists(Path.Combine(master, "save")));
        Assert.False(Directory.Exists(Path.Combine(master, "backup")));
        Assert.True(File.Exists(Path.Combine(master, "worldgenoverride.lua")));
        Assert.True(File.Exists(Path.Combine(master, "server.ini")));
        Assert.True(File.Exists(Path.Combine(config.ClusterPath, "cluster.ini")));
    }

    [Fact]
    public void DoesNothingWhenThereIsNoWorldYet()
    {
        using var temp = new TempDirectory();
        var config = new FarmConfig { ConfDirectory = temp.Path, Cluster = "T" };
        ClusterWriter.Write(config, overwrite: true);

        Assert.Empty(ClusterWriter.ResetWorld(config));
    }

    [Fact]
    public void CoversEveryShard()
    {
        using var temp = new TempDirectory();
        var config = new FarmConfig { ConfDirectory = temp.Path, Cluster = "T", EnableCaves = true };
        ClusterWriter.Write(config, overwrite: true);
        foreach (var shard in config.Shards)
            Directory.CreateDirectory(Path.Combine(config.ClusterPath, shard, "save"));

        var removed = ClusterWriter.ResetWorld(config);

        Assert.Equal(2, removed.Count);
        Assert.False(Directory.Exists(Path.Combine(config.ClusterPath, "Caves", "save")));
    }

    [Fact]
    public void KeepsUserModsUntouched()
    {
        using var temp = new TempDirectory();
        var config = new FarmConfig { ConfDirectory = temp.Path, Cluster = "T" };
        ClusterWriter.Write(config, overwrite: true);
        var mods = Path.Combine(config.ClusterPath, "Master", "modoverrides.lua");
        File.WriteAllText(mods, "-- мои моды");
        Directory.CreateDirectory(Path.Combine(config.ClusterPath, "Master", "save"));

        ClusterWriter.ResetWorld(config);

        Assert.Equal("-- мои моды", File.ReadAllText(mods));
    }
}
