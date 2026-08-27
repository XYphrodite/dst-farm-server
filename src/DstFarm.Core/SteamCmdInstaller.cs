using System.Diagnostics;
using System.IO.Compression;
using System.Globalization;

namespace DstFarm.Core;

/// <summary>Разворачивает steamcmd и ставит DST Dedicated Server (app 343050, анонимный вход).</summary>
public sealed class SteamCmdInstaller(FarmConfig config)
{
    private readonly FarmConfig config = config ?? throw new ArgumentNullException(nameof(config));

    public async Task<string> EnsureSteamCmdAsync(Action<string>? log, CancellationToken cancellationToken)
    {
        if (File.Exists(config.SteamCmdExe))
            return config.SteamCmdExe;

        Directory.CreateDirectory(config.SteamCmdPath);
        var archive = Path.Combine(config.SteamCmdPath, "steamcmd.zip");
        log?.Invoke($"качаю steamcmd -> {archive}");

        using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
        await using (var source = await client.GetStreamAsync(FarmConfig.SteamCmdUrl, cancellationToken).ConfigureAwait(false))
        await using (var target = File.Create(archive))
        {
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }

        ZipFile.ExtractToDirectory(archive, config.SteamCmdPath, overwriteFiles: true);
        File.Delete(archive);

        if (!File.Exists(config.SteamCmdExe))
            throw new InvalidOperationException($"steamcmd.exe не появился в {config.SteamCmdPath}");

        return config.SteamCmdExe;
    }

    public async Task<string> InstallServerAsync(bool validate, Action<string>? log, CancellationToken cancellationToken)
    {
        await EnsureSteamCmdAsync(log, cancellationToken).ConfigureAwait(false);
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

        var exitCode = await RunSteamCmdAsync(arguments, log, cancellationToken).ConfigureAwait(false);

        // steamcmd после самообновления штатно отдаёт 7, при успехе — 0.
        if ((exitCode != 0 && exitCode != 7) || !File.Exists(config.ServerExe))
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"steamcmd вернул {exitCode}, сервер не установлен: {config.ServerExe}"));

        log?.Invoke($"готово: {config.ServerExe}");
        return config.ServerExe;
    }

    private async Task<int> RunSteamCmdAsync(IEnumerable<string> arguments, Action<string>? log, CancellationToken cancellationToken)
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
        process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) log?.Invoke(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) log?.Invoke(e.Data); };

        if (!process.Start())
            throw new InvalidOperationException("не удалось запустить steamcmd");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }
}
