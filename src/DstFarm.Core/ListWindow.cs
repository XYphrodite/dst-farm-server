namespace DstFarm.Core;

/// <summary>
/// Какой кусок длинного списка показать, чтобы выбранная строка всегда была видна.
/// Без этого нижние настройки просто обрезались панелью и до них нельзя было добраться.
/// </summary>
public static class ListWindow
{
    /// <param name="selected">Индекс выбранной строки.</param>
    /// <param name="count">Сколько всего строк.</param>
    /// <param name="capacity">Сколько строк влезает в панель, включая указатели «ещё N».</param>
    public static (int First, int Last) Around(int selected, int count, int capacity)
    {
        if (count <= 0)
            return (0, -1);

        selected = Math.Clamp(selected, 0, count - 1);
        if (capacity >= count)
            return (0, count - 1);

        capacity = Math.Max(1, capacity);

        // Указатель снизу нужен почти всегда, сверху — только когда список уже прокручен.
        var window = Math.Max(1, capacity - 1);
        var first = Math.Clamp(selected - (window / 2), 0, count - window);
        if (first > 0)
        {
            window = Math.Max(1, capacity - 2);
            first = Math.Clamp(selected - (window / 2), 0, count - window);
        }

        // Внизу указателя нет, если конец списка виден — тогда можно показать строкой больше.
        if (first + window >= count)
        {
            window = Math.Max(1, capacity - (first > 0 ? 1 : 0));
            first = Math.Max(0, count - window);
        }

        return (first, Math.Min(count - 1, first + window - 1));
    }
}
