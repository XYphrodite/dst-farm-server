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
            throw new InvalidOperationException($"сервер не установлен: {config.ServerExe}");
        if (!config.HasClusterToken())
            throw new InvalidOperationException("нет cluster_token.txt: сервер не стартует и дропы не капают");

        Directory.CreateDirectory(config.StatePath);
        Directory.CreateDirectory(config.LogPath);
        File.Delete(StopFlagFile);
        await File.WriteAllTextAsync(PidFile, Environment.ProcessId.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);

        foreach (var conflict in PortProbe.Conflicts(config))
            Log?.Invoke($"внимание: порт {conflict.Port} ({conflict.Purpose}) уже занят — сервер может не подняться");

        StartedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        var nextDailyRestart = NextDailyRestart();
        Log?.Invoke($"супервизор запущен, кластер {config.Cluster}, порт {config.ServerPort}");

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
                        Log?.Invoke($"{shard} завершился (код {runner.ExitCode}), перезапуск через {config.RestartDelaySeconds} с");
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

                if (File.Exists(StopFlagFile))
                {
                    Log?.Invoke("получен стоп-сигнал");
                    break;
                }

                if (nextDailyRestart is { } due && DateTimeOffset.Now >= due)
                {
                    Log?.Invoke("плановый перезапуск по расписанию");
                    await ShutdownAllAsync().ConfigureAwait(false);
                    nextDailyRestart = NextDailyRestart();
                }

                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            Log?.Invoke("остановка по запросу");
        }
        finally
        {
            await ShutdownAllAsync().ConfigureAwait(false);
            uptime.Add(stopwatch.Elapsed);
            StartedAt = null;
            File.Delete(PidFile);
            File.Delete(StopFlagFile);
            Log?.Invoke("сервер остановлен");
        }
    }

    private void Spawn(string shard, int restarts)
    {
        var runner = new ShardRunner(config, shard, restarts, line => Log?.Invoke(line));
        lock (sync)
            runners[shard] = runner;
        Log?.Invoke($"{shard} запущен, pid={runner.ProcessId}");
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
                Log?.Invoke($"штатное завершение {runner.Shard}, мир сохраняется");
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
            logWriter.WriteLine(string.Create(CultureInfo.InvariantCulture, $"===== старт {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} ====="));

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
                throw new InvalidOperationException($"не удалось запустить шард {shard}");

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
                        log($"[{Shard}] {line}");
                    },
                    TimeSpan.FromMilliseconds(500),
                    TimeSpan.FromSeconds(20),
                    tailCts.Token),
                CancellationToken.None);
        }

        public string Shard { get; }

        public int Restarts { get; }

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
            if (!tailAlive)
                log($"[{Shard}] {line}");
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
