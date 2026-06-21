using System.Text.Json;

namespace Scalpel.E2E;

public sealed record LogEntry(
    DateTime Ts, string Level, string Cat, string Event, string Msg,
    JsonElement? Data, JsonElement? Error)
{
    public bool IsFailure =>
        Level == "ERROR" || Event.StartsWith("crash.") || Event.EndsWith(".fail");

    public static LogEntry? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            DateTime ts = root.TryGetProperty("ts", out var tsEl) &&
                          tsEl.TryGetDateTime(out var parsed)
                ? parsed : default;
            // Clone Data/Error so they survive disposal of the JsonDocument.
            JsonElement? data = root.TryGetProperty("data", out var d) ? d.Clone() : null;
            JsonElement? err = root.TryGetProperty("error", out var er) ? er.Clone() : null;
            return new LogEntry(
                ts,
                root.TryGetProperty("level", out var l) ? l.GetString() ?? "" : "",
                root.TryGetProperty("cat", out var c) ? c.GetString() ?? "" : "",
                root.TryGetProperty("event", out var ev) ? ev.GetString() ?? "" : "",
                root.TryGetProperty("msg", out var m) ? m.GetString() ?? "" : "",
                data, err);
        }
        catch { return null; }
    }
}
