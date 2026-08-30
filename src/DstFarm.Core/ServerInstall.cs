namespace DstFarm.Core;

public sealed record InstallTarget(string Path, long Bytes);

/// <summary>
/// Удаление установленного сервера. Он занимает около 4.2 ГБ, а нужен не всегда:
/// мир, токен и настройки живут отдельно и переустановку переживают.
/// </summary>
public static class ServerInstall
{
    public static bool IsInstalled(FarmConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return File.Exists(config.ServerExe);
    }

    /// <summary>Что можно удалить и сколько это весит.</summary>
    public static IReadOnlyList<InstallTarget> Removable(FarmConfig config, bool includeSteamCmd)
    {
        ArgumentNullException.ThrowIfNull(config);

        var targets = new List<InstallTarget>();
        Add(config.ServerPath);
        if (includeSteamCmd)
            Add(config.SteamCmdPath);
        return targets;

        void Add(string path)
        {
            if (Directory.Exists(path))
                targets.Add(new InstallTarget(path, DirectorySize(path)));
        }
    }

    public static IReadOnlyList<InstallTarget> Remove(FarmConfig config, bool includeSteamCmd)
    {
        var targets = Removable(config, includeSteamCmd);
        foreach (var target in targets)
            Directory.Delete(target.Path, recursive: true);
        return targets;
    }

    public static long DirectorySize(string path)
    {
        try
        {
            return new DirectoryInfo(path)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(file => file.Length);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Размер — справочная величина, ради неё падать незачем.
            return 0;
        }
    }
}
