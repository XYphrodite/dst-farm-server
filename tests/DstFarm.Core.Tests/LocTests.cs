using System.Globalization;
using DstFarm.Core;
using Xunit;

namespace DstFarm.Core.Tests;

[Collection(nameof(LocTests))]
[CollectionDefinition(nameof(LocTests), DisableParallelization = true)]
public sealed class LocTests : IDisposable
{
    private readonly Language original = Loc.Current;
    private readonly CultureInfo originalCulture = CultureInfo.CurrentUICulture;

    public void Dispose()
    {
        Loc.Current = original;
        CultureInfo.CurrentUICulture = originalCulture;
    }

    [Theory]
    [InlineData("ru", Language.Russian)]
    [InlineData("RU", Language.Russian)]
    [InlineData("russian", Language.Russian)]
    [InlineData("en", Language.English)]
    [InlineData("English", Language.English)]
    public void ResolvesExplicitSetting(string setting, Language expected)
    {
        Assert.Equal(expected, Loc.Resolve(setting));
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("кто-то вписал ерунду")]
    public void FallsBackToSystemLanguage(string? setting)
    {
        CultureInfo.CurrentUICulture = new CultureInfo("ru-RU");
        Assert.Equal(Language.Russian, Loc.Resolve(setting));

        CultureInfo.CurrentUICulture = new CultureInfo("de-DE");
        Assert.Equal(Language.English, Loc.Resolve(setting));
    }

    [Fact]
    public void EnglishIsTheDefaultForAnyNonRussianSystem()
    {
        foreach (var culture in new[] { "en-US", "de-DE", "fr-FR", "ja-JP", "uk-UA" })
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            Assert.Equal(Language.English, Loc.Detect());
        }
    }

    [Fact]
    public void RussianSystemGetsRussian()
    {
        foreach (var culture in new[] { "ru-RU", "ru" })
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            Assert.Equal(Language.Russian, Loc.Detect());
        }
    }

    [Fact]
    public void PicksTheStringForTheCurrentLanguage()
    {
        Loc.Current = Language.Russian;
        Assert.Equal("сервер", Loc.T("сервер", "server"));

        Loc.Current = Language.English;
        Assert.Equal("server", Loc.T("сервер", "server"));
    }

    [Fact]
    public void ByteSizesFollowTheLanguage()
    {
        Loc.Current = Language.Russian;
        Assert.Equal("3.0 ГБ", SteamProgress.Format(3L << 30));

        Loc.Current = Language.English;
        Assert.Equal("3.0 GB", SteamProgress.Format(3L << 30));
    }

    [Fact]
    public void PortPurposesFollowTheLanguage()
    {
        var config = new FarmConfig();

        Loc.Current = Language.Russian;
        Assert.Equal("мир (Master)", PortProbe.Inspect(config)[0].Purpose);

        Loc.Current = Language.English;
        Assert.Equal("world (Master)", PortProbe.Inspect(config)[0].Purpose);
    }

    [Fact]
    public void NamesAreTheShortCodes()
    {
        Assert.Equal("ru", Loc.Name(Language.Russian));
        Assert.Equal("en", Loc.Name(Language.English));
    }

    [Fact]
    public void ConfigDefaultsToAuto()
    {
        Assert.Equal(Loc.Auto, new FarmConfig().Language);
    }
}
