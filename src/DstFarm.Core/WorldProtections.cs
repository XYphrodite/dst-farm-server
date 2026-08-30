using System.Text.RegularExpressions;

namespace DstFarm.Core;

public sealed record ProtectionCheck(string Key, string Expected, string? Actual)
{
    public bool Applied => string.Equals(Expected, Actual, StringComparison.Ordinal);
}

public sealed record ProtectionReport(IReadOnlyList<ProtectionCheck> Checks, bool LogFound, int Observed = 0)
{
    /// <summary>Ни одного оверрайда в логе: мир, похоже, вообще не создавался.</summary>
    public bool NothingObserved => LogFound && Observed == 0;

    public int Applied => Checks.Count(c => c.Applied);

    public int Total => Checks.Count;

    public IReadOnlyList<ProtectionCheck> Missing => [.. Checks.Where(c => !c.Applied)];

    public bool AllApplied => LogFound && Total > 0 && Applied == Total;
}

/// <summary>
/// Что из настроек мир принял на самом деле. Настройки вшиваются в мир при генерации,
/// поэтому файл на диске и работающий мир легко расходятся — а по логу видно правду.
/// </summary>
public static partial class WorldProtections
{
    /// <summary>Игра печатает: OVERRIDE: setting\tимя\tto\tзначение.</summary>
    [GeneratedRegex(@"OVERRIDE: setting\s+(?<key>[a-z_0-9]+)\s+to\s+(?<value>[a-z_0-9]+)", RegexOptions.CultureInvariant)]
    private static partial Regex OverridePattern { get; }

    /// <summary>
    /// Настройки только для генерации: в игровом мире они не применяются заново
    /// и в логе не печатаются, поэтому сверять по ним нечего. Список берётся у того же
    /// ClusterWriter, что их и пишет.
    /// </summary>
    private static IReadOnlyList<string> GenerationOnly => ClusterWriter.GenerationOnlyKeys;

    /// <summary>Лог шарда сервер переписывает при каждом старте, поэтому там всегда текущая сессия.</summary>
    public static string LogPathFor(FarmConfig config, string shard = "Master")
    {
        ArgumentNullException.ThrowIfNull(config);
        return Path.Combine(config.ClusterPath, shard, "server_log.txt");
    }

    public static IReadOnlyDictionary<string, string> ParseApplied(string logText)
    {
        var applied = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in OverridePattern.Matches(logText ?? string.Empty))
            applied[match.Groups["key"].Value] = match.Groups["value"].Value;
        return applied;
    }

    public static ProtectionReport Inspect(FarmConfig config, string shard = "Master")
    {
        ArgumentNullException.ThrowIfNull(config);

        var path = LogPathFor(config, shard);
        string text;
        try
        {
            if (!File.Exists(path))
                return new ProtectionReport([], LogFound: false);

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            text = reader.ReadToEnd();
        }
        catch (IOException)
        {
            return new ProtectionReport([], LogFound: false);
        }

        var applied = ParseApplied(text);
        return Evaluate(config, applied, shard == "Caves");
    }

    public static ProtectionReport Evaluate(FarmConfig config, IReadOnlyDictionary<string, string> applied, bool caves = false)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(applied);

        var checks = new List<ProtectionCheck>();
        foreach (var (key, value) in ClusterWriter.BuildOverrides(config, caves))
        {
            if (GenerationOnly.Contains(key, StringComparer.Ordinal))
                continue;

            // Значение default игра не печатает: оно и так стоит по умолчанию.
            if (string.Equals(value, "default", StringComparison.Ordinal))
                continue;

            checks.Add(new ProtectionCheck(key, value, applied.GetValueOrDefault(key)));
        }

        return new ProtectionReport(checks, LogFound: true, Observed: applied.Count);
    }
}
