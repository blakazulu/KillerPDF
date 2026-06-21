# Scalpel E2E Test Harness — Design

**Date:** 2026-06-21
**Status:** Approved (design); pending implementation plan
**Author:** brainstorming session

## Goal

A full end-to-end test harness that drives the real Scalpel WPF UI — exercising every button, the major functions, realistic workflows, combinatorial action pairs, randomized stress, and window resizing — then ingests the app's JSONL session logs, correlates failures to the actions that triggered them, and emits a human-readable Markdown report plus a machine-readable JSON sidecar.

The harness leans on Scalpel's existing per-click + per-operation JSONL logging (see `docs/LOGGING.md`): the app already logs every button click by control name and the `.success`/`.fail` outcome of major operations. The harness drives the UI, reads those logs back, and verifies.

## Non-goals (YAGNI)

- No CI wiring, no cross-machine/cloud runs.
- No performance/memory profiling.
- No visual-diff / screenshot baseline. Correctness is established by log events + UI-state assertions, not pixels.

These are possible follow-ups, explicitly out of scope here.

## Key decisions (from brainstorming)

1. **Driver:** FlaUI (Windows UI Automation). It is the only option that genuinely tests "every button" through the real rendered UI, and the app's existing per-click JSONL logging is built for exactly this drive→read-logs→verify loop.
2. **Verification model:** **C-as-target on a B foundation.** Every control gets a cheap liveness + log check (B); high-value actions additionally get specific UI-state assertions (C).
3. **Combinatorial depth:** **Layered suites** — exhaustive singles + scripted journeys (everyday smoke), plus pairwise-within-mode and seeded-monkey fuzz (deep run). Each suite runs independently.
4. **Fixtures:** **Generated baseline corpus + optional real files.** A deterministic synthesized corpus so the suite runs anywhere with zero setup, plus any real PDFs dropped into `tests/fixtures/`.
5. **Reporting:** **Markdown narrative + JSON sidecar.** The Markdown is the thing a human reads (failure correlated to its triggering action with the surrounding JSONL slice); the JSON is for later machine gating.

## Architecture & project layout

A new **C# console project, `Scalpel.E2E`** (net48, x64), referencing `FlaUI.Core` + `FlaUI.UIA3`. It is kept separate from `Scalpel.Tests` (which holds isolated xUnit unit tests) because this is a long-running, stateful UI driver rather than a set of independent test cases.

```
Scalpel.E2E/
  Scalpel.E2E.csproj
  Program.cs              # CLI entry: parse --suite, --seed, --app, --report-dir
  AppDriver.cs            # launch/attach Scalpel.exe, find controls, click, type, resize, drive dialogs
  Fixtures/Corpus.cs      # generate the baseline PDF corpus (PdfSharpCore)
  Verify/LogReader.cs     # locate + tail the session JSONL, correlate events to actions
  Verify/Assertions.cs    # C-tier UI-state assertions (mode panel visible, zoom changed, ...)
  Suites/Singles.cs       # exhaustive single-control pass
  Suites/Journeys.cs      # scripted realistic workflows
  Suites/Pairwise.cs      # 2-action combos within each mode
  Suites/Monkey.cs        # seeded random-action stress
  Report/Reporter.cs      # Markdown + JSON emitters
tests/fixtures/           # optional real PDFs you drop in (picked up if present)
```

**Invocation:**

```
Scalpel.E2E.exe --suite all --seed 1234 --app <path-to-Scalpel.exe> --report-dir <dir>
```

- `--suite singles|journeys|pairwise|monkey|all` (default `all`)
- `--seed <int>` — seeds the monkey suite so any crash it finds is reproducible
- `--app <path>` — path to `Scalpel.exe` (defaults to the latest `bin/.../publish/Scalpel.exe` if omitted)
- `--report-dir <dir>` — where the Markdown + JSON land (defaults to a `e2e-reports/` dir)

## Control discovery (keeping "every button" honest)

Hybrid discovery:

