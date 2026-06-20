using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Scalpel.Services
{
    /// <summary>
    /// Local-only JSONL session logger. One file per app session under
    /// %LOCALAPPDATA%\Scalpel\logs. Thread-safe; never throws into callers.
    /// </summary>
    public static class Logger
    {
        public enum Level { Debug = 0, Info = 1, Warn = 2, Error = 3 }

        private static readonly object _gate = new();
        private static readonly JsonSerializerOptions _json = new() { WriteIndented = false };

        private static StreamWriter? _writer;
        private static string _dir = DefaultDir;
        private static string? _currentFile;
        private static Level _minLevel = Level.Debug;

        public static bool Enabled { get; set; } = true;
        public static string LogDirectory => _dir;

        private static string DefaultDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Scalpel", "logs");

        /// <summary>Open a new session log, sweeping logs older than 7 days.</summary>
        public static void Init(string? baseDir = null, Level minLevel = Level.Debug, bool enabled = true)
        {
            lock (_gate)
            {
                _dir = baseDir ?? DefaultDir;
                _minLevel = minLevel;
                Enabled = enabled;
                try
                {
                    Directory.CreateDirectory(_dir);
                    SweepOldLogs(_dir, TimeSpan.FromDays(7));
                    if (enabled) OpenWriter();
                }
                catch { /* logging must never throw */ }
            }
        }

        /// <summary>Toggle logging at runtime; opens the session file lazily when enabled.</summary>
        public static void SetEnabled(bool on)
        {
            lock (_gate)
            {
                Enabled = on;
                try { if (on && _writer == null) OpenWriter(); }
                catch { }
            }
        }

        public static void Debug(string cat, string evt, string msg, object? data = null) => Write(Level.Debug, cat, evt, msg, null, data);
        public static void Info (string cat, string evt, string msg, object? data = null) => Write(Level.Info,  cat, evt, msg, null, data);
        public static void Warn (string cat, string evt, string msg, object? data = null) => Write(Level.Warn,  cat, evt, msg, null, data);
        public static void Error(string cat, string evt, string msg, Exception? ex = null, object? data = null) => Write(Level.Error, cat, evt, msg, ex, data);

        public static void Flush()
        {
            lock (_gate) { try { _writer?.Flush(); } catch { } }
        }

        public static void Shutdown()
        {
            lock (_gate)
            {
                try { _writer?.Flush(); _writer?.Dispose(); } catch { }
                _writer = null;
                _currentFile = null;
            }
        }

        /// <summary>Delete every session log except the one currently open.</summary>
        public static void ClearLogs()
        {
            lock (_gate)
            {
                try
                {
                    foreach (var f in Directory.GetFiles(_dir, "scalpel-*.jsonl"))
                    {
                        if (_currentFile != null &&
                            string.Equals(f, _currentFile, StringComparison.OrdinalIgnoreCase))
                            continue;
                        try { File.Delete(f); } catch { }
                    }
                }
                catch { }
            }
        }

        // ── internals ──────────────────────────────────────────────────

        private static void OpenWriter()
        {
            var name = $"scalpel-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl";
            _currentFile = Path.Combine(_dir, name);
            // FileShare.ReadWrite so logs can be tailed/read while the app runs.
            var fs = new FileStream(_currentFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _writer = new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = true };
        }

        private static void Write(Level level, string cat, string evt, string msg, Exception? ex, object? data)
        {
            // Intentional unlocked fast-path read: a line dropped or emitted under a race is acceptable.
            if (!Enabled || level < _minLevel) return;
            try
            {
                var rec = new Dictionary<string, object?>
                {
                    ["ts"]    = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    ["level"] = level.ToString().ToUpperInvariant(),
                    ["cat"]   = cat,
                    ["event"] = evt,
                    ["msg"]   = msg,
                };
                if (data != null) rec["data"] = data;
                if (ex != null)
                    rec["error"] = new { type = ex.GetType().Name, message = ex.Message, stack = ex.StackTrace };

                string line = JsonSerializer.Serialize(rec, _json);
                lock (_gate) { _writer?.WriteLine(line); }
            }
            catch { /* never throw from logging */ }
        }

        private static void SweepOldLogs(string dir, TimeSpan maxAge)
        {
            try
            {
                var cutoff = DateTime.Now - maxAge;
                foreach (var f in Directory.GetFiles(dir, "scalpel-*.jsonl"))
                    try { if (File.GetLastWriteTime(f) < cutoff) File.Delete(f); } catch { }
            }
            catch { }
        }
    }
}
