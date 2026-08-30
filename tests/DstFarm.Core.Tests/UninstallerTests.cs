using DstFarm.Core;
using Xunit;

namespace DstFarm.Core.Tests;

public sealed class UninstallerTests
{
    private const char Sep = ';';

    [Fact]
    public void DropsOurDirectoryAndKeepsTheRest()
    {
        var path = string.Join(Sep, [@"C:\Windows", @"C:\Users\me\AppData\Local\Programs\dstfarm", @"C:\Git\bin"]);

        var cleaned = Uninstaller.RemoveFromPath(path, @"C:\Users\me\AppData\Local\Programs\dstfarm");

        Assert.Equal(string.Join(Sep, [@"C:\Windows", @"C:\Git\bin"]), cleaned);
    }

    [Fact]
    public void IgnoresCaseAndATrailingSlash()
    {
        var path = string.Join(Sep, [@"C:\Windows", @"C:\Programs\DSTFARM\"]);

        Assert.Equal(@"C:\Windows", Uninstaller.RemoveFromPath(path, @"c:\programs\dstfarm"));
    }

    [Fact]
    public void LeavesPathAloneWhenWeAreNotInIt()
    {
        var path = string.Join(Sep, [@"C:\Windows", @"C:\Git\bin"]);

        Assert.Equal(path, Uninstaller.RemoveFromPath(path, @"C:\Programs\dstfarm"));
    }

    [Fact]
    public void SurvivesEmptyAndMissingPath()
    {
        Assert.Equal(string.Empty, Uninstaller.RemoveFromPath(null, @"C:\dstfarm"));
        Assert.Equal(string.Empty, Uninstaller.RemoveFromPath(string.Empty, @"C:\dstfarm"));
    }

    [Fact]
    public void DropsEmptyEntriesLeftBehind()
    {
        var path = @"C:\Windows;;C:\dstfarm;";

        Assert.Equal(@"C:\Windows", Uninstaller.RemoveFromPath(path, @"C:\dstfarm"));
    }

    [Fact]
    public void RemovesEveryCopyOfTheEntry()
    {
        var path = string.Join(Sep, [@"C:\dstfarm", @"C:\Windows", @"C:\dstfarm\"]);

        Assert.Equal(@"C:\Windows", Uninstaller.RemoveFromPath(path, @"C:\dstfarm"));
    }

    /// <summary>Мир и токен уходят только по явной просьбе.</summary>
    [Fact]
    public void KeepsTheWorldUnlessAskedOtherwise()
    {
        using var temp = new TempDirectory();
        var config = new FarmConfig { Root = Path.Combine(temp.Path, "runtime"), ConfDirectory = Path.Combine(temp.Path, "klei"), Cluster = "T" };
        Directory.CreateDirectory(config.RootPath);
        File.WriteAllText(Path.Combine(config.RootPath, "state.bin"), "x");
        ClusterWriter.Write(config, overwrite: true);

        var withoutCluster = Uninstaller.Plan(config, includeCluster: false);
        var withCluster = Uninstaller.Plan(config, includeCluster: true);

        Assert.DoesNotContain(withoutCluster, t => t.Path == config.ClusterPath);
        Assert.Contains(withCluster, t => t.Path == config.ClusterPath);
    }

    [Fact]
    public void RemovesWhatItPlanned()
    {
        using var temp = new TempDirectory();
        var config = new FarmConfig { Root = Path.Combine(temp.Path, "runtime"), ConfDirectory = Path.Combine(temp.Path, "klei"), Cluster = "T" };
        Directory.CreateDirectory(config.RootPath);
        File.WriteAllText(Path.Combine(config.RootPath, "state.bin"), "x");
        ClusterWriter.Write(config, overwrite: true);

        Uninstaller.RemovePlanned(config, includeCluster: true);

        Assert.False(Directory.Exists(config.RootPath));
        Assert.False(Directory.Exists(config.ClusterPath));
    }

    [Fact]
    public void PlanningACleanMachineFindsNothing()
    {
        using var temp = new TempDirectory();
        var config = new FarmConfig { Root = Path.Combine(temp.Path, "runtime"), ConfDirectory = Path.Combine(temp.Path, "klei") };

        Assert.DoesNotContain(Uninstaller.Plan(config, includeCluster: true), t => t.Path.StartsWith(temp.Path, StringComparison.Ordinal));
    }
}
