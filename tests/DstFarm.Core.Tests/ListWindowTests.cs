using DstFarm.Core;
using Xunit;

namespace DstFarm.Core.Tests;

public sealed class ListWindowTests
{
    [Fact]
    public void ShowsEverythingWhenItFits()
    {
        Assert.Equal((0, 18), ListWindow.Around(selected: 0, count: 19, capacity: 19));
        Assert.Equal((0, 18), ListWindow.Around(selected: 18, count: 19, capacity: 40));
    }

    /// <summary>Ровно та поломка, из-за которой до строки Cluster token нельзя было добраться.</summary>
    [Theory]
    [InlineData(19, 5)]
    [InlineData(19, 8)]
    [InlineData(19, 12)]
    [InlineData(19, 18)]
    [InlineData(40, 3)]
    public void SelectedRowIsAlwaysVisible(int count, int capacity)
    {
        for (var selected = 0; selected < count; selected++)
        {
            var (first, last) = ListWindow.Around(selected, count, capacity);

            Assert.True(first <= selected, $"строка {selected} выше окна {first}..{last}");
            Assert.True(selected <= last, $"строка {selected} ниже окна {first}..{last}");
        }
    }

    [Theory]
    [InlineData(19, 5)]
    [InlineData(19, 12)]
    [InlineData(40, 4)]
    public void WindowNeverExceedsTheAvailableRows(int count, int capacity)
    {
        for (var selected = 0; selected < count; selected++)
        {
            var (first, last) = ListWindow.Around(selected, count, capacity);
            var rows = last - first + 1;
            var markers = (first > 0 ? 1 : 0) + (last < count - 1 ? 1 : 0);

            Assert.True(rows + markers <= capacity, $"строк {rows} плюс указателей {markers} не влезает в {capacity}");
            Assert.True(rows >= 1);
        }
    }

    [Fact]
    public void StaysAtTheTopWhileTheSelectionIsThere()
    {
        var (first, _) = ListWindow.Around(selected: 0, count: 19, capacity: 6);

        Assert.Equal(0, first);
    }

    [Fact]
    public void ReachesTheVeryLastRow()
    {
        var (_, last) = ListWindow.Around(selected: 18, count: 19, capacity: 6);

        Assert.Equal(18, last);
    }

    [Fact]
    public void HandlesEmptyAndSingleItemLists()
    {
        Assert.Equal((0, -1), ListWindow.Around(selected: 0, count: 0, capacity: 5));
        Assert.Equal((0, 0), ListWindow.Around(selected: 0, count: 1, capacity: 5));
        Assert.Equal((0, 0), ListWindow.Around(selected: 0, count: 1, capacity: 1));
    }

    [Fact]
    public void ClampsSelectionOutsideTheList()
    {
        var (first, last) = ListWindow.Around(selected: 999, count: 19, capacity: 6);

        Assert.True(last <= 18);
        Assert.True(first >= 0);
    }
}
