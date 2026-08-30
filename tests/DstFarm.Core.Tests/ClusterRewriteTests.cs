using DstFarm.Core;
using Xunit;

namespace DstFarm.Core.Tests;

/// <summary>
/// DST переписывает worldgenoverride.lua после генерации мира: меняет preset на
/// worldgen_preset/settings_preset, добавляет свой заголовок и ключи игрового режима.
/// Побайтовое сравнение после этого не совпадёт никогда, и интерфейс вечно требовал бы G.
/// </summary>
public sealed class ClusterRewriteTests
{
    private static FarmConfig Written(TempDirectory temp)
    {
        var config = new FarmConfig { ConfDirectory = temp.Path, Cluster = "T" };
        ClusterWriter.Write(config, overwrite: true);
        return config;
    }

    private static string WorldGenPath(FarmConfig config) =>
        Path.Combine(config.ClusterPath, "Master", "worldgenoverride.lua");

    [Fact]
    public void StillMatchesAfterTheGameRewritesTheFile()
    {
        using var temp = new TempDirectory();
        var config = Written(temp);
        var ours = File.ReadAllText(WorldGenPath(config));

        // Ровно то, что делает игра: свой заголовок, другое имя пресета, свои ключи.
        var rewritten = "KLEI     1 " + ours
            .Replace("preset = \"SURVIVAL_TOGETHER\"", "worldgen_preset = \"SURVIVAL_TOGETHER\",\n  settings_preset = \"SURVIVAL_TOGETHER\"", StringComparison.Ordinal)
            .Replace("  },", "    ghostsanitydrain = \"none\",\n    portalresurection = \"always\",\n    resettime = \"none\",\n  },", StringComparison.Ordinal);
        File.WriteAllText(WorldGenPath(config), rewritten);

        Assert.True(ClusterWriter.MatchesDisk(config));
    }

    [Fact]
    public void StillSpotsASettingThatWasActuallyChanged()
    {
        using var temp = new TempDirectory();
        var config = Written(temp);
        var path = WorldGenPath(config);
        File.WriteAllText(path, File.ReadAllText(path).Replace("day = \"onlyday\"", "day = \"default\"", StringComparison.Ordinal));

        Assert.False(ClusterWriter.MatchesDisk(config));
    }

    [Fact]
    public void StillSpotsASettingThatDisappeared()
    {
        using var temp = new TempDirectory();
        var config = Written(temp);
        var path = WorldGenPath(config);
        var without = string.Join('\n', File.ReadAllLines(path).Where(l => !l.Contains("hunger", StringComparison.Ordinal)));
        File.WriteAllText(path, without);

        Assert.False(ClusterWriter.MatchesDisk(config));
    }

    [Fact]
    public void StillSpotsARenamedServer()
    {
        using var temp = new TempDirectory();
        var config = Written(temp);

        config.ClusterName = "Another name";

        Assert.False(ClusterWriter.MatchesDisk(config));
    }

    [Fact]
    public void ExtraKeysAddedByTheGameAreNotAComplaint()
    {
        using var temp = new TempDirectory();
        var config = Written(temp);
        var path = Path.Combine(config.ClusterPath, "cluster.ini");
        File.AppendAllText(path, "\nsomething_the_game_added = 42\n");

        Assert.True(ClusterWriter.MatchesDisk(config));
    }

    [Fact]
    public void ParsesBothIniAndLuaPairs()
    {
        var ini = ClusterWriter.ParseSettings("[GAMEPLAY]\ngame_mode = endless\nmax_players = 6\n");
        Assert.Equal("endless", ini["game_mode"]);
        Assert.Equal("6", ini["max_players"]);

        var lua = ClusterWriter.ParseSettings("return {\n  overrides = {\n    day = \"onlyday\",\n  },\n}\n");
        Assert.Equal("onlyday", lua["day"]);
    }
}
