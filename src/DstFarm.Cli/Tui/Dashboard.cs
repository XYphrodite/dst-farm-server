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
    private static readonly string[] Languages = [Loc.Auto, "ru", "en"];

    private readonly IAnsiConsole console;
    private readonly FarmConfig config;
    private readonly LogBuffer log = new();
    private List<SettingItem> settings;

    private int selected;
    private bool clusterDirty;
    private string? editingBuffer;
    private string message = string.Empty;
    private DateTimeOffset messageAt = DateTimeOffset.MinValue;
    private bool clusterMatches = true;
    private DateTimeOffset clusterCheckedAt = DateTimeOffset.MinValue;
    private ProtectionReport? protections;
    private DateTimeOffset protectionsCheckedAt = DateTimeOffset.MinValue;
    private PlayerReport? players;
    private DateTimeOffset playersCheckedAt = DateTimeOffset.MinValue;
    private bool busy;
    private SteamProgress? download;

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
        SettingItem.Flag(Loc.T("Вечный день", "Eternal day"), () => config.OnlyDay, v => Change(() => config.OnlyDay = v)),
        SettingItem.Flag(Loc.T("Вечная осень", "Eternal autumn"), () => config.EternalAutumn, v => Change(() => config.EternalAutumn = v)),
        SettingItem.Flag(Loc.T("Голод не убивает", "Hunger cannot kill"), () => config.NoHunger, v => Change(() => config.NoHunger = v)),
        SettingItem.Flag(Loc.T("Темнота и твари безвредны", "Darkness and shadows harmless"), () => config.NoSanityDrain, v => Change(() => config.NoSanityDrain = v)),
        SettingItem.Flag(Loc.T("Без боссов и тварей", "No bosses or hostiles"), () => config.DisableThreats, v => Change(() => config.DisableThreats = v)),
        SettingItem.Choice(Loc.T("Размер мира", "World size"), WorldSizes, () => config.WorldSize, v => Change(() => config.WorldSize = v)),
        SettingItem.Choice(Loc.T("Режим", "Game mode"), GameModes, () => config.GameMode, v => Change(() => config.GameMode = v)),
        SettingItem.Flag(Loc.T("Пещеры (второй шард)", "Caves (second shard)"), () => config.EnableCaves, v => Change(() => config.EnableCaves = v)),
        SettingItem.Text(Loc.T("Имя сервера", "Server name"), () => config.ClusterName, v => Change(() => config.ClusterName = v)),
        SettingItem.Text(Loc.T("Пароль", "Password"), () => config.ClusterPassword, v => Change(() => config.ClusterPassword = v), masked: true),
        SettingItem.Number(Loc.T("Порт", "Port"), 1024, 65000, 1, () => config.ServerPort, v => Change(() => config.ServerPort = v)),
        SettingItem.Number(Loc.T("Максимум игроков", "Max players"), 1, 64, 1, () => config.MaxPlayers, v => Change(() => config.MaxPlayers = v)),
        SettingItem.Number("Steam master port", 1024, 65000, 1, () => config.MasterServerPort, v => Change(() => config.MasterServerPort = v)),
        SettingItem.Number("Steam auth port", 1024, 65000, 1, () => config.AuthenticationPort, v => Change(() => config.AuthenticationPort = v)),
        SettingItem.Flag(Loc.T("Перезапуск при падении", "Restart on crash"), () => config.RestartOnExit, v => config.RestartOnExit = v),
        SettingItem.Number(Loc.T("Пауза перед рестартом, с", "Restart delay, s"), 0, 600, 5, () => config.RestartDelaySeconds, v => config.RestartDelaySeconds = v),
        SettingItem.Number(Loc.T("Плановый рестарт, час (-1 выкл)", "Daily restart hour (-1 off)"), -1, 23, 1, () => config.DailyRestartHour, v => config.DailyRestartHour = v),
        SettingItem.Text("Cluster token", () => config.ClusterToken, ApplyToken, masked: true),
        SettingItem.Choice(
            Loc.T("Язык", "Language"),
            Languages,
            () => config.Language,
            value =>
            {
                config.Language = value;
                Loc.Current = Loc.Resolve(value);
                // Подписи строятся один раз, поэтому пересобираем их на новом языке.
                settings = BuildSettings();
            }),
    ];

    /// <summary>Сообщение показывается ограниченное время: устаревшее вводит в заблуждение.</summary>
    private string Message
    {
        get => DateTimeOffset.Now - messageAt < TimeSpan.FromSeconds(20) ? message : string.Empty;
        set
        {
            message = value;
            messageAt = DateTimeOffset.Now;
        }
    }

    /// <summary>
    /// Совпадают ли файлы кластера с настройками на экране. Проверяем не каждый кадр:
    /// это чтение с диска.
    /// </summary>
    private bool ClusterInSync()
    {
        if (DateTimeOffset.Now - clusterCheckedAt > TimeSpan.FromSeconds(2))
        {
            clusterMatches = ClusterWriter.MatchesDisk(config);
            clusterCheckedAt = DateTimeOffset.Now;
        }

        return clusterMatches;
    }

    /// <summary>Лог шарда весит сотни килобайт, а панель перерисовывается несколько раз в секунду.</summary>
    private ProtectionReport Protections()
    {
        if (protections is null || DateTimeOffset.Now - protectionsCheckedAt > TimeSpan.FromSeconds(5))
        {
            protections = WorldProtections.Inspect(config);
            protectionsCheckedAt = DateTimeOffset.Now;
        }

        return protections;
    }

    /// <summary>Тот же кеш, что и у защит: лог тяжёлый, а панель перерисовывается часто.</summary>
    private PlayerReport Players()
    {
        if (players is null || DateTimeOffset.Now - playersCheckedAt > TimeSpan.FromSeconds(3))
        {
            players = PlayerWatch.Inspect(config);
            playersCheckedAt = DateTimeOffset.Now;
        }

        return players;
    }

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
        log.Add(Loc.T($"токен записан в {config.ClusterTokenFile}", $"token written to {config.ClusterTokenFile}"));
    }

    /// <summary>Полноэкранный режим требует настоящего терминала: в пайпе Spectre не умеет прятать курсор.</summary>
    public static bool IsInteractiveConsole => !Console.IsInputRedirected && !Console.IsOutputRedirected;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        log.Add(Loc.T("dstfarm готов. F1 — подсказка по клавишам.", "dstfarm is ready. Press F1 for the key list."));
        if (!File.Exists(config.ServerExe))
            log.Add(Loc.T("сервер ещё не установлен: нажмите I", "the server is not installed yet: press I"));
        if (!config.HasClusterToken())
            log.Add(Loc.T("нет cluster_token.txt: выберите «Cluster token», Enter и вставьте токен Klei", "cluster_token.txt is missing: select \"Cluster token\", press Enter and paste your Klei token"));

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

        console.MarkupLine(Loc.T("[grey]настройки сохранены в config.json[/]", "[grey]settings saved to config.json[/]"));
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
                Message = Loc.T("изменение отменено", "edit cancelled");
                return true;
            case ConsoleKey.Backspace when key.Modifiers.HasFlag(ConsoleModifiers.Control):
            case ConsoleKey.Delete:
                // Токен длинный: стирать его по символу — издевательство.
                editingBuffer = string.Empty;
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
                    Message = Loc.T(
                        "введите значение, Del — очистить, Enter — применить, Esc — отмена",
                        "type a value, Del clears, Enter applies, Esc cancels");
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
            case ConsoleKey.U:
                await SelfUpdateAsync(cancellationToken).ConfigureAwait(false);
                break;
            case ConsoleKey.F1:
                Message = Loc.T("стрелки — выбор и значение, Enter — правка, S — старт/стоп, I — установка сервера, G — применить, U — обновить dstfarm, Q — выход", "arrows select and change, Enter edits, S starts/stops, I installs the server, G applies, U updates dstfarm, Q quits");
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
        clusterCheckedAt = DateTimeOffset.MinValue;
        Message = Loc.T($"кластер обновлён: {written.Count} файлов", $"cluster updated: {written.Count} files");
        log.Add(Loc.T($"кластер записан в {config.ClusterPath}", $"cluster written to {config.ClusterPath}"));
    }

    private async Task InstallAsync(CancellationToken cancellationToken)
    {
        if (busy)
        {
            Message = Loc.T("уже идёт установка", "an install is already running");
            return;
        }

        busy = true;
        Message = Loc.T("установка сервера, это надолго", "installing the server, this takes a while");
        try
        {
            var installer = new SteamCmdInstaller(config);
            var progress = new Progress<SteamProgress>(report => download = report);
            await installer.InstallServerAsync(validate: true, line => log.Add(line), progress, cancellationToken).ConfigureAwait(false);
            config.Save();
            Message = Loc.T("сервер установлен", "server installed");
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or IOException)
        {
            log.Add(Loc.T($"ошибка установки: {exception.Message}", $"install failed: {exception.Message}"));
            Message = Loc.T("установка не удалась, подробности в логе", "install failed, see the log");
        }
        finally
        {
            busy = false;
            download = null;
        }
    }

    /// <summary>Текстовый бар: внутри Live-дисплея встроенный прогресс Spectre использовать нельзя.</summary>
    private static string RenderBar(SteamProgress report, int cells = 24)
    {
        var filled = (int)Math.Round(Math.Clamp(report.Percent, 0, 100) / 100 * cells);
        var bar = new string('#', filled) + new string('-', cells - filled);
        var text = report.HasTotal
            ? $"{report.Percent:F1}%  {SteamProgress.Format(report.BytesDone)} / {SteamProgress.Format(report.BytesTotal)}"
            : $"{report.Percent:F1}%";
        var state = string.IsNullOrWhiteSpace(report.State) ? Loc.T("загрузка", "downloading") : report.State;
        return $"[cyan][[{Markup.Escape(bar)}]][/] {Markup.Escape(text)}  [grey]{Markup.Escape(state)}[/]";
    }

    private async Task SelfUpdateAsync(CancellationToken cancellationToken)
    {
        if (busy)
        {
            Message = Loc.T("дождитесь окончания текущей операции", "wait for the current operation to finish");
            return;
        }

        if (supervisorTask is not null)
        {
            Message = Loc.T("сначала остановите сервер клавишей S", "stop the server first with S");
            return;
        }

        if (Environment.ProcessPath is not { } exePath)
        {
            Message = Loc.T("не удалось определить путь к dstfarm.exe", "could not determine the path to dstfarm.exe");
            return;
        }

        busy = true;
        Message = Loc.T("проверяю обновления", "checking for updates");
        try
        {
            var updater = new SelfUpdater();
            var release = await updater.FetchLatestAsync(cancellationToken).ConfigureAwait(false);
            if (release is null)
            {
                Message = Loc.T("релиз с файлом dstfarm.exe не найден", "no release with a dstfarm.exe asset was found");
                return;
            }

            var current = SelfUpdater.CurrentVersion;
            if (release.Version <= current)
            {
                Message = Loc.T($"уже последняя версия ({current.ToString(3)})", $"already the latest version ({current.ToString(3)})");
                return;
            }

            log.Add(Loc.T($"найдено обновление {release.Tag}, качаю", $"update {release.Tag} found, downloading"));
            var progress = new Progress<SteamProgress>(report => download = report);
            var file = await updater.DownloadAsync(release, progress, cancellationToken).ConfigureAwait(false);
            SelfUpdater.Apply(file, exePath);

            Message = Loc.T($"обновлено до {release.Version.ToString(3)} — перезапустите dstfarm", $"updated to {release.Version.ToString(3)} — restart dstfarm");
            log.Add(Loc.T($"новая версия установлена: {exePath}", $"new version installed: {exePath}"));
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or IOException)
        {
            log.Add(Loc.T($"обновление не удалось: {exception.Message}", $"update failed: {exception.Message}"));
            Message = Loc.T("обновление не удалось, подробности в логе", "update failed, see the log");
        }
        finally
        {
            busy = false;
            download = null;
        }
    }

    private async Task ToggleServerAsync(CancellationToken cancellationToken)
    {
        if (supervisorTask is not null)
        {
            Message = Loc.T("останавливаю сервер", "stopping the server");
            await StopSupervisorAsync().ConfigureAwait(false);
            Message = Loc.T("сервер остановлен", "server stopped");
            return;
        }

        if (clusterDirty)
            ApplyCluster();

        if (!File.Exists(config.ServerExe))
        {
            Message = Loc.T("сервер не установлен: нажмите I", "the server is not installed: press I");
            return;
        }

        if (!config.HasClusterToken())
        {
            Message = Loc.T("нет cluster_token.txt: заполните поле Cluster token", "cluster_token.txt is missing: fill in the Cluster token field");
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
                log.Add(Loc.T($"супервизор упал: {exception.Message}", $"supervisor crashed: {exception.Message}"));
            }
        }, CancellationToken.None);
        Message = Loc.T("сервер запускается", "server is starting");
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

        // Панели с фиксированной высотой: шапка 3, лог logRows+2, подвал 3.
        // Остальное достаётся телу, и внутри него настройкам нужны свои рамки.
        var settingsRows = Math.Max(3, height - 3 - (logRows + 2) - 3 - 2);

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

        layout["settings"].Update(new Panel(SettingsTable(settingsRows))
            .Header(Loc.T("[cyan] настройки фарма [/]", "[cyan] farm settings [/]"))
            .Border(BoxBorder.Rounded)
            .Expand());

        layout["status"].Update(new Panel(StatusTable())
            .Header(Loc.T("[cyan] статус [/]", "[cyan] status [/]"))
            .Border(BoxBorder.Rounded)
            .Expand());

        var logWidth = Math.Max(20, console.Profile.Width - 6);
        layout["log"].Update(new Panel(new Rows(log.Tail(logRows)
                .Select(line => new Markup(Markup.Escape(Fit(line, logWidth))))
                .ToArray()))
            .Header(Loc.T("[cyan] лог [/]", "[cyan] log [/]"))
            .Border(BoxBorder.Rounded)
            .Expand());

        layout["footer"].Update(new Panel(new Markup(FooterMarkup()))
            .Border(BoxBorder.Rounded)
            .BorderStyle(new Style(Color.Grey35))
            .Expand());

        return layout;
    }

    /// <summary>Обрезает строку под ширину панели: перенос ломал рамку.</summary>
    private static string Fit(string line, int width)
    {
        var flat = string.Create(line.Length, line, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
                span[i] = char.IsControl(source[i]) ? ' ' : source[i];
        });
        return flat.Length <= width ? flat : string.Concat(flat.AsSpan(0, width - 1), "…");
    }

    private string HeaderMarkup()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
        var state = supervisorTask is not null ? Loc.T("[green]сервер работает[/]", "[green]server running[/]") : Loc.T("[grey]сервер остановлен[/]", "[grey]server stopped[/]");
        var dirty = clusterDirty || !ClusterInSync()
            ? Loc.T("  [yellow]настройки не применены к кластеру (G)[/]", "  [yellow]settings not applied to the cluster (G)[/]")
            : string.Empty;
        if (download is { } report)
            return $"[bold]dstfarm {version}[/]   {RenderBar(report)}";
        return $"[bold]dstfarm {version}[/]   {state}{dirty}";
    }

    /// <summary>
    /// Рисует настройки окном вокруг выбранной строки: список длиннее панели,
    /// а без прокрутки нижние строки просто обрезались и до них нельзя было добраться.
    /// </summary>
    private Table SettingsTable(int capacity)
    {
        var table = new Table().Border(TableBorder.None).HideHeaders().Expand();
        table.AddColumn(new TableColumn(string.Empty));
        table.AddColumn(new TableColumn(string.Empty).RightAligned());

        var (first, last) = VisibleRange(capacity);
        if (first > 0)
            table.AddRow(new Markup($"  [grey]↑ ещё {first}[/]"), new Markup(string.Empty));

        for (var i = first; i <= last; i++)
        {
            var item = settings[i];
            var focused = i == selected;
            var label = focused ? $"[cyan1 bold]> {Markup.Escape(item.Label)}[/]" : $"  {Markup.Escape(item.Label)}";
            var value = focused && editingBuffer is not null
                ? $"[black on cyan1]{Markup.Escape(editingBuffer)}|[/]"
                : item.Display;
            table.AddRow(new Markup(label), new Markup(value));
        }

        var below = settings.Count - 1 - last;
        if (below > 0)
            table.AddRow(new Markup($"  [grey]↓ ещё {below}[/]"), new Markup(string.Empty));

        return table;
    }

    /// <summary>Какие строки показать, чтобы выбранная всегда была видна.</summary>
    private (int First, int Last) VisibleRange(int capacity) =>
        ListWindow.Around(selected, settings.Count, capacity);

    private Table StatusTable()
    {
        var uptime = new UptimeTracker(config).Read();
        var table = new Table().Border(TableBorder.None).HideHeaders().Expand();
        table.AddColumn(new TableColumn(string.Empty));
        table.AddColumn(new TableColumn(string.Empty).RightAligned());

        table.AddRow(Loc.T("кластер", "cluster"), Markup.Escape(config.Cluster));
        table.AddRow(Loc.T("сервер", "server"), File.Exists(config.ServerExe) ? Loc.T("[green]установлен[/]", "[green]installed[/]") : Loc.T("[red]нет[/]", "[red]missing[/]"));
        table.AddRow("cluster_token", config.HasClusterToken() ? Loc.T("[green]есть[/]", "[green]present[/]") : Loc.T("[red]нет[/]", "[red]missing[/]"));
        table.AddRow(Loc.T("порт", "port"), config.ServerPort.ToString(CultureInfo.InvariantCulture));

        if (supervisorTask is not null)
        {
            table.AddRow(Loc.T("порты", "ports"), Loc.T("[grey]слушает сервер[/]", "[grey]held by the server[/]"));
        }
        else
        {
            var conflicts = PortProbe.Conflicts(config);
            table.AddRow(
                Loc.T("порты", "ports"),
                conflicts.Count == 0
                    ? Loc.T("[green]свободны[/]", "[green]free[/]")
                    : Loc.T($"[red]занят {string.Join(", ", conflicts.Select(c => c.Port))}[/]", $"[red]in use: {string.Join(", ", conflicts.Select(c => c.Port))}[/]"));
        }

        var protections = Protections();
        table.AddRow(
            Loc.T("защиты мира", "protections"),
            !protections.LogFound
                ? Loc.T("[grey]нет данных[/]", "[grey]no data[/]")
                : protections.AllApplied
                    ? Loc.T($"[green]все {protections.Total}[/]", $"[green]all {protections.Total}[/]")
                    : $"[yellow]{protections.Applied}/{protections.Total}[/]");

        if (supervisorTask is not null)
        {
            var connected = Players();
            table.AddRow(
                Loc.T("игроков", "players"),
                connected.Count == 0
                    ? Loc.T("[yellow]никого[/]", "[yellow]nobody[/]")
                    : $"[green]{connected.Count}[/] {Markup.Escape(connected.Describe())}");
        }

        var current = supervisor?.StartedAt is { } started
            ? DateTimeOffset.Now - started
            : TimeSpan.Zero;
        table.AddRow(Loc.T("сессия", "session"), current == TimeSpan.Zero ? "[grey]—[/]" : $"{current:hh\\:mm\\:ss}");
        var hours = uptime.Total.TotalHours.ToString("F1", CultureInfo.InvariantCulture);
        table.AddRow(Loc.T("всего аптайма", "total uptime"), Loc.T($"{hours} ч", $"{hours} h"));
        table.AddRow(Loc.T("сессий", "sessions"), uptime.Sessions.ToString(CultureInfo.InvariantCulture));

        foreach (var shard in supervisor?.Snapshot() ?? [])
        {
            var value = shard.Running
                ? $"[green]pid {shard.ProcessId}[/] [grey]/[/] {shard.Restarts}"
                : Loc.T("[grey]не запущен[/]", "[grey]not running[/]");
            table.AddRow(Markup.Escape(shard.Name), value);
        }

        if (Message is { Length: > 0 } note)
            table.AddRow(string.Empty, $"[yellow]{Markup.Escape(note)}[/]");

        return table;
    }

    private string FooterMarkup()
    {
        if (editingBuffer is not null)
            return Loc.T(
                "[cyan]Del[/] очистить   [cyan]Enter[/] применить   [cyan]Esc[/] отмена",
                "[cyan]Del[/] clear   [cyan]Enter[/] apply   [cyan]Esc[/] cancel");
        var toggle = supervisorTask is not null ? Loc.T("остановить", "stop") : Loc.T("запустить", "start");
        return Loc.T($"[cyan]стрелки[/] выбор/значение   [cyan]Enter[/] правка   [cyan]S[/] {toggle}   ", $"[cyan]arrows[/] select/change   [cyan]Enter[/] edit   [cyan]S[/] {toggle}   ") +
               Loc.T($"[cyan]I[/] установить сервер   [cyan]G[/] применить   [cyan]U[/] обновить   [cyan]Q[/] выход", $"[cyan]I[/] install server   [cyan]G[/] apply   [cyan]U[/] update   [cyan]Q[/] quit");
    }
}
