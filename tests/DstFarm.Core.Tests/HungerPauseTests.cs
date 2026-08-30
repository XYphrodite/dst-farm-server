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

    /// <summary>
    /// Пауза больше не строчка в OnPlayerJoin: между подключением и появлением персонажа
    /// проходит выбор героя, и однократная команда попадала в пустой AllPlayers.
    /// </summary>
    [Fact]
    public void IsNotAJoinCommandAnyMore()
    {
        var config = new FarmConfig { HungerPaused = true };

        Assert.True(config.HungerPaused);
        Assert.Empty(config.OnPlayerJoin);
    }

    [Fact]
    public void CommandFeedsFirstThenPauses()
    {
        Assert.Contains("hunger:SetPercent(1)", FarmConfig.PauseHungerCommand, StringComparison.Ordinal);
        Assert.Contains("hunger:Pause()", FarmConfig.PauseHungerCommand, StringComparison.Ordinal);
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

    /// <summary>Старый config.json, где пауза лежала в OnPlayerJoin, должен переехать сам.</summary>
    [Fact]
    public void MigratesTheOldJoinCommandOnLoad()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "config.json");
        var old = new FarmConfig();
        old.OnPlayerJoin.Add(FarmConfig.PauseHungerCommand);
        old.OnPlayerJoin.Add("c_announce(\"привет\")");
        old.Save(file);

        var loaded = FarmConfig.Load(file);

        Assert.True(loaded.HungerPaused);
        Assert.Equal(["c_announce(\"привет\")"], loaded.OnPlayerJoin);
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
    public void NothingIsMaintainedWhileEverythingIsOff()
    {
        Assert.Empty(new FarmConfig().MaintenanceCommands);
    }

    [Fact]
    public void EachFlagContributesItsOwnCommand()
    {
        Assert.Equal([FarmConfig.PauseHungerCommand], new FarmConfig { HungerPaused = true }.MaintenanceCommands);
        Assert.Equal([FarmConfig.GiveAllRecipesCommand], new FarmConfig { AllRecipes = true }.MaintenanceCommands);
        Assert.Equal(2, new FarmConfig { HungerPaused = true, AllRecipes = true }.MaintenanceCommands.Count);
    }

    [Fact]
    public void AllRecipesSurvivesSavingAndLoading()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "config.json");
        new FarmConfig { AllRecipes = true }.Save(file);

        Assert.True(FarmConfig.Load(file).AllRecipes);
    }

    [Fact]
    public void EveryMaintenanceCommandIsOneLineSoTheConsoleCanTakeIt()
    {
        var config = new FarmConfig { HungerPaused = true, AllRecipes = true };

        foreach (var command in config.MaintenanceCommands)
        {
            Assert.DoesNotContain((char)10, command);
            Assert.Contains("ipairs(AllPlayers)", command, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CommandIsOneLineSoTheConsoleCanTakeIt()
    {
        Assert.DoesNotContain('\n', FarmConfig.PauseHungerCommand);
        Assert.Contains("ipairs(AllPlayers)", FarmConfig.PauseHungerCommand, StringComparison.Ordinal);
    }
}
