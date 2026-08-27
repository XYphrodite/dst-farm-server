using System.Globalization;
using System.Text;

namespace DstFarm.Core;

/// <summary>Генерация каталога кластера DST: cluster.ini, шарды, worldgenoverride.lua.</summary>
public static class ClusterWriter
{
    /// <summary>Оверрайды, снимающие всё, что убивает AFK-персонажа или грузит машину.</summary>
    private static readonly (string Key, string Value)[] ThreatOverrides =
    [
        ("hounds", "never"),
        ("hunt", "never"),
        ("deerclops", "never"),
        ("bearger", "never"),
        ("dragonfly", "never"),
        ("beequeen", "never"),
        ("klaus", "never"),
        ("malbatross", "never"),
        ("toadstool", "never"),
        ("antliontribute", "never"),
        ("liefs", "never"),
        ("lightning", "never"),
        ("earthquakes", "never"),
        ("wildfires", "never"),
        ("frograin", "never"),
    ];

    public static IReadOnlyList<string> Write(FarmConfig config, bool overwrite, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var written = new List<string>();
        Directory.CreateDirectory(config.ClusterPath);

        Put(Path.Combine(config.ClusterPath, "cluster.ini"), BuildClusterIni(config), overwrite, written, log);
        if (!string.IsNullOrWhiteSpace(config.ClusterToken))
            Put(config.ClusterTokenFile, config.ClusterToken.Trim() + Environment.NewLine, true, written, log);

        foreach (var shard in config.Shards)
        {
            var isCaves = shard == "Caves";
            var directory = Path.Combine(config.ClusterPath, shard);
            Directory.CreateDirectory(directory);
            Put(Path.Combine(directory, "server.ini"), BuildServerIni(config, isCaves), overwrite, written, log);
            Put(Path.Combine(directory, "worldgenoverride.lua"), BuildWorldGen(config, isCaves), overwrite, written, log);
            Put(Path.Combine(directory, "modoverrides.lua"), "return {" + Environment.NewLine + "}" + Environment.NewLine, false, written, log);
        }

        return written;
    }

    internal static string BuildClusterIni(FarmConfig config)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[GAMEPLAY]");
        builder.AppendLine(CultureInfo.InvariantCulture, $"game_mode = {config.GameMode}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"max_players = {config.MaxPlayers}");
        builder.AppendLine("pvp = false");
        builder.AppendLine("pause_when_empty = false");
        builder.AppendLine("vote_enabled = false");
        builder.AppendLine();
        builder.AppendLine("[NETWORK]");
        builder.AppendLine(CultureInfo.InvariantCulture, $"cluster_name = {config.ClusterName}");
        builder.AppendLine("cluster_description = idle uptime server");
        builder.AppendLine(CultureInfo.InvariantCulture, $"cluster_password = {config.ClusterPassword}");
        builder.AppendLine("cluster_intention = cooperative");
        builder.AppendLine("lan_only_cluster = false");
        // Офлайн-кластер не даёт дропов Klei — принудительно false.
        builder.AppendLine("offline_cluster = false");
        builder.AppendLine("tick_rate = 15");
        builder.AppendLine("whitelist_slots = 0");
        builder.AppendLine();
        builder.AppendLine("[MISC]");
        builder.AppendLine("console_enabled = true");
        builder.AppendLine("autosaver_enabled = true");
        builder.AppendLine("max_snapshots = 6");
        builder.AppendLine();
        builder.AppendLine("[SHARD]");
        builder.AppendLine(CultureInfo.InvariantCulture, $"shard_enabled = {Lua(config.EnableCaves)}");
        builder.AppendLine("bind_ip = 127.0.0.1");
        builder.AppendLine("master_ip = 127.0.0.1");
        builder.AppendLine("master_port = 10888");
        builder.AppendLine(CultureInfo.InvariantCulture, $"cluster_key = {config.Cluster}Key");
        builder.AppendLine();
        builder.AppendLine("[STEAM]");
        builder.AppendLine("steam_group_only = false");
        builder.AppendLine("steam_group_id = 0");
        builder.AppendLine("steam_group_admins = false");
        return builder.ToString();
    }

    internal static string BuildServerIni(FarmConfig config, bool caves)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[NETWORK]");
        builder.AppendLine(CultureInfo.InvariantCulture, $"server_port = {(caves ? config.ServerPort + 1 : config.ServerPort)}");
        builder.AppendLine();
        builder.AppendLine("[SHARD]");
        builder.AppendLine(CultureInfo.InvariantCulture, $"is_master = {Lua(!caves)}");
        if (caves)
            builder.AppendLine("name = Caves");
        builder.AppendLine();
        builder.AppendLine("[STEAM]");
        builder.AppendLine(CultureInfo.InvariantCulture, $"master_server_port = {(caves ? 27019 : 27018)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"authentication_port = {(caves ? 8769 : 8768)}");
        builder.AppendLine();
        builder.AppendLine("[ACCOUNT]");
        builder.AppendLine("encode_user_path = true");
        return builder.ToString();
    }

    internal static string BuildWorldGen(FarmConfig config, bool caves)
    {
        var overrides = new List<(string Key, string Value)> { ("world_size", config.WorldSize) };

        if (config.NoHunger)
            overrides.Add(("hunger", "none"));
        if (config.NoSanityDrain)
            overrides.Add(("sanity", "none"));

        if (!caves)
        {
            if (config.OnlyDay)
                overrides.Add(("day", "onlyday"));
            if (config.EternalAutumn)
            {
                overrides.Add(("season_start", "autumn"));
                overrides.Add(("autumn", "verylongseason"));
                overrides.Add(("winter", "noseason"));
                overrides.Add(("spring", "noseason"));
                overrides.Add(("summer", "noseason"));
            }

            if (config.DisableThreats)
                overrides.AddRange(ThreatOverrides);
        }
        else if (config.DisableThreats)
        {
            overrides.Add(("earthquakes", "never"));
        }

        var builder = new StringBuilder();
        builder.AppendLine("return {");
        builder.AppendLine("  override_enabled = true,");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  preset = \"{(caves ? "DST_CAVE" : "SURVIVAL_TOGETHER")}\",");
        builder.AppendLine("  overrides = {");
        foreach (var (key, value) in overrides.OrderBy(o => o.Key, StringComparer.Ordinal))
            builder.AppendLine(CultureInfo.InvariantCulture, $"    {key} = \"{value}\",");
        builder.AppendLine("  },");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string Lua(bool value) => value ? "true" : "false";

    private static void Put(string path, string content, bool overwrite, List<string> written, Action<string>? log)
    {
        if (File.Exists(path) && !overwrite)
            return;
        File.WriteAllText(path, content);
        written.Add(path);
        log?.Invoke($"{(overwrite ? "обновлён" : "создан")} {Path.GetFileName(path)}");
    }
}
