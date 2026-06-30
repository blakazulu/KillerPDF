# Recent Files (MRU) — Design

**Status:** approved
**Tier:** 2 (KillerPDF feature-port program)
**Date:** 2026-06-30

## Goal

Let users reopen recently-opened PDFs from two surfaces — the empty-state DropZone (visible on launch / when no document is open) and the Open-button right-click menu — in Scalpel's Clinical design language, RTL-aware, localized to all 9 locales.

## Non-goals (YAGNI)

- Pinned / favorite files.
- Windows taskbar Jump Lists.
- Thumbnails / preview images in the list.
- A separate Settings toggle to disable the feature.

## Architecture

A pure MRU service holds all list logic, decoupled from the registry and WPF so it is unit-testable. Thin `App` wrappers persist it. Two view surfaces render the same data on demand.

```
Services/RecentFiles.cs   (pure, WPF-free, unit-tested)
        ▲ used by
App.xaml.cs wrappers       (GetRecentFiles / AddRecentFile / RemoveRecentFile / ClearRecentFiles)
        ▲ used by
MainWindow.Recent.cs       (PopulateRecentList + menu rebuild + row/menu handlers)
        ▲ triggered by
FinishOpenFile (capture) · CloseFile + ctor (empty-state) · Open ContextMenuOpening (menu)
```

## Components

### 1. `Services/RecentFiles.cs` (new, pure)

Static helpers operating on plain `List<string>` — no registry, no WPF:

- `Parse(string? raw) → List<string>` — split on `'|'`, drop empties/whitespace. `'|'` is illegal in Windows paths, so it is a safe separator (matches upstream).
- `Serialize(IEnumerable<string> list) → string` — join with `'|'`.
- `Add(IReadOnlyList<string> current, string path, int max = 10) → List<string>` — prepend `path`, remove any prior **case-insensitive** duplicate of it, cap length at `max` (drop from the tail).
- `Remove(IReadOnlyList<string> current, string path) → List<string>` — remove case-insensitive matches.

`Max` default is 10.

### 2. `App.xaml.cs` wrappers

Registry-backed, mirroring upstream names; persist to the `RecentFiles` value under `HKCU\Software\Scalpel\Settings` via the existing `GetSetting`/`SetSetting`:

- `internal static List<string> GetRecentFiles()` → `RecentFiles.Parse(GetSetting("RecentFiles"))`.
- `internal static void AddRecentFile(string path)` → `SetSetting("RecentFiles", RecentFiles.Serialize(RecentFiles.Add(GetRecentFiles(), path)))`.
- `internal static void RemoveRecentFile(string path)` → `SetSetting(... RecentFiles.Remove(...))`.
- `internal static void ClearRecentFiles()` → `SetSetting("RecentFiles", "")` (no `RemoveSetting` exists; an empty string parses to an empty list).

### 3. Capture on open

In `MainWindow.FileOps.cs` `FinishOpenFile(string displayPath, string workingPath)` (line ~547), after the document is successfully finalized:

```csharp
try { if (System.IO.File.Exists(displayPath)) App.AddRecentFile(displayPath); } catch { }
```

`displayPath` is the user's real path (blank/New docs never reach here with a real path, so they are naturally skipped). Wrapped defensively per the codebase convention.

### 4. Empty-state list (DropZone)

`MainWindow.xaml`: inside the existing DropZone card, below the "Drop PDF here / or click to browse" `StackPanel`, add a **Recent** section:

- A `TextBlock` header `x:Name="RecentHeader"` bound to `Str_Recent` (`FontUI`, `TextDim`, SemiBold), with a thin top separator `Border`.
- An `ItemsControl x:Name="RecentList"` whose `ItemTemplate` is a row: a left file glyph (`FontIcon` Tabler), the filename (`TextPrimary`), the dim parent-directory (`TextDim`, right-aligned/ellipsized), and a trailing ✕ `Button` (Tabler `Ico_WinClose`, tooltip `Str_Recent_Remove`).
- The whole row is click-to-open; the ✕ button removes just that entry.

