# Branded Install / Uninstall Experience + Complete Data Wipe — Design

**Date:** 2026-06-21
**Status:** Approved (design); pending implementation plan

## Problem

Scalpel's portable EXE is its own installer/uninstaller (no MSI, per-user, no UAC). Two problems:

1. **The dialogs are not on-brand.** The install launcher is a hand-built WPF window with a hardcoded green palette (`#4ade80`) unrelated to the current "Studio" amber identity; uninstall uses plain `MessageBox` calls. They do not feel "beautiful and amazing."
2. **Uninstall leaves data behind.** The current `Uninstall()` removes the registry keys, shortcuts, and the *install* directory, but **never deletes `%LOCALAPPDATA%\Scalpel\`** — the data directory holding saved signatures, logs, temp files, and crash logs. It is not a zero-leftover uninstall.
3. The Add/Remove Programs entry lists a placeholder `Publisher = "Your Name"`.

All of this lives inside the already-oversized `App.xaml.cs` monolith.

## Goal

- A fixed, branded install + uninstall experience (deep canvas + amber accent + Geist/Tabler + subtle grain), consistent regardless of the user's chosen theme.
- A **zero-leftover** uninstall: nothing of Scalpel remains in the registry or filesystem afterward, including saved signatures.
- Publisher = **"Liraz Amir"**.
- Extract install/uninstall logic and UI out of `App.xaml.cs` into focused, testable units.

## Decisions (from brainstorming)

1. **Visual identity:** fixed brand look (not theme-aware). These dialogs run before the main window / theme dictionaries load, so they are self-contained with a hardcoded brand palette and pack-URI font references — no dependency on `Themes/_Shared.xaml` or theme color tokens.
2. **Wipe scope:** everything — install dir, the entire `%LOCALAPPDATA%\Scalpel\` data dir (saved signatures included), all registry keys/values, shortcuts, and `%TEMP%` scratch. The confirm dialog clearly warns that saved signatures will be removed.
3. **Uninstall UX:** confirm → progress ("Removing Scalpel…") → farewell ("Done — thanks for using Scalpel") that auto-closes.
4. Install gets the same brand treatment (launcher + brief "Installing…" → relaunch).

## Architecture

Two new units; `App.xaml.cs` keeps only thin orchestration.

### `Services/Installer.cs` — install/uninstall logic + canonical inventory

The single source of truth for everything Scalpel owns, so install (creates) and uninstall (removes) cannot drift.

```csharp
internal static class Installer
{
    // Canonical inventory — the ONLY place these are enumerated.
    // HKCU-relative subkey paths fully removed via DeleteSubKeyTree.
    public static IReadOnlyList<string> OwnedRegistryKeys { get; }     // subtrees
    // (parentKeyPath, valueName) pairs removed via DeleteValue — stray values
    // under keys we must NOT delete wholesale (.pdf, RegisteredApplications).
    public static IReadOnlyList<(string KeyPath, string ValueName)> OwnedRegistryValues { get; }
    // Absolute filesystem paths (dirs + the two .lnk files).
    public static IReadOnlyList<string> OwnedPaths { get; }

    public static void Install(bool wantDesktopShortcut);   // = today's DoInstall
    public static void RegisterFileHandler();
    public static void WipeAllData();                        // registry + shortcuts + %TEMP% sweep, in-process
    public static string WriteDeferredDirWipeScript();       // returns .bat path; rmdir install+data dirs post-exit
}
```

`OwnedRegistryKeys` (HKCU subtrees):
- `Software\Scalpel` (covers `Settings` and `Capabilities`)
- `Software\Microsoft\Windows\CurrentVersion\Uninstall\Scalpel`
- `Software\Classes\Scalpel.pdf`

`OwnedRegistryValues` (delete the value, keep the parent):
- (`Software\Classes\.pdf\OpenWithProgids`, `Scalpel.pdf`)
- (`Software\RegisteredApplications`, `Scalpel`)

`OwnedPaths`:
- `%LOCALAPPDATA%\Programs\Scalpel` (install dir)
- `%LOCALAPPDATA%\Scalpel` (data dir: `signatures.json`, `logs\`, `Temp\`, crash logs)
- Start-Menu dir `…\Programs\Scalpel` + its `Scalpel.lnk`
- Desktop `Scalpel.lnk`
- (`%TEMP%\scalpel_*.pdf` swept by glob, not a fixed path)

**Why split keys vs. values:** `.pdf\OpenWithProgids` and `RegisteredApplications` are shared shell keys owned by Windows — we delete only our value, never the key.

### `Services/InstallerUI.cs` — branded dialogs (self-contained)

Holds the brand palette constants and the dialogs. No dependency on theme dictionaries.

```csharp
internal static class InstallerUI
{
    // Brand palette (fixed): Canvas #0A0B0E, Panel #14161A, Accent #F2A93B,
    // AccentHover #F6C170, TextPrimary #E7E9EE, TextDim #7C818C, Danger #EF4444.
    // Fonts via pack URI to the embedded Geist + Tabler resources.

    // Install launcher. Returns the user's choice.
    public static (bool cancelled, bool install, bool wantDesktop) ShowLauncher(bool alreadyInstalled);

