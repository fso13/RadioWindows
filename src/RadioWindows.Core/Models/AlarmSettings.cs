namespace RadioWindows.Core.Models;

/// <summary>
/// Persisted schedule for the wake-up radio. <see cref="Weekdays"/> uses
/// Sunday-first numbering (1 = Sunday … 7 = Saturday), matching the macOS app;
/// the UI displays them Monday-first.
/// </summary>
public sealed class AlarmSettings
{
    public static readonly IReadOnlyList<int> WeekdayOrder = [2, 3, 4, 5, 6, 7, 1];

    public bool Enabled { get; set; }

    public int Hour { get; set; } = 7;

    public int Minute { get; set; } = 30;

    public HashSet<int> Weekdays { get; set; } = [2, 3, 4, 5, 6];

    public string? StationId { get; set; }

    public int FadeInSeconds { get; set; } = 30;

    public AlarmSettings Clone() => new()
    {
        Enabled = Enabled,
        Hour = Hour,
        Minute = Minute,
        Weekdays = [.. Weekdays],
        StationId = StationId,
        FadeInSeconds = FadeInSeconds,
    };

    public AlarmSettings Normalized()
    {
        return new AlarmSettings
        {
            Enabled = Enabled,
            Hour = Math.Clamp(Hour, 0, 23),
            Minute = Math.Clamp(Minute, 0, 59),
            Weekdays = Weekdays.Where(day => day is >= 1 and <= 7).ToHashSet(),
            StationId = string.IsNullOrWhiteSpace(StationId) ? null : StationId,
            FadeInSeconds = Math.Clamp(FadeInSeconds, 0, 120),
        };
    }

    public static string ShortLabelKey(int weekday) => $"Alarm.Weekday.Short.{weekday}";

    public static string FullLabelKey(int weekday) => $"Alarm.Weekday.Full.{weekday}";

    /// <summary>Next matching local date strictly after <paramref name="now"/>, or null.</summary>
    public DateTimeOffset? NextFireDate(DateTimeOffset now)
    {
        if (!Enabled || Weekdays.Count == 0)
        {
            return null;
        }

        for (var offset = 0; offset <= 7; offset++)
        {
            var day = now.AddDays(offset);
            var weekday = (int)day.DayOfWeek + 1;
            if (!Weekdays.Contains(weekday))
            {
                continue;
            }

            var candidate = new DateTimeOffset(
                day.Year, day.Month, day.Day, Hour, Minute, 0, now.Offset);
            if (candidate > now)
            {
                return candidate;
            }
        }

        return null;
    }
}
