# "Edit with Scalpel PDF" context-menu entry — design

**Date:** 2026-06-21
**Status:** Approved (pending spec review)

## Goal

When Scalpel is installed (per-user portable install), add a Windows Explorer
right-click context-menu entry **"Edit with Scalpel PDF"** for PDF files.
Clicking it launches Scalpel with the file and jumps straight into Edit mode.

## Background — how install/registration works today

- `App.RegisterFileHandler()` (`App.xaml.cs`, called from `InstallAndRelaunch`)
  writes all per-user (`HKCU`) registration: the `Scalpel.pdf` ProgID
  (`shell\open\command`), `Applications\Scalpel.exe` (+ `SupportedTypes`),
  `.pdf\OpenWithProgids`, and `RegisteredApplications` capabilities. It ends by
  firing `SHChangeNotify(SHCNE_ASSOCCHANGED, …)`.
- `Services/Installer.cs` owns the cleanup inventory. `OwnedRegistryKeys` lists
  the `HKCU` subtrees removed wholesale on uninstall; `WipeAllData()` iterates
  it calling `Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey:
  false)`.
- Install/registration is **per-user, no admin**, all under `HKCU`.
- **Packaged (MSIX) mode:** `App.IsPackaged()` gates the self-installer off; in
  packaged mode the manifest owns file associations and `InstallAndRelaunch`
  no-ops. New install-side registration must stay behind this gate.
- The app already accepts a file path on the command line: `MainWindow`'s
  `Loaded` handler does
  `var args = Environment.GetCommandLineArgs(); if (args.Length > 1 &&
  File.Exists(args[1])) OpenFile(args[1]);` and otherwise reopens `LastFile`.
- `MainWindow` has `private enum AppMode { View, Edit, Pages, Sign }` and
  `private void SetMode(AppMode mode)` which switches the active toolbar mode.

## Decisions (from brainstorming)

1. **Mechanism:** classic per-user registry verb (no admin, fits the
   single-portable-EXE design). **Not** an `IExplorerCommand` COM handler.
2. **Scope:** PDF files only, shown regardless of the default PDF handler.
3. **Icon:** show the Scalpel icon next to the entry.
4. **Click behavior:** open the PDF **and** switch to Edit mode.
5. **Display text (verbatim):** `Edit with Scalpel PDF`.

## Known limitation (accepted)

On **Windows 11** a registry verb appears under **"Show more options"** (the
legacy menu / Shift+F10), not the top-level compact menu — reaching the Win11
top level would require a packaged `IExplorerCommand` COM handler, which is out
of scope. On **Windows 10** the entry appears on the main context menu directly.

## Registry layout

Written in `RegisterFileHandler()` (so it is created on install and only when
`!IsPackaged()`), placed under `SystemFileAssociations\.pdf` so it applies to
all `.pdf` files independent of the active ProgID / default app:

```
HKCU\Software\Classes\SystemFileAssociations\.pdf\shell\Scalpel.edit
    (default) = "Edit with Scalpel PDF"
    "Icon"    = "<Installer.InstallExe>,0"
HKCU\Software\Classes\SystemFileAssociations\.pdf\shell\Scalpel.edit\command
    (default) = "\"<Installer.InstallExe>\" /edit \"%1\""
```

- The verb subkey is namespaced `Scalpel.edit` (not the bare `edit`) to avoid
  colliding with any built-in `edit` verb. The menu label is the subkey's
  `(default)` value.
- The `command` value runs the installed exe with a new `/edit` flag before the
  `"%1"` file argument.
- `RegisterFileHandler()` already calls `SHChangeNotify` at its end, so the
  shell picks up the new verb without an extra call.

## Click behavior — the `/edit` flag

The verb invokes `"<exe>" /edit "<file>"`, so the process command line is e.g.
`["Scalpel.exe", "/edit", "C:\doc.pdf"]`. The current `Loaded` handler assumes
`args[1]` is the file and would mis-handle `/edit`. Change the handler to:

1. Scan `Environment.GetCommandLineArgs()` (skipping `args[0]`, the exe path)
   for the first argument that is an existing file (`File.Exists`) — that is the
   file to open. (Robust regardless of flag order.)
2. Detect the `/edit` flag: any arg equal to `/edit`
   (`StringComparison.OrdinalIgnoreCase`).
3. If a file was found, `OpenFile(file)`.
4. After opening, `if (editMode && _doc is not null) SetMode(AppMode.Edit);`
   — `OpenFile`/`FinishOpenFile` run synchronously, so `_doc` is set on success
   and null on failure; Edit mode is entered only when a document actually
   loaded.
5. If no file argument is present, fall through to the existing
   "reopen LastFile" branch unchanged.

Normal double-click / "Open" (no `/edit` flag) is unaffected and still lands in
the default View mode.

The `/edit` flag is purely a file-open concern handled in `MainWindow`; it does
not interact with the `/uninstall` flag handled earlier in `App.OnStartup`.

## Uninstall cleanup (zero-leftover)

Add the verb subtree to `Installer.OwnedRegistryKeys`:

```
@"Software\Classes\SystemFileAssociations\.pdf\shell\Scalpel.edit"
```

`WipeAllData()` already `DeleteSubKeyTree`s each owned key; deleting this path
removes only our `Scalpel.edit` verb (and its `command` subkey), leaving the
shared `SystemFileAssociations\.pdf\shell` parent intact. No change to
`WipeAllData()` logic is needed — only the inventory entry.

## Components touched

- `App.xaml.cs` — `RegisterFileHandler()`: add the verb + command + icon keys.
- `MainWindow.xaml.cs` — `Loaded` handler: robust file-arg detection + `/edit`
  flag → `SetMode(AppMode.Edit)` after a successful open.
- `Services/Installer.cs` — add the verb key to `OwnedRegistryKeys`.
- `Scalpel.Tests/InstallerInventoryTests.cs` — assert the verb key is in the
  inventory.

## Out of scope

- Win11 top-level (compact menu) entry / `IExplorerCommand` COM handler.
- Context-menu entries for non-PDF files, folders, or the desktop background.
- MSIX manifest changes (packaged mode already gates this off; the Store
  package can declare its own verbs in a separate effort if desired).

## Testing / verification

- **Unit (TDD):** extend `InstallerInventoryTests` to assert
  `OwnedRegistryKeys` contains
  `Software\Classes\SystemFileAssociations\.pdf\shell\Scalpel.edit`. Run the
  suite (`dotnet test`) red → green.
- **Build:** `dotnet build` succeeds (0 errors).
- **Manual (human, deferred — agent can't run the GUI or a real install):**
  install → right-click a `.pdf` (Win10: main menu; Win11: "Show more options")
  → "Edit with Scalpel PDF" appears with the Scalpel icon → clicking opens the
  file in Edit mode → uninstall → the entry is gone and the
  `SystemFileAssociations\.pdf\shell\Scalpel.edit` key is removed.
