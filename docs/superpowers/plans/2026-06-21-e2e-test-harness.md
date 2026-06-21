# Scalpel E2E Test Harness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a FlaUI-driven end-to-end test harness (`Scalpel.E2E`) that exercises every Scalpel button/function/workflow, verifies behavior via the existing JSONL session logs plus UI-state assertions, and emits a Markdown + JSON report.

**Architecture:** A new net48 x64 console project drives the real `Scalpel.exe` through Windows UI Automation (FlaUI). Pure logic (log parsing, report emission, the PDF corpus generator, the control catalog) is isolated into FlaUI-free files that are unit-tested by linking them into the existing `Scalpel.Tests` xUnit project (matching the repo's `<Compile Include>` link convention). The FlaUI driver, assertions, and suites are verified by running against the real app and inspecting the emitted report/logs.

**Tech Stack:** C# / .NET Framework 4.8 (built with .NET 8 SDK), FlaUI.Core + FlaUI.UIA3, PdfSharpCore (corpus generation), System.Text.Json, xUnit (for the harness's own unit tests), PdfPig (corpus verification in tests).

## Global Constraints

- Target framework `net48`, platform x64. Build requires the **.NET 8 SDK** (`dotnet` may be at `~/.dotnet/dotnet.exe`).
- FlaUI dependencies (`FlaUI.Core`, `FlaUI.UIA3`) live **only** in `Scalpel.E2E`. The main app and `Scalpel.Tests` stay FlaUI-free.
- Pure-logic files (`LogReader`, `Reporter`, `Corpus`, `Catalog`, result models) must **not** reference FlaUI, so they can be linked into `Scalpel.Tests`.
- C# style: `Nullable` enabled, `ImplicitUsings` enabled, `LangVersion=latest`. Use collection expressions (`[]`), target-typed `new`, switch expressions — match the codebase.
- I/O and parsing wrapped in defensive `try { } catch { }` that swallow and fall back (PDFs and logs are untrusted/possibly truncated).
- The harness always launches `Scalpel.exe` with an **explicit fixture path** argument (the app auto-reopens `LastFile` when launched with no arg — never rely on that).
- Logs are read from `%LOCALAPPDATA%\Scalpel\logs\scalpel-*.jsonl` (newest by `LastWriteTime` for the run). JSONL = one JSON object per line; fields `ts,level,cat,event,msg` always present, `data,error` optional.
- Build/test commands: `dotnet build`, `dotnet test`, `dotnet test --filter "FullyQualifiedName~<Class>"`.

---

## File Structure

```
Scalpel.E2E/
  Scalpel.E2E.csproj          # net48 x64 console, refs FlaUI.Core + FlaUI.UIA3 + PdfSharpCore
  Program.cs                  # CLI entry: parse args, orchestrate suites, emit report, exit code
  Model/LogEntry.cs           # one parsed JSONL line (FlaUI-free)
  Model/ActionResult.cs       # one action's pass/fail record (FlaUI-free)
  Model/RunReport.cs          # whole-run aggregate (FlaUI-free)
  Verify/LogReader.cs         # locate + parse + tail session JSONL (FlaUI-free)
  Report/Reporter.cs          # Markdown + JSON emitters (FlaUI-free)
  Fixtures/Corpus.cs          # generate baseline PDF corpus via PdfSharpCore (FlaUI-free)
  Catalog/ControlSpec.cs      # one control's catalog entry (FlaUI-free)
  Catalog/Catalog.cs          # the declared control catalog keyed by x:Name (FlaUI-free)
  Driver/AppDriver.cs         # FlaUI: launch/attach, find, click, type, resize, dialogs
  Driver/ActionRunner.cs      # FlaUI: the B+C verification wrapper around one action
  Driver/Assertions.cs        # FlaUI: C-tier UI-state assertions
  Suites/SinglesSuite.cs      # FlaUI
  Suites/JourneysSuite.cs     # FlaUI
  Suites/PairwiseSuite.cs     # FlaUI
  Suites/MonkeySuite.cs       # FlaUI
tests/fixtures/               # optional real PDFs (gitkeep'd; picked up if present)
Scalpel.Tests/                # existing xUnit project; links the FlaUI-free files for unit tests
```

---

### Task 1: Scaffold the `Scalpel.E2E` project

**Files:**
- Create: `Scalpel.E2E/Scalpel.E2E.csproj`
- Create: `Scalpel.E2E/Program.cs`
- Modify: `Scalpel.sln` (add the project)

**Interfaces:**
- Produces: a buildable console project named `Scalpel.E2E` targeting `net48`/x64 with FlaUI + PdfSharpCore package references.

- [ ] **Step 1: Create the csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net48</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <AssemblyName>Scalpel.E2E</AssemblyName>
    <RootNamespace>Scalpel.E2E</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="FlaUI.Core" Version="4.0.0" />
    <PackageReference Include="FlaUI.UIA3" Version="4.0.0" />
    <PackageReference Include="PdfSharpCore" Version="1.3.67" />
    <PackageReference Include="System.Text.Json" Version="8.0.5" />
  </ItemGroup>

</Project>
```

> `PdfSharpCore` 1.3.67 matches the version in `Scalpel.csproj` — keep them in lockstep. `FlaUI.Core`/`FlaUI.UIA3` 4.0.0 is the current stable release.

- [ ] **Step 2: Create a minimal Program.cs**

```csharp
namespace Scalpel.E2E;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.WriteLine("Scalpel.E2E harness — scaffolding only.");
        return 0;
    }
}
```

- [ ] **Step 3: Add the project to the solution**

Run: `dotnet sln Scalpel.sln add Scalpel.E2E/Scalpel.E2E.csproj`
Expected: "Project ... added to the solution."

- [ ] **Step 4: Build to verify it compiles and restores FlaUI**

Run: `dotnet build Scalpel.E2E/Scalpel.E2E.csproj`
Expected: Build succeeded, FlaUI packages restored.

- [ ] **Step 5: Commit**

```bash
git add Scalpel.E2E/Scalpel.E2E.csproj Scalpel.E2E/Program.cs Scalpel.sln
git commit -m "Scaffold Scalpel.E2E console project"
```

---

### Task 2: `LogEntry` model + JSONL parsing

**Files:**
- Create: `Scalpel.E2E/Model/LogEntry.cs`
- Test: `Scalpel.Tests/E2E/LogEntryTests.cs`
- Modify: `Scalpel.Tests/Scalpel.Tests.csproj` (link the source file)

**Interfaces:**
- Produces:
  - `record LogEntry(DateTime Ts, string Level, string Cat, string Event, string Msg, JsonElement? Data, JsonElement? Error)`
  - `static LogEntry? LogEntry.Parse(string line)` — returns null on a blank/unparseable line (never throws).
  - `bool LogEntry.IsFailure` — true when `Level` is `ERROR` or `Event` starts with `crash.` or `Event` ends with `.fail`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Text.Json;
using Scalpel.E2E;
using Xunit;

namespace Scalpel.Tests.E2E;

public class LogEntryTests
{
    [Fact]
    public void Parse_ValidLine_PopulatesFields()
    {
        var line = "{\"ts\":\"2026-06-21T05:11:44.007Z\",\"level\":\"INFO\",\"cat\":\"File\",\"event\":\"open.success\",\"msg\":\"PDF opened\",\"data\":{\"pages\":2}}";
        var e = LogEntry.Parse(line);
        Assert.NotNull(e);
        Assert.Equal("INFO", e!.Level);
        Assert.Equal("File", e.Cat);
        Assert.Equal("open.success", e.Event);
        Assert.Equal(2, e.Data!.Value.GetProperty("pages").GetInt32());
        Assert.False(e.IsFailure);
    }

    [Fact]
    public void Parse_BlankOrGarbage_ReturnsNull()
    {
        Assert.Null(LogEntry.Parse(""));
        Assert.Null(LogEntry.Parse("   "));
        Assert.Null(LogEntry.Parse("not json"));
    }

    [Theory]
    [InlineData("ERROR", "GetPageFormFields", true)]
    [InlineData("INFO", "crash.dispatcher", true)]
    [InlineData("INFO", "save.fail", true)]
    [InlineData("INFO", "click", false)]
    [InlineData("INFO", "open.success", false)]
    public void IsFailure_ClassifiesCorrectly(string level, string ev, bool expected)
    {
        var line = $"{{\"ts\":\"2026-06-21T05:11:44.007Z\",\"level\":\"{level}\",\"cat\":\"X\",\"event\":\"{ev}\",\"msg\":\"m\"}}";
        Assert.Equal(expected, LogEntry.Parse(line)!.IsFailure);
    }
}
```

