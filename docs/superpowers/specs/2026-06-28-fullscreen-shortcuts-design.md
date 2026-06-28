# Design Spec — Full-screen (F11) + Keyboard Shortcuts (Tier 1, feature 3)

**Date:** 2026-06-28
**Status:** Approved (design), proceeding to plan
**Program:** `killerpdf-feature-port-program` memory. Foundation refactor + Line tool + Document Info done.

## Goal

(A) A distraction-free **Full-screen mode (F11)** that hides all chrome and fills the monitor with just the document. (B) A batch of **keyboard shortcuts**: F-keys for view modes and help/about, and bare-letter keys for Edit tools. Plus the shortcut-overlay and localization updates.

## Part A — Full-screen (F11)

New partial file **`MainWindow.FullScreen.cs`**. State fields: `bool _fullScreen`, saved window placement (`_fsPrevLeft/Top/W/H`, `_fsPrevState`, `_fsPrevTopmost`, `_fsPrevResize`), saved sidebar (`_fsSidebarWidth`, `_fsSidebarMin`, `_fsSplitterWidth`), saved chrome-row heights (`_fsRow0/1/2/4`).

`ToggleFullScreen()` flips `_fullScreen` and calls `ApplyFullScreen(entering)`.

**`ApplyFullScreen(bool entering)`** — entering:
- Save state. Set the 4 chrome borders `Visibility=Collapsed` and zero their row heights: `RootGrid.RowDefinitions[0/1/2/4].Height = new GridLength(0)`. (Row 3 = content `*` fills.)
- Collapse sidebar: save `SidebarCol.Width`/`MinWidth` and the splitter column width; set `SidebarCol.MinWidth = 0`, `SidebarCol.Width = 0`, splitter column (index 1) width `0`; collapse `SidebarBorder`, `SidebarSplitter`, and the sidebar toggle strip/button.
- Dark backdrop: `PagePreviewPanel.Background = new SolidColorBrush(Color.FromRgb(0x26,0x26,0x26))`.
- Monitor bounds (reuse the EXISTING `MonitorFromWindow`/`GetMonitorInfo`/`MONITORINFO`/`RECT`/`MONITOR_DEFAULTTONEAREST` in `MainWindow.Settings.cs` — do NOT redeclare): compute the current monitor's full bounds in DIPs (`VisualTreeHelper.GetDpi(this)` to convert), then `Topmost = true; ResizeMode = NoResize; Left/Top/Width/Height = bounds; if (WindowState==Maximized) WindowState=Normal; Left/Top/Width/Height = bounds` again (upstream's set→Normal→re-apply sequence avoids the multi-monitor flash and the maximized-clamp-to-workarea).

Exiting: restore every saved value in reverse; `PagePreviewPanel.Background` back to its theme brush via `SetResourceReference`; `Topmost/ResizeMode/WindowState` restored; re-show the chrome borders + sidebar elements.

**Toast hint** (`ShowFullScreenHint()`): on entering, a brief centered "Press F11 or Esc to exit" toast that fades out after ~2s (simple `DispatcherTimer` + opacity animation). **YAGNI:** no black cross-fade (upstream's elaborate transition) — direct toggle is simpler and robust; fade is later polish.

**XAML changes (`MainWindow.xaml`):** add `x:Name` to the root grid (`RootGrid`) and the 4 currently-unnamed chrome borders: `TitleBarBorder` (Grid.Row=0), `RibbonTabBorder` (Row=1), `RibbonBandBorder` (Row=2), `StatusBarBorder` (Row=4). No layout change — names only.

**Invocation:** F11 in `OnPreviewKeyDown` → `ToggleFullScreen()`. Esc: extend the existing Escape arm so that if `_fullScreen`, it exits full-screen and is handled (instead of closing the app / overlays).

## Part B — Keyboard shortcuts

All added as new arms in the existing `OnPreviewKeyDown` (`MainWindow.KeyboardShortcuts.cs`), which already has the top guards `if (e.OriginalSource is TextBox) return;` and the `_activeTextBox` focus check (so none of these fire while typing). Place F-key arms near the F12 arm; place the Escape/full-screen change in the Escape arm.

- **F11** → `ToggleFullScreen(); e.Handled = true;`
- **F5/F6/F7/F8** → `SetViewMode(ViewMode.Single/Continuous/TwoPage/Grid)`
- **F1** → toggle `ShortcutOverlay` visibility (same as `ShortcutHelp_Click`)
- **F2** → `ShowAboutOverlay()`
- **Tool-letter keys** (only when `Keyboard.Modifiers == ModifierKeys.None`): each switches to Edit mode then activates the tool — `SetMode(AppMode.Edit); SetTool(EditTool.X);`
  - `V`→Select, `T`→Text, `H`→Highlight, `D`→Draw, `L`→Line, `I`→Image
  - (Crop and Signature deferred: they live in non-Edit ribbon modes; bare-key mode-jumps there are murkier. Out of scope for v1.)

**Shortcut overlay (`MainWindow.xaml`, `ShortcutOverlay`):** add new `DockPanel` rows (matching the existing row format — fixed-120 mono key TextBlock + localized description) for: Full screen (F11), the four view modes (F5–F8), Help (F1), About (F2), and a "Tools (V/T/H/D/L/I)" row, plus Document Info (F12, added last feature but not yet in the overlay).

## Localization (all 9 `Strings/*.xaml`)
New `Str_KS_*` description keys (English shown; translate the rest; RTL he/ar included):
- `Str_KS_FullScreen` = "Full screen"
- `Str_KS_ViewSingle` = "Single page view"
- `Str_KS_ViewContinuous` = "Continuous view"
- `Str_KS_ViewTwoPage` = "Two-page view"
- `Str_KS_ViewGrid` = "Grid view"
- `Str_KS_Help` = "Keyboard shortcuts"
- `Str_KS_About` = "About"
- `Str_KS_Tools` = "Edit tools (Select / Text / Highlight / Draw / Line / Image)"
- `Str_KS_DocInfo` = "Document info"
- `Str_FullScreen_Hint` = "Press F11 or Esc to exit full screen"

## Changelog
One bullet in the newest `Release` (`Services/Changelog.cs`): full-screen mode (F11) and new keyboard shortcuts (F1 help, F2 about, F5–F8 view modes, F11 full screen, and letter keys for Edit tools).

## Testing
- No extractable pure logic (window/visibility manipulation + key routing). Verified by `dotnet build` clean + the 187 suite staying green.
- **Locale completeness:** `grep -L "Str_KS_FullScreen" Strings/*.xaml` → nothing.
- **Manual smoke (owed to user — GUI can't run headless):** F11 hides all chrome and fills the monitor (sidebar gone, no side strips, taskbar covered); F11/Esc restores exactly (window placement, sidebar width, maximized state); on a second monitor it fills the correct screen. F5–F8 switch view modes; F1 toggles the shortcut overlay (now listing the new keys); F2 opens About; V/T/H/D/L/I switch Edit tools (and into Edit mode); none fire while typing in a text field or annotation edit box.

## Out of scope (YAGNI)
- No black cross-fade transition (direct toggle + toast).
- No Crop/Signature letter keys, no Ctrl+B sidebar toggle (deselected), no remembering full-screen across sessions.

## Definition of done
F11 toggles a true full-screen mode that hides all chrome and restores cleanly (incl. multi-monitor + maximized); F1/F2/F5–F8 and V/T/H/D/L/I shortcuts work and don't fire while typing; the shortcut overlay lists the new keys; build clean; 187 tests green; all 9 locales + changelog updated.