Because the DropZone intercepts `MouseLeftButtonDown` to browse, the Recent section must mark its own clicks `Handled` so clicking a row/✕ does not also fire the browse dialog.

`PopulateRecentList()` (in new `MainWindow.Recent.cs`):
- Reads `App.GetRecentFiles()`, filters to entries where `File.Exists(path)` is true.
- Sets `RecentList.ItemsSource` to a small view-model list (`RecentItemVm { Path, FileName, Dir }`).
- Toggles `RecentHeader`/`RecentList` visibility — hidden when the filtered list is empty (no "empty" text needed beyond the existing DropZone prompt).

Called from: the `MainWindow` constructor (initial empty state) and `CloseFile()` (`MainWindow.DirtyTracking.cs` ~line 72, where `DropZone.Visibility = Visible`).

Row handlers:
- Click → `OpenRecent(path)`: if `File.Exists` → `OpenFile(path)`; else `ShowToast(Loc("Str_Recent_NotFound"))` + `App.RemoveRecentFile(path)` + `PopulateRecentList()`.
- ✕ → `App.RemoveRecentFile(path)` + `PopulateRecentList()` (mark event Handled).

### 5. Open-button context menu

`MainWindow.xaml`: the existing Open `Button.ContextMenu` (Open/New/Close) gets `x:Name="OpenContextMenu"` and an `Opened`/`ContextMenuOpening` handler `RebuildOpenRecentMenu`.

`RebuildOpenRecentMenu` (in `MainWindow.Recent.cs`):
- Removes any previously-added recent items (tagged so static Open/New/Close items are preserved).
- For each existing recent file (filtered by `File.Exists`): a `Separator` (once), then a `MenuItem` with `Header = filename`, `ToolTip = full path`, `Click → OpenRecent(path)`.
- If any items were added: a trailing `Separator` + a **Clear recent** `MenuItem` (`Str_Recent_Clear`, `Click → App.ClearRecentFiles()`).
- If no recent files exist, only the static Open/New/Close items show.

### 6. Localization

New keys in **all 9** `Strings/*.xaml` (en-US, es, zh-TW, zh-CN, bn, tr-TR, he, ar, ru):
- `Str_Recent` — "Recent"
- `Str_Recent_Clear` — "Clear recent"
- `Str_Recent_Remove` — "Remove from recent" (✕ tooltip)
- `Str_Recent_NotFound` — "File not found — removed from Recent"

### 7. Changelog

Prepend a bullet to the current release in `Services/Changelog.cs`: e.g. "Recent files: reopen recent PDFs from the start screen or the Open menu."

## Error handling

All registry / file I/O is wrapped in defensive `try/catch` that swallow and no-op (per codebase convention). A missing file is never a hard error — it is filtered on display and self-heals on click. A corrupt `RecentFiles` value parses to whatever entries are valid (empties dropped).

## Testing

- **xUnit (`Scalpel.Tests`)** — `RecentFilesTests` covering: add prepends; add dedupes case-insensitively and moves-to-front; cap at max drops the oldest; remove is case-insensitive; parse drops empties/whitespace; serialize↔parse round-trips; paths containing spaces and non-ASCII survive. Link `Services/RecentFiles.cs` into the test csproj (the project links sources directly).
- **UI** — populate/menu logic is data-driven by the tested service; manual smoke verifies both surfaces (open a few files → see them on the start screen and in the Open menu; ✕ and Clear work; deleting a file removes it on next show).

## Files

- Create: `Services/RecentFiles.cs`, `MainWindow.Recent.cs`, `Scalpel.Tests/RecentFilesTests.cs`.
- Modify: `App.xaml.cs` (wrappers), `MainWindow.FileOps.cs` (capture in `FinishOpenFile`), `MainWindow.DirtyTracking.cs` (populate in `CloseFile`), `MainWindow.xaml.cs` (populate in ctor), `MainWindow.xaml` (DropZone Recent section + Open menu name/handler), `Strings/*.xaml` ×9, `Services/Changelog.cs`, `Scalpel.Tests/Scalpel.Tests.csproj` (link new source).
