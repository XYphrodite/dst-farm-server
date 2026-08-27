using System.Globalization;
using System.Reflection;
using DstFarm.Core;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace DstFarm.Cli.Tui;

/// <summary>Полноэкранный интерфейс: настройки фарма слева, статус справа, лог снизу.</summary>
internal sealed class Dashboard
{
    private const string EnterAlternateScreen = "[?1049h[H";
    private const string LeaveAlternateScreen = "[?1049l";

    private static readonly string[] WorldSizes = ["small", "medium", "default", "large", "huge"];
    private static readonly string[] GameModes = ["endless", "survival", "wilderness"];

    private readonly IAnsiConsole console;
    private readonly FarmConfig config;
    private readonly LogBuffer log = new();
    private readonly List<SettingItem> settings;

    private int selected;
    private bool clusterDirty;
    private string? editingBuffer;
    private string message = string.Empty;
    private bool busy;

    private ServerSupervisor? supervisor;
    private CancellationTokenSource? supervisorCts;
    private Task? supervisorTask;

    public Dashboard(IAnsiConsole console, FarmConfig config)
    {
        this.console = console ?? throw new ArgumentNullException(nameof(console));
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        settings = BuildSettings();
    }

    private List<SettingItem> BuildSettings() =>
    [
        SettingItem.Flag("Вечный день", () => config.OnlyDay, v => Change(() => config.OnlyDay = v)),
        SettingItem.Flag("Вечная осень", () => config.EternalAutumn, v => Change(() => config.EternalAutumn = v)),
        SettingItem.Flag("Без голода", () => config.NoHunger, v => Change(() => config.NoHunger = v)),
        SettingItem.Flag("Без потери рассудка", () => config.NoSanityDrain, v => Change(() => config.NoSanityDrain = v)),
        SettingItem.Flag("Без боссов и угроз", () => config.DisableThreats, v => Change(() => config.DisableThreats = v)),
        SettingItem.Choice("Размер мира", WorldSizes, () => config.WorldSize, v => Change(() => config.WorldSize = v)),
        SettingItem.Choice("Режим", GameModes, () => config.GameMode, v => Change(() => config.GameMode = v)),
        SettingItem.Flag("Пещеры (второй шард)", () => config.EnableCaves, v => Change(() => config.EnableCaves = v)),
        SettingItem.Text("Имя сервера", () => config.ClusterName, v => Change(() => config.ClusterName = v)),
        SettingItem.Text("Пароль", () => config.ClusterPassword, v => Change(() => config.ClusterPassword = v), masked: true),
        SettingItem.Number("Порт", 1024, 65000, 1, () => config.ServerPort, v => Change(() => config.ServerPort = v)),
        SettingItem.Number("Максимум игроков", 1, 64, 1, () => config.MaxPlayers, v => Change(() => config.MaxPlayers = v)),
        SettingItem.Number("Steam master port", 1024, 65000, 1, () => config.MasterServerPort, v => Change(() => config.MasterServerPort = v)),
        SettingItem.Number("Steam auth port", 1024, 65000, 1, () => config.AuthenticationPort, v => Change(() => config.AuthenticationPort = v)),
        SettingItem.Flag("Перезапуск при падении", () => config.RestartOnExit, v => config.RestartOnExit = v),
        SettingItem.Number("Пауза перед рестартом, с", 0, 600, 5, () => config.RestartDelaySeconds, v => config.RestartDelaySeconds = v),
        SettingItem.Number("Плановый рестарт, час (-1 выкл)", -1, 23, 1, () => config.DailyRestartHour, v => config.DailyRestartHour = v),
        SettingItem.Text("Cluster token", () => config.ClusterToken, ApplyToken, masked: true),
    ];

    private void Change(Action apply)
    {
        apply();
        clusterDirty = true;
    }

    private void ApplyToken(string value)
    {
        config.ClusterToken = value.Trim();
        Directory.CreateDirectory(config.ClusterPath);
        File.WriteAllText(config.ClusterTokenFile, config.ClusterToken + Environment.NewLine);
        log.Add($"токен записан в {config.ClusterTokenFile}");
    }

    /// <summary>Полноэкранный режим требует настоящего терминала: в пайпе Spectre не умеет прятать курсор.</summary>
    public static bool IsInteractiveConsole => !Console.IsInputRedirected && !Console.IsOutputRedirected;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        log.Add("dstfarm готов. F1 — подсказка по клавишам.");
        if (!File.Exists(config.ServerExe))
            log.Add("сервер ещё не установлен: нажмите I");
        if (!config.HasClusterToken())
            log.Add("нет cluster_token.txt: выберите «Cluster token», Enter и вставьте токен Klei");

        console.Write(new ControlCode(EnterAlternateScreen));
        try
        {
            await console.Live(new Text(string.Empty))
                .AutoClear(true)
                .Overflow(VerticalOverflow.Crop)
                .StartAsync(async context => await LoopAsync(context, cancellationToken).ConfigureAwait(false))
                .ConfigureAwait(false);
        }
        finally
        {
            await StopSupervisorAsync().ConfigureAwait(false);
            console.Write(new ControlCode(LeaveAlternateScreen));
            config.Save();
        }