- [ ] **Step 2: Link the (not-yet-existing) source into the test project**

Add inside the existing `<ItemGroup>` of compile links in `Scalpel.Tests/Scalpel.Tests.csproj`:

```xml
    <Compile Include="..\Scalpel.E2E\Model\LogEntry.cs" Link="E2E\LogEntry.cs" />
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~LogEntryTests"`
Expected: FAIL — `LogEntry` does not exist (compile error).

- [ ] **Step 4: Implement `LogEntry`**

```csharp
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
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~LogEntryTests"`
Expected: PASS (all cases).

- [ ] **Step 6: Commit**

```bash
git add Scalpel.E2E/Model/LogEntry.cs Scalpel.Tests/E2E/LogEntryTests.cs Scalpel.Tests/Scalpel.Tests.csproj
git commit -m "Add LogEntry JSONL parsing with failure classification"
```

---

### Task 3: `LogReader` — locate session log + tail new lines

**Files:**
- Create: `Scalpel.E2E/Verify/LogReader.cs`
- Test: `Scalpel.Tests/E2E/LogReaderTests.cs`
- Modify: `Scalpel.Tests/Scalpel.Tests.csproj` (link the source file)

**Interfaces:**
- Consumes: `LogEntry`.
- Produces:
  - `class LogReader` with constructor `LogReader(string filePath)`.
  - `static string? LogReader.FindLatestLog(string logDir)` — newest `scalpel-*.jsonl` by write time, or null.
  - `static string DefaultLogDir()` — `%LOCALAPPDATA%\Scalpel\logs`.
  - `int LogReader.Snapshot()` — current line count.
  - `IReadOnlyList<LogEntry> LogReader.NewSince(int snapshot)` — parsed entries appended after the snapshot line index (skips unparseable lines). Reads with shared/read access so it works while the app holds the file open.

- [ ] **Step 1: Write the failing test**

```csharp
using System.IO;
using Scalpel.E2E;
using Xunit;

namespace Scalpel.Tests.E2E;

public class LogReaderTests
{
    private static string WriteTemp(params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), $"scalpel-test-{Guid.NewGuid():N}.jsonl");
        File.WriteAllLines(path, lines);
        return path;
    }

    private static string ClickLine(string name) =>
        $"{{\"ts\":\"2026-06-21T05:11:44.007Z\",\"level\":\"INFO\",\"cat\":\"UI\",\"event\":\"click\",\"msg\":\"{name}\"}}";

    [Fact]
    public void NewSince_ReturnsOnlyAppendedLines()
    {
        var path = WriteTemp(ClickLine("A"), ClickLine("B"));
        var reader = new LogReader(path);
        int snap = reader.Snapshot();
        Assert.Equal(2, snap);

        File.AppendAllLines(path, [ClickLine("C")]);
        var fresh = reader.NewSince(snap);
        Assert.Single(fresh);
        Assert.Equal("C", fresh[0].Msg);

        File.Delete(path);
    }

    [Fact]
    public void NewSince_SkipsUnparseableLines()
    {
        var path = WriteTemp(ClickLine("A"));
        var reader = new LogReader(path);
        int snap = reader.Snapshot();
        File.AppendAllLines(path, ["", "garbage", ClickLine("B")]);
        var fresh = reader.NewSince(snap);
        Assert.Single(fresh);
        Assert.Equal("B", fresh[0].Msg);
        File.Delete(path);
    }

    [Fact]
    public void FindLatestLog_PicksNewestByWriteTime()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"logs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var older = Path.Combine(dir, "scalpel-20260101-000000.jsonl");
        var newer = Path.Combine(dir, "scalpel-20260621-000000.jsonl");
        File.WriteAllText(older, ClickLine("old"));
        File.WriteAllText(newer, ClickLine("new"));
        File.SetLastWriteTimeUtc(older, new DateTime(2026, 1, 1));
        File.SetLastWriteTimeUtc(newer, new DateTime(2026, 6, 21));

        Assert.Equal(newer, LogReader.FindLatestLog(dir));
        Directory.Delete(dir, true);
    }
}
```

- [ ] **Step 2: Link the source into the test project**

Add to `Scalpel.Tests/Scalpel.Tests.csproj`:

```xml
    <Compile Include="..\Scalpel.E2E\Verify\LogReader.cs" Link="E2E\LogReader.cs" />
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~LogReaderTests"`
Expected: FAIL — `LogReader` does not exist.

- [ ] **Step 4: Implement `LogReader`**

```csharp
namespace Scalpel.E2E;

public sealed class LogReader
{
    private readonly string _path;
    public LogReader(string filePath) => _path = filePath;
    public string FilePath => _path;

    public static string DefaultLogDir() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Scalpel", "logs");

    public static string? FindLatestLog(string logDir)
    {
        try
        {
            var dir = new DirectoryInfo(logDir);
            if (!dir.Exists) return null;
            return dir.GetFiles("scalpel-*.jsonl")
                      .OrderByDescending(f => f.LastWriteTimeUtc)
                      .FirstOrDefault()?.FullName;
        }
        catch { return null; }
    }

    private string[] ReadAllLinesShared()
    {
        try
        {
            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            var lines = new List<string>();
            string? line;
            while ((line = sr.ReadLine()) != null) lines.Add(line);
            return [.. lines];
        }
        catch { return []; }
    }

    public int Snapshot() => ReadAllLinesShared().Length;

    public IReadOnlyList<LogEntry> NewSince(int snapshot)
    {
        var all = ReadAllLinesShared();
        var result = new List<LogEntry>();
        for (int i = Math.Max(0, snapshot); i < all.Length; i++)
        {
            var e = LogEntry.Parse(all[i]);
            if (e != null) result.Add(e);
        }
        return result;
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~LogReaderTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Scalpel.E2E/Verify/LogReader.cs Scalpel.Tests/E2E/LogReaderTests.cs Scalpel.Tests/Scalpel.Tests.csproj
git commit -m "Add LogReader for locating and tailing session logs"
```

---

### Task 4: Result models (`ActionResult`, `RunReport`)

**Files:**
- Create: `Scalpel.E2E/Model/ActionResult.cs`
- Create: `Scalpel.E2E/Model/RunReport.cs`
- Test: `Scalpel.Tests/E2E/RunReportTests.cs`
- Modify: `Scalpel.Tests/Scalpel.Tests.csproj` (link both source files)

**Interfaces:**
- Consumes: `LogEntry`.
- Produces:
  - `enum Outcome { Pass, Fail }`
  - `record ActionResult(string Suite, string Action, Outcome Outcome, string? FailReason, IReadOnlyList<LogEntry> LogContext)`
  - `class RunReport` with: `List<ActionResult> Results`, `List<string> UntestedControls`, methods `int Total()`, `int Passed()`, `int Failed()`, and `int FailedFor(string suite)` / `int TotalFor(string suite)`, and `IEnumerable<string> Suites()`.

- [ ] **Step 1: Write the failing test**

```csharp
using Scalpel.E2E;
using Xunit;

namespace Scalpel.Tests.E2E;

public class RunReportTests
{
    [Fact]
    public void Counts_AggregateBySuiteAndOverall()
    {
        var r = new RunReport();
        r.Results.Add(new ActionResult("singles", "ZoomInBtn", Outcome.Pass, null, []));
        r.Results.Add(new ActionResult("singles", "ToolDrawBtn", Outcome.Fail, "no click logged", []));
        r.Results.Add(new ActionResult("journeys", "open", Outcome.Pass, null, []));

        Assert.Equal(3, r.Total());
        Assert.Equal(2, r.Passed());
        Assert.Equal(1, r.Failed());
        Assert.Equal(2, r.TotalFor("singles"));
        Assert.Equal(1, r.FailedFor("singles"));
        Assert.Equal(0, r.FailedFor("journeys"));
        Assert.Equal(["singles", "journeys"], r.Suites());
    }
}
```

- [ ] **Step 2: Link both sources into the test project**

Add to `Scalpel.Tests/Scalpel.Tests.csproj`:

```xml
    <Compile Include="..\Scalpel.E2E\Model\ActionResult.cs" Link="E2E\ActionResult.cs" />
    <Compile Include="..\Scalpel.E2E\Model\RunReport.cs" Link="E2E\RunReport.cs" />
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~RunReportTests"`
Expected: FAIL — types don't exist.

