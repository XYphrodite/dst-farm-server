using System.Text.RegularExpressions;
using DstFarm.Core;
using Xunit;

namespace DstFarm.Core.Tests;

/// <summary>
/// Сторожевой тест: игра на неизвестный ключ или значение только пишет предупреждение
/// в лог и молча игнорирует настройку. Так «Без голода» год мог бы не работать.
/// Список сверен с scripts/map/customize.lua из серверной сборки.
/// </summary>
public sealed class WorldGenValidityTests
{
    private static readonly string[] Frequency = ["never", "rare", "default", "often", "always"];
    private static readonly string[] NonLethal = ["nonlethal", "default"];
    private static readonly string[] Season = ["noseason", "veryshortseason", "shortseason", "default", "longseason", "verylongseason", "random"];

    private static readonly Dictionary<string, string[]> Known = new(StringComparer.Ordinal)
    {
        ["world_size"] = ["small", "medium", "default", "large", "huge"],
        ["day"] = ["default", "longday", "longdusk", "longnight", "noday", "nodusk", "nonight", "onlyday", "onlydusk", "onlynight"],
        ["season_start"] = ["default", "winter", "spring", "summer"],
        ["autumn"] = Season,
        ["winter"] = Season,
        ["spring"] = Season,
        ["summer"] = Season,
        ["hunger"] = NonLethal,
        ["darkness"] = NonLethal,
        ["temperaturedamage"] = NonLethal,
        ["shadowcreatures"] = Frequency,
        ["brightmarecreatures"] = Frequency,
        ["hounds"] = Frequency,
        ["hunt"] = Frequency,
        ["deerclops"] = Frequency,
        ["bearger"] = Frequency,
        ["dragonfly"] = Frequency,
        ["beequeen"] = Frequency,
        ["klaus"] = Frequency,
        ["malbatross"] = Frequency,
        ["toadstool"] = Frequency,
        ["antliontribute"] = Frequency,
        ["liefs"] = Frequency,
        ["lightning"] = Frequency,
        ["earthquakes"] = Frequency,
        ["wildfires"] = Frequency,
        ["frograin"] = Frequency,
        ["lureplants"] = Frequency,
        ["hound_mounds"] = Frequency,
        ["mosquitos"] = Frequency,
        ["sharks"] = Frequency,
        ["squid"] = Frequency,
        ["wasps"] = Frequency,
        ["frogs"] = Frequency,
        ["walrus_setting"] = Frequency,
        ["cookiecutters"] = Frequency,
        ["pirateraids"] = Frequency,
        ["merms"] = Frequency,
        ["spiders_setting"] = Frequency,
        ["spider_warriors"] = Frequency,
        ["bats_setting"] = Frequency,
        ["nightmarecreatures"] = Frequency,
        ["spider_hider"] = Frequency,
        ["spider_spitter"] = Frequency,
        ["spider_dropper"] = Frequency,
        ["molebats"] = Frequency,
        ["itemmimics"] = Frequency,
        ["chest_mimics"] = Frequency,
        ["spiders"] = Frequency,
        ["cave_spiders"] = Frequency,
        ["houndmound"] = Frequency,
        ["merm"] = Frequency,
        ["tentacles"] = Frequency,
        ["chess"] = Frequency,
        ["walrus"] = Frequency,
        ["angrybees"] = Frequency,
        ["tallbirds"] = Frequency,
        ["bats"] = Frequency,
        ["fissure"] = Frequency,
        ["worms"] = Frequency,
    };

    public static TheoryData<FarmConfig> Configurations() =>
    [
        new FarmConfig(),
        new FarmConfig { EnableCaves = true },
        new FarmConfig { OnlyDay = false, EternalAutumn = false },
        new FarmConfig { NoHunger = false, NoSanityDrain = false },
        new FarmConfig { DisableThreats = false },
        new FarmConfig { WorldSize = "huge", GameMode = "survival" },
    ];

    [Theory]
    [MemberData(nameof(Configurations))]
    public void EveryGeneratedOverrideIsSomethingTheGameUnderstands(FarmConfig config)
    {
        foreach (var caves in new[] { false, true })
        {
            var lua = ClusterWriter.BuildWorldGen(config, caves);
            foreach (Match match in Regex.Matches(lua, @"^ {4}(?<key>[a-z_0-9]+) = ""(?<value>[^""]+)"",", RegexOptions.Multiline))
            {
                var key = match.Groups["key"].Value;
                var value = match.Groups["value"].Value;

                Assert.True(Known.ContainsKey(key), $"неизвестный ключ worldgen: {key}");
                Assert.True(
                    Known[key].Contains(value, StringComparer.Ordinal),
                    $"недопустимое значение {key} = \"{value}\", ожидалось одно из: {string.Join(", ", Known[key])}");
            }
        }
    }

    [Fact]
    public void CaveHostilesGoToTheCaveShardAndSurfaceOnesToTheSurface()
    {
        var config = new FarmConfig { EnableCaves = true };

        var surface = ClusterWriter.BuildWorldGen(config, caves: false);
        var caves = ClusterWriter.BuildWorldGen(config, caves: true);

        Assert.Contains("frogs = \"never\"", surface, StringComparison.Ordinal);
        Assert.DoesNotContain("frogs", caves, StringComparison.Ordinal);
        Assert.Contains("nightmarecreatures = \"never\"", caves, StringComparison.Ordinal);
        Assert.DoesNotContain("nightmarecreatures", surface, StringComparison.Ordinal);
    }

    [Fact]
    public void HungerUsesNonLethalBecauseNoneIsNotARealValue()
    {
        var lua = ClusterWriter.BuildWorldGen(new FarmConfig(), caves: false);

        Assert.Contains("hunger = \"nonlethal\"", lua, StringComparison.Ordinal);
        Assert.DoesNotContain("\"none\"", lua, StringComparison.Ordinal);
    }

    [Fact]
    public void ThereIsNoSanityKeyBecauseTheGameHasNoSuchSetting()
    {
        var lua = ClusterWriter.BuildWorldGen(new FarmConfig(), caves: false);

        Assert.DoesNotContain("sanity = ", lua, StringComparison.Ordinal);
    }

    [Fact]
    public void AutumnStartUsesDefaultBecauseSeasonStartRejectsAutumn()
    {
        var lua = ClusterWriter.BuildWorldGen(new FarmConfig { EternalAutumn = true }, caves: false);

        Assert.Contains("season_start = \"default\"", lua, StringComparison.Ordinal);
        Assert.DoesNotContain("season_start = \"autumn\"", lua, StringComparison.Ordinal);
    }
}
