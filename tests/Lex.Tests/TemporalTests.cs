using Lex.Temporal;

namespace Lex.Tests;

public class TemporalTests
{
    [Fact]
    public void Contains_open_interval()
    {
        var iv = new DateInterval(new DateOnly(2020, 1, 1), null);
        Assert.True(iv.Contains(new DateOnly(2030, 1, 1)));
        Assert.False(iv.Contains(new DateOnly(2019, 12, 31)));
    }

    [Fact]
    public void Contains_closed_interval_is_inclusive_both_ends()
    {
        var iv = new DateInterval(new DateOnly(2020, 1, 1), new DateOnly(2020, 12, 31));
        Assert.True(iv.Contains(new DateOnly(2020, 1, 1)));
        Assert.True(iv.Contains(new DateOnly(2020, 12, 31)));
        Assert.False(iv.Contains(new DateOnly(2021, 1, 1)));
    }

    private sealed record Item(string Name, DateInterval Iv);

    [Fact]
    public void Resolve_picks_latest_start_when_intervals_overlap()
    {
        var items = new[]
        {
            new Item("old", new DateInterval(new DateOnly(2020, 1, 1), null)),
            new Item("new", new DateInterval(new DateOnly(2022, 1, 1), null)),
        };
        var hit = AsOfResolver.Resolve(items, i => i.Iv, new DateOnly(2023, 6, 1));
        Assert.Equal("new", hit!.Name);
    }

    [Fact]
    public void Resolve_returns_null_outside_all_intervals()
    {
        var items = new[] { new Item("a", new DateInterval(new DateOnly(2020, 1, 1), new DateOnly(2020, 6, 30))) };
        Assert.Null(AsOfResolver.Resolve(items, i => i.Iv, new DateOnly(2019, 1, 1)));
    }
}