- [ ] **Step 4: Implement the models**

`Scalpel.E2E/Model/ActionResult.cs`:

```csharp
namespace Scalpel.E2E;

public enum Outcome { Pass, Fail }

public sealed record ActionResult(
    string Suite,
    string Action,
    Outcome Outcome,
    string? FailReason,
    IReadOnlyList<LogEntry> LogContext);
```

`Scalpel.E2E/Model/RunReport.cs`:

```csharp
namespace Scalpel.E2E;

public sealed class RunReport
{
    public List<ActionResult> Results { get; } = [];
    public List<string> UntestedControls { get; } = [];

    public int Total() => Results.Count;
    public int Passed() => Results.Count(r => r.Outcome == Outcome.Pass);
    public int Failed() => Results.Count(r => r.Outcome == Outcome.Fail);

    public int TotalFor(string suite) => Results.Count(r => r.Suite == suite);
    public int FailedFor(string suite) =>
        Results.Count(r => r.Suite == suite && r.Outcome == Outcome.Fail);

    // Distinct suites in first-seen order.
    public IEnumerable<string> Suites()
    {
        var seen = new HashSet<string>();
        foreach (var r in Results)
            if (seen.Add(r.Suite)) yield return r.Suite;
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~RunReportTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Scalpel.E2E/Model/ActionResult.cs Scalpel.E2E/Model/RunReport.cs Scalpel.Tests/E2E/RunReportTests.cs Scalpel.Tests/Scalpel.Tests.csproj
git commit -m "Add ActionResult and RunReport aggregation models"
```

---

### Task 5: `Reporter` — Markdown + JSON emitters

**Files:**
- Create: `Scalpel.E2E/Report/Reporter.cs`
- Test: `Scalpel.Tests/E2E/ReporterTests.cs`
- Modify: `Scalpel.Tests/Scalpel.Tests.csproj` (link the source file)

**Interfaces:**
- Consumes: `RunReport`, `ActionResult`, `LogEntry`.
- Produces:
  - `static string Reporter.ToMarkdown(RunReport report)` — summary table + per-failure narrative (action, reason, JSONL context lines) + untested-controls section.
  - `static string Reporter.ToJson(RunReport report)` — structured JSON.
  - `static (string mdPath, string jsonPath) Reporter.Write(RunReport report, string dir, string timestamp)` — writes both files named `test-report-<timestamp>.md/.json`, returns paths.

- [ ] **Step 1: Write the failing test**

```csharp
using System.IO;
using Scalpel.E2E;
using Xunit;

namespace Scalpel.Tests.E2E;

public class ReporterTests
{
    private static RunReport Sample()
    {
        var r = new RunReport();
        r.Results.Add(new ActionResult("singles", "ZoomInBtn", Outcome.Pass, null, []));
        r.Results.Add(new ActionResult("singles", "ToolDrawBtn", Outcome.Fail,
            "expected click 'ToolDrawBtn' not logged",
            [LogEntry.Parse("{\"ts\":\"2026-06-21T05:11:44.007Z\",\"level\":\"INFO\",\"cat\":\"UI\",\"event\":\"click\",\"msg\":\"ToolSelectBtn\"}")!]));
        r.UntestedControls.Add("MysteryButton");
        return r;
    }

    [Fact]
    public void ToMarkdown_ContainsSummaryFailureAndUntested()
    {
        var md = Reporter.ToMarkdown(Sample());
        Assert.Contains("singles", md);
        Assert.Contains("ToolDrawBtn", md);
        Assert.Contains("expected click 'ToolDrawBtn' not logged", md);
        Assert.Contains("ToolSelectBtn", md);          // the log-context line
        Assert.Contains("MysteryButton", md);          // untested control
    }

    [Fact]
    public void ToJson_IsParseableAndHasCounts()
    {
        var json = Reporter.ToJson(Sample());
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(2, doc.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("failed").GetInt32());
    }

    [Fact]
    public void Write_ProducesBothFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"rep-{Guid.NewGuid():N}");
        var (md, json) = Reporter.Write(Sample(), dir, "20260621-090000");
        Assert.True(File.Exists(md));
        Assert.True(File.Exists(json));
        Directory.Delete(dir, true);
    }
}
```

- [ ] **Step 2: Link the source into the test project**

```xml
    <Compile Include="..\Scalpel.E2E\Report\Reporter.cs" Link="E2E\Reporter.cs" />
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ReporterTests"`
Expected: FAIL — `Reporter` does not exist.

- [ ] **Step 4: Implement `Reporter`**

```csharp
using System.Text;
using System.Text.Json;

namespace Scalpel.E2E;

public static class Reporter
{
    public static string ToMarkdown(RunReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Scalpel E2E Test Report");
        sb.AppendLine();
        sb.AppendLine($"**Total:** {report.Total()} &nbsp; **Passed:** {report.Passed()} &nbsp; **Failed:** {report.Failed()} &nbsp; **Untested controls:** {report.UntestedControls.Count}");
        sb.AppendLine();
        sb.AppendLine("## Summary by suite");
        sb.AppendLine();
        sb.AppendLine("| Suite | Total | Passed | Failed |");
        sb.AppendLine("|---|---:|---:|---:|");
        foreach (var s in report.Suites())
        {
            int total = report.TotalFor(s);
            int failed = report.FailedFor(s);
            sb.AppendLine($"| {s} | {total} | {total - failed} | {failed} |");
        }
        sb.AppendLine();

        var failures = report.Results.Where(r => r.Outcome == Outcome.Fail).ToList();
        sb.AppendLine($"## Failures ({failures.Count})");
        sb.AppendLine();
        if (failures.Count == 0)
        {
            sb.AppendLine("_None._");
        }
        else
        {
            foreach (var f in failures)
            {
                sb.AppendLine($"### [{f.Suite}] {f.Action}");
                sb.AppendLine();
                sb.AppendLine($"**Reason:** {f.FailReason}");
                sb.AppendLine();
                if (f.LogContext.Count > 0)
                {
                    sb.AppendLine("Log context:");
                    sb.AppendLine();
                    sb.AppendLine("```");
                    foreach (var e in f.LogContext)
                        sb.AppendLine($"{e.Ts:O} {e.Level} {e.Cat}/{e.Event} {e.Msg}");
                    sb.AppendLine("```");
                    sb.AppendLine();
                }
            }
        }

        sb.AppendLine("## Controls never exercised");
        sb.AppendLine();
        if (report.UntestedControls.Count == 0)
            sb.AppendLine("_None — full coverage._");
        else
            foreach (var c in report.UntestedControls)
                sb.AppendLine($"- {c}");

        return sb.ToString();
    }

    public static string ToJson(RunReport report)
    {
        var payload = new
        {
            total = report.Total(),
            passed = report.Passed(),
            failed = report.Failed(),
            untestedControls = report.UntestedControls,
            suites = report.Suites().Select(s => new
            {
                name = s,
                total = report.TotalFor(s),
                failed = report.FailedFor(s),
            }),
            results = report.Results.Select(r => new
            {
                suite = r.Suite,
                action = r.Action,
                outcome = r.Outcome.ToString(),
                failReason = r.FailReason,
                logContext = r.LogContext.Select(e => new
                {
                    ts = e.Ts, level = e.Level, cat = e.Cat, ev = e.Event, msg = e.Msg,
                }),
            }),
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    public static (string mdPath, string jsonPath) Write(RunReport report, string dir, string timestamp)
    {
        Directory.CreateDirectory(dir);
        string md = Path.Combine(dir, $"test-report-{timestamp}.md");
        string json = Path.Combine(dir, $"test-report-{timestamp}.json");
        File.WriteAllText(md, ToMarkdown(report));
        File.WriteAllText(json, ToJson(report));
        return (md, json);
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~ReporterTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Scalpel.E2E/Report/Reporter.cs Scalpel.Tests/E2E/ReporterTests.cs Scalpel.Tests/Scalpel.Tests.csproj
git commit -m "Add Markdown + JSON reporter"
```

---

### Task 6: `Corpus` — generate the baseline PDF fixtures

**Files:**
- Create: `Scalpel.E2E/Fixtures/Corpus.cs`
- Test: `Scalpel.Tests/E2E/CorpusTests.cs`
- Modify: `Scalpel.Tests/Scalpel.Tests.csproj` (link the source file; PdfPig already referenced)

**Interfaces:**
- Produces:
  - `record CorpusFile(string Key, string Path, int ExpectedPages)`
  - `static IReadOnlyList<CorpusFile> Corpus.Generate(string outDir)` — writes the baseline set and returns their descriptors. Deterministic (no timestamps/random in content). Includes at minimum: `simple-1p` (1 page), `large-50p` (50 pages), `image-only` (1 page with a drawn image). For `corrupted`, writes a file with a valid `%PDF-` header followed by truncated/garbage bytes (ExpectedPages 0). The `form-acroform` and `encrypted` variants are added if straightforward with PdfSharpCore; otherwise document them as TODO-not-blocking and the suites simply skip a missing key.
  - `static IReadOnlyList<string> Corpus.RealFixtures(string fixturesDir)` — every `*.pdf` in `tests/fixtures/` (empty list if the dir is absent).

> Implementation note: `simple-1p`, `large-50p`, and `image-only` are the load-bearing fixtures the suites depend on — they MUST be generated. `form-acroform` and `encrypted` are best-effort; generate them if PdfSharpCore makes it easy, otherwise omit the key (suites guard on key presence).

- [ ] **Step 1: Write the failing test**

```csharp
using System.IO;
using System.Linq;
using Scalpel.E2E;
using UglyToad.PdfPig;
using Xunit;

namespace Scalpel.Tests.E2E;

public class CorpusTests
{
    [Fact]
    public void Generate_ProducesLoadBearingFixturesWithCorrectPageCounts()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"corpus-{Guid.NewGuid():N}");
        var files = Corpus.Generate(dir);

        var simple = files.Single(f => f.Key == "simple-1p");
        var large = files.Single(f => f.Key == "large-50p");

        Assert.True(File.Exists(simple.Path));
        using (var doc = PdfDocument.Open(simple.Path))
            Assert.Equal(1, doc.NumberOfPages);
        using (var doc = PdfDocument.Open(large.Path))
            Assert.Equal(50, doc.NumberOfPages);

        Directory.Delete(dir, true);
    }

    [Fact]
    public void Generate_CorruptedFile_HasPdfHeaderButIsNotAValidDocument()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"corpus-{Guid.NewGuid():N}");
        var files = Corpus.Generate(dir);
        var corrupted = files.Single(f => f.Key == "corrupted");

        var bytes = File.ReadAllBytes(corrupted.Path);
        Assert.True(bytes.Length > 5);
        Assert.Equal((byte)'%', bytes[0]);                 // starts with %PDF-
        Assert.Throws<UglyToad.PdfPig.Core.PdfDocumentFormatException>(
            () => PdfDocument.Open(corrupted.Path));

        Directory.Delete(dir, true);
    }
}
```

> Note: if PdfPig throws a different exception type for the corrupted file when you run Step 3, change the `Assert.Throws<...>` to the actual type observed — the point is that opening fails. Verify the real type rather than guessing.

- [ ] **Step 2: Link the source into the test project**

```xml
    <Compile Include="..\Scalpel.E2E\Fixtures\Corpus.cs" Link="E2E\Corpus.cs" />
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~CorpusTests"`
Expected: FAIL — `Corpus` does not exist.

