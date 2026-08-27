namespace DstFarm.Core;

/// <summary>
/// Читает дописываемые строки файла. Нужен потому, что stdout сервера уходит в pipe
/// и буферизуется блоками: панель выглядит замёрзшей, хотя сервер работает.
/// Собственный лог DST пишет сразу, его и читаем.
/// </summary>
public static class LogTail
{
    /// <summary>
    /// Следит за файлом, пока не отменят. Если файла нет дольше <paramref name="waitForFile"/>,
    /// выходит — чтобы вызывающий мог вернуться к запасному источнику.
    /// </summary>
    public static async Task FollowAsync(
        string path,
        Action<string> onLine,
        TimeSpan pollInterval,
        TimeSpan waitForFile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onLine);

        long position = 0;
        var seenFile = false;
        var started = DateTimeOffset.Now;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (File.Exists(path))
                {
                    seenFile = true;
                    position = await ReadAppendedAsync(path, position, onLine, cancellationToken).ConfigureAwait(false);
                }
                else if (!seenFile && DateTimeOffset.Now - started > waitForFile)
                {
                    return;
                }

                await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task<long> ReadAppendedAsync(
        string path,
        long position,
        Action<string> onLine,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            // Сервер обрезает лог при старте: тогда читаем с начала.
            if (stream.Length < position)
                position = 0;

            if (stream.Length <= position)
                return position;

            stream.Seek(position, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    onLine(line);
            }

            return stream.Position;
        }
        catch (IOException)
        {
            // Файл занят на запись — вернёмся на следующем круге.
            return position;
        }
    }
}
