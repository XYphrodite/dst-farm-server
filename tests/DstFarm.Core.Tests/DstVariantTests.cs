using DstFarm.Core;
using SelfUpdateKit;
using Xunit;

namespace DstFarm.Core.Tests;

public sealed class DstVariantTests
{
    private const string FullHash = "830884397b6faf92b6314ede36926da491eb0296ab2786bb4482c27c81c01eb9";
    private const string LightHash = "1111111111111111111111111111111111111111111111111111111111111111";

    private const string Notes = """
        ### Check

        SHA-256 `dstfarm.exe`:

        ```
        830884397b6faf92b6314ede36926da491eb0296ab2786bb4482c27c81c01eb9
        ```

        SHA-256 `dstfarm-light.exe`:

        ```
        1111111111111111111111111111111111111111111111111111111111111111
        ```
        """;

    [Theory]
    [InlineData("Microsoft.NETCore.App 10.0.10 [C:\\Program Files\\dotnet\\shared\\Microsoft.NETCore.App]")]
    [InlineData("Microsoft.WindowsDesktop.App 10.0.5 [C:\\Program Files\\dotnet\\shared\\Microsoft.WindowsDesktop.App]")]
    public void RuntimeLineAcceptsBaseOrDesktopRuntimeOfCurrentMajor(string line)
    {
        Assert.True(DstFarmUpdate.IsRuntimeLine(line));
    }

    [Theory]
    [InlineData("Microsoft.NETCore.App 9.0.1 [C:\\Program Files\\dotnet\\shared\\Microsoft.NETCore.App]")]
    [InlineData("Microsoft.NETCore.App 11.0.0 [C:\\Program Files\\dotnet\\shared\\Microsoft.NETCore.App]")]
    [InlineData("Microsoft.WindowsDesktop.App 9.0.1 [C:\\Program Files\\dotnet\\shared\\Microsoft.WindowsDesktop.App]")]
    [InlineData("Microsoft.AspNetCore.App 10.0.10 [C:\\Program Files\\dotnet\\shared\\Microsoft.AspNetCore.App]")]
    [InlineData("Microsoft.NETCore.App")]
    [InlineData("not a runtime line")]
    [InlineData("")]
    [InlineData(null)]
    public void RuntimeLineRejectsOtherMajorsAndGarbage(string? line)
    {
        Assert.False(DstFarmUpdate.IsRuntimeLine(line));
    }

    [Fact]
    public void FullOptionsSelectFullAssetWithNotesChecksum()
    {
        var options = DstFarmUpdate.Options(DstFarmUpdate.Variant.Full);

        Assert.Equal([DstFarmUpdate.FullAssetName], options.ExecutableAssetNames);
        Assert.Equal("dstfarm.exe", DstFarmUpdate.AssetName(DstFarmUpdate.Variant.Full));
        Assert.Equal(ChecksumMode.ReleaseNotes, options.ChecksumMode);
    }

    [Fact]
    public void LightOptionsSelectLightAssetWithNotesChecksum()
    {
        var options = DstFarmUpdate.Options(DstFarmUpdate.Variant.Light);

        Assert.Equal([DstFarmUpdate.LightAssetName], options.ExecutableAssetNames);
        Assert.Equal("dstfarm-light.exe", DstFarmUpdate.AssetName(DstFarmUpdate.Variant.Light));
        Assert.Equal(ChecksumMode.ReleaseNotes, options.ChecksumMode);
    }

    [Fact]
    public void InstalledVariantDefaultsToFullWithoutMarker()
    {
        var directory = Directory.CreateTempSubdirectory("dstfarm-variant-").FullName;
        try
        {
            Assert.Equal(
                DstFarmUpdate.Variant.Full,
                DstFarmUpdate.InstalledVariant(Path.Combine(directory, "dstfarm.exe")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("light")]
    [InlineData(" light \n")]
    [InlineData("LIGHT")]
    public void InstalledVariantReadsLightMarker(string marker)
    {
        var directory = Directory.CreateTempSubdirectory("dstfarm-variant-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, ".dstfarm-variant"), marker);

            Assert.Equal(
                DstFarmUpdate.Variant.Light,
                DstFarmUpdate.InstalledVariant(Path.Combine(directory, "dstfarm.exe")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("full")]
    [InlineData("self-contained")]
    [InlineData("")]
    [InlineData(null)]
    public void InstalledVariantTreatsMissingOrOtherMarkersAsFull(string? processPathOrMarker)
    {
        Assert.Equal(DstFarmUpdate.Variant.Full, DstFarmUpdate.InstalledVariant(null));
        Assert.Equal(DstFarmUpdate.Variant.Full, DstFarmUpdate.InstalledVariant(""));

        if (processPathOrMarker is null)
            return;

        var directory = Directory.CreateTempSubdirectory("dstfarm-variant-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, ".dstfarm-variant"), processPathOrMarker);

            Assert.Equal(
                DstFarmUpdate.Variant.Full,
                DstFarmUpdate.InstalledVariant(Path.Combine(directory, "dstfarm.exe")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ChecksumNotesScopeToFullBlock()
    {
        var scoped = DstFarmUpdate.SelectChecksumNotes(Notes, "dstfarm.exe");

        Assert.Equal(FullHash, ReleaseChecksum.ExtractFromNotes(scoped));
    }

    [Fact]
    public void ChecksumNotesScopeToLightBlock()
    {
        var scoped = DstFarmUpdate.SelectChecksumNotes(Notes, "dstfarm-light.exe");

        Assert.Equal(LightHash, ReleaseChecksum.ExtractFromNotes(scoped));
    }

    [Fact]
    public void ChecksumNotesRefuseAnAssetWithoutBlock()
    {
        Assert.Throws<InvalidDataException>(() => DstFarmUpdate.SelectChecksumNotes(Notes, "dstfarm-arm64.exe"));
        Assert.Throws<InvalidDataException>(() => DstFarmUpdate.SelectChecksumNotes(null, "dstfarm.exe"));
    }
}
