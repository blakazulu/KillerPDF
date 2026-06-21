# Store Screenshot Capture Harness — Design

**Date:** 2026-06-21
**Status:** Approved (design); pending implementation plan
**Goal:** Replace the 6 outdated, KillerPDF-branded store screenshots with a reproducible,
script-driven set of 6 clean 1920×1080 PNGs that show the current Scalpel app across a hybrid
of real editing modes and theme variety.

---

## 1. Problem

`screenshots/` holds 6 PNGs (`1_Blood.png` … `6_Dark.png`) that are stale:

- Old branding ("KillerPDF"), old version (1.5.0).
- Old theme names (Blood / Greed / Cyanotic) that were migrated to the new two-axis model
  (base `Dark` / `Light` / `HighContrast` + accent `Amber` / `Red` / `Green` / `Cyan`).
- They were produced by hand — a marketing "feature showcase" PDF was loaded into the app and
  the window screenshotted per theme. No script exists to regenerate them.

We need new screenshots for the Microsoft Store listing (≥1 required, 4–6 recommended, 1920×1080
PNG — see `docs/MS-STORE-REQUIREMENTS.md` §5c) and a repeatable way to regenerate them as the UI
evolves.

---

## 2. Approach (selected: B)

An **in-app screenshot harness** triggered by a hidden, dev-gated `/shoot` CLI flag, plus a thin
PowerShell wrapper. The app self-captures its own visual tree with `RenderTargetBitmap`, giving
pixel-perfect 1920×1080 output that is DPI-independent and free of DWM/occlusion/foreground
artifacts. This is more robust than external window capture (Approach A: UI-Automation + Win32
`MoveWindow` + `PrintWindow`) and becomes a permanent "regenerate all store screenshots" tool.

Rejected alternatives:
- **A — pure external PowerShell** (UI Automation + `PrintWindow`): fragile against the 9,200-line
  monolith, DPI/occlusion-dependent output.
- **C — minimal hybrid** (auto theme shots, manual mode shots): doesn't deliver full automation.

---

## 3. Components

### 3.1 `Services/ScreenshotHarness.cs` (new, dev-gated)

The capture engine. Public entry point invoked from `MainWindow` when `/shoot` is present.

For each shot in the recipe it performs, on the UI thread:

1. **Size the window** to exactly 1920×1080 logical units: `WindowState = Normal`,
   `WindowStyle`/chrome unchanged (we want the real app chrome), `Width = 1920`, `Height = 1080`,
   centered. (Capture is of the WPF root visual at this size, so physical monitor size/DPI is
   irrelevant — the window need not even be fully on-screen.)
2. **Apply theme + accent:** `ThemeManager.ApplyTheme(theme)` then `ThemeManager.ApplyAccent(accent)`.
3. **Open the sample document:** `OpenFile(samplePath)` (synchronous; `_doc` non-null on success).
4. **Set mode:** `SetMode(mode)`.
5. **Seed annotations** (shots 2 & 4 only) — see §3.4.
6. **Settle layout + render** — see §3.5 (the key correctness step).
7. **Capture:** `RenderTargetBitmap` over the window root visual at 1920×1080 @ 96 DPI →
   `PngBitmapEncoder` → write `screenshots/<name>.png`.

After the last shot: close the doc, clean up the temp sample, and shut the app down
(`Application.Current.Shutdown()`).

The recipe is a hardcoded `IReadOnlyList<Shot>` in this file (YAGNI — no JSON config; the shot
list changes rarely and lives with the code). `Shot` = `{ string FileName, AppMode Mode,
Theme Theme, Accent Accent, bool SeedAnnotations }`.

### 3.2 Sample-document generator

A small PdfSharpCore builder (the app already references PdfSharpCore) producing a believable
multi-page document so shots look like real work, not a lorem-ipsum stub:

- Page 1: a colored header bar, a title, two body paragraphs, a simple bordered table.
- Pages 2–3: headings + body text (gives the Pages/organize view something to show).

Written to a temp file (`%TEMP%\scalpel_sample_shot.pdf`), deleted after the run. Not committed.
Lives either as a private method in `ScreenshotHarness` or a sibling `SampleDocument.cs`; decided
at implementation time based on size.

### 3.3 Hook in `MainWindow` `Loaded`

Extend the existing arg loop (`MainWindow.xaml.cs:240`, which already handles `/edit` + a file
path). Add: if any arg equals `/shoot` (case-insensitive) **and** the dev gate passes (§3.6),
invoke `ScreenshotHarness.Run(this)` and return early — skipping the normal file-open / last-file
restore path.

### 3.4 Annotation seeding (shots 2 & 4)

