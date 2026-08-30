namespace DstFarm.Core;

/// <summary>
/// Очередь команд для консоли сервера. Через файл, потому что stdin сервера держит
/// супервизор, и другой процесс дописать туда напрямую не может.
/// </summary>
public static class ConsoleQueue
{
    public static string PathFor(FarmConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return Path.Combine(config.StatePath, "console.queue");
    }

    public static void Enqueue(FarmConfig config, string command)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(command))
            return;

        Directory.CreateDirectory(config.StatePath);
        // Перевод строки внутри команды сломал бы разбор на стороне сервера.
        var line = command.ReplaceLineEndings(" ").Trim();
        File.AppendAllText(PathFor(config), line + Environment.NewLine);
    }

    /// <summary>Забирает накопленное и очищает очередь.</summary>
    public static IReadOnlyList<string> Drain(FarmConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var path = PathFor(config);
        if (!File.Exists(path))
            return [];

        try
        {
            var lines = File.ReadAllLines(path);
            File.Delete(path);
            return [.. lines.Where(line => !string.IsNullOrWhiteSpace(line))];
        }
        catch (IOException)
        {
            // Файл ещё дописывают — заберём на следующем круге.
            return [];
        }
    }
}
