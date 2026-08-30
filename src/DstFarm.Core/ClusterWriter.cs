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
        // weather = never переводит мир в ms_setprecipitationmode "never": дождя нет вовсе,
        // а с ним уходят промокание, охлаждение и убыль рассудка от сырости.
        ("weather", "never"),
        // Перегрев и переохлаждение тоже убивают AFK-персонажа.
        ("temperaturedamage", "nonlethal"),
    ];

    /// <summary>
    /// Группа «враждебные существа» из scripts/map/customize.lua, наземная часть.
    /// Боссы боссами, а AFK-персонажа догрызают именно жабы, пчёлы и пауки.
    /// </summary>
    private static readonly string[] HostileSurface =
    [
        "lureplants", "hound_mounds", "mosquitos", "sharks", "squid", "wasps", "frogs",
        "walrus_setting", "cookiecutters", "pirateraids", "merms", "spiders_setting",
        "spider_warriors", "bats_setting",
    ];

    /// <summary>
    /// Вторая группа враждебных существ — та, что решает, что вообще будет поставлено
    /// на карту при генерации. Заводные фигуры, щупальца и tallbird-ы живут именно здесь.
    /// </summary>
    private static readonly string[] HostileSpawnsSurface =
    [
        "spiders", "houndmound", "merm", "tentacles", "chess", "walrus", "angrybees", "tallbirds",
    ];

    /// <summary>
    /// Ключи, которые действуют только при генерации мира: игра применяет их один раз
    /// и строкой OVERRIDE не печатает, поэтому сверять живой мир по ним нельзя.
    /// </summary>
    public static IReadOnlyList<string> GenerationOnlyKeys =>
        [.. new[] { "world_size" }.Concat(HostileSpawnsSurface).Concat(HostileSpawnsCaves).Distinct(StringComparer.Ordinal)];

    /// <summary>Та же генерационная группа, пещерная часть.</summary>
    private static readonly string[] HostileSpawnsCaves =
    [
        "spiders", "cave_spiders", "tentacles", "chess", "bats", "fissure", "worms",
    ];

    /// <summary>Та же группа, пещерная часть.</summary>
    private static readonly string[] HostileCaves =
    [
        "merms", "spiders_setting", "spider_warriors", "bats_setting", "nightmarecreatures",
        "spider_hider", "spider_spitter", "spider_dropper", "molebats", "itemmimics", "chest_mimics",
    ];

    /// <summary>
    /// Отдельной настройки «рассудок» в игре нет: за него отвечают темнота и порождаемые
    /// низким рассудком твари. Ключи и значения сверены с scripts/map/customize.lua.
    /// </summary>
    private static readonly (string Key, string Value)[] SanityOverrides =
    [
        ("darkness", "nonlethal"),
        ("shadowcreatures", "never"),
        ("brightmarecreatures", "never"),
    ];

    /// <summary>
    /// Совпадают ли файлы кластера с текущими настройками. Нужен, чтобы интерфейс не показывал
    /// значения, по которым сервер на самом деле не работает.
    /// </summary>
    public static bool MatchesDisk(FarmConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (!SameContent(Path.Combine(config.ClusterPath, "cluster.ini"), BuildClusterIni(config)))
            return false;

        foreach (var shard in config.Shards)
        {
            var isCaves = shard == "Caves";
            var directory = Path.Combine(config.ClusterPath, shard);
            if (!SameContent(Path.Combine(directory, "server.ini"), BuildServerIni(config, isCaves)))
                return false;
            if (!SameWorldGen(Path.Combine(directory, "worldgenoverride.lua"), BuildWorldGen(config, isCaves), isCaves))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Удаляет сохранённый мир. Часть настроек вшивается в мир при генерации, поэтому
    /// изменить их у существующего мира нельзя — только пересоздать.
    /// </summary>
    public static IReadOnlyList<string> ResetWorld(FarmConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var removed = new List<string>();
        foreach (var shard in config.Shards)
        {
            foreach (var name in (string[])["save", "backup"])
            {
                var path = Path.Combine(config.ClusterPath, shard, name);
                if (!Directory.Exists(path))
                    continue;
                Directory.Delete(path, recursive: true);
                removed.Add(path);
            }
        }

        return removed;
    }

    /// <summary>
    /// Сравнение по смыслу, а не побайтово: DST переписывает worldgenoverride.lua после
    /// генерации мира — меняет preset на worldgen_preset/settings_preset и подмешивает
    /// ключи игрового режима. Побайтовое сравнение после этого не совпадёт никогда.
    /// Считаем совпавшим, если все наши пары присутствуют; лишнее игнорируем.
    /// </summary>
    /// <summary>
    /// Игра переименовывает preset в worldgen_preset и settings_preset, поэтому сверять
    /// по нему нельзя — на смысл настроек он всё равно не влияет.
    /// </summary>
    private static readonly string[] RewrittenByGame = ["preset"];

    /// <summary>Все ключи, которые наш генератор способен написать в файл мира.</summary>
    private static IReadOnlyCollection<string> ManagedWorldGenKeys(bool caves)
    {
        var everything = new FarmConfig
        {
            OnlyDay = true,
            EternalAutumn = true,
            NoHunger = true,
            NoSanityDrain = true,
            DisableThreats = true,
            EnableCaves = true,
        };

        return [.. BuildOverrides(everything, caves).Select(o => o.Key)];
    }

    private static bool SameContent(string path, string expected)
    {
        if (!File.Exists(path))
            return false;
        try
        {
            return Matches(ParseSettings(expected), ParseSettings(File.ReadAllText(path)), managed: null);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool SameWorldGen(string path, string expected, bool caves)
    {
        if (!File.Exists(path))
            return false;
        try
        {
            return Matches(ParseSettings(expected), ParseSettings(File.ReadAllText(path)), ManagedWorldGenKeys(caves));
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Наши ключи должны совпадать, лишнее от игры игнорируется. Для файла мира
    /// проверяется весь наш словарь: иначе выключенная настройка осталась бы незамеченной.
    /// </summary>
    private static bool Matches(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual,
        IReadOnlyCollection<string>? managed)
    {
        var keys = managed is null
            ? expected.Keys
            : [.. expected.Keys.Concat(managed).Distinct(StringComparer.Ordinal)];

        foreach (var key in keys)
        {
            if (RewrittenByGame.Contains(key, StringComparer.Ordinal))
                continue;

            var wanted = expected.GetValueOrDefault(key);
            var found = actual.GetValueOrDefault(key);
            if (!string.Equals(wanted, found, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    /// <summary>Достаёт пары ключ-значение из ini и из lua-таблицы оверрайдов.</summary>
    internal static IReadOnlyDictionary<string, string> ParseSettings(string text)
    {
        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in (text ?? string.Empty).Split((char)10))
        {
            var line = raw.Trim().TrimEnd(',');
            if (line.Length == 0 || line.StartsWith('[') || line.StartsWith("--", StringComparison.Ordinal))
                continue;

            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
                continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"');
            if (key.Length > 0)
                pairs[key] = value;
        }

        return pairs;
    }

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
        builder.AppendLine(CultureInfo.InvariantCulture, $"master_server_port = {(caves ? config.MasterServerPort + 1 : config.MasterServerPort)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"authentication_port = {(caves ? config.AuthenticationPort + 1 : config.AuthenticationPort)}");
        builder.AppendLine();
        builder.AppendLine("[ACCOUNT]");
        builder.AppendLine("encode_user_path = true");
        return builder.ToString();
    }

    /// <summary>Что мы просим у генерации мира. Один источник правды для файла и для сверки.</summary>
    public static IReadOnlyList<(string Key, string Value)> BuildOverrides(FarmConfig config, bool caves)
    {
        ArgumentNullException.ThrowIfNull(config);

        var overrides = new List<(string Key, string Value)> { ("world_size", config.WorldSize) };

        // "none" игра не понимает: допустимы только nonlethal и default.
        if (config.NoHunger)
            overrides.Add(("hunger", "nonlethal"));
        if (config.NoSanityDrain)
            overrides.AddRange(SanityOverrides);

        if (!caves)
        {
            if (config.OnlyDay)
                overrides.Add(("day", "onlyday"));
            if (config.EternalAutumn)
            {
                // season_start принимает только default/winter/spring/summer,
                // а осень и так стартовый сезон.
                overrides.Add(("season_start", "default"));
                overrides.Add(("autumn", "verylongseason"));
                overrides.Add(("winter", "noseason"));
                overrides.Add(("spring", "noseason"));
                overrides.Add(("summer", "noseason"));
            }

            if (config.DisableThreats)
            {
                overrides.AddRange(ThreatOverrides);
                overrides.AddRange(HostileSurface.Select(key => (key, "never")));
                overrides.AddRange(HostileSpawnsSurface.Select(key => (key, "never")));
            }
        }
        else if (config.DisableThreats)
        {
            overrides.Add(("earthquakes", "never"));
            overrides.Add(("temperaturedamage", "nonlethal"));
            overrides.Add(("weather", "never"));
            // Кислотный дождь в пещерах включён по умолчанию и наносит урон.
            overrides.Add(("acidrain_enabled", "none"));
            overrides.AddRange(HostileCaves.Select(key => (key, "never")));
            overrides.AddRange(HostileSpawnsCaves.Select(key => (key, "never")));
        }

        return overrides;
    }

    internal static string BuildWorldGen(FarmConfig config, bool caves)
    {
        var overrides = BuildOverrides(config, caves);

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
        log?.Invoke($"{(overwrite ? Loc.T("обновлён", "updated") : Loc.T("создан", "created"))} {Path.GetFileName(path)}");
    }
}
