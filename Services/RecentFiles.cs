using System;
using System.Collections.Generic;
using System.Linq;

namespace Scalpel.Services
{
    /// <summary>
    /// Pure most-recently-used list logic for recently-opened files. No registry, no WPF — the
    /// caller persists the serialized string. Entries are de-duplicated case-insensitively (Windows
    /// paths are case-insensitive), most-recent first, capped to a maximum. The list is serialized
    /// as a '|'-joined string; '|' is illegal in Windows paths, so it is a safe separator.
    /// </summary>
    public static class RecentFiles
    {
        public const int DefaultMax = 10;

        public static List<string> Parse(string? raw) =>
            string.IsNullOrEmpty(raw)
                ? new List<string>()
                : raw!.Split('|').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        public static string Serialize(IEnumerable<string> list) => string.Join("|", list);

        public static List<string> Add(IReadOnlyList<string> current, string path, int max = DefaultMax)
        {
            var result = new List<string> { path };
            result.AddRange(current.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)));
            if (result.Count > max) result.RemoveRange(max, result.Count - max);
            return result;
        }

        public static List<string> Remove(IReadOnlyList<string> current, string path) =>
            current.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
