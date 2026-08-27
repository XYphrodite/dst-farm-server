using DstFarm.Core;
using Xunit;

namespace DstFarm.Core.Tests;

public sealed class SteamProgressTests
{
    [Fact]
    public void ParsesDownloadLine()
    {
        var line = "Update state (0x61) downloading, progress: 35.24 (1568432128 / 4448147036)";

        Assert.True(SteamCmdOutput.TryParseProgress(line, out var progress));
        Assert.Equal("downloading", progress.State);
        Assert.Equal(35.24, progress.Percent, 2);
        Assert.Equal(1568432128, progress.BytesDone);
        Assert.Equal(4448147036, progress.BytesTotal);
    }

    [Fact]
    public void ParsesVerifyingLine()
    {
        var line = " Update state (0x81) verifying update, progress: 4.02 (178915328 / 4448147036)";

        Assert.True(SteamCmdOutput.TryParseProgress(line, out var progress));
        Assert.Equal("verifying update", progress.State);
        Assert.Equal(4.02, progress.Percent, 2);
    }

    [Fact]
    public void ParsesIntegerPercent()
    {
        Assert.True(SteamCmdOutput.TryParseProgress("Update state (0x61) downloading, progress: 100 (10 / 10)", out var progress));
        Assert.Equal(100, progress.Percent);
    }

    [Fact]
    public void ParsesSteamCmdBootstrapLine()
    {
        var line = "[ 47%] Downloading update (35,048 of 43,472 KB)...";

        Assert.True(SteamCmdOutput.TryParseProgress(line, out var progress));
        Assert.Equal("Downloading update", progress.State);
        Assert.Equal(47, progress.Percent);
        Assert.Equal(35_048L * 1024, progress.BytesDone);
        Assert.Equal(43_472L * 1024, progress.BytesTotal);
    }

    [Fact]
    public void IgnoresBootstrapLinesWithoutPercent()
    {
        Assert.False(SteamCmdOutput.TryParseProgress("[----] Extracting package...", out _));
    }

    [Theory]
    [InlineData("Success! App '343050' fully installed.")]
    [InlineData("Logging in user ... to Steam Public...")]
    [InlineData("")]
    [InlineData(null)]
    public void IgnoresEverythingElse(string? line)
    {
        Assert.False(SteamCmdOutput.TryParseProgress(line, out _));
    }

    [Fact]
    public void DescribeShowsBothNumbersWhenTotalIsKnown()
    {
        var progress = new SteamProgress("downloading", 35.2, 1L << 30, 4L << 30);

        Assert.True(progress.HasTotal);
        Assert.Equal("35.2%  1.0 ГБ / 4.0 ГБ", progress.Describe());
    }

    [Fact]
    public void DescribeDropsTotalWhenItIsUnknown()
    {
        var progress = new SteamProgress("steamcmd", 12.5, 1024, 0);

        Assert.False(progress.HasTotal);
        Assert.Equal("12.5%", progress.Describe());
    }

    [Theory]
    [InlineData(512L, "512 Б")]
    [InlineData(2048L, "2 КБ")]
    [InlineData(5L * 1024 * 1024, "5 МБ")]
    [InlineData(3221225472L, "3.0 ГБ")]
    public void FormatPicksReadableUnits(long bytes, string expected)
    {
        Assert.Equal(expected, SteamProgress.Format(bytes));
    }
}