        console.MarkupLine("[grey]настройки сохранены в config.json[/]");
        return 0;
    }

    private async Task LoopAsync(LiveDisplayContext context, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            context.UpdateTarget(Render());
            context.Refresh();

            if (!KeyAvailable())
            {
                await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken).ConfigureAwait(false);
                continue;
            }

            var key = Console.ReadKey(intercept: true);
            if (editingBuffer is not null)
            {
                if (HandleEditing(key))
                    continue;
                continue;
            }

            if (await HandleCommandAsync(key, cancellationToken).ConfigureAwait(false))
                return;
        }
    }

    private static bool KeyAvailable()
    {
        try
        {
            return Console.KeyAvailable;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private bool HandleEditing(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Enter:
                settings[selected].WriteText?.Invoke(editingBuffer ?? string.Empty);
                editingBuffer = null;
                clusterDirty = true;
                return true;
            case ConsoleKey.Escape:
                editingBuffer = null;
                message = "изменение отменено";
                return true;
            case ConsoleKey.Backspace:
                if (editingBuffer!.Length > 0)
                    editingBuffer = editingBuffer[..^1];
                return true;
            default:
                if (!char.IsControl(key.KeyChar))
                    editingBuffer += key.KeyChar;
                return true;
        }
    }

    /// <returns><c>true</c>, если пора выходить из интерфейса.</returns>
    private async Task<bool> HandleCommandAsync(ConsoleKeyInfo key, CancellationToken cancellationToken)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                selected = (selected - 1 + settings.Count) % settings.Count;
                break;
            case ConsoleKey.DownArrow:
                selected = (selected + 1) % settings.Count;
                break;
            case ConsoleKey.LeftArrow:
                settings[selected].Cycle(-1);
                break;
            case ConsoleKey.RightArrow:
            case ConsoleKey.Spacebar:
                settings[selected].Cycle(1);
                break;
            case ConsoleKey.Enter:
                if (settings[selected].IsEditable)
                {
                    editingBuffer = settings[selected].ReadText?.Invoke() ?? string.Empty;
                    message = "введите значение, Enter — применить, Esc — отмена";
                }
                else
                {
                    settings[selected].Cycle(1);
                }

                break;
            case ConsoleKey.F5:
            case ConsoleKey.S:
                await ToggleServerAsync(cancellationToken).ConfigureAwait(false);
                break;
            case ConsoleKey.I:
                await InstallAsync(cancellationToken).ConfigureAwait(false);
                break;
            case ConsoleKey.G:
                ApplyCluster();
                break;
            case ConsoleKey.F1:
                message = "стрелки — выбор и значение, Enter — правка, S — старт/стоп, I — установка, G — применить, Q — выход";
                break;
            case ConsoleKey.Q:
            case ConsoleKey.Escape:
                return true;
            default:
                break;
        }

        return false;
    }

    private void ApplyCluster()
    {
        var written = ClusterWriter.Write(config, overwrite: true, line => log.Add(line));
        config.Save();
        clusterDirty = false;
        message = $"кластер обновлён: {written.Count} файлов";
        log.Add($"кластер записан в {config.ClusterPath}");
    }

    private async Task InstallAsync(CancellationToken cancellationToken)
    {
        if (busy)
        {
            message = "уже идёт установка";
            return;
        }

        busy = true;
        message = "установка сервера, это надолго";
        try
        {
            var installer = new SteamCmdInstaller(config);
            await installer.InstallServerAsync(validate: true, line => log.Add(line), cancellationToken).ConfigureAwait(false);
            config.Save();
            message = "сервер установлен";
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or IOException)
        {
            log.Add($"ошибка установки: {exception.Message}");
            message = "установка не удалась, подробности в логе";
        }
        finally
        {
            busy = false;
        }
    }

    private async Task ToggleServerAsync(CancellationToken cancellationToken)
    {
        if (supervisorTask is not null)
        {
            message = "останавливаю сервер";
            await StopSupervisorAsync().ConfigureAwait(false);
            message = "сервер остановлен";
            return;
        }

        if (clusterDirty)
            ApplyCluster();

        if (!File.Exists(config.ServerExe))
        {
            message = "сервер не установлен: нажмите I";
            return;
        }

        if (!config.HasClusterToken())
        {
            message = "нет cluster_token.txt: заполните поле Cluster token";
            return;
        }

        supervisor = new ServerSupervisor(config);
        supervisor.Log += line => log.Add(line);
        supervisorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = supervisorCts.Token;
        var instance = supervisor;
        supervisorTask = Task.Run(async () =>
        {
            try
            {
                await instance.RunAsync(token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException)
            {
                log.Add($"супервизор упал: {exception.Message}");
            }
        }, CancellationToken.None);
        message = "сервер запускается";
    }

    private async Task StopSupervisorAsync()
    {
        if (supervisorTask is null)
            return;
        await (supervisorCts?.CancelAsync() ?? Task.CompletedTask).ConfigureAwait(false);
        try
        {
            await supervisorTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        supervisorCts?.Dispose();
        supervisor?.Dispose();
        supervisorCts = null;
        supervisor = null;
        supervisorTask = null;
    }

    private IRenderable Render()
    {
        var height = Math.Max(24, console.Profile.Height);
        var logRows = Math.Clamp(height - settings.Count - 10, 4, 16);

        var layout = new Layout("root").SplitRows(
            new Layout("header").Size(3),
            new Layout("body"),
            new Layout("log").Size(logRows + 2),
            new Layout("footer").Size(3));

        layout["body"].SplitColumns(
            new Layout("settings"),
            new Layout("status").Size(42));

        layout["header"].Update(new Panel(new Markup(HeaderMarkup()))
            .Border(BoxBorder.Rounded)
            .BorderStyle(new Style(Color.Cyan1))
            .Expand());

        layout["settings"].Update(new Panel(SettingsTable())
            .Header("[cyan] настройки фарма [/]")
            .Border(BoxBorder.Rounded)
            .Expand());

        layout["status"].Update(new Panel(StatusTable())
            .Header("[cyan] статус [/]")
            .Border(BoxBorder.Rounded)
            .Expand());

        layout["log"].Update(new Panel(new Rows(log.Tail(logRows).Select(line => new Markup(Markup.Escape(line))).ToArray()))
            .Header("[cyan] лог [/]")
            .Border(BoxBorder.Rounded)
            .Expand());

        layout["footer"].Update(new Panel(new Markup(FooterMarkup()))
            .Border(BoxBorder.Rounded)
            .BorderStyle(new Style(Color.Grey35))
            .Expand());

        return layout;
    }

    private string HeaderMarkup()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
        var state = supervisorTask is not null ? "[green]сервер работает[/]" : "[grey]сервер остановлен[/]";
        var dirty = clusterDirty ? "  [yellow]есть непринятые изменения (G)[/]" : string.Empty;
        return $"[bold]dstfarm {version}[/]   {state}{dirty}";
    }

    private Table SettingsTable()
    {
        var table = new Table().Border(TableBorder.None).HideHeaders().Expand();
        table.AddColumn(new TableColumn(string.Empty));
        table.AddColumn(new TableColumn(string.Empty).RightAligned());

        for (var i = 0; i < settings.Count; i++)
        {
            var item = settings[i];
            var focused = i == selected;
            var label = focused ? $"[cyan1 bold]> {Markup.Escape(item.Label)}[/]" : $"  {Markup.Escape(item.Label)}";
            var value = focused && editingBuffer is not null
                ? $"[black on cyan1]{Markup.Escape(editingBuffer)}|[/]"
                : item.Display;
            table.AddRow(new Markup(label), new Markup(value));
        }

        return table;
    }

    private Table StatusTable()
    {
        var uptime = new UptimeTracker(config).Read();
        var table = new Table().Border(TableBorder.None).HideHeaders().Expand();
        table.AddColumn(new TableColumn(string.Empty));
        table.AddColumn(new TableColumn(string.Empty).RightAligned());

        table.AddRow("кластер", Markup.Escape(config.Cluster));
        table.AddRow("сервер", File.Exists(config.ServerExe) ? "[green]установлен[/]" : "[red]нет[/]");
        table.AddRow("cluster_token", config.HasClusterToken() ? "[green]есть[/]" : "[red]нет[/]");
        table.AddRow("порт", config.ServerPort.ToString(CultureInfo.InvariantCulture));

        var conflicts = PortProbe.Conflicts(config);
        table.AddRow(
            "порты",
            conflicts.Count == 0
                ? "[green]свободны[/]"
                : $"[red]занят {string.Join(", ", conflicts.Select(c => c.Port))}[/]");

        var current = supervisor?.StartedAt is { } started
            ? DateTimeOffset.Now - started
            : TimeSpan.Zero;
        table.AddRow("сессия", current == TimeSpan.Zero ? "[grey]—[/]" : $"{current:hh\\:mm\\:ss}");
        table.AddRow("всего аптайма", string.Create(CultureInfo.InvariantCulture, $"{uptime.Total.TotalHours:F1} ч"));
        table.AddRow("сессий", uptime.Sessions.ToString(CultureInfo.InvariantCulture));

        foreach (var shard in supervisor?.Snapshot() ?? [])
        {
            var value = shard.Running
                ? $"[green]pid {shard.ProcessId}[/]  рестартов: {shard.Restarts}"
                : "[grey]не запущен[/]";
            table.AddRow(Markup.Escape(shard.Name), value);
        }

        if (!string.IsNullOrEmpty(message))
            table.AddRow(string.Empty, $"[yellow]{Markup.Escape(message)}[/]");

        return table;
    }

    private string FooterMarkup()
    {
        if (editingBuffer is not null)
            return "[cyan]Enter[/] применить   [cyan]Esc[/] отмена";
        var toggle = supervisorTask is not null ? "остановить" : "запустить";
        return $"[cyan]стрелки[/] выбор/значение   [cyan]Enter[/] правка   [cyan]S[/] {toggle}   " +
               $"[cyan]I[/] установить сервер   [cyan]G[/] применить настройки   [cyan]Q[/] выход";
    }
}