    // Uninstall confirm → progress → farewell. Runs the wipe via the provided
    // callbacks so UI and logic stay separated. Returns false if the user cancels.
    public static bool RunUninstallFlow(Action inProcessWipe, Func<string> writeDeferredScript);
}
```

- **Launcher:** one branded screen — Scalpel logo (amber), version, one line of copy, a "Create desktop shortcut" toggle, `[Run]` (portable) and `[Install]` (primary, amber) buttons. On Install, a brief "Installing…" state, then `App` relaunches from the installed copy.
- **Uninstall flow:** themed confirm card with the zero-leftover warning and `[Cancel]` / `[Remove]`; on Remove, a "Removing Scalpel…" progress state while `inProcessWipe()` runs and the deferred bat is launched; then a farewell card that auto-closes after ~2 s; the process then exits and the bat completes the directory removal.
- The custom-chrome window scaffolding (borderless, draggable title bar, custom close button) is reused from today's `ShowLauncher`, refactored into a small private helper shared by both dialogs.

### `App.xaml.cs` — orchestration only

- `OnStartup`: the existing packaged-mode gate and portable/install decision stays; the launcher call becomes `InstallerUI.ShowLauncher(...)` and install becomes `Installer.Install(...)`.
- `/uninstall` arg → `InstallerUI.RunUninstallFlow(Installer.WipeAllData, Installer.WriteDeferredDirWipeScript)`.
- Existing constants (`InstallDir`, `InstallExe`, shortcut paths, `TempDir`) move to / are shared with `Installer`.
- The `IsPackaged()` suppression of all self-install behavior is preserved unchanged.

## Uninstall flow (detailed)

1. `/uninstall` → `RunUninstallFlow`.
2. Confirm card (warning that ALL data incl. signatures is removed). Cancel → return, app exits.
3. Remove → progress card. `WipeAllData()`:
   - `DeleteSubKeyTree` each `OwnedRegistryKeys` (try/catch each).
   - `DeleteValue` each `OwnedRegistryValues` (try/catch each).
   - Delete the `.lnk`s and Start-Menu dir.
   - Sweep `%TEMP%\scalpel_*.pdf`.
   - `SHChangeNotify(SHCNE_ASSOCCHANGED, …)` so Explorer drops the association.
4. `WriteDeferredDirWipeScript()` writes `%TEMP%\scalpel_uninstall.bat` that sleeps ~2 s, `rmdir /s /q` **both** `%LOCALAPPDATA%\Programs\Scalpel` and `%LOCALAPPDATA%\Scalpel`, then deletes itself; launched hidden.
5. Farewell card auto-closes (~2 s); app exits; bat completes the directory wipe.

**Locks:** the running uninstaller process holds the install-dir EXE (itself) and may hold an open log handle in the data dir, so both directory removals are deferred to the post-exit bat; registry, shortcuts, and `%TEMP%` have no locks and are removed in-process.

## Install trust + downgrade behavior (unchanged)

The existing Authenticode trust gate (refuse to install an unsigned/wrong-publisher EXE) and the downgrade-confirmation guard are preserved in `Installer.Install`. The Add/Remove Programs `Publisher` value becomes "Liraz Amir".

## Error handling

- Every registry/file deletion is individually wrapped in `try/catch` that swallows and continues (matches the codebase's defensive convention) — a locked or already-absent item never aborts the wipe.
- Install failures show a branded error card (replacing the current `MessageBox`) and leave the app runnable in portable mode.

## Testing

- **Unit (xUnit, `Services/Installer.cs` linked into the test project, WPF-free):**
  - `OwnedRegistryKeys` contains `Software\Scalpel`, the `Uninstall\Scalpel` key, and `Software\Classes\Scalpel.pdf`.
  - `OwnedRegistryValues` contains the `.pdf\OpenWithProgids\Scalpel.pdf` and `RegisteredApplications\Scalpel` pairs.
  - `OwnedPaths` contains both `Programs\Scalpel` and the `Scalpel` data dir, and both `.lnk` paths.
  - Guard test documenting the inventory so a new persistence sink added without registering it for cleanup fails CI. (`InstallerUI.cs` and the real registry/FS deletions are not unit-tested — verified manually.)
- **Manual:** install (branded launcher, desktop toggle, relaunch); confirm `.pdf` association + ARP entry shows Publisher "Liraz Amir"; uninstall (confirm→progress→farewell); then verify via `reg query`/Explorer that **no** `Software\Scalpel`, ARP entry, ProgID, `%LOCALAPPDATA%\Scalpel`, `%LOCALAPPDATA%\Programs\Scalpel`, or shortcuts remain.

## Out of scope (YAGNI)

- Theme-aware installer dialogs (fixed brand look chosen).
- A "keep my signatures" option (full wipe chosen).
- MSI/WiX or any external installer.
- Changes to the MSIX/Store packaged path (the package still owns its own install/uninstall; all self-install code stays gated behind `IsPackaged()`).
- Roaming/HKLM cleanup (Scalpel only ever writes HKCU + LocalApplicationData).
```
