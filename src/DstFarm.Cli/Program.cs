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
            console.MarkupLineInterpolated($"[red]ошибка:[/] {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> RunDashboardAsync(IAnsiConsole console, FarmConfig config, CancellationToken cancellationToken)
    {
        if (!Dashboard.IsInteractiveConsole)
        {
            console.MarkupLine("[yellow]полноэкранный режим требует настоящего терминала[/] (сейчас ввод или вывод перенаправлен).");
            console.MarkupLine("Запустите [cyan]dstfarm[/] прямо в консоли или используйте команды: [cyan]dstfarm --help[/].");
            console.WriteLine();
            return Status(console, config);
        }

        return await new Dashboard(console, config).RunAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> InstallAsync(IAnsiConsole console, FarmConfig config, string[] args, CancellationToken cancellationToken)
    {
        var validate = !args.Contains("--no-validate", StringComparer.OrdinalIgnoreCase);
        var installer = new SteamCmdInstaller(config);
        await installer.InstallServerAsync(validate, line => console.WriteLine(line), cancellationToken).ConfigureAwait(false);
        config.Save();
        return 0;
    }

    private static int Init(IAnsiConsole console, FarmConfig config, string[] args)
    {
        var force = args.Contains("--force", StringComparer.OrdinalIgnoreCase);
        ClusterWriter.Write(config, force, line => console.MarkupLineInterpolated($"[grey]{line}[/]"));
        config.Save();
        console.MarkupLineInterpolated($"конфиг кластера: [cyan]{config.ClusterPath}[/]");
        if (!config.HasClusterToken())
            console.MarkupLine("[yellow]нет cluster_token.txt — без него сервер не стартует. Команда: dstfarm token <ТОКЕН>[/]");
        return 0;
    }

    private static int Token(IAnsiConsole console, FarmConfig config, string[] args)
    {
        if (args.Length < 2)
        {
            console.MarkupLine("[red]использование:[/] dstfarm token <ТОКЕН>");
            return 2;
        }

        config.ClusterToken = args[1].Trim();
        config.Save();
        Directory.CreateDirectory(config.ClusterPath);
        File.WriteAllText(config.ClusterTokenFile, config.ClusterToken + Environment.NewLine);
        console.MarkupLineInterpolated($"токен записан в [cyan]{config.ClusterTokenFile}[/]");
        return 0;
    }

    private static async Task<int> StartAsync(IAnsiConsole console, FarmConfig config, string[] args, CancellationToken cancellationToken)
    {
        if (args.Contains("--detach", StringComparer.OrdinalIgnoreCase))
        {
            var pid = new SupervisorControl(config).StartDetached();
            console.MarkupLineInterpolated($"супервизор в фоне, pid=[cyan]{pid}[/]");
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
        table.AddColumn("параметр");
        table.AddColumn("значение");
        table.AddRow("кластер", $"{config.Cluster}  ({config.ClusterPath})");
        table.AddRow("сервер", File.Exists(config.ServerExe) ? "установлен" : "[red]не установлен[/]");
        table.AddRow("cluster_token", config.HasClusterToken() ? "есть" : "[red]нет[/]");
        table.AddRow("состояние", running ? "[green]работает[/]" : "остановлен");
        table.AddRow("суммарный аптайм", string.Create(CultureInfo.InvariantCulture, $"{uptime.Total.TotalHours:F1} ч за {uptime.Sessions} сессий"));

        foreach (var port in PortProbe.Inspect(config))
        {
            var state = port.Busy ? "[red]занят[/]" : "свободен";
            table.AddRow($"порт {port.Port}", $"{port.Purpose} — {state}");
        }

        console.Write(table);

        var conflicts = PortProbe.Conflicts(config);
        if (conflicts.Count > 0)
        {
            console.MarkupLine("[yellow]Занятые порты нужно освободить или сдвинуть:[/] "
                + "dstfarm config --set MasterServerPort=27020 AuthenticationPort=8770, затем dstfarm init --force");
        }
        return running ? 0 : 1;
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
                    console.MarkupLineInterpolated($"[red]ожидается KEY=VALUE, получено:[/] {pair}");
                    return 2;
                }

                var name = pair[..separator];
                var value = pair[(separator + 1)..];
                var property = typeof(FarmConfig).GetProperty(name,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                if (property is null || !property.CanWrite)
                {
                    console.MarkupLineInterpolated($"[red]неизвестный параметр:[/] {name}");
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
            console.MarkupLineInterpolated($"сохранено: [cyan]{file}[/]");
            console.MarkupLine("[grey]применить к кластеру: dstfarm init --force[/]");
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("параметр");
        table.AddColumn("значение");
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
        console.MarkupLine("[bold]dstfarm[/] — выделенный сервер Don't Starve Together под идл-фарм дропов Klei");
        console.WriteLine();
        console.MarkupLine("  [cyan]dstfarm[/]                    полноэкранный интерфейс (по умолчанию)");
        console.MarkupLine("  [cyan]dstfarm install[/]            steamcmd + установка сервера (app 343050)");
        console.MarkupLine("  [cyan]dstfarm init[/] [grey][[--force]][/]    сгенерировать кластер с фарм-настройками");
        console.MarkupLine("  [cyan]dstfarm token <ТОКЕН>[/]      записать cluster token из аккаунта Klei");
        console.MarkupLine("  [cyan]dstfarm start[/] [grey][[--detach]][/]  поднять сервер и держать живым");
        console.MarkupLine("  [cyan]dstfarm stop[/]               штатная остановка с сохранением мира");
        console.MarkupLine("  [cyan]dstfarm status[/]             состояние и накопленный аптайм");
        console.MarkupLine("  [cyan]dstfarm config[/] [grey][[--set KEY=VALUE ...]][/]");
        return 0;
    }

    private static int Unknown(IAnsiConsole console, string command)
    {
        console.MarkupLineInterpolated($"[red]неизвестная команда:[/] {command}");
        Help(console);
        return 2;
    }
}
