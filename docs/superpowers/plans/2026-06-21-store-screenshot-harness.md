# Store Screenshot Capture Harness — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the 6 stale KillerPDF-branded store screenshots with a script-driven harness that self-captures the running app to 6 reproducible 1920×1080 PNGs.

**Architecture:** A dev-only (`#if DEBUG`) `/shoot` CLI path runs an in-app harness implemented as a `partial class MainWindow` (so it reaches the existing private `OpenFile`/`SetMode`/`AddAnnotation`/`_annotations` without new public surface). For each shot it applies a theme/accent, opens a generated sample PDF, switches mode, waits for the dispatcher to go idle (the app's render path settles at `DispatcherPriority.Background`), then renders the window's content visual via `RenderTargetBitmap` → PNG. A PowerShell wrapper deletes the old PNGs, builds Debug, runs `Scalpel.exe /shoot`, and verifies the output.

**Tech Stack:** .NET Framework 4.8, WPF, PdfSharpCore (sample PDF), PdfPig (test assertions), xUnit, PowerShell.

## Global Constraints

- C# `Nullable` + `ImplicitUsings` enabled; `LangVersion=latest`; collection expressions (`[]`), target-typed `new`, switch expressions are idiomatic here.
- The `/shoot` path MUST be compiled out of non-DEBUG builds (wrap in `#if DEBUG`). End users can never trigger it.
- Output PNGs: exactly **1920×1080**, written to the repo `screenshots/` directory, names per the shot table.
- I/O wrapped in defensive `try { } catch { }` that fall back (matches repo convention).
- `dotnet` may not be on PATH; fall back to `~/.dotnet/dotnet.exe` (mirror existing scripts).
- Windows-only (the whole project is). PdfSharpCore's built-in font resolver handles `XFont("Arial")` with no `GlobalFontSettings.FontResolver` setup — do not add one.
- Capture is the window **content** visual (no OS title bar) — this is intentional and preferred for Store assets.

**Shot recipe (core deliverable, no annotation seeding):**

| # | File | Mode | Theme | Accent |
|---|------|------|-------|--------|
| 1 | `01-view-dark.png` | View | Dark | Amber |
| 2 | `02-edit-light.png` | Edit | Light | Amber |
| 3 | `03-pages-dark.png` | Pages | Dark | Cyan |
| 4 | `04-sign-dark.png` | Sign | Dark | Amber |
| 5 | `05-highcontrast.png` | View | HighContrast | Amber |
| 6 | `06-view-green.png` | View | Dark | Green |

Annotation seeding (shots 2 & 4) is **optional** — Task 5, only if overlays render cleanly.

Key facts confirmed in the codebase (use these exact members):
- `MainWindow.xaml.cs:42` — `private enum AppMode { View, Edit, Pages, Sign }` (nested → harness must be a partial of `MainWindow`).
- `MainWindow.xaml.cs:842` — `private void OpenFile(string path)` (synchronous).
- `MainWindow.xaml.cs:3104` — `private void SetMode(AppMode mode)`.
- `MainWindow.xaml.cs:1610` — `private void RenderPage(int pageIndex)`.
- `MainWindow.xaml.cs:6865` — `private void AddAnnotation(PageAnnotation annotation)`.
- `MainWindow.xaml.cs:54` — `private readonly Dictionary<int, List<PageAnnotation>> _annotations`.
- `MainWindow.xaml.cs:236-279` — the `Loaded` handler with the existing `/edit` + file-path arg loop.
- `Services/ThemeManager.cs` — `namespace Scalpel.Services`; `public static void ApplyTheme(Theme)` / `ApplyAccent(Accent)`; enums `Theme { Dark, Light, HighContrast }`, `Accent { Amber, Red, Green, Cyan }`.
- Annotation models in `Models/EditingTypes.cs` (namespace `Scalpel`): `HighlightAnnotation { Rect Bounds }`, `InkAnnotation { List<Point> Points; double StrokeWidth }`, `TextAnnotation { Point Position; string Content; double FontSize }`, `SignatureAnnotation : PlacedAnnotation { List<List<Point>> Strokes }` (`PlacedAnnotation` has `Point Position`).

---

### Task 1: Sample-document generator

**Files:**
- Create: `Services/SampleDocument.cs`
- Test: `Scalpel.Tests/SampleDocumentTests.cs`
- Modify: `Scalpel.Tests/Scalpel.Tests.csproj` (add a `<Compile Include>` link)

**Interfaces:**
- Produces: `Scalpel.Services.SampleDocument.Generate(string path) -> string` (writes a 3-page PDF to `path`, returns `path`).

- [ ] **Step 1: Add the source link to the test project**

In `Scalpel.Tests/Scalpel.Tests.csproj`, inside the `<ItemGroup>` that holds the `<Compile Include>` links (after line 33's `Installer.cs` link), add:

```xml
    <Compile Include="..\Services\SampleDocument.cs" Link="Services\SampleDocument.cs" />
```

- [ ] **Step 2: Write the failing test**

Create `Scalpel.Tests/SampleDocumentTests.cs`:

```csharp
using System.IO;
using Scalpel.Services;
using UglyToad.PdfPig;
using Xunit;

namespace Scalpel.Tests
{
    public class SampleDocumentTests
    {
        [Fact]
        public void Generate_WritesValidThreePagePdf()
        {
            string path = Path.Combine(Path.GetTempPath(), $"scalpel_sample_test_{System.Guid.NewGuid():N}.pdf");
            try
            {
                string returned = SampleDocument.Generate(path);

                Assert.Equal(path, returned);
                Assert.True(File.Exists(path));

                using var pdf = PdfDocument.Open(path);
                Assert.Equal(3, pdf.NumberOfPages);

                // Page 1 carries the title text, proving content (not a blank page) was drawn.
                string page1 = pdf.GetPage(1).Text;
                Assert.Contains("Quarterly", page1);
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `~/.dotnet/dotnet.exe test Scalpel.Tests --filter "FullyQualifiedName~SampleDocumentTests"`
Expected: FAIL — compile error, `SampleDocument` does not exist.

- [ ] **Step 4: Implement the generator**

Create `Services/SampleDocument.cs`:

```csharp
using PdfSharpCore.Drawing;
using PdfSharpCore.Drawing.Layout;
using PdfSharpCore.Pdf;

namespace Scalpel.Services
{
    /// <summary>
    /// Generates a believable multi-page sample PDF for the screenshot harness, so captured
    /// shots show a realistic document rather than a lorem-ipsum stub. Windows-only (relies on
    /// PdfSharpCore's built-in font resolver). Not shipped behavior — used by the dev /shoot path.
    /// </summary>
    public static class SampleDocument
    {
        public static string Generate(string path)
        {
            using var doc = new PdfDocument();

            var titleFont   = new XFont("Arial", 26, XFontStyle.Bold);
            var headingFont = new XFont("Arial", 15, XFontStyle.Bold);
            var bodyFont    = new XFont("Arial", 11, XFontStyle.Regular);
            var header      = new XSolidBrush(XColor.FromArgb(255, 30, 41, 59));   // slate header bar
            var ink         = XBrushes.Black;

            string body =
                "This document is a sample used to demonstrate the application. It contains " +
                "multiple paragraphs of body text, headings, and a small table so the page looks " +
                "like a real document a user would view, annotate, and sign. The layout wraps " +
                "across the width of the page and continues onto the following pages.";

            // ── Page 1: header bar + title + table ──────────────────────────────
            {
                var page = doc.AddPage();
                using var gfx = XGraphics.FromPdfPage(page);
                var tf = new XTextFormatter(gfx);

                gfx.DrawRectangle(header, new XRect(0, 0, page.Width, 70));
                gfx.DrawString("Quarterly Report", titleFont, XBrushes.White, new XPoint(40, 46));

                double y = 100;
                gfx.DrawString("Overview", headingFont, ink, new XPoint(40, y));
                y += 14;
                tf.DrawString(body, bodyFont, ink, new XRect(40, y, page.Width - 80, 120), XStringFormats.TopLeft);
                y += 130;

                gfx.DrawString("Summary", headingFont, ink, new XPoint(40, y));
                y += 20;
                var pen = new XPen(XColors.Gray, 0.75);
                string[,] cells =
                {
                    { "Region",  "Result", "Change" },
                    { "North",   "1,204",  "+8%" },
                    { "South",   "  986",  "+3%" },
                    { "West",    "1,540",  "+12%" },
                };
                double colW = (page.Width - 80) / 3, rowH = 24;
                for (int r = 0; r < 4; r++)
                    for (int c = 0; c < 3; c++)
                    {
                        var cell = new XRect(40 + c * colW, y + r * rowH, colW, rowH);
                        gfx.DrawRectangle(pen, cell);
                        var f = r == 0 ? headingFont : bodyFont;
                        tf.DrawString(cells[r, c], f, ink,
                            new XRect(cell.X + 6, cell.Y + 5, cell.Width - 12, cell.Height), XStringFormats.TopLeft);
                    }
            }

            // ── Pages 2-3: heading + wrapped body ───────────────────────────────
            for (int p = 2; p <= 3; p++)
            {
                var page = doc.AddPage();
                using var gfx = XGraphics.FromPdfPage(page);
                var tf = new XTextFormatter(gfx);
                gfx.DrawString($"Section {p}", headingFont, ink, new XPoint(40, 60));
                tf.DrawString(body + " " + body, bodyFont, ink,
                    new XRect(40, 80, page.Width - 80, page.Height - 140), XStringFormats.TopLeft);
            }

            doc.Save(path);
            return path;
        }
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `~/.dotnet/dotnet.exe test Scalpel.Tests --filter "FullyQualifiedName~SampleDocumentTests"`
Expected: PASS (1 test).

- [ ] **Step 6: Commit**

```bash
git add Services/SampleDocument.cs Scalpel.Tests/SampleDocumentTests.cs Scalpel.Tests/Scalpel.Tests.csproj
git commit -m "feat: add sample-document generator for screenshot harness"
```

---

### Task 2: Screenshot harness (partial MainWindow, DEBUG-only)

**Files:**
- Create: `ScreenshotHarness.cs` (repo root, alongside `MainWindow.xaml.cs`)

**Interfaces:**
- Consumes: `SampleDocument.Generate` (Task 1); `MainWindow` privates `OpenFile`, `SetMode`, `AppMode`, `Content`, `Dispatcher`; `ThemeManager.ApplyTheme/ApplyAccent`.
- Produces: `internal void MainWindow.RunScreenshotHarness()` — called from the `Loaded` hook (Task 3). Renders all 6 PNGs then shuts the app down.

- [ ] **Step 1: Create the harness file**

Create `ScreenshotHarness.cs`:

```csharp
#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Scalpel.Services;

namespace Scalpel
{
    public partial class MainWindow
    {
        private const int ShotW = 1920, ShotH = 1080;

        private sealed record Shot(string FileName, AppMode Mode, Theme Theme, Accent Accent, bool Seed);

        private static readonly IReadOnlyList<Shot> ShotRecipe =
        [
            new Shot("01-view-dark.png",    AppMode.View,  Theme.Dark,         Accent.Amber, false),
            new Shot("02-edit-light.png",   AppMode.Edit,  Theme.Light,        Accent.Amber, false),
            new Shot("03-pages-dark.png",   AppMode.Pages, Theme.Dark,         Accent.Cyan,  false),
            new Shot("04-sign-dark.png",    AppMode.Sign,  Theme.Dark,         Accent.Amber, false),
            new Shot("05-highcontrast.png", AppMode.View,  Theme.HighContrast, Accent.Amber, false),
            new Shot("06-view-green.png",   AppMode.View,  Theme.Dark,         Accent.Green, false),
        ];

        /// <summary>
        /// Dev-only: renders the store screenshot set, then exits. Triggered by `/shoot`.
        /// Never compiled into release builds (whole file is #if DEBUG).
        /// </summary>
        internal async void RunScreenshotHarness()
        {
            try
            {
                string outDir = LocateScreenshotsDir();
                Directory.CreateDirectory(outDir);
                string sample = SampleDocument.Generate(
                    Path.Combine(Path.GetTempPath(), "scalpel_sample_shot.pdf"));

                // Render off-screen at a fixed size — monitor size / DPI are irrelevant.
                WindowState = WindowState.Normal;
                ShowInTaskbar = false;
                Left = -10000; Top = -10000;
                Width = ShotW; Height = ShotH;

                foreach (var shot in ShotRecipe)
                {
                    ThemeManager.ApplyTheme(shot.Theme);
                    ThemeManager.ApplyAccent(shot.Accent);
                    OpenFile(sample);
                    SetMode(shot.Mode);
                    if (shot.Seed) SeedShotAnnotations(shot.Mode);   // no-op unless Task 5 lands
                    await SettleAsync();
                    CaptureContent(Path.Combine(outDir, shot.FileName));
                }

                try { File.Delete(sample); } catch { }
            }
            catch (Exception ex)
            {
                try { Console.Error.WriteLine("screenshot harness failed: " + ex); } catch { }
            }
            finally
            {
                Application.Current.Shutdown();
            }
        }

        // Until Task 5, seeding is a no-op so shots 2 & 4 show the mode toolbar over the doc.
        partial void SeedShotAnnotations(AppMode mode);

        private async Task SettleAsync()
        {
            UpdateLayout();
            // The open/render path schedules its final auto-fit at DispatcherPriority.Background
            // (see FinishOpenFile). Drain everything down to idle, twice, before capturing.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        }

        private void CaptureContent(string path)
        {
            var root = (FrameworkElement)Content;
            root.Measure(new Size(ShotW, ShotH));
            root.Arrange(new Rect(0, 0, ShotW, ShotH));
            root.UpdateLayout();

            var rtb = new RenderTargetBitmap(ShotW, ShotH, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(path);
            encoder.Save(fs);
        }

        private static string LocateScreenshotsDir()
        {
            // Walk up from the exe dir (bin\Debug\net48) to the repo root that holds 'screenshots'.
            string? dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 6 && dir is not null; i++)
            {
                string candidate = Path.Combine(dir, "screenshots");
                if (Directory.Exists(candidate)) return candidate;
                dir = Directory.GetParent(dir)?.FullName;
            }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshots");
        }
    }
}
#endif
```

Note: `SeedShotAnnotations` is declared as a `partial` method with no body here, so it compiles to a no-op until Task 5 supplies an implementing partial. (A `partial void` with no implementation is legal and elided by the compiler.)

- [ ] **Step 2: Build Debug to verify it compiles**

Run: `~/.dotnet/dotnet.exe build -c Debug`
Expected: Build succeeded, 0 errors. (Close any running Scalpel.exe first — it locks `pdfium.dll`.)

- [ ] **Step 3: Commit**

```bash
git add ScreenshotHarness.cs
git commit -m "feat: add DEBUG-only in-app screenshot harness"
```

---

### Task 3: Wire the `/shoot` CLI flag

**Files:**
- Modify: `MainWindow.xaml.cs` (the `Loaded` handler, around line 236-252)

**Interfaces:**
- Consumes: `RunScreenshotHarness()` (Task 2).

- [ ] **Step 1: Add the `/shoot` short-circuit at the top of the `Loaded` handler**

In `MainWindow.xaml.cs`, the `Loaded += (_, _) => { ... }` body currently starts with `RestoreWindowSettings();` then `var args = Environment.GetCommandLineArgs();`. Replace the start of that lambda body:

Find:

```csharp
            Loaded += (_, _) =>
            {
                RestoreWindowSettings();

                var args = Environment.GetCommandLineArgs();
```

Replace with:

```csharp
            Loaded += (_, _) =>
            {
#if DEBUG
                // Dev-only screenshot capture: `Scalpel.exe /shoot` renders the store
                // screenshot set and exits. Compiled out of release builds entirely.
                if (Environment.GetCommandLineArgs()
                        .Any(a => string.Equals(a, "/shoot", StringComparison.OrdinalIgnoreCase)))
                {
                    RunScreenshotHarness();
                    return;
                }
#endif
                RestoreWindowSettings();

                var args = Environment.GetCommandLineArgs();
```

(`System.Linq` is available via ImplicitUsings, so `.Any(...)` resolves. If a build error reports `Any` missing, add `using System.Linq;` to the file's usings.)

- [ ] **Step 2: Build Debug to verify it compiles**

Run: `~/.dotnet/dotnet.exe build -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Smoke-test the harness end to end**

Run (close any running Scalpel first):
```
./bin/Debug/net48/Scalpel.exe /shoot
```
Expected: the process runs briefly with no visible window and exits on its own. Then:

Run: `ls screenshots/*.png`
Expected: the 6 new files `01-view-dark.png` … `06-view-green.png` exist (these will be committed in Task 4 after the old ones are removed and dimensions verified).

- [ ] **Step 4: Commit**

```bash
git add MainWindow.xaml.cs
git commit -m "feat: wire /shoot CLI flag to the screenshot harness (DEBUG)"
```

---

### Task 4: Capture script + replace the old screenshots

**Files:**
- Create: `screenshots/capture-screenshots.ps1`
- Delete: `screenshots/1_Blood.png`, `2_Greed.png`, `3_Cyanotic.png`, `4_High_Contrast.png`, `5_Light.png`, `6_Dark.png`

**Interfaces:**
- Consumes: the `/shoot` flag (Task 3).

- [ ] **Step 1: Write the capture script**

Create `screenshots/capture-screenshots.ps1`:

```powershell
<#
.SYNOPSIS
    Regenerates the Microsoft Store screenshots by driving the app's DEBUG /shoot harness.
.DESCRIPTION
    Builds Scalpel (Debug), runs `Scalpel.exe /shoot` which renders 6 PNGs into this folder,
    then verifies each is exactly 1920x1080. Close any running Scalpel.exe first (it locks
    pdfium.dll). The /shoot path exists only in DEBUG builds.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$shotDir = $PSScriptRoot

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) {
    $cand = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    if (Test-Path $cand) { $dotnet = $cand }
}
if (-not $dotnet) { throw "dotnet SDK not found (PATH or ~/.dotnet/dotnet.exe)." }

Write-Host "==> Building Debug..." -ForegroundColor Cyan
& $dotnet build -c Debug (Join-Path $repo 'Scalpel.csproj')
if ($LASTEXITCODE -ne 0) { throw "build failed." }

$exe = Join-Path $repo 'bin\Debug\net48\Scalpel.exe'
if (-not (Test-Path $exe)) { throw "Scalpel.exe not found at $exe" }

Write-Host "==> Running /shoot harness..." -ForegroundColor Cyan
Start-Process -FilePath $exe -ArgumentList '/shoot' -Wait

$expected = @(
    '01-view-dark.png','02-edit-light.png','03-pages-dark.png',
    '04-sign-dark.png','05-highcontrast.png','06-view-green.png')

Add-Type -AssemblyName System.Drawing
$fail = $false
foreach ($name in $expected) {
    $p = Join-Path $shotDir $name
    if (-not (Test-Path $p)) { Write-Host "  MISSING: $name" -ForegroundColor Red; $fail = $true; continue }
    $img = [System.Drawing.Image]::FromFile($p)
    try {
        $ok = ($img.Width -eq 1920 -and $img.Height -eq 1080)
        $color = if ($ok) { 'Green' } else { 'Red' }
        Write-Host ("  {0}  {1}x{2}" -f $name, $img.Width, $img.Height) -ForegroundColor $color
        if (-not $ok) { $fail = $true }
    } finally { $img.Dispose() }
}
if ($fail) { throw "One or more screenshots missing or not 1920x1080." }
Write-Host "==> Done. 6 screenshots verified." -ForegroundColor Green
```

- [ ] **Step 2: Delete the old screenshots**

```bash
git rm screenshots/1_Blood.png screenshots/2_Greed.png screenshots/3_Cyanotic.png screenshots/4_High_Contrast.png screenshots/5_Light.png screenshots/6_Dark.png
```

- [ ] **Step 3: Run the capture script**

Run: `pwsh -File screenshots/capture-screenshots.ps1`
Expected: build succeeds, harness runs, and the summary prints all 6 files at `1920x1080` in green, ending `Done. 6 screenshots verified.`

- [ ] **Step 4: Visually verify each PNG**

Open each of the 6 PNGs and confirm: current **Scalpel** branding (no "KillerPDF"), the correct theme/accent per the shot table, the sample document is **fully rendered** (not blank/half-drawn), and the mode's panel/toolbar is visible. If any shot is blank or half-rendered, the dispatcher settle needs strengthening — add a bounded retry: in `SettleAsync` (Task 2) append one more `await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);` and re-run. Do not proceed until all 6 look correct.

- [ ] **Step 5: Commit**

```bash
git add screenshots/capture-screenshots.ps1 screenshots/*.png
git commit -m "feat: script-driven store screenshots; replace stale KillerPDF set"
```

---

### Task 5 (OPTIONAL): Annotation seeding for shots 2 & 4

Only do this if the core set looks good and you want richer Edit/Sign shots. This is **best-effort** per the spec (§3.4): if seeded overlays don't render in the right place, revert this task — the toolbar-over-doc shots from Task 4 are already valid.

**Files:**
- Create: `ScreenshotHarness.Seed.cs` (repo root) — implements the `partial void SeedShotAnnotations`.
- Modify: `ScreenshotHarness.cs` — flip `Seed` to `true` for shots 2 and 4.

**Interfaces:**
- Consumes: `AddAnnotation(PageAnnotation)`, `RenderPage(int)`, annotation models from `Models/EditingTypes.cs`.

- [ ] **Step 1: Implement the seeding partial**

Create `ScreenshotHarness.Seed.cs`:

```csharp
#if DEBUG
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Scalpel
{
    public partial class MainWindow
    {
        partial void SeedShotAnnotations(AppMode mode)
        {
            // Canvas-space coordinates (top-left origin) over the rendered first page.
            if (mode == AppMode.Edit)
            {
                AddAnnotation(new HighlightAnnotation
                {
                    PageIndex = 0,
                    Bounds = new Rect(140, 210, 360, 22),
                });
                AddAnnotation(new TextAnnotation
                {
                    PageIndex = 0,
                    Position = new Point(150, 320),
                    Content = "Review this section",
                    FontSize = 16,
                });
                AddAnnotation(new InkAnnotation
                {
                    PageIndex = 0,
                    StrokeWidth = 3,
                    Points = new List<Point>
                    {
                        new(150, 380), new(190, 360), new(230, 388), new(270, 360), new(310, 384),
                    },
                });
            }
            else if (mode == AppMode.Sign)
            {
                AddAnnotation(new SignatureAnnotation
                {
                    PageIndex = 0,
                    Position = new Point(360, 560),
                    Scale = 0.6,
                    Strokes = new List<List<Point>>
                    {
                        new() { new(0, 40), new(30, 5), new(55, 45), new(85, 8), new(120, 42), new(160, 12) },
                    },
                });
            }

            // Redraw the page so the freshly-added overlays appear before capture.
            RenderPage(0);
        }
    }
}
#endif
```

- [ ] **Step 2: Enable seeding for shots 2 & 4**

In `ScreenshotHarness.cs`, change the recipe lines for `02-edit-light.png` and `04-sign-dark.png` so their last argument is `true`:

```csharp
            new Shot("02-edit-light.png",   AppMode.Edit,  Theme.Light,        Accent.Amber, true),
            ...
            new Shot("04-sign-dark.png",    AppMode.Sign,  Theme.Dark,         Accent.Amber, true),
```

- [ ] **Step 3: Re-run capture and verify the overlays**

Run: `pwsh -File screenshots/capture-screenshots.ps1`
Then open `02-edit-light.png` and `04-sign-dark.png`. Expected: the highlight/text/ink (shot 2) and signature (shot 4) appear **on the page, in sensible positions**.

Decision gate:
- If they render correctly → continue to Step 4.
- If overlays are missing, clipped, or mispositioned → **revert this task**: `git checkout -- ScreenshotHarness.cs && git rm ScreenshotHarness.Seed.cs` (and re-run capture to restore the unseeded shots). The core set remains valid. Stop here.

- [ ] **Step 4: Commit**

```bash
git add ScreenshotHarness.cs ScreenshotHarness.Seed.cs screenshots/02-edit-light.png screenshots/04-sign-dark.png
git commit -m "feat: seed annotations into Edit/Sign store screenshots"
```

---

## Self-Review

**Spec coverage:**
- §3.1 harness engine → Task 2. §3.2 sample doc → Task 1. §3.3 `/shoot` hook → Task 3. §3.4 annotation seeding (soft) → Task 5 (optional, with revert gate). §3.5 render-settle → `SettleAsync` in Task 2 + verify/retry in Task 4 Step 4. §3.6 safety gate → `#if DEBUG` throughout (Tasks 2 & 3). §3.7 PowerShell wrapper → Task 4. §4 shot list → Global Constraints recipe + Task 2. §5 files → covered. Deletion of old PNGs → Task 4 Step 2. Success criteria §8 → Task 4 Steps 3-4.
- Deviation from spec: harness is a `partial class MainWindow` file (repo root) rather than a standalone `Services/ScreenshotHarness.cs`, because `AppMode` is private-nested and the harness needs private members. This is a strictly better design with no new public surface; intent unchanged.
- Q1 resolved: build **Debug** (gate via `#if DEBUG`); WPF visuals are identical to Release, and Debug keeps the gate airtight. Q3 resolved: settle via dispatcher idle (`Background` is where the app's render finalizes). Q2 deferred to optional Task 5 with a revert gate.

**Placeholder scan:** No TBD/TODO; every code step shows complete code; commands have expected output. The one conditional ("if blank, add another idle await") is a concrete, bounded remedy, not a placeholder.

**Type consistency:** `Generate(string)->string`, `RunScreenshotHarness()`, `SettleAsync()`, `CaptureContent(string)`, `LocateScreenshotsDir()->string`, `SeedShotAnnotations(AppMode)` partial, `Shot` record fields, and the annotation model members (`Bounds`, `Position`, `Content`, `FontSize`, `Points`, `StrokeWidth`, `Strokes`, `Scale`, `PageIndex`) all match `Models/EditingTypes.cs` and the confirmed MainWindow members. `Theme`/`Accent`/`AppMode` enum values match their definitions.
