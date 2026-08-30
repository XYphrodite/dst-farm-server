using DstFarm.Core;
using Xunit;

namespace DstFarm.Core.Tests;

public sealed class ConsoleQueueTests
{
    private static FarmConfig Config(TempDirectory temp) => new() { Root = temp.Path };

    [Fact]
    public void KeepsOrderAndClearsAfterDraining()
    {
        using var temp = new TempDirectory();
        var config = Config(temp);

        ConsoleQueue.Enqueue(config, "c_give(\"goldnugget\", 4)");
        ConsoleQueue.Enqueue(config, "c_announce(\"привет\")");

        Assert.Equal(
            ["c_give(\"goldnugget\", 4)", "c_announce(\"привет\")"],
            ConsoleQueue.Drain(config));
        Assert.Empty(ConsoleQueue.Drain(config));
    }

    [Fact]
    public void EmptyQueueIsNotAnError()
    {
        using var temp = new TempDirectory();

        Assert.Empty(ConsoleQueue.Drain(Config(temp)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IgnoresEmptyCommands(string? command)
    {
        using var temp = new TempDirectory();
        var config = Config(temp);

        ConsoleQueue.Enqueue(config, command!);

        Assert.Empty(ConsoleQueue.Drain(config));
    }

    /// <summary>Перевод строки внутри команды сломал бы разбор на стороне сервера.</summary>
    [Fact]
    public void FlattensMultilineCommandsIntoOneLine()
    {
        using var temp = new TempDirectory();
        var config = Config(temp);

        ConsoleQueue.Enqueue(config, "local p = AllPlayers[1]\nprint(p)");

        var command = Assert.Single(ConsoleQueue.Drain(config));
        Assert.DoesNotContain('\n', command);
        Assert.Equal("local p = AllPlayers[1] print(p)", command);
    }

    [Fact]
    public void SurvivesQuotesAndNonAsciiText()
    {
        using var temp = new TempDirectory();
        var config = Config(temp);
        const string command = "c_announce(\"Мир, дружба, жвачка\")";

        ConsoleQueue.Enqueue(config, command);

        Assert.Equal(command, Assert.Single(ConsoleQueue.Drain(config)));
    }
}
