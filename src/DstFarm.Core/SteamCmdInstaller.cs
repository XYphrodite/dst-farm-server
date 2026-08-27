using System.Diagnostics;
using System.IO.Compression;
using System.Globalization;

namespace DstFarm.Core;

/// <summary>Разворачивает steamcmd и ставит DST Dedicated Server (app 343050, анонимный вход).</summary>
public sealed class SteamCmdInstaller(FarmConfig config)
{
    private const int MaxInstallAttempts = 3;

    private readonly FarmConfig config = config ?? throw new ArgumentNullException(nameof(config));

    public async Task<string> EnsureSteamCmdAsync(Action<string>? log, IProgress<SteamProgress>? progress, CancellationToken cancellationToken)
    {
        if (File.Exists(config.SteamCmdExe))
            return config.SteamCmdExe;

        Directory.CreateDirectory(config.SteamCmdPath);
        var archive = Path.Combine(config.SteamCmdPath, "steamcmd.zip");
        log?.Invoke($"качаю steamcmd -> {archive}");

        using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
        using (var response = await client.GetAsync(FarmConfig.SteamCmdUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? 0;

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var target = File.Create(archive);
            await CopyWithProgressAsync(source, target, total, "steamcmd", progress, cancellationToken).ConfigureAwait(false);
        }

        ZipFile.ExtractToDirectory(archive, config.SteamCmdPath, overwriteFiles: true);
        File.Delete(archive);

        if (!File.Exists(config.SteamCmdExe))
            throw new InvalidOperationException($"steamcmd.exe не появился в {config.SteamCmdPath}");

        return config.SteamCmdExe;
    }

    public async Task<string> InstallServerAsync(bool validate, Action<string>? log, IProgress<SteamProgress>? progress, CancellationToken cancellationToken)
    {
        await EnsureSteamCmdAsync(log, progress, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(config.ServerPath);

        var arguments = new List<string>
        {
            "+force_install_dir", config.ServerPath,
            "+login", "anonymous",
            "+app_update", FarmConfig.DedicatedServerAppId,
        };
        if (validate)
            arguments.Add("validate");
        arguments.Add("+quit");

        log?.Invoke($"установка DST Dedicated Server в {config.ServerPath}");
        log?.Invoke("первый прогон качает ~2.9 ГБ и разворачивает ~4.2 ГБ, это надолго");

        // На чистой машине первый запуск уходит на самообновление steamcmd: он выходит
        // с кодом 7, не выполнив app_update. Поэтому пробуем несколько раз.
        var exitCode = 0;
        for (var attempt = 1; attempt <= MaxInstallAttempts; attempt++)
        {
            exitCode = await RunSteamCmdAsync(arguments, log, progress, cancellationToken).ConfigureAwait(false);
            if (File.Exists(config.ServerExe))
                break;

            if (exitCode != 0 && exitCode != 7)
                break;

            if (attempt < MaxInstallAttempts)
                log?.Invoke($"steamcmd обновил сам себя (код {exitCode}), повторяю установку — попытка {attempt + 1}");
        }

        if (!File.Exists(config.ServerExe))
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"steamcmd вернул {exitCode}, сервер не установлен: {config.ServerExe}"));

        log?.Invoke($"готово: {config.ServerExe}");
        return config.ServerExe;
    }

    private static async Task CopyWithProgressAsync(
        Stream source,
        Stream target,
        long total,
        string state,
        IProgress<SteamProgress>? progress,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            copied += read;
            var percent = total > 0 ? copied * 100.0 / total : 0;
            progress?.Report(new SteamProgress(state, percent, copied, total));
        }
    }

    private async Task<int> RunSteamCmdAsync(
        IEnumerable<string> arguments,
        Action<string>? log,
        IProgress<SteamProgress>? progress,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(config.SteamCmdExe)
        {
            WorkingDirectory = config.SteamCmdPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => Handle(e.Data, log, progress);
        process.ErrorDataReceived += (_, e) => Handle(e.Data, log, progress);

        if (!process.Start())
            throw new InvalidOperationException("не удалось запустить steamcmd");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    /// <summary>Строки прогресса уходят в прогресс-бар, остальное — в лог.</summary>
    private static void Handle(string? line, Action<string>? log, IProgress<SteamProgress>? progress)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        // Пустой прогресс (0 из 0) steamcmd печатает между этапами — он только сбрасывал бы бар.
        if (SteamCmdOutput.TryParseProgress(line, out var parsed) && (parsed.HasTotal || parsed.Percent > 0))
        {
            progress?.Report(parsed);
            if (progress is null)
                log?.Invoke(line);
            return;
        }

        log?.Invoke(line);
    }
}
