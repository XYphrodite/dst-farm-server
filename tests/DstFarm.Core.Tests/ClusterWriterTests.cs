using DstFarm.Core;
using Xunit;

namespace DstFarm.Core.Tests;

public sealed class ClusterWriterTests
{
    [Fact]
    public void ClusterIniKeepsOfflineDisabledSoKleiDropsCount()
    {
        var ini = ClusterWriter.BuildClusterIni(new FarmConfig());

        Assert.Contains("offline_cluster = false", ini, StringComparison.Ordinal);
        Assert.Contains("pause_when_empty = false", ini, StringComparison.Ordinal);
        Assert.Contains("game_mode = endless", ini, StringComparison.Ordinal);
    }

    [Fact]
    public void ClusterIniTogglesShardWithCaves()
    {
        Assert.Contains("shard_enabled = false", ClusterWriter.BuildClusterIni(new FarmConfig()), StringComparison.Ordinal);
        Assert.Contains("shard_enabled = true", ClusterWriter.BuildClusterIni(new FarmConfig { EnableCaves = true }), StringComparison.Ordinal);
    }

    [Fact]
    public void CavesShardGetsItsOwnPorts()
    {
        var config = new FarmConfig { ServerPort = 10999 };

        var master = ClusterWriter.BuildServerIni(config, caves: false);
        var caves = ClusterWriter.BuildServerIni(config, caves: true);

        Assert.Contains("server_port = 10999", master, StringComparison.Ordinal);
        Assert.Contains("is_master = true", master, StringComparison.Ordinal);
        Assert.Contains("server_port = 11000", caves, StringComparison.Ordinal);
        Assert.Contains("is_master = false", caves, StringComparison.Ordinal);
        Assert.Contains("name = Caves", caves, StringComparison.Ordinal);
    }

    [Fact]
    public void FarmProfileDisablesEverythingThatKillsAnIdleCharacter()
    {
        var lua = ClusterWriter.BuildWorldGen(new FarmConfig(), caves: false);

        Assert.Contains("day = \"onlyday\"", lua, StringComparison.Ordinal);
        Assert.Contains("hunger = \"nonlethal\"", lua, StringComparison.Ordinal);
        Assert.Contains("darkness = \"nonlethal\"", lua, StringComparison.Ordinal);
        Assert.Contains("shadowcreatures = \"never\"", lua, StringComparison.Ordinal);
        Assert.Contains("winter = \"noseason\"", lua, StringComparison.Ordinal);
        Assert.Contains("deerclops = \"never\"", lua, StringComparison.Ordinal);
        // Боссы боссами, а догрызают AFK-персонажа жабы и пауки.
        Assert.Contains("frogs = \"never\"", lua, StringComparison.Ordinal);
        Assert.Contains("spiders_setting = \"never\"", lua, StringComparison.Ordinal);
        Assert.Contains("wasps = \"never\"", lua, StringComparison.Ordinal);
        Assert.Contains("preset = \"SURVIVAL_TOGETHER\"", lua, StringComparison.Ordinal);
    }

    [Fact]
    public void DisablingFarmFlagsRemovesTheirOverrides()
    {
        var config = new FarmConfig
        {
            OnlyDay = false,
            EternalAutumn = false,
            NoHunger = false,
            NoSanityDrain = false,
            DisableThreats = false,
        };

        var lua = ClusterWriter.BuildWorldGen(config, caves: false);

        Assert.DoesNotContain("day = ", lua, StringComparison.Ordinal);
        Assert.DoesNotContain("hunger = ", lua, StringComparison.Ordinal);
        Assert.DoesNotContain("darkness = ", lua, StringComparison.Ordinal);
        Assert.DoesNotContain("deerclops", lua, StringComparison.Ordinal);
        Assert.DoesNotContain("frogs", lua, StringComparison.Ordinal);
        Assert.Contains("world_size = \"small\"", lua, StringComparison.Ordinal);
    }

    [Fact]
    public void CavesWorldGenUsesCavePreset()
    {
        var lua = ClusterWriter.BuildWorldGen(new FarmConfig { EnableCaves = true }, caves: true);

        Assert.Contains("preset = \"DST_CAVE\"", lua, StringComparison.Ordinal);
        // Дневных и сезонных оверрайдов в пещерах быть не должно.
        Assert.DoesNotContain("day = ", lua, StringComparison.Ordinal);
        Assert.DoesNotContain("autumn", lua, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteCreatesMasterOnlyByDefault()
    {
        using var temp = new TempDirectory();
        var config = new FarmConfig { ConfDirectory = temp.Path, Cluster = "T" };

        ClusterWriter.Write(config, overwrite: true);

        Assert.True(File.Exists(Path.Combine(config.ClusterPath, "cluster.ini")));
        Assert.True(File.Exists(Path.Combine(config.ClusterPath, "Master", "server.ini")));
        Assert.True(File.Exists(Path.Combine(config.ClusterPath, "Master", "worldgenoverride.lua")));
        Assert.False(Directory.Exists(Path.Combine(config.ClusterPath, "Caves")));
    }

    [Fact]
    public void WriteKeepsExistingFilesUnlessOverwriteRequested()
    {
        using var temp = new TempDirectory();
        var config = new FarmConfig { ConfDirectory = temp.Path, Cluster = "T" };
        ClusterWriter.Write(config, overwrite: true);
        var modoverrides = Path.Combine(config.ClusterPath, "Master", "modoverrides.lua");
        File.WriteAllText(modoverrides, "-- мои моды");

        ClusterWriter.Write(config, overwrite: true);

        Assert.Equal("-- мои моды", File.ReadAllText(modoverrides));
    }

    [Fact]
    public void WriteStoresClusterTokenWhenConfigured()
    {
        using var temp = new TempDirectory();
        var config = new FarmConfig
        {
            ConfDirectory = temp.Path,
            Cluster = "T",
            ClusterToken = new string('a', 40),
        };

        ClusterWriter.Write(config, overwrite: true);

        Assert.True(config.HasClusterToken());
    }
}

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dstfarm-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
