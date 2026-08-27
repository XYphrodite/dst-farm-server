using System.Globalization;
using DstFarm.Cli.Tui;
using DstFarm.Core;
using Spectre.Console;

namespace DstFarm.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var console = AnsiConsole.Console;
        var config = FarmConfig.Load();
        Loc.Current = Loc.Resolve(config.Language);

        if (Environment.ProcessPath is { } processPath)
            SelfUpdater.CleanupBackup(processPath);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var command = args.Length > 0 ? args[0].ToLowerInvariant() : "ui";

        try
        {
            return command switch
            {
                "ui" or "dashboard" => await RunDashboardAsync(console, config, cts.Token).ConfigureAwait(false),
                "install" => await InstallAsync(console, config, args, cts.Token).ConfigureAwait(false),
                "init" => Init(console, config, args),
                "token" => Token(console, config, args),
                "start" => await StartAsync(console, config, args, cts.Token).ConfigureAwait(false),
                "supervise" => await SuperviseAsync(console, config, cts.Token).ConfigureAwait(false),
                "stop" => await StopAsync(console, config, cts.Token).ConfigureAwait(false),
                "status" => Status(console, config),
                "update" => await UpdateAsync(console, config, args, cts.Token).ConfigureAwait(false),
                "config" => ShowConfig(console, config, args),
                "--help" or "-h" or "help" => Help(console),
                _ => Unknown(console, command),
            };
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
        catch (InvalidOperationException exception)
        {
            console.MarkupLine(Loc.T($"[red]ошибка:[/] {Markup.Escape(exception.Message)}", $"[red]error:[/] {Markup.Escape(exception.Message)}"));
            return 1;
        }
    }

    private static async Task<int> RunDashboardAsync(IAnsiConsole console, FarmConfig config, CancellationToken cancellationToken)
    {
        if (!Dashboard.IsInteractiveConsole)
        {
            console.MarkupLine(Loc.T("[yellow]полноэкранный режим требует настоящего терминала[/] (сейчас ввод или вывод перенаправлен).", "[yellow]the full-screen mode needs a real terminal[/] (input or output is redirected right now)."));
            console.MarkupLine(Loc.T("Запустите [cyan]dstfarm[/] прямо в консоли или используйте команды: [cyan]dstfarm --help[/].", "Run [cyan]dstfarm[/] in a console window, or use the commands: [cyan]dstfarm --help[/]."));
            console.WriteLine();
            return Status(console, config);
        }

        return await new Dashboard(console, config).RunAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> InstallAsync(IAnsiConsole console, FarmConfig config, string[] args, CancellationToken cancellationToken)
    {
        var validate = !args.Contains("--no-validate", StringComparer.OrdinalIgnoreCase);
        var installer = new SteamCmdInstaller(config);

        if (!Dashboard.IsInteractiveConsole)
        {
            // Без живой консоли рисовать бар нечем: печатаем вехи, но не каждую строку steamcmd.
            var reporter = new ThrottledProgressReporter(line => console.WriteLine(line));
            await installer.InstallServerAsync(validate, line => console.WriteLine(line), reporter, cancellationToken).ConfigureAwait(false);
            config.Save();
            return 0;
        }

        // Внутри Live-дисплея писать в консоль нельзя, поэтому лог копим и показываем только при провале.
        var tail = new Queue<string>();
        void Remember(string line)
        {
            tail.Enqueue(line);
            while (tail.Count > 20)
                tail.Dequeue();
        }

        try
        {
            await console.Progress()
                .AutoClear(false)
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new RemainingTimeColumn(),
                    new SpinnerColumn())
                .StartAsync(async context =>
                {
                    var task = context.AddTask(Loc.T("подготовка", "preparing"), maxValue: 100);
                    var progress = new Progress<SteamProgress>(report =>
                    {
                        task.Description = Markup.Escape(Describe(report));
                        task.Value = Math.Clamp(report.Percent, 0, 100);
                    });

                    await installer.InstallServerAsync(validate, Remember, progress, cancellationToken).ConfigureAwait(false);
                    task.Description = Loc.T("готово", "done");
                    task.Value = 100;
                }).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            foreach (var line in tail)
                console.WriteLine(line);
            throw;
        }

        config.Save();
        console.MarkupLine(Loc.T($"сервер установлен: [cyan]{config.ServerExe}[/]", $"server installed: [cyan]{config.ServerExe}[/]"));
        return 0;
    }

    private static string Describe(SteamProgress report)
    {
        var state = string.IsNullOrWhiteSpace(report.State) ? Loc.T("загрузка", "downloading") : report.State;
        return report.HasTotal
            ? $"{state}  {SteamProgress.Format(report.BytesDone)} / {SteamProgress.Format(report.BytesTotal)}"
            : state;
    }

    /// <summary>Печатает прогресс не чаще раза в 3 секунды и на каждых 5 процентах.</summary>
    private sealed class ThrottledProgressReporter(Action<string> write) : IProgress<SteamProgress>
    {
        private readonly Action<string> write = write;
        private DateTimeOffset last = DateTimeOffset.MinValue;
        private int lastBucket = -1;

        public void Report(SteamProgress value)
        {
            var bucket = (int)(value.Percent / 5);
            var now = DateTimeOffset.Now;
            if (bucket == lastBucket && now - last < TimeSpan.FromSeconds(3))
                return;

            lastBucket = bucket;
            last = now;
            write($"{Describe(value)}  —  {value.Percent.ToString("F1", CultureInfo.InvariantCulture)}%");
        }
    }

    private static int Init(IAnsiConsole console, FarmConfig config, string[] args)
    {
        var force = args.Contains("--force", StringComparer.OrdinalIgnoreCase);
        ClusterWriter.Write(config, force, line => console.MarkupLineInterpolated($"[grey]{line}[/]"));
        config.Save();
        console.MarkupLine(Loc.T($"конфиг кластера: [cyan]{config.ClusterPath}[/]", $"cluster config: [cyan]{config.ClusterPath}[/]"));
        if (!config.HasClusterToken())
            console.MarkupLine(Loc.T("[yellow]нет cluster_token.txt — без него сервер не стартует. Команда: dstfarm token <ТОКЕН>[/]", "[yellow]cluster_token.txt is missing — the server will not start without it. Run: dstfarm token <TOKEN>[/]"));
        return 0;
    }

    private static int Token(IAnsiConsole console, FarmConfig config, string[] args)
    {
        if (args.Length < 2)
        {
            console.MarkupLine(Loc.T("[red]использование:[/] dstfarm token <ТОКЕН>", "[red]usage:[/] dstfarm token <TOKEN>"));
            return 2;
        }

        config.ClusterToken = args[1].Trim();
        config.Save();
        Directory.CreateDirectory(config.ClusterPath);
        File.WriteAllText(config.ClusterTokenFile, config.ClusterToken + Environment.NewLine);
        console.MarkupLine(Loc.T($"токен записан в [cyan]{config.ClusterTokenFile}[/]", $"token written to [cyan]{config.ClusterTokenFile}[/]"));
        return 0;
    }

    private static async Task<int> StartAsync(IAnsiConsole console, FarmConfig config, string[] args, CancellationToken cancellationToken)
    {
        if (args.Contains("--detach", StringComparer.OrdinalIgnoreCase))
        {
            var pid = new SupervisorControl(config).StartDetached();
            console.MarkupLine(Loc.T($"супервизор в фоне, pid=[cyan]{pid}[/]", $"supervisor running in the background, pid=[cyan]{pid}[/]"));
            return 0;
        }

        return await SuperviseAsync(console, config, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> SuperviseAsync(IAnsiConsole console, FarmConfig config, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(config.ClusterPath))
            ClusterWriter.Write(config, overwrite: false);

        using var supervisor = new ServerSupervisor(config);
        supervisor.Log += line => console.WriteLine(line);
        await supervisor.RunAsync(cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> StopAsync(IAnsiConsole console, FarmConfig config, CancellationToken cancellationToken)
    {
        var control = new SupervisorControl(config);
        var stopped = await control.StopAsync(TimeSpan.FromSeconds(90), line => console.WriteLine(line), cancellationToken).ConfigureAwait(false);
        return stopped ? 0 : 1;
    }

    private static int Status(IAnsiConsole console, FarmConfig config)
    {
        var control = new SupervisorControl(config);
        var uptime = new UptimeTracker(config).Read();
        var running = control.IsRunning;

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn(Loc.T("параметр", "setting"));
        table.AddColumn(Loc.T("значение", "value"));
        table.AddRow(Loc.T("кластер", "cluster"), $"{config.Cluster}  ({config.ClusterPath})");
        table.AddRow(Loc.T("сервер", "server"), File.Exists(config.ServerExe) ? Loc.T("установлен", "installed") : Loc.T("[red]не установлен[/]", "[red]not installed[/]"));
        table.AddRow("cluster_token", config.HasClusterToken() ? Loc.T("есть", "present") : Loc.T("[red]нет[/]", "[red]missing[/]"));
        table.AddRow(
            Loc.T("настройки", "settings"),
            ClusterWriter.MatchesDisk(config)
                ? Loc.T("применены к кластеру", "applied to the cluster")
                : Loc.T("[yellow]не применены — dstfarm init --force[/]", "[yellow]not applied — dstfarm init --force[/]"));
        table.AddRow(Loc.T("состояние", "state"), running ? Loc.T("[green]работает[/]", "[green]running[/]") : Loc.T("остановлен", "stopped"));
        var hours = uptime.Total.TotalHours.ToString("F1", CultureInfo.InvariantCulture);
        table.AddRow(
            Loc.T("суммарный аптайм", "total uptime"),
            Loc.T($"{hours} ч за {uptime.Sessions} сессий", $"{hours} h over {uptime.Sessions} sessions"));

        foreach (var port in PortProbe.Inspect(config))
        {
            // Когда сервер поднят, порты занимает он сам — это не конфликт.
            var state = port.Busy
                ? running ? Loc.T("[grey]слушает сервер[/]", "[grey]held by the server[/]") : Loc.T("[red]занят[/]", "[red]in use[/]")
                : Loc.T("свободен", "free");
            table.AddRow(Loc.T($"порт {port.Port}", $"port {port.Port}"), $"{port.Purpose} — {state}");
        }

        console.Write(table);

        var conflicts = running ? [] : PortProbe.Conflicts(config);
        if (conflicts.Count > 0)
        {
            console.MarkupLine(Loc.T("[yellow]Занятые порты нужно освободить или сдвинуть:[/] ", "[yellow]Free the busy ports or move them:[/] ")
                + Loc.T("dstfarm config --set MasterServerPort=27020 AuthenticationPort=8770, затем dstfarm init --force", "dstfarm config --set MasterServerPort=27020 AuthenticationPort=8770, then dstfarm init --force"));
        }
        return running ? 0 : 1;
    }

    private static async Task<int> UpdateAsync(IAnsiConsole console, FarmConfig config, string[] args, CancellationToken cancellationToken)
    {
        var checkOnly = args.Contains("--check", StringComparer.OrdinalIgnoreCase);
        var force = args.Contains("--force", StringComparer.OrdinalIgnoreCase);
        var updater = new SelfUpdater();
        var current = SelfUpdater.CurrentVersion;

        console.MarkupLine(Loc.T($"текущая версия: [cyan]{current.ToString(3)}[/]", $"current version: [cyan]{current.ToString(3)}[/]"));

        var release = await updater.FetchLatestAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(Loc.T("не нашёл релиз с файлом dstfarm.exe", "no release with a dstfarm.exe asset was found"));

        console.MarkupLine(Loc.T($"последний релиз: [cyan]{release.Tag}[/]", $"latest release: [cyan]{release.Tag}[/]"));

        if (release.Version <= current && !force)
        {
            console.MarkupLine(Loc.T("[green]обновление не требуется[/]", "[green]already up to date[/]"));
            return 0;
        }

        if (checkOnly)
        {
            console.MarkupLine(Loc.T($"[yellow]доступно обновление[/] {release.Version.ToString(3)}: dstfarm update", $"[yellow]update available[/] {release.Version.ToString(3)}: dstfarm update"));
            return 0;
        }

        if (Environment.ProcessPath is not { } exePath)
            throw new InvalidOperationException(Loc.T("не удалось определить путь к dstfarm.exe", "could not determine the path to dstfarm.exe"));

        if (new SupervisorControl(config).IsRunning)
            console.MarkupLine(Loc.T("[yellow]сервер запущен: новая версия начнёт работать после dstfarm stop и следующего запуска[/]", "[yellow]the server is running: the new version takes effect after dstfarm stop and the next start[/]"));

        if (release.Sha256 is null)
            console.MarkupLine(Loc.T("[yellow]в описании релиза нет SHA-256, проверка контрольной суммы пропущена[/]", "[yellow]the release notes carry no SHA-256, checksum verification skipped[/]"));

        string downloaded;
        if (Dashboard.IsInteractiveConsole)
        {
            downloaded = string.Empty;
            await console.Progress()
                .AutoClear(false)
                .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new RemainingTimeColumn())
                .StartAsync(async context =>
                {
                    var task = context.AddTask(Loc.T("загрузка обновления", "downloading update"), maxValue: 100);
                    var progress = new Progress<SteamProgress>(report =>
                    {
                        task.Description = Markup.Escape(Describe(report));
                        task.Value = Math.Clamp(report.Percent, 0, 100);
                    });
                    downloaded = await updater.DownloadAsync(release, progress, cancellationToken).ConfigureAwait(false);
                    task.Value = 100;
                }).ConfigureAwait(false);
        }
        else
        {
            var reporter = new ThrottledProgressReporter(line => console.WriteLine(line));
            downloaded = await updater.DownloadAsync(release, reporter, cancellationToken).ConfigureAwait(false);
        }

        SelfUpdater.Apply(downloaded, exePath);
        console.MarkupLine(Loc.T($"[green]обновлено до {release.Version.ToString(3)}[/]: {exePath}", $"[green]updated to {release.Version.ToString(3)}[/]: {exePath}"));
        console.MarkupLine(Loc.T("[grey]старая версия останется рядом как .old и удалится при следующем запуске[/]", "[grey]the old build stays next to it as .old and is removed on the next run[/]"));
        return 0;
    }

    private static int ShowConfig(IAnsiConsole console, FarmConfig config, string[] args)
    {
        var setIndex = Array.FindIndex(args, a => a.Equals("--set", StringComparison.OrdinalIgnoreCase));
        if (setIndex >= 0)
        {
            foreach (var pair in args.Skip(setIndex + 1))
            {
                var separator = pair.IndexOf('=', StringComparison.Ordinal);
                if (separator <= 0)
                {
                    console.MarkupLine(Loc.T($"[red]ожидается KEY=VALUE, получено:[/] {pair}", $"[red]expected KEY=VALUE, got:[/] {pair}"));
                    return 2;
                }

                var name = pair[..separator];
                var value = pair[(separator + 1)..];
                var property = typeof(FarmConfig).GetProperty(name,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                if (property is null || !property.CanWrite)
                {
                    console.MarkupLine(Loc.T($"[red]неизвестный параметр:[/] {name}", $"[red]unknown setting:[/] {name}"));
                    return 2;
                }

                object converted = property.PropertyType == typeof(bool)
                    ? value is "1" or "true" or "yes" or "on" or "да"
                    : property.PropertyType == typeof(int)
                        ? int.Parse(value, CultureInfo.InvariantCulture)
                        : value;
                property.SetValue(config, converted);
            }

            var file = config.Save();
            console.MarkupLine(Loc.T($"сохранено: [cyan]{file}[/]", $"saved: [cyan]{file}[/]"));
            console.MarkupLine(Loc.T("[grey]применить к кластеру: dstfarm init --force[/]", "[grey]apply to the cluster: dstfarm init --force[/]"));
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn(Loc.T("параметр", "setting"));
        table.AddColumn(Loc.T("значение", "value"));
        foreach (var property in typeof(FarmConfig).GetProperties().Where(p => p.CanWrite))
        {
            var value = property.GetValue(config)?.ToString() ?? string.Empty;
            if (property.Name == nameof(FarmConfig.ClusterToken) && value.Length > 6)
                value = value[..6] + "...";
            table.AddRow(property.Name, value);
        }

        console.Write(table);
        return 0;
    }

    private static int Help(IAnsiConsole console)
    {
        console.MarkupLine(Loc.T("[bold]dstfarm[/] — выделенный сервер Don't Starve Together под идл-фарм дропов Klei", "[bold]dstfarm[/] — a Don't Starve Together dedicated server for idle Klei drop farming"));
        console.WriteLine();
        console.MarkupLine(Loc.T("  [cyan]dstfarm[/]                    полноэкранный интерфейс (по умолчанию)", "  [cyan]dstfarm[/]                    full-screen interface (default)"));
        console.MarkupLine(Loc.T("  [cyan]dstfarm install[/]            steamcmd + установка сервера (app 343050)", "  [cyan]dstfarm install[/]            steamcmd + server install (app 343050)"));
        console.MarkupLine(Loc.T("  [cyan]dstfarm init[/] [grey][[--force]][/]    сгенерировать кластер с фарм-настройками", "  [cyan]dstfarm init[/] [grey][[--force]][/]    generate the cluster with farm settings"));
        console.MarkupLine(Loc.T("  [cyan]dstfarm token <ТОКЕН>[/]      записать cluster token из аккаунта Klei", "  [cyan]dstfarm token <TOKEN>[/]      store the cluster token from your Klei account"));
        console.MarkupLine(Loc.T("  [cyan]dstfarm start[/] [grey][[--detach]][/]  поднять сервер и держать живым", "  [cyan]dstfarm start[/] [grey][[--detach]][/]  start the server and keep it alive"));
        console.MarkupLine(Loc.T("  [cyan]dstfarm stop[/]               штатная остановка с сохранением мира", "  [cyan]dstfarm stop[/]               graceful stop, the world is saved"));
        console.MarkupLine(Loc.T("  [cyan]dstfarm status[/]             состояние и накопленный аптайм", "  [cyan]dstfarm status[/]             state and accumulated uptime"));
        console.MarkupLine(Loc.T("  [cyan]dstfarm update[/] [grey][[--check]][/]   обновить себя из релиза на GitHub", "  [cyan]dstfarm update[/] [grey][[--check]][/]   update itself from the GitHub release"));
        console.MarkupLine("  [cyan]dstfarm config[/] [grey][[--set KEY=VALUE ...]][/]");
        return 0;
    }

    private static int Unknown(IAnsiConsole console, string command)
    {
        console.MarkupLine(Loc.T($"[red]неизвестная команда:[/] {command}", $"[red]unknown command:[/] {command}"));
        Help(console);
        return 2;
    }
}
