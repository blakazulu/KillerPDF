# Update Notification — Design

> Status: approved design, pre-implementation.
> Date: 2026-06-25.

## Goal

Notify the user, inside the app, when a newer version of Scalpel is available, and send
them to the right place to get it — **the Microsoft Store** for Store (MSIX) installs, the
**website** (`https://scalpel-pdf.netlify.app`) for regular installed or portable builds.

The check must respect Scalpel's core promise of **no telemetry / no phone-home**: it is
**opt-in**, sends no information about the user or their files, and is fully reversible.

## Non-goals

- **No in-app auto-update.** The notification's action button opens the download page (or the
  Store); it does not download, replace, or relaunch the EXE. A real self-updater is a separate,
  larger feature and is explicitly out of scope.
- No background polling while the app runs. The check happens once per startup (throttled to once
  per day), never on a timer.

## Version source — `version.json` on the website

A static file is added at `website/public/version.json`, served at
`https://scalpel-pdf.netlify.app/version.json`:

```json
{
  "version": "1.7.0",
  "siteUrl": "https://scalpel-pdf.netlify.app",
  "storeUrl": "https://apps.microsoft.com/detail/9n9hn8xw4lf3",
  "notes": ["Optional one-line highlight", "Another highlight"]
}
```

- `version` — the latest released version, in `Major.Minor.Build` form.
- `siteUrl` / `storeUrl` — destination links, **kept in the JSON** so they can change without
  shipping a new app build. `storeUrl` is the live listing
  (`https://apps.microsoft.com/detail/9n9hn8xw4lf3`); the app falls back to a Microsoft Store
  search URL only if `storeUrl` is somehow missing/empty.
- `notes` — optional; shown as highlight bullets in the overlay. If absent, the overlay omits them.

The release process updates `version` (and optionally `notes`) when publishing; committing the change
triggers a Netlify redeploy. (Wiring this into `release.ps1` is an implementation detail; manual
edit is acceptable initially.)

## Opt-in gate

- The setting `UpdateCheckEnabled` lives in `HKCU\Software\Scalpel\Settings` (via `App.GetSetting` /
  `SetSetting`). Three states: unset (never asked), `1` (enabled), `0` (disabled).
- **First run with the setting unset** → a one-time branded `ScalpelDialog` (Yes/No):
  > "Scalpel can check `scalpel-pdf.netlify.app` once a day for a new version. No information about
  > you or your files is sent. Enable update checks?"
  - Yes → `UpdateCheckEnabled = 1`. No → `UpdateCheckEnabled = 0`.
  - The dialog is shown once regardless of answer; the unset→answered transition is what gates it.
- **Default behavior while unset = off** (no network call until the user agrees).
- A **"Check for updates"** toggle is added to the Settings panel so the choice is discoverable and
  reversible. Toggling it writes `UpdateCheckEnabled`.
- Applies to all distributions (Store / installed / portable). Store builds also honor opt-in even
  though the Store auto-updates, because the user asked for the Store-link prompt.

## The check

`Services/UpdateService.cs` (WPF-free, testable):

- `bool ShouldCheck()` — true when `UpdateCheckEnabled == 1` and `now - LastUpdateCheck > 24h`.
- `Task<UpdateInfo?> CheckAsync()` — GET `https://scalpel-pdf.netlify.app/version.json`,
  TLS 1.2, ~5s timeout, parse with `System.Text.Json`. On any failure (offline, timeout, malformed)
  returns `null` and the caller does nothing — no error surfaced. Writes `LastUpdateCheck` **after
  every attempt, success or failure**, so the 24h throttle holds even when offline (no per-launch
  retry storm).
- `static bool IsNewer(string latest, Version current)` — parse `latest` (`Major.Minor.Build`) and
  compare against `current` truncated to 3 components. Tolerant of extra/missing components.
- `string ResolveUrl(UpdateInfo info)` — `App.IsPackaged()` → `storeUrl` (or Store-search fallback);
  otherwise `siteUrl`.

Settings keys (all under `HKCU\Software\Scalpel\Settings`):
`UpdateCheckEnabled`, `LastUpdateCheck` (ISO-8601 or ticks), `UpdateDismissedVersion`.

## Notification UI — branded overlay on startup

A new overlay mirrors the existing `WhatsNewOverlay` (same `StudioOverlayCard` style, same
show/dismiss handler shape). On `MainWindow` load:

1. If `ShouldCheck()`, run `CheckAsync()` off the UI thread.
2. If it returns an update where `IsNewer(latest, current)` **and** `latest != UpdateDismissedVersion`,
   marshal back to the UI thread and show the overlay:
   - Heading: "Update available".
   - Body: "Scalpel **{latest}** is available — you have {current}." + optional `notes` bullets.
   - **Primary button "Get the update"** → `Process.Start(ResolveUrl(info))` (`UseShellExecute = true`),
     then closes the overlay.
   - **"Later"** → closes and sets `UpdateDismissedVersion = latest` so the same version does not
     re-prompt; a newer version later will.

The overlay never blocks startup (the check is async; the window is already interactive when it
appears).

## Localization

New user-facing strings (opt-in dialog, Settings toggle label, overlay heading/body/buttons) are
added to **all 9 locale files** (`Strings/*.xaml`) with matching keys, per the project's
all-keys-in-every-locale rule. Dynamic values (`{latest}`, `{current}`) are composed in code.

## Changelog

Per the repo rule, add a user-facing entry to `Services/Changelog.cs` describing the new
opt-in update check.

## Testing

`Scalpel.Tests` (xUnit, links source directly):

- `UpdateService.IsNewer` — newer / older / equal / extra-component / malformed inputs.
- `version.json` parse — valid JSON maps to `UpdateInfo`; missing `notes`/`storeUrl` tolerated;
  malformed JSON yields `null` without throwing.
- `ResolveUrl` — packaged → storeUrl (and fallback when empty); non-packaged → siteUrl. (`IsPackaged`
  is injected/abstracted for the test, or `ResolveUrl` takes the packaged flag as a parameter.)

Network fetch itself is not unit-tested (no live calls in tests); `CheckAsync` is structured so the
parse/compare logic is separately testable from the HTTP call.

## Files touched

- `website/public/version.json` — new.
- `Services/UpdateService.cs` — new.
- `MainWindow.xaml` / `MainWindow.xaml.cs` — update overlay markup + `CheckForUpdatesAsync()` on load,
  Settings toggle + handler, opt-in dialog call.
- `Strings/*.xaml` (9 files) — new keys.
- `Services/Changelog.cs` — new entry.
- `Scalpel.Tests/*.csproj` + new test file — `UpdateService` tests (add `<Compile Include>` link).

## Open items / placeholders

- Wiring `version.json` bumping into `release.ps1` is optional polish; manual update is acceptable
  initially.
- For Store builds, `ms-windows-store://pdp/?ProductId=9N9HN8XW4LF3` could be used to open the Store
  app directly instead of the web listing; the web `storeUrl` works everywhere and is the default.