- [ ] **Step 4: Implement `Corpus`**

```csharp
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace Scalpel.E2E;

public sealed record CorpusFile(string Key, string Path, int ExpectedPages);

public static class Corpus
{
    public static IReadOnlyList<CorpusFile> Generate(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var files = new List<CorpusFile>
        {
            WriteSimple(outDir),
            WriteLarge(outDir),
            WriteImageOnly(outDir),
            WriteCorrupted(outDir),
        };
        return files;
    }

    private static CorpusFile WriteSimple(string dir)
    {
        string path = Path.Combine(dir, "simple-1p.pdf");
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", 20);
            gfx.DrawString("Scalpel E2E — simple 1 page", font, XBrushes.Black,
                new XRect(0, 0, page.Width, page.Height), XStringFormats.Center);
        }
        doc.Save(path);
        return new CorpusFile("simple-1p", path, 1);
    }

    private static CorpusFile WriteLarge(string dir)
    {
        string path = Path.Combine(dir, "large-50p.pdf");
        using var doc = new PdfDocument();
        var font = new XFont("Arial", 20);
        for (int i = 1; i <= 50; i++)
        {
            var page = doc.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString($"Page {i} of 50", font, XBrushes.Black,
                new XRect(0, 0, page.Width, page.Height), XStringFormats.Center);
        }
        doc.Save(path);
        return new CorpusFile("large-50p", path, 50);
    }

    private static CorpusFile WriteImageOnly(string dir)
    {
        string path = Path.Combine(dir, "image-only.pdf");
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            // A drawn filled rectangle stands in for an image — no text content.
            gfx.DrawRectangle(XBrushes.SteelBlue, new XRect(40, 40, 200, 200));
            gfx.DrawEllipse(XBrushes.Goldenrod, new XRect(80, 80, 120, 120));
        }
        doc.Save(path);
        return new CorpusFile("image-only", path, 1);
    }

    private static CorpusFile WriteCorrupted(string dir)
    {
        string path = Path.Combine(dir, "corrupted.pdf");
        // Valid header, then truncated garbage so the parser must fail/repair.
        byte[] header = System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\n");
        byte[] garbage = [0x25, 0x25, 0x00, 0xFF, 0xFE, 0x0A, 0x42, 0x41, 0x44];
        File.WriteAllBytes(path, [.. header, .. garbage]);
        return new CorpusFile("corrupted", path, 0);
    }

    public static IReadOnlyList<string> RealFixtures(string fixturesDir)
    {
        try
        {
            if (!Directory.Exists(fixturesDir)) return [];
            return [.. Directory.GetFiles(fixturesDir, "*.pdf")];
        }
        catch { return []; }
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~CorpusTests"`
Expected: PASS (adjust the corrupted-file exception type if Step 3 revealed a different one).

- [ ] **Step 6: Commit**

```bash
git add Scalpel.E2E/Fixtures/Corpus.cs Scalpel.Tests/E2E/CorpusTests.cs Scalpel.Tests/Scalpel.Tests.csproj
git commit -m "Add deterministic PDF corpus generator"
```

---

### Task 7: Control catalog (`ControlSpec`, `Catalog`)

**Files:**
- Create: `Scalpel.E2E/Catalog/ControlSpec.cs`
- Create: `Scalpel.E2E/Catalog/Catalog.cs`
- Test: `Scalpel.Tests/E2E/CatalogTests.cs`
- Modify: `Scalpel.Tests/Scalpel.Tests.csproj` (link both source files)

**Interfaces:**
- Produces:
  - `enum Surface { AlwaysVisible, ViewMode, EditMode, PagesMode, SignMode, SettingsOverlay }`
  - `record ControlSpec(string AutomationId, Surface Surface, bool RequiresOpenFile, string? AssertionKey)` — `AutomationId` equals the control's `x:Name` (also its logged click `msg`). `AssertionKey` names a C-tier assertion (Task 10) or null for B-only.
  - `static class Catalog` with `static IReadOnlyList<ControlSpec> All`, `static ControlSpec? Find(string automationId)`, and `static IReadOnlyList<string> KnownIds`.

> This catalog is the declared source of truth for "every button." It is seeded from `MainWindow.xaml` `x:Name`s. The Singles suite (Task 12) cross-checks the live UIA tree against `KnownIds` and reports any tree control absent here as "untested."

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using Scalpel.E2E;
using Xunit;

namespace Scalpel.Tests.E2E;

public class CatalogTests
{
    [Fact]
    public void All_ContainsCoreControlsWithCorrectSurfaces()
    {
        Assert.Contains(Catalog.All, c => c.AutomationId == "ZoomInBtn" && c.Surface == Surface.AlwaysVisible);
        Assert.Contains(Catalog.All, c => c.AutomationId == "ToolDrawBtn" && c.Surface == Surface.EditMode);
        Assert.Contains(Catalog.All, c => c.AutomationId == "ViewGridBtn" && c.Surface == Surface.ViewMode);
        Assert.Contains(Catalog.All, c => c.AutomationId == "ThemeBloodRadio" && c.Surface == Surface.SettingsOverlay);
    }

    [Fact]
    public void Find_ReturnsSpecOrNull()
    {
        Assert.NotNull(Catalog.Find("SettingsBtn"));
        Assert.Null(Catalog.Find("NoSuchControl"));
    }

