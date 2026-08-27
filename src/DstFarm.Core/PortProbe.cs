using System.Net.NetworkInformation;

namespace DstFarm.Core;

public sealed record PortUsage(int Port, string Purpose, bool Busy);

/// <summary>
/// Проверяет UDP-порты сервера. Когда клиент игры и Steam живут на той же машине,
/// они занимают часть диапазона 27015-27050, и сервер молча не поднимается.
/// </summary>
public static class PortProbe
{
    public static IReadOnlyList<PortUsage> Inspect(FarmConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var busy = ActiveUdpPorts();
        var planned = new List<(int Port, string Purpose)>
        {
            (config.ServerPort, "мир (Master)"),
            (config.MasterServerPort, "steam master (Master)"),
            (config.AuthenticationPort, "steam auth (Master)"),
        };

        if (config.EnableCaves)
        {
            planned.Add((config.ServerPort + 1, "мир (Caves)"));
            planned.Add((config.MasterServerPort + 1, "steam master (Caves)"));
            planned.Add((config.AuthenticationPort + 1, "steam auth (Caves)"));
        }

        return [.. planned.Select(p => new PortUsage(p.Port, p.Purpose, busy.Contains(p.Port)))];
    }

    /// <summary>Занятые порты, из-за которых сервер не стартует.</summary>
    public static IReadOnlyList<PortUsage> Conflicts(FarmConfig config) =>
        [.. Inspect(config).Where(p => p.Busy)];

    private static HashSet<int> ActiveUdpPorts()
    {
        try
        {
            return [.. IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Select(e => e.Port)];
        }
        catch (NetworkInformationException)
        {
            // Диагностика не критична: если снять список не вышло, считаем порты свободными.
            return [];
        }
    }
}
