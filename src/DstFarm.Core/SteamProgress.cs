using System.Globalization;
using System.Text.RegularExpressions;

namespace DstFarm.Core;

/// <summary>Состояние загрузки: сколько процентов и байт из скольких.</summary>
public sealed record SteamProgress(string State, double Percent, long BytesDone, long BytesTotal)
{
    /// <summary>Есть ли осмысленный знаменатель — иначе показывать «X из ?» нечестно.</summary>
    public bool HasTotal => BytesTotal > 0;

    public string Describe() => HasTotal
        ? string.Create(CultureInfo.InvariantCulture, $"{Percent:F1}%  {Format(BytesDone)} / {Format(BytesTotal)}")
        : string.Create(CultureInfo.InvariantCulture, $"{Percent:F1}%");

    public static string Format(long bytes)
    {
        if (bytes >= 1L << 30)
            return string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 30):F1} {Loc.T("ГБ", "GB")}");
        if (bytes >= 1L << 20)
            return string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 20):F0} {Loc.T("МБ", "MB")}");
        if (bytes >= 1024)
            return string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:F0} {Loc.T("КБ", "KB")}");
        return string.Create(CultureInfo.InvariantCulture, $"{bytes} {Loc.T("Б", "B")}");
    }
}

/// <summary>Разбор строк прогресса, которые steamcmd печатает в stdout.</summary>
public static partial class SteamCmdOutput
{
    // Update state (0x61) downloading, progress: 35.24 (1568432128 / 4448147036)
    [GeneratedRegex(
        @"Update state \((?<code>0x[0-9a-fA-F]+)\)\s*(?<state>[^,]+),\s*progress:\s*(?<percent>[0-9]+(?:\.[0-9]+)?)\s*\((?<done>[0-9]+)\s*/\s*(?<total>[0-9]+)\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex ProgressPattern { get; }

    // [ 47%] Downloading update (35,048 of 43,472 KB)...
    [GeneratedRegex(
        @"\[\s*(?<percent>[0-9]+)%\]\s*(?<state>[^(]+?)\s*\((?<done>[0-9,]+) of (?<total>[0-9,]+) KB\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex BootstrapPattern { get; }

    public static bool TryParseProgress(string? line, out SteamProgress progress)
    {
        progress = new SteamProgress(string.Empty, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var match = ProgressPattern.Match(line);
        if (match.Success)
        {
            progress = new SteamProgress(
                match.Groups["state"].Value.Trim(),
                double.Parse(match.Groups["percent"].Value, CultureInfo.InvariantCulture),
                long.Parse(match.Groups["done"].Value, CultureInfo.InvariantCulture),
                long.Parse(match.Groups["total"].Value, CultureInfo.InvariantCulture));
            return true;
        }

        // Обновление самого steamcmd печатается в другом формате и в килобайтах.
        match = BootstrapPattern.Match(line);
        if (!match.Success)
            return false;

        progress = new SteamProgress(
            match.Groups["state"].Value.Trim(),
            double.Parse(match.Groups["percent"].Value, CultureInfo.InvariantCulture),
            ParseKilobytes(match.Groups["done"].Value),
            ParseKilobytes(match.Groups["total"].Value));
        return true;
    }

    private static long ParseKilobytes(string value) =>
        long.Parse(value.Replace(",", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture) * 1024;
}