    [Fact]
    public void KnownIds_AreUnique()
    {
        Assert.Equal(Catalog.KnownIds.Count, Catalog.KnownIds.Distinct().Count());
    }
}
```

- [ ] **Step 2: Link both sources into the test project**

```xml
    <Compile Include="..\Scalpel.E2E\Catalog\ControlSpec.cs" Link="E2E\ControlSpec.cs" />
    <Compile Include="..\Scalpel.E2E\Catalog\Catalog.cs" Link="E2E\Catalog.cs" />
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~CatalogTests"`
Expected: FAIL — types don't exist.

- [ ] **Step 4: Implement the catalog**

`Scalpel.E2E/Catalog/ControlSpec.cs`:

```csharp
namespace Scalpel.E2E;

public enum Surface
{
    AlwaysVisible, ViewMode, EditMode, PagesMode, SignMode, SettingsOverlay
}

public sealed record ControlSpec(
    string AutomationId,
    Surface Surface,
    bool RequiresOpenFile,
    string? AssertionKey);
```

`Scalpel.E2E/Catalog/Catalog.cs` (seed from `MainWindow.xaml` names; extend as the UI grows):

```csharp
namespace Scalpel.E2E;

public static class Catalog
{
    public static IReadOnlyList<ControlSpec> All { get; } =
    [
        // Mode tabs (always visible)
        new("ModeViewTab",  Surface.AlwaysVisible, false, "modeViewActive"),
        new("ModeEditTab",  Surface.AlwaysVisible, false, "modeEditActive"),
        new("ModePagesTab", Surface.AlwaysVisible, false, "modePagesActive"),
        new("ModeSignTab",  Surface.AlwaysVisible, false, "modeSignActive"),

        // File group / zoom (always visible)
        new("OpenMenuBtn",   Surface.AlwaysVisible, false, null),
        new("SaveAsBtn",     Surface.AlwaysVisible, true,  null),
        new("SaveMenuBtn",   Surface.AlwaysVisible, true,  null),
        new("ZoomOutBtn",    Surface.AlwaysVisible, true,  "zoomDecreased"),
        new("ZoomInBtn",     Surface.AlwaysVisible, true,  "zoomIncreased"),
        new("SettingsBtn",   Surface.AlwaysVisible, false, "settingsOverlayOpen"),
        new("SidebarToggleBtn", Surface.AlwaysVisible, true, null),

        // View mode panel
        new("ViewSingleBtn",     Surface.ViewMode, true, null),
        new("ViewContinuousBtn", Surface.ViewMode, true, null),
        new("ViewTwoPageBtn",    Surface.ViewMode, true, null),
        new("ViewGridBtn",       Surface.ViewMode, true, null),
        new("ViewFitBtn",        Surface.ViewMode, true, null),

        // Edit mode panel
        new("ToolSelectBtn",    Surface.EditMode, true, null),
        new("ToolTextBtn",      Surface.EditMode, true, null),
        new("ToolHighlightBtn", Surface.EditMode, true, null),
        new("ToolDrawBtn",      Surface.EditMode, true, null),
        new("ToolImageBtn",     Surface.EditMode, true, null),
        new("ToolCropBtn",      Surface.EditMode, true, null),

        // Sign mode panel
        new("ToolSignatureBtn", Surface.SignMode, true, null),

        // Settings overlay
        new("ThemeDarkRadio",     Surface.SettingsOverlay, false, null),
        new("ThemeLightRadio",    Surface.SettingsOverlay, false, null),
        new("ThemeHCRadio",       Surface.SettingsOverlay, false, null),
        new("ThemeBloodRadio",    Surface.SettingsOverlay, false, null),
        new("ThemeGreedRadio",    Surface.SettingsOverlay, false, null),
        new("ThemeCyanoticRadio", Surface.SettingsOverlay, false, null),
        new("LangEnRadio",        Surface.SettingsOverlay, false, null),
        new("LangEsRadio",        Surface.SettingsOverlay, false, null),
        new("LangZhTWRadio",      Surface.SettingsOverlay, false, null),
        new("LangZhCNRadio",      Surface.SettingsOverlay, false, null),
        new("LangBnRadio",        Surface.SettingsOverlay, false, null),
        new("LangTrRadio",        Surface.SettingsOverlay, false, null),
        new("LogEnabledCheck",    Surface.SettingsOverlay, false, null),
        new("OpenLogsBtn",        Surface.SettingsOverlay, false, null),
        new("ClearLogsBtn",       Surface.SettingsOverlay, false, null),
    ];

    public static ControlSpec? Find(string automationId) =>
        All.FirstOrDefault(c => c.AutomationId == automationId);

    public static IReadOnlyList<string> KnownIds { get; } =
        [.. All.Select(c => c.AutomationId)];
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~CatalogTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Scalpel.E2E/Catalog/ControlSpec.cs Scalpel.E2E/Catalog/Catalog.cs Scalpel.Tests/E2E/CatalogTests.cs Scalpel.Tests/Scalpel.Tests.csproj
git commit -m "Add declared control catalog seeded from MainWindow.xaml"
```

---

### Task 8: `AppDriver` — launch/attach + control interaction (FlaUI)

**Files:**
- Create: `Scalpel.E2E/Driver/AppDriver.cs`

**Interfaces:**
- Consumes: FlaUI; `Surface` enum.
- Produces (`class AppDriver : IDisposable`):
  - `static AppDriver Launch(string exePath, string openWithPath)` — starts `Scalpel.exe "<openWithPath>"`, attaches UIA3, waits for the main window.
  - `Window MainWindow { get; }`, `bool IsAlive { get; }`
  - `void Relaunch(string openWithPath)` — kill + relaunch (used after a crash).
  - `AutomationElement? Find(string automationId)` — find a descendant by AutomationId (which equals `x:Name`), or null.
  - `bool Click(string automationId)` — find + invoke/click; returns false if not found.
  - `void EnsureSurface(Surface surface)` — click the mode tab / open the settings overlay so the target control is reachable.
  - `void Resize(int width, int height)` — set the main window bounds.
  - `void DismissModals()` — watchdog: close any unexpected modal/dialog window.
  - `bool DriveOpenDialog(string path)` / `bool DriveSaveDialog(string path)` — type into the native dialog's filename edit and confirm.

> This task is **not** unit-tested in isolation (it requires the real app). It is verified by Task 12's smoke run. Write it with real FlaUI calls; do not stub.

- [ ] **Step 1: Implement `AppDriver`**

```csharp
using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace Scalpel.E2E;

public sealed class AppDriver : IDisposable
{
    private readonly UIA3Automation _automation;
    private Application _app;
    private readonly string _exePath;

    private AppDriver(string exePath, Application app, UIA3Automation automation)
    {
        _exePath = exePath;
        _app = app;
        _automation = automation;
    }

    public static AppDriver Launch(string exePath, string openWithPath)
    {
        var automation = new UIA3Automation();
        var psi = new ProcessStartInfo(exePath, $"\"{openWithPath}\"") { UseShellExecute = false };
        var app = Application.Launch(psi);
        var driver = new AppDriver(exePath, app, automation);
        driver.WaitForMainWindow();
        return driver;
    }

    private void WaitForMainWindow()
    {
        // Retry up to ~15s for the window to appear and become responsive.
        for (int i = 0; i < 60; i++)
        {
            try
            {
                var w = _app.GetMainWindow(_automation, TimeSpan.FromMilliseconds(500));
                if (w != null) return;
            }
            catch { }
            System.Threading.Thread.Sleep(250);
        }
        throw new InvalidOperationException("Scalpel main window did not appear.");
    }

    public Window MainWindow => _app.GetMainWindow(_automation, TimeSpan.FromSeconds(5));

    public bool IsAlive
    {
        get
        {
            try { return !_app.HasExited && MainWindow != null; }
            catch { return false; }
        }
    }

    public void Relaunch(string openWithPath)
    {
        try { _app.Close(); _app.Dispose(); } catch { }
        var psi = new ProcessStartInfo(_exePath, $"\"{openWithPath}\"") { UseShellExecute = false };
        _app = Application.Launch(psi);
        WaitForMainWindow();
    }

    public AutomationElement? Find(string automationId)
    {
        try
        {
            return MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
        }
        catch { return null; }
    }

    public bool Click(string automationId)
    {
        var el = Find(automationId);
        if (el == null) return false;
        try
        {
            if (el.Patterns.Invoke.IsSupported) el.Patterns.Invoke.Pattern.Invoke();
            else el.Click();
            return true;
        }
        catch { return false; }
    }

    public void EnsureSurface(Surface surface)
    {
        switch (surface)
        {
            case Surface.ViewMode:  Click("ModeViewTab");  break;
            case Surface.EditMode:  Click("ModeEditTab");  break;
            case Surface.PagesMode: Click("ModePagesTab"); break;
            case Surface.SignMode:  Click("ModeSignTab");  break;
            case Surface.SettingsOverlay: Click("SettingsBtn"); break;
            case Surface.AlwaysVisible: default: break;
        }
        System.Threading.Thread.Sleep(150);
    }

    public void Resize(int width, int height)
    {
        try { MainWindow.AsWindow()?.SetTransform(width, height); } catch { }
    }

    public void DismissModals()
    {
        try
        {
            foreach (var w in _app.GetAllTopLevelWindows(_automation))
            {
                if (w.Equals(MainWindow)) continue;
                w.AsWindow()?.Close();
            }
        }
        catch { }
    }

    public bool DriveOpenDialog(string path) => DriveFileDialog(path, confirmButtonName: "Open");
    public bool DriveSaveDialog(string path) => DriveFileDialog(path, confirmButtonName: "Save");

    private bool DriveFileDialog(string path, string confirmButtonName)
    {
        try
        {
            for (int i = 0; i < 40; i++)
            {
                var dialog = _app.GetAllTopLevelWindows(_automation)
                    .FirstOrDefault(w => w.AsWindow()?.IsModal == true || (w.Name?.Contains("PDF") ?? false));
                if (dialog != null)
                {
                    var edit = dialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit));
                    edit?.AsTextBox()?.Enter(path);
                    var btn = dialog.FindFirstDescendant(cf => cf.ByName(confirmButtonName))?.AsButton();
                    btn?.Invoke();
                    return true;
                }
                System.Threading.Thread.Sleep(250);
            }
            return false;
        }
        catch { return false; }
    }

    public void Dispose()
    {
        try { _app.Close(); } catch { }
        try { _app.Dispose(); } catch { }
        try { _automation.Dispose(); } catch { }
    }
}
```