Best-effort enrichment. If the existing annotation model/collections expose a clean way to add a
`HighlightAnnotation`, `InkAnnotation`, and `TextAnnotation` (shot 2) and a `SignatureAnnotation`
(shot 4) to the current page programmatically, do so. If wiring that up proves fiddly or fragile,
**fall back** to capturing the mode's toolbar over the sample doc with no seeded annotations — the
shot still conveys the feature. This is explicitly a soft requirement; do not block the harness on it.

### 3.5 Render-settle (correctness-critical)

PDF pages are rendered to bitmaps and placed in the WPF tree; rendering may not be complete at the
moment `OpenFile`/`SetMode` returns. Before capture the harness must:

- Call `UpdateLayout()` (Measure/Arrange) on the window.
- Pump the dispatcher to let pending render/load work run — e.g. await a
  `Dispatcher.InvokeAsync(() => {}, DispatcherPriority.ContextIdle)` (or `Loaded`/`Background`),
  and if the page-render path is async, await its completion signal rather than a blind sleep.
- A bounded fallback delay is acceptable as a last resort, but prefer awaiting the actual render.

This is the area most likely to need an iteration to get right (blank/half-rendered captures are
the failure mode). The implementation plan must include a verify step that visually confirms each
PNG is fully rendered.

### 3.6 Safety gate

`/shoot` is honored **only** when it is safe and dev-only:

- `#if DEBUG` builds, **or**
- running loose from `bin\` (mirror the existing "dev build running loose" detection used by the
  pdfium integrity skip in `App`), **and**
- never when `App.IsPackaged()` or running from the installed location.

In any shipped/packaged/installed context `/shoot` is ignored (falls through to normal startup),
so end users can never trigger it.

### 3.7 `screenshots/capture-screenshots.ps1` (new)

The wrapper. Steps:

1. Locate `dotnet` (PATH or `~/.dotnet/dotnet.exe` — match existing scripts).
2. `dotnet publish -c Release` (or a debug build — see open question Q1).
3. Run `<publishDir>\Scalpel.exe /shoot`.
4. Verify 6 PNGs appeared in `screenshots/`; print a summary (names + dimensions).
5. Non-zero exit if any shot is missing.

Guidance comment at top: close any running Scalpel.exe first (it locks `pdfium.dll`).

---

## 4. Shot list (hybrid, 6 × 1920×1080 PNG)

| # | File | Mode | Theme | Accent | Seed annotations | Shows |
|---|------|------|-------|--------|------------------|-------|
| 1 | `01-view-dark.png` | View | Dark | Amber | no | Clean read of sample doc (hero) |
| 2 | `02-edit-light.png` | Edit | Light | Amber | yes | Edit toolbar + highlight/ink/text |
| 3 | `03-pages-dark.png` | Pages | Dark | Cyan | no | Page organize / grid view |
| 4 | `04-sign-dark.png` | Sign | Dark | Amber | yes | Signature placed on doc |
| 5 | `05-highcontrast.png` | View | HighContrast | (n/a) | no | Accessibility / theme variety |
| 6 | `06-view-green.png` | View | Dark | Green | no | Theme-variety highlight |

The 6 existing `*_*.png` files are deleted.

---

## 5. Files touched

- **New:** `Services/ScreenshotHarness.cs`, `screenshots/capture-screenshots.ps1`,
  (optional) `Services/SampleDocument.cs`.
- **Edited:** `MainWindow.xaml.cs` (arg-parse hook in `Loaded`, ~5 lines).
- **Deleted:** `screenshots/1_Blood.png` … `6_Dark.png`.
- **Added at runtime, not committed:** the regenerated PNGs replace the deleted ones; the temp
  sample PDF is transient.

---

## 6. Non-goals

- No JSON/config-driven recipe (hardcoded list is sufficient).
- No promo hero art (1920×1080 16:9) or trailers — optional for the Store, out of scope here.
- No in-package MSIX tile/logo assets — those are handled by `packaging/generate-assets.ps1`.
- The `/shoot` path is never a shipped user feature.

---

## 7. Open questions for the plan

- **Q1 — build config:** publish **Release** (matches what ships, real visuals) vs **Debug**
  (simpler gate via `#if DEBUG`). Leaning Release + the "running loose from bin" gate so the gate
  works in Release too. Resolve in the plan.
- **Q2 — annotation seeding feasibility:** confirm whether the annotation model exposes a clean
  programmatic add path; if not, ship shots 2 & 4 as toolbar-over-doc (per §3.4).
- **Q3 — render-settle mechanism:** identify the actual page-render completion signal to await
  (preferred over a fixed delay).

---

## 8. Success criteria

- `pwsh -File screenshots/capture-screenshots.ps1` produces 6 fully-rendered 1920×1080 PNGs in
  `screenshots/`, each matching its row in §4, with current Scalpel branding and correct theme.
- Re-running the script reproduces the set.
- `/shoot` is inert in packaged/installed/shipped builds.
- No PDF binary or temp artifact is committed.
