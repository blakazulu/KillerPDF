# About entry in the mode-tab strip — design

**Date:** 2026-06-21
**Status:** Approved (pending spec review)

## Goal

Give the existing About overlay a discoverable entry point in the top mode-tab
strip (next to View/Edit/Pages/Sign), and enrich its content with author,
repository, and license information.

## Background — what already exists

- `AboutOverlay` (`MainWindow.xaml:1152`) is a complete modal overlay card:
  logo, tagline, and a build-integrity info block (version, publisher signer
  subject, certificate thumbprint, EXE SHA256).
- `ShowAboutOverlay()` (`MainWindow.xaml.cs:8862`) populates it and computes the
  SHA256 off the UI thread.
- Today it is reachable **only** by clicking the small `v1.5.1` version label in
  the status bar (`VersionLabel_MouseLeftButtonDown`, `MainWindow.xaml.cs:1838`).
  This is undiscoverable.
- The logo hyperlink points to a **dead placeholder** `https://scalpel.example.com`.
- There is **no repository link and no author credit**.

The overlay follows the established pattern shared with `SettingsOverlay`:
backdrop `Grid` + `StudioOverlayCard` `Border`, backdrop click-to-close,
card click swallowed (`e.Handled = true`), and an overlaid `Ico_WinClose` button.

## Scope of changes

### 1. Tab-strip entry point

Add a plain `Button` labelled **"About"** immediately after the Sign tab in the
mode-tab `DockPanel` (`MainWindow.xaml:464`–`475`), on the left side.

- It is a **plain `Button`**, NOT a grouped `RadioButton`. About is informational,
  not an `AppMode`; it must not participate in mode selection or stay
  highlighted/checked.
- Styled to sit cleanly beside the `StudioModeTab` radio buttons (visually
  consistent but clearly a one-shot action).
- `Click` handler calls `ShowAboutOverlay()`.
- Label comes from a localized string `Str_Mode_About` (new key) so it matches
  the other tab labels' localization model.

No change to `SetMode`, `AppMode`, `_suppressModeEvents`, or the four
`ModePanel*` panels. The button opens the overlay and nothing else.

The status-bar version-label entry point is **kept** (no regression).

### 2. Enrich the overlay content

In the info card (`MainWindow.xaml:1173`–`1209`), add three rows and fix the
dead link. All hyperlinks use the existing
`Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })` pattern.

- **Author:** static text `Liraz Amir` (label `Str_About_Author`).
- **Repository:** clickable hyperlink `github.com/blakazulu/ScalpelPDF`
  → opens `https://github.com/blakazulu/ScalpelPDF` (label `Str_About_Repository`).
- **License:** clickable hyperlink `GPLv3`
  → opens `https://www.gnu.org/licenses/gpl-3.0.html` (label `Str_About_License`).
- **Fix dead logo link:** repoint `AboutLogoBlock`'s hyperlink from
  `https://scalpel.example.com` to `https://github.com/blakazulu/ScalpelPDF`.

Existing rows (version, publisher, thumbprint, SHA256) are unchanged. The
version row still links to the GitHub release tag.

### 2b. Fix the footer credit link

The status-bar footer (`MainWindow.xaml:935`–`943`) shows `© 2026 Liraz Amir`,
where `Liraz Amir` is a `Hyperlink` pointing to the same dead placeholder
`https://scalpel.example.com`. Repoint its `NavigateUri` to
`https://github.com/blakazulu/ScalpelPDF`. This is the same dead-link fix as the
About logo and is done in the same pass. The text and styling are unchanged.

Row population for the new author/license/repo rows can be static XAML
(`Hyperlink` with `RequestNavigate="Hyperlink_RequestNavigate"`, which already
exists at `MainWindow.xaml.cs:8930`) rather than code-behind, keeping
`ShowAboutOverlay()` minimal. The repository row likewise uses a static
`Hyperlink`.

### 3. Localization

Per the project rule (every key must exist in every locale file or a
`DynamicResource` lookup blanks out), add these keys to **all six**
`Strings/*.xaml` files (en-US, es, zh-TW, zh-CN, bn, tr-TR):

- `Str_Mode_About` — the tab button label ("About").
- `Str_About_Author` — info-card label ("Author").
- `Str_About_Repository` — info-card label ("Repository").
- `Str_About_License` — info-card label ("License").

Values `Liraz Amir`, the GitHub URL, and `GPLv3` are proper nouns / URLs and are
not translated; only the row labels are localized.

## Out of scope

- No change to `SetMode`/mode panels.
- No new icon glyph (text label chosen, so the Tabler subset is untouched).
- No change to build-integrity rows or SHA256 computation.
- MSIX/packaged behavior is unaffected (overlay is pure UI).

## Testing / verification

- Build (`dotnet build`) succeeds.
- Manual: launch app → "About" button appears after the Sign tab → click opens
  the overlay → Repository and License links open the browser → logo link opens
  the repo (no longer the dead placeholder) → backdrop/X close the overlay.
- Switch locale → tab label and info-card labels remain populated (no blanks),
  confirming all six locale files carry the new keys.
- Status-bar version label still opens the same overlay.
- Footer `© 2026 Liraz Amir` link opens the GitHub repo (no longer the dead
  placeholder).
