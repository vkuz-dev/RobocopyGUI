using System;
using System.Globalization;

namespace RobocopyGui
{
    /// <summary>Time-of-day helpers for scheduled start and stop.</summary>
    internal static class Scheduling
    {
        /// <summary>The next moment after <paramref name="from"/> at which the clock shows <paramref name="timeOfDay"/>.</summary>
        public static DateTime NextOccurrence(TimeSpan timeOfDay, DateTime from)
        {
            var candidate = from.Date + timeOfDay;
            return candidate > from ? candidate : candidate.AddDays(1);
        }

        /// <summary>"at 22:00", "tomorrow at 07:00" or "on 2026-09-21 at 22:00".</summary>
        public static string Describe(DateTime when, DateTime now)
        {
            string time = when.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (when.Date == now.Date)
                return "at " + time;
            if (when.Date == now.Date.AddDays(1))
                return "tomorrow at " + time;
            return "on " + when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " at " + time;
        }

        public static string FormatDuration(TimeSpan span)
        {
            if (span < TimeSpan.Zero)
                span = TimeSpan.Zero;
            if (span.TotalHours >= 1)
                return string.Format(CultureInfo.InvariantCulture, "{0} h {1:00} min", (int)span.TotalHours, span.Minutes);
            if (span.TotalMinutes >= 1)
                return string.Format(CultureInfo.InvariantCulture, "{0} min {1:00} s", span.Minutes, span.Seconds);
            return string.Format(CultureInfo.InvariantCulture, "{0} s", span.Seconds);
        }
    }
}
