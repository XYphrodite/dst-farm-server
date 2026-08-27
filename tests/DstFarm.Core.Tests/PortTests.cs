using DstFarm.Core;
using Xunit;

namespace DstFarm.Core.Tests;

public sealed class PortTests
{
    [Fact]
    public void ServerIniUsesConfiguredSteamPorts()
    {
        var config = new FarmConfig { MasterServerPort = 27030, AuthenticationPort = 8790 };

        var master = ClusterWriter.BuildServerIni(config, caves: false);
        var caves = ClusterWriter.BuildServerIni(config, caves: true);

        Assert.Contains("master_server_port = 27030", master, StringComparison.Ordinal);
        Assert.Contains("authentication_port = 8790", master, StringComparison.Ordinal);
        Assert.Contains("master_server_port = 27031", caves, StringComparison.Ordinal);
        Assert.Contains("authentication_port = 8791", caves, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultSteamPortsMatchKleiDefaults()
    {
        var master = ClusterWriter.BuildServerIni(new FarmConfig(), caves: false);

        Assert.Contains("master_server_port = 27018", master, StringComparison.Ordinal);
        Assert.Contains("authentication_port = 8768", master, StringComparison.Ordinal);
    }

    [Fact]
    public void InspectCoversEveryPortTheServerBinds()
    {
        var config = new FarmConfig { ServerPort = 10999, MasterServerPort = 27018, AuthenticationPort = 8768 };

        var ports = PortProbe.Inspect(config).Select(p => p.Port).ToList();

        Assert.Equal([10999, 27018, 8768], ports);
    }

    [Fact]
    public void InspectAddsCavesPortsWhenSecondShardEnabled()
    {
        var config = new FarmConfig { EnableCaves = true, ServerPort = 10999 };

        var ports = PortProbe.Inspect(config).Select(p => p.Port).ToList();

        Assert.Equal([10999, 27018, 8768, 11000, 27019, 8769], ports);
    }

    [Fact]
    public void BusyPortIsReportedAsConflict()
    {
        using var socket = new System.Net.Sockets.UdpClient(0);
        var taken = ((System.Net.IPEndPoint)socket.Client.LocalEndPoint!).Port;
        var config = new FarmConfig { ServerPort = taken };

        var conflicts = PortProbe.Conflicts(config);

        Assert.Contains(conflicts, c => c.Port == taken);
    }
}
