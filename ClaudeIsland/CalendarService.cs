using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace ClaudeIsland;

public record CalEvent(string Title, DateTime Start, DateTime End);

/// Next meeting from a calendar's private iCal (.ics) link (Google Calendar: "Secret address in iCal format";
/// Outlook: a published calendar link). No sign-in or permissions needed. The link is kept in your local settings only.
public class CalendarService
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public List<CalEvent> Upcoming { get; private set; } = new();
    public string Status { get; private set; } = "";
    public event Action? Changed; // raised on a background thread

    DateTime lastFetch = DateTime.MinValue;
    bool busy, lastOk;

    public void Tick()
    {
        var s = AppSettings.Current;
        if (!s.ShowCalendar || string.IsNullOrWhiteSpace(s.CalendarUrl))
        {
            if (Upcoming.Count > 0 || Status != "") { Upcoming = new(); Status = ""; Changed?.Invoke(); }
            return;
        }
        if (busy) return;
        double age = (DateTime.Now - lastFetch).TotalMinutes;
        if (lastOk ? age < 15 : age < 2) return;
        _ = Fetch();
    }

    public void Invalidate() { lastFetch = DateTime.MinValue; lastOk = false; }

    public CalEvent? Next(DateTime now) => Upcoming.Where(e => e.End > now).OrderBy(e => e.Start).FirstOrDefault();

    async Task Fetch()
    {
        busy = true;
        lastFetch = DateTime.Now;
        try
        {
            string url = AppSettings.Current.CalendarUrl.Trim();
            if (url.StartsWith("webcal", StringComparison.OrdinalIgnoreCase)) url = "https" + url[6..];
            string text = await Http.GetStringAsync(url);
            Upcoming = IcsParser.Parse(text, DateTime.Now.AddHours(-1), DateTime.Now.AddHours(48));
            Status = Upcoming.Count == 0 ? "Connected · nothing in the next 2 days" : $"Connected · {Upcoming.Count} upcoming";
            lastOk = true;
        }
        catch { Status = "Couldn't load the calendar link"; lastOk = false; }
        finally { busy = false; }
        Changed?.Invoke();
    }
}

/// A small iCalendar reader: single events plus DAILY/WEEKLY/MONTHLY/YEARLY repeats (INTERVAL, COUNT, UNTIL, BYDAY, EXDATE).
static class IcsParser
{
    class Raw
    {
        public Dictionary<string, (string Params, string Value)> P = new();
        public List<string> ExDates = new();
    }