- The harness **walks the live UIA tree** to enumerate every clickable control, so newly added buttons are automatically in scope and none is silently missed.
- It cross-checks that live set against a small **declared catalog** keyed by `x:Name` (e.g. `ModeEditTab`, `ToolDrawBtn`, `ZoomInBtn`, `ViewGridBtn`, `OpenMenuBtn`, `SaveAsBtn`, `SettingsBtn`, the `Theme*Radio`/`Lang*Radio` sets, `LogEnabledCheck`, `OpenLogsBtn`, `ClearLogsBtn`, ...). Per control the catalog declares:
  - which mode/surface it lives in (`View` / `Edit` / `Pages` / `Sign` / Settings overlay / always-visible),
  - how to reach it (open a file first? switch mode? open the Settings overlay?),
  - its expected `UI/click` event name (the control's `x:Name`),
  - an optional C-tier UI-state assertion.

Any control found in the UIA tree but **absent from the catalog** is reported as **"untested — please classify."** Coverage gaps surface loudly in the report (and via non-zero exit) rather than hiding.

Known surfaces from `MainWindow.xaml` to seed the catalog: mode tabs (`ModeViewTab/Edit/Pages/Sign`), File group (`OpenMenuBtn`, `SaveAsBtn`, `SaveMenuBtn`, `CloseFileMenuItem`), zoom (`ZoomOutBtn`, `ZoomBox`, `ZoomInBtn`), View panel (`ViewSingleBtn`, `ViewContinuousBtn`, `ViewTwoPageBtn`, `ViewGridBtn`, `ViewFitBtn`), Edit tools (`ToolSelectBtn`, `ToolTextBtn`, `ToolHighlightBtn`, `ToolDrawBtn`, `ToolImageBtn`, `ToolCropBtn`), Sign (`ToolSignatureBtn`), sidebar (`SidebarPagesTab`, `SidebarOutlinesTab`, `SidebarToggleBtn`, `PageJumpBox`), Settings overlay (theme radios, language radios, `LogEnabledCheck`, `OpenLogsBtn`, `ClearLogsBtn`), and overlays (`SettingsOverlay`, `ShortcutOverlay`, `AboutOverlay`).

## Verification model (B foundation + C on key actions)

Every action runs through a single wrapper:

1. **Pre:** snapshot the session log's current line count (LogReader).
2. **Act:** perform the click / keystroke / resize via UIA.
3. **Post — B (every control):**
   - assert the main window is still present and responsive,
   - read the newly appended log lines,
   - assert the expected `UI/click` line for this control appeared,
   - assert **no** new `ERROR` / `crash.*` / `.fail` line appeared.
4. **Post — C (key actions only):** run the catalog's UI-state assertion, e.g.:
   - `ModeEditTab` → `ModePanelEdit` is visible (others hidden),
   - `ZoomInBtn` → `ZoomBox` value increased; `ZoomOutBtn` → decreased,
   - open → `PageImage` populated **and** `File/open.success` logged with expected page count,
   - merge/split/flatten/extract → corresponding `File/*.success` logged and resulting page count matches expectation,
   - sign → `Sign/sign.success` logged,
   - view-mode buttons → expected layout panel active (`ContinuousPanel` vs single, grid, two-page).

A failure records: the action, expected-vs-actual, and the surrounding JSONL slice. It does **not** abort the run — each suite is fault-tolerant. If the app actually crashed (window gone), the driver relaunches it before continuing, and the crash is attributed to the triggering action.

## Fixtures & native dialogs

**Baseline corpus** (generated deterministically at startup via PdfSharpCore/Docnet, regenerated on demand):

| Fixture | Purpose / path exercised |
|---|---|
| `simple-1p` | trivial single page |
| `large-50p` | navigation, grid/continuous, page jump, large render |
| `form-acroform` | form-fill path, flatten |
| `encrypted` | the decrypt-to-temp save path |
| `image-only` | image-only render, OCR-less text edge cases |
| `corrupted` | the PDFium rasterize/repair fallback path |

Plus any real PDFs found in `tests/fixtures/` (enumerated and smoke-run if present).

**Native dialogs:** Open / Save / Print use Win32 dialogs. The driver:

- detects the dialog window (by window class / automation id),
- types the target path into its edit box and confirms,
- for **Save**, writes into a throwaway temp dir swept at the end of the run,
- for **Print**, targets the **Microsoft Print to PDF** device so nothing physically prints,
- runs a **watchdog** that dismisses any unexpected modal, so one stuck dialog cannot wedge the whole run.

## The suites

- **Singles** — every catalogued control pressed at least once in a valid context (reaching it as the catalog prescribes). The exhaustive coverage pass; also emits the "untested control" gaps.
- **Journeys** — curated end-to-end workflows mirroring real usage, e.g. open → annotate (text/ink/highlight) → sign → save; open → merge → split → save; open form → fill → flatten → print; open → zoom/view-mode tour → page-navigate.
- **Pairwise** — all ordered 2-action pairs **within each mode** (e.g. every Edit tool followed by every other Edit tool) to catch interaction/state-leak bugs.
- **Monkey** — a seeded random-action stress run (N random clicks including resize and mode switches). The seed makes any discovered crash reproducible; the JSONL click-trail reconstructs the exact sequence.

Window **resize** is a first-class action available to journeys and monkey (and asserted not to crash/relayout-fault): the driver sets several window sizes including small/large extremes.

## Reporting & log correlation

At the end of the run (and after any crash-relaunch), the reporter ingests the run's `scalpel-*.jsonl` session file(s) from `%LOCALAPPDATA%\Scalpel\logs\` (or the MSIX-virtualized path), correlates each failure to its triggering action by timestamp, and emits:

- **`test-report-<ts>.md`:**
  - summary table — per suite: total / passed / failed / untested-controls,
  - per-failure narrative — the action, expected vs actual, and the JSONL slice that preceded it,
  - a "controls never exercised" section.
- **`test-report-<ts>.json`** — the same data, structured for later CI gating.

**Exit code** is non-zero if any action failed **or** any uncatalogued control was found, so the harness can gate a build later.

## Risks / open considerations

- **FlaUI brittleness to layout changes** — mitigated by finding controls via `x:Name`/AutomationId (stable) rather than coordinates, and by the UIA-tree discovery cross-check.
- **Native dialog variance across Windows builds** — mitigated by the watchdog and by matching dialog edit boxes via automation rather than fixed coordinates.
- **Stateful, long-running session** — mitigated by per-action fault tolerance and crash-relaunch; suites are independent so one wedge doesn't lose the rest.
- **Generated-corpus realism** — mitigated by the optional `tests/fixtures/` real-file pickup.
```
