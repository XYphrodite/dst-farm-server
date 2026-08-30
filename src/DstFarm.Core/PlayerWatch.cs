using System.Text.RegularExpressions;

namespace DstFarm.Core;

public sealed record ConnectedPlayer(string Guid, string? UserId, string? Name);

public sealed record PlayerReport(IReadOnlyList<ConnectedPlayer> Players, bool LogFound)
{
    public int Count => Players.Count;

    public string Describe() => Players.Count == 0
        ? string.Empty
        : string.Join(", ", Players.Select(p => p.Name ?? p.UserId ?? p.Guid));
}

/// <summary>
/// Кто сейчас на сервере, по его собственному логу. Нужно потому, что сервер отключает
/// бездействующего игрока через полчаса, и заметить это, не переключаясь в игру, иначе нельзя.
/// </summary>
public static partial class PlayerWatch
{
    // Client authenticated: (KU_XXXXXXXX) Имя игрока
    [GeneratedRegex(@"Client authenticated:\s*\((?<userid>KU_\w+)\)\s*(?<name>.*)", RegexOptions.CultureInvariant)]
    private static partial Regex AuthenticatedPattern { get; }

    // [ClientObject] Initialized (authenticated) on server: guid=123 userid=KU_XXX netid=...
    [GeneratedRegex(@"\[ClientObject\] Initialized \(authenticated\) on server:\s*guid=(?<guid>\d+)\s*userid=(?<userid>\w*)", RegexOptions.CultureInvariant)]
    private static partial Regex JoinedPattern { get; }

    // Connection lost to 172.18.0.1|64864 <123>
    [GeneratedRegex(@"Connection lost to\s+\S+\s*<(?<guid>\d+)>", RegexOptions.CultureInvariant)]
    private static partial Regex LeftPattern { get; }

    public static PlayerReport Parse(string? logText)
    {
        var players = new Dictionary<string, ConnectedPlayer>(StringComparer.Ordinal);
        string? lastName = null;

        foreach (var line in (logText ?? string.Empty).Split('\n'))
        {
            var auth = AuthenticatedPattern.Match(line);
            if (auth.Success)
            {
                // Имя приходит строкой раньше, чем guid, поэтому запоминаем его до присоединения.
                lastName = auth.Groups["name"].Value.Trim();
                if (lastName.Length == 0)
                    lastName = null;
                continue;
            }

            var joined = JoinedPattern.Match(line);
            if (joined.Success)
            {
                var guid = joined.Groups["guid"].Value;
                var userId = joined.Groups["userid"].Value;
                players[guid] = new ConnectedPlayer(guid, userId.Length > 0 ? userId : null, lastName);
                lastName = null;
                continue;
            }

            var left = LeftPattern.Match(line);
            if (left.Success)
                players.Remove(left.Groups["guid"].Value);
        }

        return new PlayerReport([.. players.Values], LogFound: true);
    }

    public static PlayerReport Inspect(FarmConfig config, string shard = "Master")
    {
        ArgumentNullException.ThrowIfNull(config);

        var path = WorldProtections.LogPathFor(config, shard);
        try
        {
            if (!File.Exists(path))
                return new PlayerReport([], LogFound: false);

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }
        catch (IOException)
        {
            return new PlayerReport([], LogFound: false);
        }
    }
}
