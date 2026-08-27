using System.Globalization;

namespace DstFarm.Core;

public enum Language
{
    Russian,
    English,
}

/// <summary>
/// Локализация. Пары строк живут прямо в месте вызова: словаря ключей нет,
/// поэтому нечему рассинхронизироваться и незачем искать, что значит ключ.
/// </summary>
public static class Loc
{
    public const string Auto = "auto";

    private static Language current = Detect();

    public static Language Current
    {
        get => current;
        set => current = value;
    }

    /// <summary>Русский — только для русской системы, остальным английский.</summary>
    public static Language Detect() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase)
            ? Language.Russian
            : Language.English;

    /// <summary><c>auto</c>, <c>ru</c>, <c>en</c>; всё непонятное — как auto.</summary>
    public static Language Resolve(string? setting) => setting?.Trim().ToLowerInvariant() switch
    {
        "ru" or "russian" or "рус" => Language.Russian,
        "en" or "english" => Language.English,
        _ => Detect(),
    };

    public static string Name(Language language) => language == Language.Russian ? "ru" : "en";

    public static string T(string russian, string english) =>
        Current == Language.Russian ? russian : english;
}
