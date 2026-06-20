# Logging System — Design

**Date:** 2026-06-21
**Status:** Approved (pending spec review)

## Goal

Add a comprehensive, local-only logging system to Scalpel so that user interactions,
operation outcomes, and failures are recorded to disk. The primary purpose is **manual
and automated QA / functional testing** — being able to run the app, exercise a feature,
and inspect a machine-parseable log of exactly what happened (every click, every success,
every failure).

Consistent with Scalpel's privacy stance: logs are written **only to the local machine**,
are **never transmitted anywhere** (no telemetry), and are easy to purge.

## Decisions (from brainstorming)

| Question | Decision |
|----------|----------|
| Consumption | Log file on disk (open/tail after using the app) |
| Privacy | Log freely (local-only), plus easy purge + age-based auto-cleanup |
| On/off | On by default, with a Settings toggle (registry-persisted) |
| Format | Structured JSONL, one object per line, with DEBUG/INFO/WARN/ERROR levels |
| Click capture | Approach C: one global click handler + manual outcome logs |
| Retention | Delete session logs older than **7 days** on startup |

## Architecture

### `Services/Logger.cs` — static, thread-safe logger

A new static class, matching the existing static-service style (`App.GetSetting`,
`SignatureStore`). It owns the log file lifecycle and a background write pump.

- **Output location:** `%LOCALAPPDATA%\Scalpel\logs\` (the app already uses
  `%LOCALAPPDATA%\Scalpel` for `signatures.json`).
- **File naming:** one file per app session — `scalpel-YYYYMMDD-HHMMSS.jsonl`. A new
  session = a new file, which keeps each test run cleanly separated.
- **Line format (JSONL):** one JSON object per line, written with `System.Text.Json`
  (already used by `SignatureStore`):
  ```json
  {"ts":"2026-06-21T01:25:03.123Z","level":"INFO","cat":"File","event":"open.success","msg":"Opened invoice.pdf (12 pages)","data":{"path":"C:\\...\\invoice.pdf","pages":12}}
  ```
  Fields: `ts` (UTC ISO-8601, ms precision), `level`, `cat` (category), `event`
  (dot-namespaced short id, e.g. `open.success`, `save.fail`, `click`), `msg`
  (human-readable), `data` (optional object with structured details).
- **Levels:** `DEBUG`, `INFO`, `WARN`, `ERROR`. A minimum level can gate output
  (default `DEBUG` so everything is captured during QA).
- **Categories:** `App`, `UI`, `File`, `Edit`, `Sign`, `Forms`, `Print`, `Render`,
  `Settings`, `Error`.

### Public API (sketch)

```csharp
public static class Logger
{
    public static bool Enabled { get; set; }          // mirrors registry setting
    public static void Init();                          // open session file, sweep old logs
    public static void Shutdown();                      // flush + close

    public static void Debug(string cat, string evt, string msg, object? data = null);
    public static void Info (string cat, string evt, string msg, object? data = null);
    public static void Warn (string cat, string evt, string msg, object? data = null);
    public static void Error(string cat, string evt, string msg, Exception? ex = null, object? data = null);

