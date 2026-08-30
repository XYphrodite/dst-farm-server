using DstFarm.Core;
using Xunit;

namespace DstFarm.Core.Tests;

public sealed class HungerPauseTests
{
    [Fact]
    public void OffByDefault()
    {
        var config = new FarmConfig();

        Assert.False(config.HungerPaused);
        Assert.Empty(config.OnPlayerJoin);
    }

    [Fact]
    public void TurningItOnAddsTheJoinCommand()
    {
        var config = new FarmConfig { HungerPaused = true };

        Assert.True(config.HungerPaused);
        var command = Assert.Single(config.OnPlayerJoin);
        Assert.Contains("hunger:Pause()", command, StringComparison.Ordinal);
        // Сперва накормить: иначе персонаж застынет на нуле сытости навсегда.
        Assert.Contains("hunger:SetPercent(1)", command, StringComparison.Ordinal);
    }

    [Fact]
    public void TurningItOffRemovesTheCommand()
    {
        var config = new FarmConfig { HungerPaused = true };

        config.HungerPaused = false;

        Assert.False(config.HungerPaused);
        Assert.Empty(config.OnPlayerJoin);
    }

    [Fact]
    public void TogglingDoesNotPileUpDuplicates()
    {
        var config = new FarmConfig();

        for (var i = 0; i < 5; i++)
            config.HungerPaused = true;

        Assert.Single(config.OnPlayerJoin);
    }

    [Fact]
    public void LeavesOtherJoinCommandsAlone()
    {
        var config = new FarmConfig();
        config.OnPlayerJoin.Add("c_announce(\"привет\")");

        config.HungerPaused = true;
        config.HungerPaused = false;

        Assert.Equal(["c_announce(\"привет\")"], config.OnPlayerJoin);
    }

    [Fact]
    public void SurvivesSavingAndLoading()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "config.json");
        var config = new FarmConfig { HungerPaused = true };

        config.Save(file);
        var loaded = FarmConfig.Load(file);

        Assert.True(loaded.HungerPaused);
    }

    [Fact]
    public void CommandIsOneLineSoTheConsoleCanTakeIt()
    {
        Assert.DoesNotContain('\n', FarmConfig.PauseHungerCommand);
        Assert.Contains("ipairs(AllPlayers)", FarmConfig.PauseHungerCommand, StringComparison.Ordinal);
    }
}