    public static List<CalEvent> Parse(string ics, DateTime from, DateTime to)
    {
        // unfold continuation lines
        var lines = new List<string>();
        foreach (var l in ics.Replace("\r\n", "\n").Split('\n'))
        {
            if (l.StartsWith(' ') || l.StartsWith('\t')) { if (lines.Count > 0) lines[^1] += l[1..]; }
            else lines.Add(l);
        }

        var events = new List<Raw>();
        Raw? cur = null;
        foreach (var l in lines)
        {
            if (l == "BEGIN:VEVENT") { cur = new Raw(); continue; }
            if (l == "END:VEVENT") { if (cur != null) events.Add(cur); cur = null; continue; }
            if (cur == null) continue;
            int colon = l.IndexOf(':');
            if (colon < 0) continue;
            string head = l[..colon], value = l[(colon + 1)..];
            string name = head, pars = "";
            int semi = head.IndexOf(';');
            if (semi >= 0) { name = head[..semi]; pars = head[(semi + 1)..]; }
            name = name.ToUpperInvariant();
            if (name == "EXDATE") cur.ExDates.AddRange(value.Split(','));
            else cur.P[name] = (pars, value);
        }

        // instances replaced by an edited copy of a repeating event
        var replaced = new HashSet<string>();
        foreach (var e in events)
            if (e.P.TryGetValue("RECURRENCE-ID", out var rid) && e.P.TryGetValue("UID", out var uid) && ParseDate(rid.Value, rid.Params, out var rs, out _))
                replaced.Add(uid.Value + "|" + rs.ToString("s"));

        var result = new List<CalEvent>();
        foreach (var e in events)
        {
            if (e.P.TryGetValue("STATUS", out var st) && st.Value.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase)) continue;
            if (!e.P.TryGetValue("DTSTART", out var ds) || !ParseDate(ds.Value, ds.Params, out var start, out bool allDay)) continue;
            if (allDay) continue; // all-day items aren't meetings

            DateTime end = start.AddHours(1);
            if (e.P.TryGetValue("DTEND", out var de) && ParseDate(de.Value, de.Params, out var e2, out _)) end = e2;
            var length = end - start;
            string title = Unescape(e.P.TryGetValue("SUMMARY", out var sm) ? sm.Value : "(no title)");
            string uidv = e.P.TryGetValue("UID", out var u) ? u.Value : "";

            if (!e.P.TryGetValue("RRULE", out var rr))
            {
                if (end > from && start < to) result.Add(new CalEvent(title, start, end));
                continue;
            }

            var ex = new HashSet<string>();
            foreach (var x in e.ExDates) if (ParseDate(x, "", out var xd, out _)) ex.Add(xd.ToString("s"));
            foreach (var occ in Expand(start, rr.Value, from - length, to))
            {
                if (ex.Contains(occ.ToString("s")) || replaced.Contains(uidv + "|" + occ.ToString("s"))) continue;
                result.Add(new CalEvent(title, occ, occ + length));
            }
        }
        return result.OrderBy(r => r.Start).ToList();
    }

    static IEnumerable<DateTime> Expand(DateTime start, string rule, DateTime from, DateTime to)
    {
        var kv = rule.Split(';').Select(p => p.Split('=', 2)).Where(p => p.Length == 2)
                     .ToDictionary(p => p[0].ToUpperInvariant(), p => p[1]);
        string freq = kv.GetValueOrDefault("FREQ", "");
        int interval = int.TryParse(kv.GetValueOrDefault("INTERVAL", "1"), out int iv) && iv > 0 ? iv : 1;
        int count = int.TryParse(kv.GetValueOrDefault("COUNT", "0"), out int cn) ? cn : 0;
        DateTime until = DateTime.MaxValue;
        if (kv.TryGetValue("UNTIL", out var un) && ParseDate(un, "", out var ud, out _)) until = ud;
        var days = (kv.GetValueOrDefault("BYDAY", "")).Split(',', StringSplitOptions.RemoveEmptyEntries)
                      .Select(d => DayOfWeekFrom(d[^2..])).Where(d => d != null).Select(d => d!.Value).ToList();

        int produced = 0;
        IEnumerable<DateTime> Candidates()
        {
            switch (freq)
            {
                case "DAILY":
                    for (int k = 0; k < 6000; k++) yield return start.AddDays((long)k * interval);
                    break;
                case "WEEKLY":
                    {
                        var weekStart = start.Date.AddDays(-(((int)start.DayOfWeek + 6) % 7)); // Monday
                        var set = days.Count > 0 ? days : new List<DayOfWeek> { start.DayOfWeek };
                        for (int k = 0; k < 1500; k++)
                            foreach (var d in set.OrderBy(d => ((int)d + 6) % 7))
                            {
                                var date = weekStart.AddDays(k * 7L * interval + ((int)d + 6) % 7);
                                var occ = date + start.TimeOfDay;
                                if (occ >= start) yield return occ;
                            }
                        break;
                    }
                case "MONTHLY":
                    for (int k = 0; k < 600; k++)
                    {
                        var m = new DateTime(start.Year, start.Month, 1).AddMonths(k * interval);
                        if (start.Day <= DateTime.DaysInMonth(m.Year, m.Month)) yield return new DateTime(m.Year, m.Month, start.Day) + start.TimeOfDay;
                    }
                    break;
                case "YEARLY":
                    for (int k = 0; k < 100; k++) { var y = start.AddYears(k * interval); yield return y; }
                    break;
            }
        }

        foreach (var occ in Candidates())
        {
            if (occ > until || occ > to) yield break;
            produced++;
            if (count > 0 && produced > count) yield break;
            if (occ >= from) yield return occ;
        }
    }

    static DayOfWeek? DayOfWeekFrom(string d) => d.ToUpperInvariant() switch
    {
        "MO" => DayOfWeek.Monday, "TU" => DayOfWeek.Tuesday, "WE" => DayOfWeek.Wednesday, "TH" => DayOfWeek.Thursday,
        "FR" => DayOfWeek.Friday, "SA" => DayOfWeek.Saturday, "SU" => DayOfWeek.Sunday, _ => null,
    };

    static bool ParseDate(string value, string pars, out DateTime local, out bool allDay)
    {
        local = default;
        allDay = false;
        value = value.Trim();
        if (value.Length == 8)
        {
            allDay = true;
            return DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out local);
        }
        bool utc = value.EndsWith('Z');
        string v = utc ? value[..^1] : value;
        if (!DateTime.TryParseExact(v, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) return false;

        if (utc) { local = DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime(); return true; }

        string tz = pars.Split(';').Select(p => p.Split('=', 2)).Where(p => p.Length == 2 && p[0].Equals("TZID", StringComparison.OrdinalIgnoreCase))
                        .Select(p => p[1].Trim('"')).FirstOrDefault() ?? "";
        if (tz != "")
        {
            try { local = TimeZoneInfo.ConvertTime(dt, TimeZoneInfo.FindSystemTimeZoneById(tz), TimeZoneInfo.Local); return true; }
            catch { }
        }
        local = dt;   // floating time, or a zone name Windows doesn't know: treat as local
        return true;
    }

    static string Unescape(string s) => s.Replace("\\n", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");
}
