using System.Diagnostics;
using System.Globalization;

namespace DstFarm.Core;

/// <summary>Управление супервизором, который работает в другом процессе.</summary>
public sealed class SupervisorControl(FarmConfig config)
{
    private readonly FarmConfig config = config ?? throw new ArgumentNullException(nameof(config));

    private string PidFile => Path.Combine(config.StatePath, "supervisor.pid");

    private string StopFlagFile => Path.Combine(config.StatePath, "stop.flag");

    public int? RunningProcessId()
    {
        if (!File.Exists(PidFile))
            return null;
        if (!int.TryParse(File.ReadAllText(PidFile).Trim(), CultureInfo.InvariantCulture, out var pid))
            return null;
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.HasExited ? null : pid;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public bool IsRunning => RunningProcessId() is not null;

    /// <summary>Просит супервизор завершиться штатно и ждёт, пока он сохранит мир.</summary>
    public async Task<bool> StopAsync(TimeSpan timeout, Action<string>? log, CancellationToken cancellationToken)
    {
        var pid = RunningProcessId();
        if (pid is null)
        {
            log?.Invoke("супервизор не запущен");
            return false;
        }

        Directory.CreateDirectory(config.StatePath);
        await File.WriteAllTextAsync(StopFlagFile, string.Empty, cancellationToken).ConfigureAwait(false);
        log?.Invoke($"жду завершения (pid={pid}), сервер сохраняет мир");

        var deadline = DateTimeOffset.Now + timeout;
        while (DateTimeOffset.Now < deadline)
        {
            if (RunningProcessId() is null)
            {
                log?.Invoke("остановлен");
                return true;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }

        log?.Invoke("не дождался, снимаю принудительно");
        try
        {
            using var process = Process.GetProcessById(pid.Value);
            process.Kill(entireProcessTree: true);
        }
        catch (ArgumentException)
        {
        }

        return true;
    }

    /// <summary>Запускает этот же exe в отдельном окне-процессе в режиме супервизора.</summary>
    public int StartDetached()
    {
        Directory.CreateDirectory(config.LogPath);
        var startInfo = new ProcessStartInfo(Environment.ProcessPath ?? "dstfarm.exe")
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Minimized,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        startInfo.ArgumentList.Add("supervise");

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("не удалось запустить супервизор");
        return process.Id;
    }
}
