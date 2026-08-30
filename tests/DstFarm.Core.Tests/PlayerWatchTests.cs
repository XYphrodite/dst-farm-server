using DstFarm.Core;
using Xunit;

namespace DstFarm.Core.Tests;

public sealed class PlayerWatchTests
{
    /// <summary>Куски настоящего лога сервера, включая объект самого сервера, который игроком не является.</summary>
    private const string ServerObject =
        "[00:00:09]: [ClientObject] Initialized (self/server object, locally trusted) on server: guid=3547479958546142412 userid=KU_HXdphSzB netid= admin=1\n";

    private const string Joined =
        "[00:06:34]: Client connected from [LAN] 172.18.0.1|64864 <8148246746928615330>\n" +
        "[00:06:35]: Client authenticated: (KU_HXdphSzB) School killed dota 2 tallent\n" +
        "[00:06:37]: [ClientObject] Initialized (authenticated) on server: guid=8148246746928615330 userid=KU_HXdphSzB netid=76561198115460594 admin=1\n";

    private const string Left =
        "[00:18:23]: Connection lost to 172.18.0.1|64864 <8148246746928615330>\n";

    [Fact]
    public void SeesAConnectedPlayerWithTheirName()
    {
        var report = PlayerWatch.Parse(ServerObject + Joined);

        var player = Assert.Single(report.Players);
        Assert.Equal("School killed dota 2 tallent", player.Name);
        Assert.Equal("KU_HXdphSzB", player.UserId);
        Assert.Equal("School killed dota 2 tallent", report.Describe());
    }

    /// <summary>Сервер заводит свой собственный ClientObject — считать его игроком нельзя.</summary>
    [Fact]
    public void DoesNotCountTheServerItself()
    {
        Assert.Equal(0, PlayerWatch.Parse(ServerObject).Count);
    }

    [Fact]
    public void ForgetsThePlayerAfterDisconnect()
    {
        var report = PlayerWatch.Parse(ServerObject + Joined + Left);

        Assert.Equal(0, report.Count);
        Assert.Equal(string.Empty, report.Describe());
    }

    [Fact]
    public void SurvivesReconnects()
    {
        var report = PlayerWatch.Parse(ServerObject + Joined + Left + Joined);

        Assert.Equal(1, report.Count);
    }

    [Fact]
    public void CountsSeveralPlayersSeparately()
    {
        var second =
            "[00:07:00]: Client authenticated: (KU_Second00) Второй игрок\n" +
            "[00:07:01]: [ClientObject] Initialized (authenticated) on server: guid=999 userid=KU_Second00 netid=1 admin=0\n";

        var report = PlayerWatch.Parse(ServerObject + Joined + second);

        Assert.Equal(2, report.Count);
        Assert.Contains(report.Players, p => p.Name == "Второй игрок");
        Assert.Contains(report.Players, p => p.Name == "School killed dota 2 tallent");
    }

    [Fact]
    public void DisconnectOfOneDoesNotDropTheOther()
    {
        var second =
            "[00:07:00]: Client authenticated: (KU_Second00) Второй игрок\n" +
            "[00:07:01]: [ClientObject] Initialized (authenticated) on server: guid=999 userid=KU_Second00 netid=1 admin=0\n";

        var report = PlayerWatch.Parse(ServerObject + Joined + second + Left);

        var player = Assert.Single(report.Players);
        Assert.Equal("Второй игрок", player.Name);
    }

    [Fact]
    public void EmptyLogMeansNobody()
    {
        Assert.Equal(0, PlayerWatch.Parse(string.Empty).Count);
        Assert.Equal(0, PlayerWatch.Parse(null).Count);
    }

    [Fact]
    public void SaysNothingWhenTheServerHasNeverRun()
    {
        using var temp = new TempDirectory();
        var config = new FarmConfig { ConfDirectory = temp.Path, Cluster = "T" };

        var report = PlayerWatch.Inspect(config);

        Assert.False(report.LogFound);
        Assert.Equal(0, report.Count);
    }

    [Fact]
    public void ReadsALogTheServerStillHoldsOpen()
    {
        using var temp = new TempDirectory();
        var config = new FarmConfig { ConfDirectory = temp.Path, Cluster = "T" };
        Directory.CreateDirectory(Path.Combine(config.ClusterPath, "Master"));

        using var writer = new StreamWriter(new FileStream(
            WorldProtections.LogPathFor(config),
            FileMode.Create,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete)) { AutoFlush = true };
        writer.Write(ServerObject + Joined);

        var report = PlayerWatch.Inspect(config);

        Assert.True(report.LogFound);
        Assert.Equal(1, report.Count);
    }
}
