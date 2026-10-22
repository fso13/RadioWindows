using RadioWindows.Core.Models;
using Xunit;

namespace RadioWindows.Core.Tests;

public sealed class AlarmTests
{
    [Fact]
    public void Finds_the_next_matching_weekday()
    {
        var alarm = new AlarmSettings
        {
            Enabled = true,
            Hour = 7,
            Minute = 30,
            Weekdays = [2, 3, 4, 5, 6], // Monday..Friday
        };

        // 2026-10-23 is a Friday at 08:00 → next is Monday 2026-10-26 07:30.
        var now = new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.FromHours(3));
        var next = alarm.NextFireDate(now);
        Assert.NotNull(next);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 7, 30, 0, TimeSpan.FromHours(3)), next);
    }

    [Fact]
    public void Uses_today_when_time_is_still_ahead()
    {
        var alarm = new AlarmSettings { Enabled = true, Hour = 9, Minute = 0, Weekdays = [6] }; // Friday
        var now = new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.FromHours(3));
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 9, 0, 0, TimeSpan.FromHours(3)), alarm.NextFireDate(now));
    }

    [Fact]
    public void Disabled_or_empty_alarms_never_fire()
    {
        var now = DateTimeOffset.Now;
        Assert.Null(new AlarmSettings { Enabled = false, Weekdays = [2] }.NextFireDate(now));
        Assert.Null(new AlarmSettings { Enabled = true, Weekdays = [] }.NextFireDate(now));
    }

    [Fact]
    public void Settings_are_clamped()
    {
        var normalized = new AlarmSettings { Hour = 99, Minute = -5, FadeInSeconds = 1000, Weekdays = [0, 3, 8] }
            .Normalized();
        Assert.Equal(23, normalized.Hour);
        Assert.Equal(0, normalized.Minute);
        Assert.Equal(120, normalized.FadeInSeconds);
        Assert.Equal([3], normalized.Weekdays);
    }
}
