using DstFarm.Core;
using Xunit;

namespace DstFarm.Core.Tests;

public sealed class SelfUpdaterTests
{
    [Theory]
    [InlineData("v0.1.3", "0.1.3")]
    [InlineData("0.1.3", "0.1.3")]
    [InlineData("V1.2.10", "1.2.10")]
    [InlineData("v2.0", "2.0.0")]
    public void ParsesReleaseTags(string tag, string expected)
    {
        Assert.Equal(Version.Parse(expected), SelfUpdater.ParseTag(tag));
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("v")]
    public void RejectsTagsThatAreNotVersions(string? tag)
    {
        Assert.Null(SelfUpdater.ParseTag(tag));
    }

    [Fact]
    public void ExtractsChecksumFromReleaseNotes()
    {
        var notes = "Правки.\n\nSHA-256 `dstfarm.exe`: `C0422477912C07ECCD57B2DE564788714DABEFABCFAC479BC9FD4BB30DC13C8B`";

        Assert.Equal(
            "c0422477912c07eccd57b2de564788714dabefabcfac479bc9fd4bb30dc13c8b",
            SelfUpdater.ExtractSha256(notes));
    }

    [Theory]
    [InlineData("релиз без контрольной суммы")]
    [InlineData("SHA-256: слишком коротко abc123")]
    [InlineData(null)]
    public void ReturnsNullWhenNotesCarryNoChecksum(string? notes)
    {
        Assert.Null(SelfUpdater.ExtractSha256(notes));
    }

    [Fact]
    public void ApplyMovesOldBinaryAsideAndPutsNewOneInPlace()
    {
        using var temp = new TempDirectory();
        var target = Path.Combine(temp.Path, "dstfarm.exe");
        var incoming = Path.Combine(temp.Path, "downloaded.exe");
        File.WriteAllText(target, "старая версия");
        File.WriteAllText(incoming, "новая версия");

        var backup = SelfUpdater.Apply(incoming, target);

        Assert.Equal("новая версия", File.ReadAllText(target));
        Assert.Equal("старая версия", File.ReadAllText(backup));
        Assert.Equal(SelfUpdater.BackupPathFor(target), backup);
        Assert.False(File.Exists(incoming));
    }

    [Fact]
    public void ApplyOverwritesLeftoverBackupFromPreviousUpdate()
    {
        using var temp = new TempDirectory();
        var target = Path.Combine(temp.Path, "dstfarm.exe");
        var incoming = Path.Combine(temp.Path, "downloaded.exe");
        File.WriteAllText(target, "версия 2");
        File.WriteAllText(incoming, "версия 3");
        File.WriteAllText(SelfUpdater.BackupPathFor(target), "версия 1");

        var backup = SelfUpdater.Apply(incoming, target);

        Assert.Equal("версия 3", File.ReadAllText(target));
        Assert.Equal("версия 2", File.ReadAllText(backup));
    }

    [Fact]
    public void ApplyWorksWhenTargetIsMissing()
    {
        using var temp = new TempDirectory();
        var target = Path.Combine(temp.Path, "dstfarm.exe");
        var incoming = Path.Combine(temp.Path, "downloaded.exe");
        File.WriteAllText(incoming, "первая установка");

        SelfUpdater.Apply(incoming, target);

        Assert.Equal("первая установка", File.ReadAllText(target));
        Assert.False(File.Exists(SelfUpdater.BackupPathFor(target)));
    }

    [Fact]
    public void CleanupRemovesLeftoverBackup()
    {
        using var temp = new TempDirectory();
        var target = Path.Combine(temp.Path, "dstfarm.exe");
        File.WriteAllText(SelfUpdater.BackupPathFor(target), "старьё");

        SelfUpdater.CleanupBackup(target);

        Assert.False(File.Exists(SelfUpdater.BackupPathFor(target)));
    }

    [Fact]
    public void CleanupIsSilentWhenThereIsNothingToRemove()
    {
        using var temp = new TempDirectory();

        SelfUpdater.CleanupBackup(Path.Combine(temp.Path, "dstfarm.exe"));
    }

    [Fact]
    public async Task ComputesSha256OfFile()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "data.bin");
        await File.WriteAllTextAsync(file, "dstfarm");

        var hash = await SelfUpdater.ComputeSha256Async(file, CancellationToken.None);

        Assert.Equal(64, hash.Length);
        Assert.Equal(hash.ToLowerInvariant(), hash);
    }
}