    public static void Flush();                         // force-drain the queue (crash path)
    public static string LogDirectory { get; }
    public static void ClearLogs();                     // delete all files in logs\
}
```

### Write pump (non-blocking, crash-safe)

- Log calls enqueue a pre-formatted line onto a `BlockingCollection<string>` (or
  `ConcurrentQueue` + signal). A single background worker thread drains the queue and
  writes to a `StreamWriter`, so the **UI thread never blocks on disk I/O**.
- The writer flushes periodically and on `Flush()` / `Shutdown()`.
- **Crash safety:** the unhandled-exception sinks call `Logger.Error(...)` then
  `Logger.Flush()` before showing the crash dialog, guaranteeing the final error reaches
  disk.
- **Logging must never crash the app:** all I/O inside `Logger` is wrapped in
  swallow-and-continue `try/catch`, matching the codebase convention for untrusted I/O.

### Enable/disable

- Registry setting `LoggingEnabled` under `HKCU\Software\Scalpel\Settings` (via
  `App.GetSetting`/`SetSetting`), **default `true`**.
- When disabled, `Logger.Enabled == false` and every `Debug/Info/Warn/Error` call returns
  immediately (cheap no-op) — no file is opened.

### Retention / cleanup

- On `Logger.Init()` (startup), sweep `logs\` and delete any `scalpel-*.jsonl` whose last-
  write time is older than **7 days**. This mirrors the existing `CleanupStaleTemps`
  pattern in `App.xaml.cs`.
- `Logger.ClearLogs()` deletes all log files except the current session's open file.

## Integration points

1. **App lifecycle (`App.xaml.cs`)**
   - In `OnStartup` (after settings init, before `MainWindow`): read `LoggingEnabled`,
     call `Logger.Init()`, log `App / app.start` with app version + session id.
   - On exit: log `App / app.exit` and call `Logger.Shutdown()`.
   - **Crash sinks (3 existing unhandled-exception handlers):** each logs
     `Error / crash.<source>` with the exception, then `Logger.Flush()`.

2. **Global click capture (Approach A, wired once)**
   - At `MainWindow` init, register class handlers via `EventManager.RegisterClassHandler`
     for `ButtonBase.ClickEvent` and `MenuItem.ClickEvent` (covers `Button`,
     `RadioButton`, `ToggleButton`, `MenuItem`). The handler logs `UI / click` with the
     source control's `Name` (and a best-effort label from its content/`ToolTip`).
   - This captures **every click** in one place, with nothing to keep in sync as buttons
     are added.

3. **Manual outcome logs (Approach C second half)**
   - Replace the scattered `System.Diagnostics.Debug.WriteLine($"...: {ex}")` lines in
     `MainWindow.xaml.cs` catch blocks (currently ~7) with `Logger.Error(...)`.
   - Add `Logger.Info(...)` at the **completion of major operations**: open, save, save-
     flattened, merge, split, sign, form-fill, print — recording success and key counts
     (pages, annotations burned, etc.). Failures in those operations log `*.fail` at
     `ERROR`/`WARN`.
   - Scope note (YAGNI): we instrument the **meaningful operations**, not literally every
     private method. High-frequency paths (per-tile render) stay at `DEBUG` and avoid
     logging inside tight loops.

4. **Settings UI (`MainWindow.xaml` + `SettingsBtn_Click`)**
   - Add to the existing Settings panel:
     - **"Enable logging"** checkbox bound to `LoggingEnabled` (toggles `Logger.Enabled`
       and persists to registry).
     - **"Open logs folder"** button → opens `Logger.LogDirectory` in Explorer.
     - **"Clear logs"** button → `Logger.ClearLogs()` with a confirmation.
   - New locale keys (`Str_*`) added to **all six** `Strings/*.xaml` files for the three
     controls' labels (per the localization rule in CLAUDE.md).

## Testing (`Scalpel.Tests`, xUnit)

Add `LoggerTests` and link `Services/Logger.cs` in the test `.csproj` (`<Compile Include>`),
following the existing convention. To keep tests filesystem-isolated, the logger's base
directory must be overridable (e.g. an internal `Init(string baseDir)` overload pointing at
a temp dir). Cases:

- **Line shape:** a logged event produces one valid JSON line with the expected
  `ts/level/cat/event/msg` fields, and `data` round-trips.
- **Level filtering:** with min level `INFO`, `Debug(...)` writes nothing; `Info`+ do.
- **Enable/disable:** when `Enabled == false`, no file is created and calls are no-ops.
- **Cleanup:** files older than 7 days are deleted on `Init`; newer ones are kept.
- **ClearLogs:** removes prior files, keeps the current session file.

## Out of scope (YAGNI)

- No live in-app log viewer panel (file-on-disk only).
- No log shipping / telemetry / network anything.
- No redaction layer (logs are local-only by design; purge handles sensitivity).
- No per-category runtime toggles (single global on/off is enough for QA).

## Files touched

- **New:** `Services/Logger.cs`, `Scalpel.Tests/LoggerTests.cs`.
- **Modified:** `App.xaml.cs` (init/shutdown/crash sinks), `MainWindow.xaml.cs` (click
  handler + outcome logs, replace `Debug.WriteLine`), `MainWindow.xaml` (Settings
  controls), `Strings/*.xaml` (6 locale files), `Scalpel.Tests/*.csproj` (compile link).