> Note: `SetTransform`, `IsModal`, and the exact pattern/property names may differ slightly across FlaUI 4.x. When the project first builds (next step), fix any member-name mismatch against the restored FlaUI API — keep the behavior identical (resize window, detect modal, type path, confirm).

- [ ] **Step 2: Build to verify it compiles against the real FlaUI API**

Run: `dotnet build Scalpel.E2E/Scalpel.E2E.csproj`
Expected: Build succeeded. (If member names differ, correct them now — behavior must match the comments.)

- [ ] **Step 3: Commit**

```bash
git add Scalpel.E2E/Driver/AppDriver.cs
git commit -m "Add FlaUI AppDriver: launch, find, click, resize, dialogs"
```

---

### Task 9: `Assertions` — C-tier UI-state checks (FlaUI)

**Files:**
- Create: `Scalpel.E2E/Driver/Assertions.cs`

**Interfaces:**
- Consumes: `AppDriver`, `LogEntry`.
- Produces:
  - `static class Assertions` with `static (bool ok, string? reason) Check(string assertionKey, AppDriver driver, IReadOnlyList<LogEntry> newLogs)`.
  - Supported keys (match `AssertionKey` values in Task 7): `modeViewActive`, `modeEditActive`, `modePagesActive`, `modeSignActive`, `zoomIncreased`, `zoomDecreased`, `settingsOverlayOpen`. Unknown key → `(true, null)` (treated as B-only, no extra assertion).

> `zoomIncreased`/`zoomDecreased` compare the `ZoomBox` text before/after. Because the ActionRunner (Task 10) captures the pre-action zoom, the simplest robust check here is panel/visibility based; for zoom, assert the relevant button stayed enabled and a `click` was logged. To compare values, the runner passes the prior value — see Task 10 wiring. For this task, implement the visibility/active-panel assertions concretely and make zoom assertions check that `ModePanelView`/zoom controls are present and the action logged a click (value-delta comparison is layered in Task 10).

- [ ] **Step 1: Implement `Assertions`**

```csharp
using FlaUI.Core.AutomationElements;

namespace Scalpel.E2E;

public static class Assertions
{
    public static (bool ok, string? reason) Check(
        string? assertionKey, AppDriver driver, IReadOnlyList<LogEntry> newLogs)
    {
        if (string.IsNullOrEmpty(assertionKey)) return (true, null);

        switch (assertionKey)
        {
            case "modeViewActive":  return PanelVisible(driver, "ModePanelView");
            case "modeEditActive":  return PanelVisible(driver, "ModePanelEdit");
            case "modePagesActive": return PanelVisible(driver, "ModePanelPages");
            case "modeSignActive":  return PanelVisible(driver, "ModePanelSign");
            case "settingsOverlayOpen": return PanelVisible(driver, "SettingsOverlay");
            case "zoomIncreased":
            case "zoomDecreased":
                // Value-delta comparison is performed by ActionRunner; here we only
                // confirm the zoom controls are still present and reachable.
                return driver.Find("ZoomBox") != null
                    ? (true, null)
                    : (false, "ZoomBox not found after zoom action");
            default:
                return (true, null);
        }
    }

    private static (bool ok, string? reason) PanelVisible(AppDriver driver, string automationId)
    {
        var el = driver.Find(automationId);
        if (el == null) return (false, $"{automationId} not found");
        try
        {
            bool offscreen = el.Properties.IsOffscreen.ValueOrDefault;
            return offscreen ? (false, $"{automationId} is offscreen/hidden") : (true, null);
        }
        catch { return (false, $"{automationId} visibility unreadable"); }
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build Scalpel.E2E/Scalpel.E2E.csproj`
Expected: Build succeeded. (Adjust `IsOffscreen` member access to the real FlaUI 4.x API if needed.)

- [ ] **Step 3: Commit**

```bash
git add Scalpel.E2E/Driver/Assertions.cs
git commit -m "Add C-tier UI-state assertions"
```

---

### Task 10: `ActionRunner` — the B+C verification wrapper (FlaUI)

**Files:**
- Create: `Scalpel.E2E/Driver/ActionRunner.cs`

**Interfaces:**
- Consumes: `AppDriver`, `LogReader`, `Assertions`, `ControlSpec`, `ActionResult`, `LogEntry`.
- Produces (`class ActionRunner`):
  - constructor `ActionRunner(AppDriver driver, LogReader log, string openWithPath)`
  - `ActionResult RunControl(string suite, ControlSpec spec)` — ensures surface, snapshots log, captures pre-zoom if relevant, clicks, then runs B checks (alive + expected click logged + no failure line) and the C assertion (incl. zoom-delta).
  - `ActionResult RunRaw(string suite, string action, Action act, string? expectClickMsg, string? assertionKey)` — generic variant for resize/journey steps that aren't a single catalogued control.
  - On a detected crash (window gone), relaunches the app and marks the result `Fail` with reason `"app crashed"`.

- [ ] **Step 1: Implement `ActionRunner`**

