using DstFarm.Core;
using Xunit;

namespace DstFarm.Core.Tests;

public sealed class WorldProtectionsTests
{
    /// <summary>Ровно тот формат, что печатает игра: разделитель — табуляция, а не пробел.</summary>
    private const string RealLogFragment =
        "[00:01:03]: OVERRIDE: setting\tshadowcreatures\tto\tnever\n" +
        "[00:01:03]: OVERRIDE: setting\tdarkness\tto\tnonlethal\n" +
        "[00:01:03]: OVERRIDE: setting\thunger\tto\tnonlethal\n" +
        "[00:01:03]: OVERRIDE: setting\ttemperaturedamage\tto\tnonlethal\n";

    [Fact]
    public void ParsesTheTabSeparatedFormatTheGameActuallyPrints()
    {
        var applied = WorldProtections.ParseApplied(RealLogFragment);

        Assert.Equal("never", applied["shadowcreatures"]);
        Assert.Equal("nonlethal", applied["darkness"]);
        Assert.Equal("nonlethal", applied["hunger"]);
        Assert.Equal("nonlethal", applied["temperaturedamage"]);
    }

    [Fact]
    public void ParsesSingleSpacedFormatToo()
    {
        var applied = WorldProtections.ParseApplied("OVERRIDE: setting hounds to never");

        Assert.Equal("never", applied["hounds"]);
    }

    [Fact]
    public void LaterSessionWins()
    {
        var applied = WorldProtections.ParseApplied(
            "OVERRIDE: setting\thunger\tto\tdefault\nOVERRIDE: setting\thunger\tto\tnonlethal\n");

        Assert.Equal("nonlethal", applied["hunger"]);
    }

    [Fact]
    public void IgnoresUnrelatedLines()
    {
        Assert.Empty(WorldProtections.ParseApplied("[00:00:05]: Reset() returning\nSome other text"));
    }

    [Fact]
    public void ReportsEverythingAppliedWhenTheWorldMatches()
    {
        var config = new FarmConfig();
        var applied = ClusterWriter.BuildOverrides(config, caves: false)
            .ToDictionary(o => o.Key, o => o.Value, StringComparer.Ordinal);

        var report = WorldProtections.Evaluate(config, applied);

        Assert.True(report.AllApplied);
        Assert.Empty(report.Missing);
        Assert.True(report.Total > 10);
    }

    [Fact]
    public void GenerationOnlySettingsAreNotChecked()
    {
        var report = WorldProtections.Evaluate(new FarmConfig(), new Dictionary<string, string>());

        // Эти игра применяет один раз при генерации и строкой OVERRIDE не печатает.
        foreach (var key in (string[])["world_size", "chess", "spiders", "tentacles", "tallbirds", "walrus", "merm", "houndmound", "angrybees"])
            Assert.DoesNotContain(report.Checks, c => c.Key == key);

        // season_start = default тоже не печатается.
        Assert.DoesNotContain(report.Checks, c => c.Key == "season_start");
    }

    /// <summary>
    /// Живой мир из лога сервера: настройки поведения печатаются, генерационные — нет.
    /// Требовать вторые означало бы вечное «применено не всё» на исправном мире.
    /// </summary>
    [Fact]
    public void ACorrectlyGeneratedWorldReportsEverythingApplied()
    {
        var config = new FarmConfig();
        var applied = ClusterWriter.BuildOverrides(config, caves: false)
            .Where(o => !ClusterWriter.GenerationOnlyKeys.Contains(o.Key, StringComparer.Ordinal))
            .ToDictionary(o => o.Key, o => o.Value, StringComparer.Ordinal);

        var report = WorldProtections.Evaluate(config, applied);

        Assert.True(report.AllApplied, $"не применено: {string.Join(", ", report.Missing.Select(m => m.Key))}");
    }

    /// <summary>Мир, созданный до исправления: голода и темноты в нём нет.</summary>
    [Fact]
    public void SpotsAWorldGeneratedBeforeTheSettingsChanged()
    {
        var config = new FarmConfig();
        var applied = ClusterWriter.BuildOverrides(config, caves: false)
            .Where(o => o.Key is not ("hunger" or "darkness" or "shadowcreatures" or "brightmarecreatures"))
            .ToDictionary(o => o.Key, o => o.Value, StringComparer.Ordinal);

        var report = WorldProtections.Evaluate(config, applied);

        Assert.False(report.AllApplied);
        Assert.Equal(4, report.Missing.Count);
        Assert.Contains(report.Missing, c => c.Key == "hunger" && c.Actual is null);
    }

    [Fact]
    public void WrongValueCountsAsMissing()
    {
        var config = new FarmConfig();
        var applied = ClusterWriter.BuildOverrides(config, caves: false)
            .ToDictionary(o => o.Key, o => o.Key == "hunger" ? "default" : o.Value, StringComparer.Ordinal);

        var report = WorldProtections.Evaluate(config, applied);

        var hunger = Assert.Single(report.Missing);
        Assert.Equal("hunger", hunger.Key);
        Assert.Equal("default", hunger.Actual);
    }

    [Fact]
    public void TellsApartAWorldThatWasNeverGeneratedFromAnOutdatedOne()
    {
        var config = new FarmConfig();

        var never = WorldProtections.Evaluate(config, new Dictionary<string, string>());
        Assert.True(never.NothingObserved);

        var outdated = WorldProtections.Evaluate(
            config,
            ClusterWriter.BuildOverrides(config, caves: false)
                .Where(o => o.Key != "hunger")
                .ToDictionary(o => o.Key, o => o.Value, StringComparer.Ordinal));
        Assert.False(outdated.NothingObserved);
        Assert.Contains(outdated.Missing, c => c.Key == "hunger");
    }

    [Fact]
    public void SaysNothingWhenTheServerHasNeverRun()
    {
        using var temp = new TempDirectory();
        var config = new FarmConfig { ConfDirectory = temp.Path, Cluster = "T" };

        var report = WorldProtections.Inspect(config);

        Assert.False(report.LogFound);
        Assert.False(report.AllApplied);
    }

    [Fact]
    public void ReadsTheShardLogFromDisk()
    {
        using var temp = new TempDirectory();
        var config = new FarmConfig { ConfDirectory = temp.Path, Cluster = "T" };
        var master = Path.Combine(config.ClusterPath, "Master");
        Directory.CreateDirectory(master);
        var expected = ClusterWriter.BuildOverrides(config, caves: false)
            .Where(o => o.Key != "world_size" && o.Value != "default")
            .Select(o => $"[00:01:00]: OVERRIDE: setting\t{o.Key}\tto\t{o.Value}");
        File.WriteAllLines(Path.Combine(master, "server_log.txt"), expected);

        var report = WorldProtections.Inspect(config);

        Assert.True(report.LogFound);
        Assert.True(report.AllApplied);
    }

    [Fact]
    public void ReadsALogTheServerStillHoldsOpen()
    {
        using var temp = new TempDirectory();
        var config = new FarmConfig { ConfDirectory = temp.Path, Cluster = "T" };
        var master = Path.Combine(config.ClusterPath, "Master");
        Directory.CreateDirectory(master);
        var path = Path.Combine(master, "server_log.txt");

        using var writer = new StreamWriter(
            new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { AutoFlush = true };
        writer.Write(RealLogFragment);

        var report = WorldProtections.Inspect(config);

        Assert.True(report.LogFound);
        Assert.Contains(report.Checks, c => c.Key == "hunger" && c.Applied);
    }
}
