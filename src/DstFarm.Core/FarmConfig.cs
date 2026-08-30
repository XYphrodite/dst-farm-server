using System.Text.Json;
using System.Text.Json.Serialization;

namespace DstFarm.Core;

/// <summary>Настройки утилиты и профиля фарма, хранятся в config.json рядом с exe.</summary>
public sealed class FarmConfig
{
    public const string DedicatedServerAppId = "343050";
    public const string SteamCmdUrl = "https://media.steampowered.com/client/installer/steamcmd.zip";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string Root { get; set; } = string.Empty;
    public string ServerDirectory { get; set; } = string.Empty;
    public string SteamCmdDirectory { get; set; } = string.Empty;
    public string ConfDirectory { get; set; } = string.Empty;

    /// <summary>auto, ru или en. auto — по языку системы.</summary>
    public string Language { get; set; } = Loc.Auto;

    public string Cluster { get; set; } = "FarmCluster";
    public string ClusterName { get; set; } = "Farm Idle Server";
    public string ClusterPassword { get; set; } = string.Empty;
    public string ClusterToken { get; set; } = string.Empty;
    public int ServerPort { get; set; } = 10999;
    public int MaxPlayers { get; set; } = 6;

    // Steam-порты сервера. Клиент игры и сам Steam на этой же машине занимают часть
    // диапазона 27015-27050, поэтому их иногда приходится сдвигать.
    public int MasterServerPort { get; set; } = 27018;
    public int AuthenticationPort { get; set; } = 8768;

    // Профиль фарма
    public string GameMode { get; set; } = "endless";
    public bool OnlyDay { get; set; } = true;
    public bool EternalAutumn { get; set; } = true;
    public bool NoHunger { get; set; } = true;
    public bool NoSanityDrain { get; set; } = true;
    public bool DisableThreats { get; set; } = true;
    public string WorldSize { get; set; } = "small";
    public bool EnableCaves { get; set; }

    // Супервизор
    public bool RestartOnExit { get; set; } = true;
    public int RestartDelaySeconds { get; set; } = 10;
    public int DailyRestartHour { get; set; } = -1;
    public List<string> ExtraArguments { get; set; } = [];

    /// <summary>
    /// Команды консоли, выполняемые при каждом входе игрока. Нужны потому, что часть
    /// состояния живёт только в компонентах и не переживает перезаход: пауза голода, например.
    /// </summary>
    public List<string> OnPlayerJoin { get; set; } = [];

    /// <summary>
    /// Полностью останавливает голод: Hunger:Pause() гасит и убывание шкалы, и урон от
    /// голодания. Сытость сперва восполняется, иначе персонаж останется на нуле навсегда.
    /// </summary>
    public const string PauseHungerCommand =
        "for i, p in ipairs(AllPlayers) do p.components.hunger:SetPercent(1) p.components.hunger:Pause() end";

    /// <summary>Стоит ли пауза голода. Живёт в OnPlayerJoin, потому что не переживает перезаход.</summary>
    [JsonIgnore]
    public bool HungerPaused
    {
        get => OnPlayerJoin.Contains(PauseHungerCommand, StringComparer.Ordinal);
        set
        {
            OnPlayerJoin.RemoveAll(c => string.Equals(c, PauseHungerCommand, StringComparison.Ordinal));
            if (value)
                OnPlayerJoin.Add(PauseHungerCommand);
        }
    }

    [JsonIgnore]
    public string RootPath => string.IsNullOrWhiteSpace(Root)
        ? Path.Combine(AppContext.BaseDirectory, ".runtime")
        : Root;

    [JsonIgnore]
    public string SteamCmdPath => string.IsNullOrWhiteSpace(SteamCmdDirectory)
        ? Path.Combine(RootPath, "steamcmd")
        : SteamCmdDirectory;

    [JsonIgnore]
    public string SteamCmdExe => Path.Combine(SteamCmdPath, "steamcmd.exe");

    [JsonIgnore]
    public string ServerPath => string.IsNullOrWhiteSpace(ServerDirectory)
        ? Path.Combine(RootPath, "server")
        : ServerDirectory;

    [JsonIgnore]
    public string ServerExe =>
        Path.Combine(ServerPath, "bin64", "dontstarve_dedicated_server_nullrenderer_x64.exe");

    [JsonIgnore]
    public string ConfPath => string.IsNullOrWhiteSpace(ConfDirectory)
        ? DefaultConfDirectory()
        : ConfDirectory;

    [JsonIgnore]
    public string ClusterPath => Path.Combine(ConfPath, Cluster);

    [JsonIgnore]
    public string ClusterTokenFile => Path.Combine(ClusterPath, "cluster_token.txt");

    [JsonIgnore]
    public string StatePath => Path.Combine(RootPath, "state");

    [JsonIgnore]
    public string LogPath => Path.Combine(RootPath, "logs");

    [JsonIgnore]
    public IReadOnlyList<string> Shards => EnableCaves ? ["Master", "Caves"] : ["Master"];

    /// <summary>Каталог, где DST по умолчанию ищет кластеры.</summary>
    public static string DefaultConfDirectory()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents))
            documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents");
        return Path.Combine(documents, "Klei", "DoNotStarveTogether");
    }

    public static string DefaultConfigFile() => Path.Combine(AppContext.BaseDirectory, "config.json");

    public static FarmConfig Load(string? path = null)
    {
        var file = path ?? DefaultConfigFile();
        if (!File.Exists(file))
            return new FarmConfig();
        var json = File.ReadAllText(file);
        return JsonSerializer.Deserialize<FarmConfig>(json, SerializerOptions) ?? new FarmConfig();
    }

    public string Save(string? path = null)
    {
        var file = path ?? DefaultConfigFile();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(this, SerializerOptions));
        return file;
    }

    /// <summary>Токен Klei записан и выглядит правдоподобно (реальный — длинная строка).</summary>
    public bool HasClusterToken() =>
        File.Exists(ClusterTokenFile) && File.ReadAllText(ClusterTokenFile).Trim().Length > 20;
}