```csharp
namespace Scalpel.E2E;

public sealed class ActionRunner
{
    private readonly AppDriver _driver;
    private readonly LogReader _log;
    private readonly string _openWith;

    public ActionRunner(AppDriver driver, LogReader log, string openWithPath)
    {
        _driver = driver;
        _log = log;
        _openWith = openWithPath;
    }

    public ActionResult RunControl(string suite, ControlSpec spec)
    {
        _driver.EnsureSurface(spec.Surface);
        string? priorZoom = ReadZoom();
        int snap = _log.Snapshot();

        bool clicked = _driver.Click(spec.AutomationId);
        System.Threading.Thread.Sleep(120);
        var newLogs = _log.NewSince(snap);

        // Crash check first.
        if (!_driver.IsAlive)
        {
            _driver.Relaunch(_openWith);
            return new ActionResult(suite, spec.AutomationId, Outcome.Fail, "app crashed", newLogs);
        }

        if (!clicked)
            return new ActionResult(suite, spec.AutomationId, Outcome.Fail, "control not found / not clickable", newLogs);

        // B: a failure line appeared.
        var failure = newLogs.FirstOrDefault(e => e.IsFailure);
        if (failure != null)
            return new ActionResult(suite, spec.AutomationId, Outcome.Fail,
                $"failure logged: {failure.Cat}/{failure.Event} {failure.Msg}", newLogs);

        // B: expected click logged (msg == AutomationId, cat == UI).
        bool clickLogged = newLogs.Any(e => e.Cat == "UI" && e.Event == "click" && e.Msg == spec.AutomationId);
        if (!clickLogged)
            return new ActionResult(suite, spec.AutomationId, Outcome.Fail,
                $"expected click '{spec.AutomationId}' not logged", newLogs);

        // C: UI-state assertion.
        var (ok, reason) = Assertions.Check(spec.AssertionKey, _driver, newLogs);
        if (ok && spec.AssertionKey is "zoomIncreased" or "zoomDecreased")
            (ok, reason) = CheckZoomDelta(spec.AssertionKey, priorZoom);
        if (!ok)
            return new ActionResult(suite, spec.AutomationId, Outcome.Fail, reason, newLogs);

        return new ActionResult(suite, spec.AutomationId, Outcome.Pass, null, newLogs);
    }

    public ActionResult RunRaw(string suite, string action, Action act,
        string? expectClickMsg, string? assertionKey)
    {
        int snap = _log.Snapshot();
        try { act(); } catch (Exception ex)
        {
            return new ActionResult(suite, action, Outcome.Fail, $"action threw: {ex.Message}", _log.NewSince(snap));
        }
        System.Threading.Thread.Sleep(120);
        var newLogs = _log.NewSince(snap);

        if (!_driver.IsAlive)
        {
            _driver.Relaunch(_openWith);
            return new ActionResult(suite, action, Outcome.Fail, "app crashed", newLogs);
        }

        var failure = newLogs.FirstOrDefault(e => e.IsFailure);
        if (failure != null)
            return new ActionResult(suite, action, Outcome.Fail,
                $"failure logged: {failure.Cat}/{failure.Event} {failure.Msg}", newLogs);

        if (expectClickMsg != null &&
            !newLogs.Any(e => e.Cat == "UI" && e.Event == "click" && e.Msg == expectClickMsg))
            return new ActionResult(suite, action, Outcome.Fail,
                $"expected click '{expectClickMsg}' not logged", newLogs);

        var (ok, reason) = Assertions.Check(assertionKey, _driver, newLogs);
        if (!ok) return new ActionResult(suite, action, Outcome.Fail, reason, newLogs);

        return new ActionResult(suite, action, Outcome.Pass, null, newLogs);
    }

    private string? ReadZoom()
    {
        try { return _driver.Find("ZoomBox")?.AsTextBox()?.Text; } catch { return null; }
    }

    private (bool, string?) CheckZoomDelta(string key, string? prior)
    {
        string? now = ReadZoom();
        double Parse(string? s) => double.TryParse(
            new string((s ?? "").Where(c => char.IsDigit(c) || c == '.').ToArray()),
            out var v) ? v : double.NaN;
        double a = Parse(prior), b = Parse(now);
        if (double.IsNaN(a) || double.IsNaN(b)) return (true, null); // can't read; don't false-fail
        if (key == "zoomIncreased") return b > a ? (true, null) : (false, $"zoom did not increase ({a}->{b})");
        return b < a ? (true, null) : (false, $"zoom did not decrease ({a}->{b})");
    }
}
```

> `AsTextBox()` requires `using FlaUI.Core.AutomationElements;` — add it if the build flags the extension. Adjust member names to the real FlaUI 4.x surface if needed.

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build Scalpel.E2E/Scalpel.E2E.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add Scalpel.E2E/Driver/ActionRunner.cs
git commit -m "Add ActionRunner B+C verification wrapper"
```

---

### Task 11: The four suites

**Files:**
- Create: `Scalpel.E2E/Suites/SinglesSuite.cs`
- Create: `Scalpel.E2E/Suites/JourneysSuite.cs`
- Create: `Scalpel.E2E/Suites/PairwiseSuite.cs`
- Create: `Scalpel.E2E/Suites/MonkeySuite.cs`

**Interfaces:**
- Consumes: `AppDriver`, `ActionRunner`, `Catalog`, `ControlSpec`, `RunReport`.
- Produces, each suite: `static void Run(AppDriver driver, ActionRunner runner, RunReport report)` — appends `ActionResult`s; Singles also appends `report.UntestedControls`. `MonkeySuite.Run` additionally takes `int seed`.

- [ ] **Step 1: Implement `SinglesSuite` (with coverage cross-check)**

```csharp
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace Scalpel.E2E;

public static class SinglesSuite
{
    public static void Run(AppDriver driver, ActionRunner runner, RunReport report)
    {
        // Exercise every catalogued control once.
        foreach (var spec in Catalog.All)
            report.Results.Add(runner.RunControl("singles", spec));

        // Coverage cross-check: any button in the live tree not in the catalog.
        try
        {
            var buttons = driver.MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Button));
            foreach (var b in buttons)
            {
                string id = b.Properties.AutomationId.ValueOrDefault ?? "";
                if (string.IsNullOrEmpty(id)) continue;
                if (!Catalog.KnownIds.Contains(id) && !report.UntestedControls.Contains(id))
                    report.UntestedControls.Add(id);
            }
        }
        catch { }
    }
}
```

- [ ] **Step 2: Implement `JourneysSuite`**

```csharp
namespace Scalpel.E2E;

public static class JourneysSuite
{
    public static void Run(AppDriver driver, ActionRunner runner, RunReport report)
    {
        // Journey 1: view-mode + zoom tour.
        report.Results.Add(runner.RunRaw("journeys", "view:single",
            () => driver.Click("ModeViewTab"), "ModeViewTab", "modeViewActive"));
        foreach (var v in new[] { "ViewContinuousBtn", "ViewTwoPageBtn", "ViewGridBtn", "ViewSingleBtn", "ViewFitBtn" })
            report.Results.Add(runner.RunRaw("journeys", $"view:{v}", () => driver.Click(v), v, null));
        report.Results.Add(runner.RunRaw("journeys", "zoom:in",
            () => driver.Click("ZoomInBtn"), "ZoomInBtn", "zoomIncreased"));

        // Journey 2: edit tools tour.
        report.Results.Add(runner.RunRaw("journeys", "mode:edit",
            () => driver.Click("ModeEditTab"), "ModeEditTab", "modeEditActive"));
        foreach (var t in new[] { "ToolSelectBtn", "ToolTextBtn", "ToolHighlightBtn", "ToolDrawBtn", "ToolCropBtn" })
            report.Results.Add(runner.RunRaw("journeys", $"tool:{t}", () => driver.Click(t), t, null));

        // Journey 3: sign mode.
        report.Results.Add(runner.RunRaw("journeys", "mode:sign",
            () => driver.Click("ModeSignTab"), "ModeSignTab", "modeSignActive"));

        // Journey 4: settings overlay round-trip (open, toggle theme, close).
        report.Results.Add(runner.RunRaw("journeys", "settings:open",
            () => driver.Click("SettingsBtn"), "SettingsBtn", "settingsOverlayOpen"));
        report.Results.Add(runner.RunRaw("journeys", "settings:theme-blood",
            () => driver.Click("ThemeBloodRadio"), "ThemeBloodRadio", null));
        report.Results.Add(runner.RunRaw("journeys", "settings:theme-dark",
            () => driver.Click("ThemeDarkRadio"), "ThemeDarkRadio", null));
    }
}
```

- [ ] **Step 3: Implement `PairwiseSuite`**

```csharp
namespace Scalpel.E2E;

public static class PairwiseSuite
{
    public static void Run(AppDriver driver, ActionRunner runner, RunReport report)
    {
        // All ordered pairs of Edit tools (state-leak detection between tools).
        var editTools = Catalog.All.Where(c => c.Surface == Surface.EditMode).ToList();
        driver.EnsureSurface(Surface.EditMode);
        foreach (var a in editTools)
            foreach (var b in editTools)
            {
                if (a.AutomationId == b.AutomationId) continue;
                string label = $"{a.AutomationId}->{b.AutomationId}";
                report.Results.Add(runner.RunRaw("pairwise", label, () =>
                {
                    driver.Click(a.AutomationId);
                    driver.Click(b.AutomationId);
                }, b.AutomationId, null));
            }
    }
}
```

- [ ] **Step 4: Implement `MonkeySuite` (seeded)**

```csharp
namespace Scalpel.E2E;

