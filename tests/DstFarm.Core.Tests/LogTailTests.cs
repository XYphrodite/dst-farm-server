using System.Collections.Concurrent;
using DstFarm.Core;
using Xunit;

namespace DstFarm.Core.Tests;

public sealed class LogTailTests
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan WaitForFile = TimeSpan.FromMilliseconds(200);

    [Fact]
    public async Task ReadsLinesThatAppearAfterTheWatchStarted()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "server_log.txt");
        var lines = new ConcurrentQueue<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var follow = LogTail.FollowAsync(path, lines.Enqueue, Poll, TimeSpan.FromSeconds(5), cts.Token);

        await AppendAsync(path, "[00:00:05]: Account Communication Success");
        await AppendAsync(path, "[00:00:09]: Starting master server");
        await WaitForCountAsync(lines, 2, cts.Token);

        await cts.CancelAsync();
        await follow;

        Assert.Equal(
            ["[00:00:05]: Account Communication Success", "[00:00:09]: Starting master server"],
            lines);
    }

    [Fact]
    public async Task ReadsExistingContentAndThenKeepsFollowing()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "server_log.txt");
        await File.WriteAllTextAsync(path, "первая строка" + Environment.NewLine, TestToken);
        var lines = new ConcurrentQueue<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var follow = LogTail.FollowAsync(path, lines.Enqueue, Poll, WaitForFile, cts.Token);
        await WaitForCountAsync(lines, 1, cts.Token);
        await AppendAsync(path, "вторая строка");
        await WaitForCountAsync(lines, 2, cts.Token);

        await cts.CancelAsync();
        await follow;

        Assert.Equal(["первая строка", "вторая строка"], lines);
    }

    [Fact]
    public async Task StartsOverWhenServerTruncatesTheLogOnRestart()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "server_log.txt");
        await File.WriteAllTextAsync(path, "старая длинная строка прошлого запуска" + Environment.NewLine, TestToken);
        var lines = new ConcurrentQueue<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var follow = LogTail.FollowAsync(path, lines.Enqueue, Poll, WaitForFile, cts.Token);
        await WaitForCountAsync(lines, 1, cts.Token);

        // Сервер перезапустился и обрезал лог: файл стал короче прочитанного.
        await File.WriteAllTextAsync(path, "новый запуск" + Environment.NewLine, TestToken);
        await WaitForCountAsync(lines, 2, cts.Token);

        await cts.CancelAsync();
        await follow;

        Assert.Equal(["старая длинная строка прошлого запуска", "новый запуск"], lines);
    }

    [Fact]
    public async Task ReadsFileThatIsStillOpenForWriting()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "server_log.txt");
        var lines = new ConcurrentQueue<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var writer = new StreamWriter(
            new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            AutoFlush = true,
        };

        var follow = LogTail.FollowAsync(path, lines.Enqueue, Poll, WaitForFile, cts.Token);
        await writer.WriteLineAsync("сервер ещё держит файл открытым");
        await WaitForCountAsync(lines, 1, cts.Token);

        await cts.CancelAsync();
        await follow;

        Assert.Equal(["сервер ещё держит файл открытым"], lines);
    }

    [Fact]
    public async Task GivesUpWhenFileNeverAppears()
    {
        using var temp = new TempDirectory();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await LogTail.FollowAsync(
            Path.Combine(temp.Path, "нет-такого.txt"),
            _ => Assert.Fail("строк быть не должно"),
            Poll,
            WaitForFile,
            cts.Token);

        // Возврат без отмены и есть проверка: ждать вечно нельзя, иначе панель молчит.
        Assert.False(cts.IsCancellationRequested);
    }

    [Fact]
    public async Task SkipsBlankLines()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "server_log.txt");
        await File.WriteAllTextAsync(path, $"первая{Environment.NewLine}{Environment.NewLine}   {Environment.NewLine}вторая{Environment.NewLine}", TestToken);
        var lines = new ConcurrentQueue<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var follow = LogTail.FollowAsync(path, lines.Enqueue, Poll, WaitForFile, cts.Token);
        await WaitForCountAsync(lines, 2, cts.Token);

        await cts.CancelAsync();
        await follow;

        Assert.Equal(["первая", "вторая"], lines);
    }

    private static CancellationToken TestToken => CancellationToken.None;

    private static async Task AppendAsync(string path, string line)
    {
        await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        await using var writer = new StreamWriter(stream);
        await writer.WriteLineAsync(line);
    }

    private static async Task WaitForCountAsync(ConcurrentQueue<string> lines, int count, CancellationToken cancellationToken)
    {
        while (lines.Count < count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(10, cancellationToken);
        }
    }
}
