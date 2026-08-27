using DstFarm.Core;
namespace DstFarm.Cli.Tui;

/// <summary>Одна строка панели настроек: как показать и что делать по стрелкам.</summary>
internal sealed class SettingItem
{
    private readonly Func<string> display;
    private readonly Action<int>? cycle;

    private SettingItem(string label, Func<string> display, Action<int>? cycle, Func<string>? readText, Action<string>? writeText)
    {
        Label = label;
        this.display = display;
        this.cycle = cycle;
        ReadText = readText;
        WriteText = writeText;
    }

    public string Label { get; }

    public Func<string>? ReadText { get; }

    public Action<string>? WriteText { get; }

    public bool IsEditable => WriteText is not null;

    public string Display => display();

    public void Cycle(int direction) => cycle?.Invoke(direction);

    public static SettingItem Flag(string label, Func<bool> get, Action<bool> set) =>
        new(label, () => get() ? Loc.T("[green]вкл[/]", "[green]on[/]") : Loc.T("[grey]выкл[/]", "[grey]off[/]"), _ => set(!get()), null, null);

    public static SettingItem Choice(string label, IReadOnlyList<string> values, Func<string> get, Action<string> set) =>
        new(
            label,
            () => Spectre.Console.Markup.Escape(get()),
            direction =>
            {
                var current = get();
                var index = 0;
                for (var i = 0; i < values.Count; i++)
                {
                    if (string.Equals(values[i], current, StringComparison.Ordinal))
                    {
                        index = i;
                        break;
                    }
                }

                var next = (index + direction + values.Count) % values.Count;
                set(values[next]);
            },
            null,
            null);

    public static SettingItem Number(string label, int minimum, int maximum, int step, Func<int> get, Action<int> set) =>
        new(
            label,
            () => get().ToString(System.Globalization.CultureInfo.InvariantCulture),
            direction => set(Math.Clamp(get() + (direction * step), minimum, maximum)),
            () => get().ToString(System.Globalization.CultureInfo.InvariantCulture),
            text =>
            {
                if (int.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                    set(Math.Clamp(parsed, minimum, maximum));
            });

    public static SettingItem Text(string label, Func<string> get, Action<string> set, bool masked = false) =>
        new(
            label,
            () =>
            {
                var value = get();
                if (string.IsNullOrEmpty(value))
                    return "[grey]—[/]";
                return masked ? new string('*', Math.Min(value.Length, 12)) : Spectre.Console.Markup.Escape(value);
            },
            null,
            get,
            set);
}