public static class MonkeySuite
{
    public static void Run(AppDriver driver, ActionRunner runner, RunReport report, int seed)
    {
        var rng = new Random(seed);
        var ids = Catalog.KnownIds.ToList();
        var sizes = new (int w, int h)[] { (800, 600), (1280, 800), (1920, 1080), (640, 480) };

        const int actions = 120;
        for (int i = 0; i < actions; i++)
        {
            int roll = rng.Next(0, 10);
            if (roll == 0)
            {
                var (w, h) = sizes[rng.Next(sizes.Length)];
                report.Results.Add(runner.RunRaw("monkey", $"resize:{w}x{h}",
                    () => driver.Resize(w, h), null, null));
            }
            else
            {
                string id = ids[rng.Next(ids.Count)];
                var spec = Catalog.Find(id)!;
                report.Results.Add(runner.RunControl("monkey", spec));
            }
            driver.DismissModals(); // keep the run from wedging on a stray dialog
        }
    }
}
```

- [ ] **Step 5: Build to verify all four suites compile**

Run: `dotnet build Scalpel.E2E/Scalpel.E2E.csproj`
Expected: Build succeeded. (Fix any FlaUI member-name drift, e.g. `Properties.AutomationId`.)

- [ ] **Step 6: Commit**

```bash
git add Scalpel.E2E/Suites/
git commit -m "Add singles, journeys, pairwise, and monkey suites"
```

---

### Task 12: `Program.cs` — CLI orchestration, run, report, exit code

**Files:**
- Modify: `Scalpel.E2E/Program.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: a runnable harness. Exit 0 if no failures and no untested controls; non-zero otherwise.

- [ ] **Step 1: Implement orchestration**

```csharp
using Scalpel.E2E;

internal static class Program
{
    private static int Main(string[] args)
    {
        string suite = ArgVal(args, "--suite") ?? "all";
        string? appPath = ArgVal(args, "--app") ?? FindDefaultApp();
        string reportDir = ArgVal(args, "--report-dir") ?? Path.Combine(Directory.GetCurrentDirectory(), "e2e-reports");
        int seed = int.TryParse(ArgVal(args, "--seed"), out var s) ? s : 1234;
        string stamp = ArgVal(args, "--stamp") ?? "run"; // caller can pass a timestamp; scripts can't use DateTime.Now

        if (appPath == null || !File.Exists(appPath))
        {
            Console.Error.WriteLine("Scalpel.exe not found. Pass --app <path>.");
            return 2;
        }

        // 1. Fixtures.
        string corpusDir = Path.Combine(Path.GetTempPath(), "scalpel-e2e-corpus");
        var corpus = Corpus.Generate(corpusDir);
        string openWith = corpus.First(c => c.Key == "simple-1p").Path;

        // 2. Launch + locate this run's log (newest after launch).
        using var driver = AppDriver.Launch(appPath, openWith);
        System.Threading.Thread.Sleep(800); // let app.start + open.success flush
        string? logPath = LogReader.FindLatestLog(LogReader.DefaultLogDir());
        if (logPath == null)
        {
            Console.Error.WriteLine("No session log found — is logging enabled?");
            return 3;
        }
        var log = new LogReader(logPath);
        var runner = new ActionRunner(driver, log, openWith);
        var report = new RunReport();

        // 3. Run the requested suite(s).
        bool all = suite == "all";
        if (all || suite == "singles")  SinglesSuite.Run(driver, runner, report);
        if (all || suite == "journeys") JourneysSuite.Run(driver, runner, report);
        if (all || suite == "pairwise") PairwiseSuite.Run(driver, runner, report);
        if (all || suite == "monkey")   MonkeySuite.Run(driver, runner, report, seed);

        // 4. Report.
        var (md, json) = Reporter.Write(report, reportDir, stamp);
        Console.WriteLine(Reporter.ToMarkdown(report));
        Console.WriteLine($"\nReport written:\n  {md}\n  {json}");

        // 5. Exit code: fail the run on any failure or any uncatalogued control.
        return (report.Failed() == 0 && report.UntestedControls.Count == 0) ? 0 : 1;
    }

    private static string? ArgVal(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return (i >= 0 && i + 1 < args.Length) ? args[i + 1] : null;
    }

    private static string? FindDefaultApp()
    {
        // Newest published Scalpel.exe under bin/.
        try
        {
            var root = Directory.GetCurrentDirectory();
            return Directory.GetFiles(root, "Scalpel.exe", SearchOption.AllDirectories)
                .Where(p => p.Contains("publish") || p.Contains("Release") || p.Contains("Debug"))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch { return null; }
    }
}
```

- [ ] **Step 2: Build the whole solution**

Run: `dotnet build Scalpel.sln`
Expected: Build succeeded (all projects).

- [ ] **Step 3: Produce a Scalpel.exe to test against**

Run: `dotnet publish Scalpel.csproj -c Release`
Expected: `bin/Release/net48/publish/Scalpel.exe` exists.

- [ ] **Step 4: Smoke-run the journeys suite against the real app**

Run:
```
dotnet run --project Scalpel.E2E/Scalpel.E2E.csproj -- --suite journeys --app bin/Release/net48/publish/Scalpel.exe --report-dir e2e-reports --stamp smoke
```
Expected: The app launches and is driven; console prints a report; `e2e-reports/test-report-smoke.md` and `.json` exist; the Markdown shows the journeys results. Investigate any FlaUI member-name errors or unexpected failures and fix the driver/assertions (behavior must match the design). Re-run until journeys pass against the real UI.

- [ ] **Step 5: Run the full suite once**

Run:
```
dotnet run --project Scalpel.E2E/Scalpel.E2E.csproj -- --suite all --app bin/Release/net48/publish/Scalpel.exe --report-dir e2e-reports --stamp full --seed 1234
```
Expected: A complete report. Real failures it surfaces are findings to triage (and may be genuine app bugs — record them; do not mask them by weakening assertions). Harness bugs (false failures) get fixed.

- [ ] **Step 6: Commit**

```bash
git add Scalpel.E2E/Program.cs
git commit -m "Wire CLI orchestration, run, report, and exit codes"
```

---

### Task 13: Docs + .gitignore + fixtures placeholder

**Files:**
- Create: `Scalpel.E2E/README.md`
- Create: `tests/fixtures/.gitkeep`
- Modify: `.gitignore` (ignore `e2e-reports/`)
- Modify: `docs/LOGGING.md` (cross-link the harness under §6 QA) — optional but recommended.

**Interfaces:** none (documentation).

- [ ] **Step 1: Write `Scalpel.E2E/README.md`**

```markdown
# Scalpel.E2E — end-to-end UI test harness

Drives the real `Scalpel.exe` via FlaUI (Windows UI Automation), verifies behavior
against the JSONL session logs (`docs/LOGGING.md`) plus UI-state assertions, and
emits a Markdown + JSON report.

## Run

    dotnet publish ..\Scalpel.csproj -c Release
    dotnet run --project . -- --suite all --app ..\bin\Release\net48\publish\Scalpel.exe --report-dir ..\e2e-reports --stamp run1 --seed 1234

Suites: `singles` | `journeys` | `pairwise` | `monkey` | `all`.
Exit code is non-zero on any failure or any uncatalogued control.

Drop real PDFs into `..\tests\fixtures\` to fold them into the run.

See `docs/superpowers/specs/2026-06-21-e2e-test-harness-design.md` for the design.
```

- [ ] **Step 2: Add the fixtures placeholder and gitignore entry**

Create `tests/fixtures/.gitkeep` (empty file). Add to `.gitignore`:

```
e2e-reports/
```

- [ ] **Step 3: Commit**

```bash
git add Scalpel.E2E/README.md tests/fixtures/.gitkeep .gitignore
git commit -m "Add E2E harness README, fixtures placeholder, ignore reports"
```

---

## Self-Review notes (for the implementer)

- **Spec coverage:** Driver=FlaUI (Tasks 8–10); verification B+C (Task 10 + Assertions Task 9); layered suites (Task 11); generated corpus + real-fixture pickup (Task 6); Markdown+JSON report with log correlation + untested-controls + exit code (Tasks 5, 12). Native dialog driving + modal watchdog (Task 8). Resize as first-class action (Tasks 11 monkey/journeys).
- **Known soft spots to resolve during implementation, not before:**
  - Exact FlaUI 4.x member names (`SetTransform`, `IsModal`, `Properties.IsOffscreen`, `AsTextBox().Enter`) — verify against the restored package at first build; keep behavior identical.
  - The corrupted-file exception type in `CorpusTests` — set it to whatever PdfPig actually throws.
  - `form-acroform` / `encrypted` fixtures are best-effort; suites must guard on key presence (they currently depend only on `simple-1p`).
  - Native Save/Print dialog driving is implemented but only lightly exercised by the default suites; deepen once journeys pass.
- **No placeholders in code steps:** every code step contains complete, compilable code (modulo the documented FlaUI member-name verification).
```
