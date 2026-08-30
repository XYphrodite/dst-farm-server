using System.Diagnostics;
using System.Globalization;

namespace DstFarm.Core;

public sealed record ShardStatus(string Name, int? ProcessId, bool Running, int Restarts);

/// <summary>Поднимает шарды DST и держит их живыми, пока не попросят остановиться.</summary>
public sealed class ServerSupervisor : IDisposable
{
    private readonly FarmConfig config;
    private readonly UptimeTracker uptime;
    private readonly Dictionary<string, ShardRunner> runners = [];
    private readonly Lock sync = new();

    /// <summary>Здоровый сервер доходит до запуска мира за считаные секунды.</summary>
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(90);

    private readonly HashSet<string> seenPlayers = [];
    private DateTimeOffset playersCheckedAt = DateTimeOffset.MinValue;

    public ServerSupervisor(FarmConfig config)
    {
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        uptime = new UptimeTracker(config);
    }

    public event Action<string>? Log;

    public DateTimeOffset? StartedAt { get; private set; }

    public string StopFlagFile => Path.Combine(config.StatePath, "stop.flag");

    public string PidFile => Path.Combine(config.StatePath, "supervisor.pid");

    public IReadOnlyList<ShardStatus> Snapshot()
    {
        lock (sync)
        {
            return [.. config.Shards.Select(name =>
                runners.TryGetValue(name, out var runner)
                    ? new ShardStatus(name, runner.ProcessId, runner.Running, runner.Restarts)
                    : new ShardStatus(name, null, false, 0))];
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(config.ServerExe))
            throw new InvalidOperationException(Loc.T($"сервер не установлен: {config.ServerExe}", $"the server is not installed: {config.ServerExe}"));
        if (!config.HasClusterToken())
            throw new InvalidOperationException(Loc.T("нет cluster_token.txt: сервер не стартует и дропы не капают", "cluster_token.txt is missing: the server will not start and no drops are earned"));

        Directory.CreateDirectory(config.StatePath);
        Directory.CreateDirectory(config.LogPath);
        File.Delete(StopFlagFile);
        await File.WriteAllTextAsync(PidFile, Environment.ProcessId.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);

        foreach (var conflict in PortProbe.Conflicts(config))
            Log?.Invoke(Loc.T($"внимание: порт {conflict.Port} ({conflict.Purpose}) уже занят — сервер может не подняться", $"warning: port {conflict.Port} ({conflict.Purpose}) is already in use — the server may fail to start"));

        StartedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        var nextDailyRestart = NextDailyRestart();
        Log?.Invoke(Loc.T($"супервизор запущен, кластер {config.Cluster}, порт {config.ServerPort}", $"supervisor started, cluster {config.Cluster}, port {config.ServerPort}"));

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                foreach (var shard in config.Shards)
                {
                    ShardRunner? runner;
                    lock (sync)
                        runners.TryGetValue(shard, out runner);

                    if (runner is { Running: true })
                        continue;

                    if (runner is not null)
                    {
                        Log?.Invoke(Loc.T($"{shard} завершился (код {runner.ExitCode}), перезапуск через {config.RestartDelaySeconds} с", $"{shard} exited (code {runner.ExitCode}), restarting in {config.RestartDelaySeconds}s"));
                        var restarts = runner.Restarts;
                        runner.Dispose();
                        if (!config.RestartOnExit)
                            return;
                        await Task.Delay(TimeSpan.FromSeconds(config.RestartDelaySeconds), cancellationToken).ConfigureAwait(false);
                        Spawn(shard, restarts + 1);
                    }
                    else
                    {
                        Spawn(shard, 0);
                    }
                }

                WarnAboutStalledShards();
                await DeliverConsoleCommandsAsync().ConfigureAwait(false);
                await GreetNewPlayersAsync().ConfigureAwait(false);

                if (File.Exists(StopFlagFile))
                {
                    Log?.Invoke(Loc.T("получен стоп-сигнал", "stop signal received"));
                    break;
                }

                if (nextDailyRestart is { } due && DateTimeOffset.Now >= due)
                {
                    Log?.Invoke(Loc.T("плановый перезапуск по расписанию", "scheduled restart"));
                    await ShutdownAllAsync().ConfigureAwait(false);
                    nextDailyRestart = NextDailyRestart();
                }

                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            Log?.Invoke(Loc.T("остановка по запросу", "stopping on request"));
        }
        finally
        {
            await ShutdownAllAsync().ConfigureAwait(false);
            uptime.Add(stopwatch.Elapsed);
            StartedAt = null;
            File.Delete(PidFile);
            File.Delete(StopFlagFile);
            Log?.Invoke(Loc.T("сервер остановлен", "server stopped"));
        }
    }

    /// <summary>
    /// Выполняет настроенные команды при входе игрока. Пауза голода и подобное живут
    /// только в компоненте и не переживают перезаход, поэтому их надо ставить заново.
    /// </summary>
    private async Task GreetNewPlayersAsync()
    {
        if (config.OnPlayerJoin.Count == 0)
            return;
        if (DateTimeOffset.Now - playersCheckedAt < TimeSpan.FromSeconds(5))
            return;

        playersCheckedAt = DateTimeOffset.Now;
        var report = PlayerWatch.Inspect(config);
        if (!report.LogFound)
            return;

        var present = report.Players.Select(p => p.Guid).ToHashSet(StringComparer.Ordinal);
        seenPlayers.IntersectWith(present);

        foreach (var player in report.Players)
        {
            if (!seenPlayers.Add(player.Guid))
                continue;

            Log?.Invoke(Loc.T(
                $"вошёл {player.Name ?? player.Guid}, применяю команды входа",
                $"{player.Name ?? player.Guid} joined, applying join commands"));
            foreach (var command in config.OnPlayerJoin)
                ConsoleQueue.Enqueue(config, command);
        }
    }

    /// <summary>Команды из очереди уходят в консоль первого шарда — им всегда Master.</summary>
    private async Task DeliverConsoleCommandsAsync()
    {
        var commands = ConsoleQueue.Drain(config);
        if (commands.Count == 0)
            return;

        ShardRunner? master;
        lock (sync)
            runners.TryGetValue("Master", out master);

        if (master is null)
            return;

        foreach (var command in commands)
        {
            var sent = await master.SendAsync(command).ConfigureAwait(false);
            Log?.Invoke(sent
                ? Loc.T($"в консоль: {command}", $"to console: {command}")
                : Loc.T($"не удалось отправить в консоль: {command}", $"could not send to console: {command}"));
        }
    }

    /// <summary>
    /// Сервер умеет молча висеть после авторизации, так и не начав поднимать мир.
    /// Молчание выглядит как «всё хорошо», поэтому говорим об этом прямо.
    /// </summary>
    private void WarnAboutStalledShards()
    {
        List<ShardRunner> current;
        lock (sync)
            current = [.. runners.Values];

        foreach (var runner in current)
        {
            if (runner.WorldStarted || runner.StallWarned || !runner.Running)
                continue;
            if (DateTimeOffset.Now - runner.StartedAt < StallTimeout)
                continue;

            runner.StallWarned = true;
            Log?.Invoke(Loc.T(
                $"{runner.Shard}: прошло {StallTimeout.TotalSeconds:F0} с, а мир так и не начал подниматься. "
                + "Сервер жив, но ждёт ответа от бэкенда Klei — проверьте, что исходящие соединения не режет VPN, "
                + "прокси или брандмауэр.",
                $"{runner.Shard}: {StallTimeout.TotalSeconds:F0}s in and the world has not started coming up. "
                + "The server is alive but waiting on the Klei backend — check that a VPN, proxy or firewall "
                + "is not blocking its outbound connections."));
        }
    }

    private void Spawn(string shard, int restarts)
    {
        var runner = new ShardRunner(config, shard, restarts, line => Log?.Invoke(line));
        lock (sync)
            runners[shard] = runner;
        Log?.Invoke(Loc.T($"{shard} запущен, pid={runner.ProcessId}", $"{shard} started, pid={runner.ProcessId}"));
    }

    private async Task ShutdownAllAsync()
    {
        List<ShardRunner> active;
        lock (sync)
        {
            active = [.. runners.Values];
            runners.Clear();
        }

        foreach (var runner in active)
        {
            if (runner.Running)
                Log?.Invoke(Loc.T($"штатное завершение {runner.Shard}, мир сохраняется", $"shutting {runner.Shard} down gracefully, the world is being saved"));
            await runner.ShutdownAsync(TimeSpan.FromSeconds(45)).ConfigureAwait(false);
            runner.Dispose();
        }
    }

    private DateTimeOffset? NextDailyRestart()
    {
        if (config.DailyRestartHour is < 0 or > 23)
            return null;
        var now = DateTimeOffset.Now;
        var target = new DateTimeOffset(now.Year, now.Month, now.Day, config.DailyRestartHour, 0, 0, now.Offset);
        return target > now ? target : target.AddDays(1);
    }

    public void Dispose()
    {
        lock (sync)
        {
            foreach (var runner in runners.Values)
                runner.Dispose();
            runners.Clear();
        }
    }

    private sealed class ShardRunner : IDisposable
    {
        private readonly Process process;
        private readonly StreamWriter logWriter;
        private readonly Lock writeSync = new();
        private readonly CancellationTokenSource tailCts = new();
        private readonly Task? tailTask;

        /// <summary>Дошли ли строки из собственного лога сервера: пока нет — показываем stdout.</summary>
        private volatile bool tailAlive;

        public ShardRunner(FarmConfig config, string shard, int restarts, Action<string> log)
        {
            Shard = shard;
            Restarts = restarts;

            var logFile = Path.Combine(config.LogPath, $"{shard.ToLowerInvariant()}.log");
            logWriter = new StreamWriter(logFile, append: true) { AutoFlush = true };
            logWriter.WriteLine(string.Create(CultureInfo.InvariantCulture, $"===== start {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} ====="));

            var startInfo = new ProcessStartInfo(config.ServerExe)
            {
                WorkingDirectory = Path.GetDirectoryName(config.ServerExe)!,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("-console");
            startInfo.ArgumentList.Add("-cluster");
            startInfo.ArgumentList.Add(config.Cluster);
            startInfo.ArgumentList.Add("-shard");
            startInfo.ArgumentList.Add(shard);
            if (!string.IsNullOrWhiteSpace(config.ConfDirectory))
            {
                // Нестандартный каталог кластеров: иначе сервер сам найдёт путь по умолчанию.
                var confPath = config.ConfPath.TrimEnd(Path.DirectorySeparatorChar);
                var parent = Directory.GetParent(confPath)!;
                startInfo.ArgumentList.Add("-persistent_storage_root");
                startInfo.ArgumentList.Add(parent.Parent?.FullName ?? parent.FullName);
                startInfo.ArgumentList.Add("-conf_dir");
                startInfo.ArgumentList.Add(Path.Combine(parent.Name, Path.GetFileName(confPath)));
            }

            foreach (var extra in config.ExtraArguments)
                startInfo.ArgumentList.Add(extra);

            process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => Write(e.Data, log);
            process.ErrorDataReceived += (_, e) => Write(e.Data, log);

            if (!process.Start())
                throw new InvalidOperationException(Loc.T($"не удалось запустить шард {shard}", $"could not start shard {shard}"));

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // stdout сервера уходит в pipe и потому буферизуется блоками: панель выглядит
            // замёрзшей. Собственный лог сервер пишет сразу, его и читаем для живого вывода.
            var shardLog = Path.Combine(config.ClusterPath, shard, "server_log.txt");
            tailTask = Task.Run(
                () => LogTail.FollowAsync(
                    shardLog,
                    line =>
                    {
                        tailAlive = true;
                        Observe(line);
                        log($"[{Shard}] {line}");
                    },
                    TimeSpan.FromMilliseconds(500),
                    TimeSpan.FromSeconds(20),
                    tailCts.Token),
                CancellationToken.None);
        }

        /// <summary>Эту строку игра печатает, когда действительно начинает поднимать мир.</summary>
        private const string WorldStartMarker = "Starting Dedicated Server Game";

        public string Shard { get; }

        public int Restarts { get; }

        public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;

        /// <summary>Дошёл ли сервер до запуска мира: до этого он может молча висеть.</summary>
        public bool WorldStarted { get; private set; }

        public bool StallWarned { get; set; }

        public int? ProcessId => Running ? process.Id : null;

        public bool Running
        {
            get
            {
                try
                {
                    return !process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }
        }

        public int ExitCode
        {
            get
            {
                try
                {
                    return process.HasExited ? process.ExitCode : 0;
                }
                catch (InvalidOperationException)
                {
                    return 0;
                }
            }
        }

        /// <summary>Отправляет строку в консоль сервера — тем же путём, что и c_shutdown.</summary>
        public async Task<bool> SendAsync(string command)
        {
            if (!Running)
                return false;
            try
            {
                await process.StandardInput.WriteLineAsync(command).ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        /// <summary>Просим сервер сохраниться и выйти, и только потом бьём процесс.</summary>
        public async Task ShutdownAsync(TimeSpan timeout)
        {
            if (!Running)
                return;
            try
            {
                await process.StandardInput.WriteLineAsync("c_shutdown(true)").ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
            }
            catch (IOException)
            {
                // Процесс уже не читает stdin, добьём ниже.
            }
            catch (ObjectDisposedException)
            {
            }

            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
            }

            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void Write(string? line, Action<string> log)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;
            lock (writeSync)
                logWriter.WriteLine(line);
            Observe(line);
            if (!tailAlive)
                log($"[{Shard}] {line}");
        }

        private void Observe(string line)
        {
            if (!WorldStarted && line.Contains(WorldStartMarker, StringComparison.Ordinal))
                WorldStarted = true;
        }


        public void Dispose()
        {
            tailCts.Cancel();
            try
            {
                tailTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
            }

            tailCts.Dispose();
            logWriter.Dispose();
            process.Dispose();
        }
    }
}
