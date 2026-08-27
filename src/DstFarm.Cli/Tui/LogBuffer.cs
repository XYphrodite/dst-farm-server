namespace DstFarm.Cli.Tui;

/// <summary>Кольцевой буфер строк лога для нижней панели.</summary>
internal sealed class LogBuffer(int capacity = 500)
{
    private readonly Queue<string> lines = new();
    private readonly Lock sync = new();

    public void Add(string line)
    {
        lock (sync)
        {
            lines.Enqueue($"{DateTimeOffset.Now:HH:mm:ss} {line}");
            while (lines.Count > capacity)
                lines.Dequeue();
        }
    }

    public IReadOnlyList<string> Tail(int count)
    {
        lock (sync)
            return [.. lines.TakeLast(Math.Max(1, count))];
    }
}
