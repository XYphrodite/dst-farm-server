using System.Diagnostics;

namespace DstFarm.Core;

/// <summary>Полное удаление dstfarm: файлы, PATH и, напоследок, сам exe.</summary>
public static class Uninstaller
{
    /// <summary>Убирает каталог из значения PATH. Отдельно от реестра, чтобы можно было проверить.</summary>
    public static string RemoveFromPath(string? pathValue, string entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry);

        var separator = Path.PathSeparator;
        var wanted = entry.TrimEnd(Path.DirectorySeparatorChar);
        var kept = (pathValue ?? string.Empty)
            .Split(separator)
            .Where(part => part.Length > 0)
            .Where(part => !string.Equals(part.TrimEnd(Path.DirectorySeparatorChar), wanted, StringComparison.OrdinalIgnoreCase));

        return string.Join(separator, kept);
    }

    /// <summary>Что будет удалено и сколько это весит.</summary>
    public static IReadOnlyList<InstallTarget> Plan(FarmConfig config, bool includeCluster)
    {
        ArgumentNullException.ThrowIfNull(config);

        var targets = new List<InstallTarget>();
        AddDirectory(config.RootPath);
        AddFile(FarmConfig.DefaultConfigFile());
        if (includeCluster)
            AddDirectory(config.ClusterPath);

        return targets;

        void AddDirectory(string path)
        {
            if (Directory.Exists(path))
                targets.Add(new InstallTarget(path, ServerInstall.DirectorySize(path)));
        }

        void AddFile(string path)
        {
            if (File.Exists(path))
                targets.Add(new InstallTarget(path, new FileInfo(path).Length));
        }
    }

    public static IReadOnlyList<InstallTarget> RemovePlanned(FarmConfig config, bool includeCluster)
    {
        var targets = Plan(config, includeCluster);
        foreach (var target in targets)
        {
            if (Directory.Exists(target.Path))
                Directory.Delete(target.Path, recursive: true);
            else
                File.Delete(target.Path);
        }

        return targets;
    }

    /// <summary>
    /// Работающий exe удалить нельзя, поэтому уборку доделывает отдельный процесс,
    /// который ждёт нашего завершения. Каталог сносится только если опустел.
    /// </summary>
    public static void ScheduleSelfDelete(string exePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);

        var directory = Path.GetDirectoryName(exePath);
        var script =
            $"""ping -n 3 127.0.0.1 >nul & del /f /q "{exePath}" "{exePath}.old" >nul 2>&1 & rmdir /q "{directory}" >nul 2>&1""";

        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(script);

        Process.Start(startInfo);
    }
}
