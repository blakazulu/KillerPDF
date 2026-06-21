# "Edit with Scalpel PDF" Context-Menu Entry — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a per-user "Edit with Scalpel PDF" right-click context-menu entry for PDF files that opens the file in Scalpel and jumps straight to Edit mode, registered on install and fully removed on uninstall.

**Architecture:** A classic per-user (`HKCU`) registry verb under `SystemFileAssociations\.pdf\shell\Scalpel.edit`, written by `App.RegisterFileHandler()` (already packaged-gated, since it only runs via `DoInstall` after `InstallAndRelaunch`'s `if (IsPackaged()) return;`). The verb command passes a new `/edit` flag; `MainWindow`'s `Loaded` handler detects it and calls `SetMode(AppMode.Edit)` after a successful open. The verb key is added to `Installer.OwnedRegistryKeys` so the existing `WipeAllData()` removes it on uninstall.

**Tech Stack:** .NET Framework 4.8 (net48, x64), WPF, `Microsoft.Win32.Registry`, xUnit. Build with the .NET 8+ SDK.

## Global Constraints

- Build requires the .NET 8 SDK even though the target is `net48`. `dotnet` is NOT on PATH — use `C:\Users\Liraz\.dotnet\dotnet.exe` (`~/.dotnet/dotnet.exe`).
- All registration is **per-user `HKCU`, no admin**. Do not write to `HKLM`.
- The canonical verb registry key, verbatim (used identically in Tasks 1, 2):
  `Software\Classes\SystemFileAssociations\.pdf\shell\Scalpel.edit`
- Menu display text, verbatim: `Edit with Scalpel PDF`
- Verb `command` value, verbatim form: `"<InstallExe>" /edit "%1"` (built with `Installer.InstallExe`).
- Icon value, verbatim form: `<InstallExe>,0` (built with `Installer.InstallExe`).
- The launch flag is exactly `/edit`, matched case-insensitively (`StringComparison.OrdinalIgnoreCase`).
- Normal double-click / "Open" (no `/edit` flag) MUST remain unchanged — it still lands in the default View mode.
- C# style: `Nullable` + `ImplicitUsings` enabled, `LangVersion=latest`; match existing `using (var k = Registry.CurrentUser.CreateSubKey(...))` and expression idioms.

---

### Task 1: Add the verb key to the cleanup inventory (TDD)

Add the context-menu verb key to `Installer.OwnedRegistryKeys` so uninstall removes it, guarded by an assertion in the existing inventory test.

**Files:**
- Modify: `Scalpel.Tests/InstallerInventoryTests.cs` (the `OwnedRegistryKeys_cover_all_owned_subtrees` test, after line 21)
- Modify: `Services/Installer.cs` (`OwnedRegistryKeys` initializer, after the `Applications\Scalpel.exe` entry at line 44)

**Interfaces:**
- Consumes: nothing.
- Produces: the string `@"Software\Classes\SystemFileAssociations\.pdf\shell\Scalpel.edit"` is present in `Installer.OwnedRegistryKeys`. Task 2 writes this exact key path; both must match verbatim.

- [ ] **Step 1: Add the failing assertion**

In `Scalpel.Tests/InstallerInventoryTests.cs`, inside `OwnedRegistryKeys_cover_all_owned_subtrees`, immediately after the existing `Applications\Scalpel.exe` assertion (line 21), add:

```csharp
            Assert.Contains(@"Software\Classes\SystemFileAssociations\.pdf\shell\Scalpel.edit",
                Installer.OwnedRegistryKeys);
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `~/.dotnet/dotnet.exe test Scalpel.Tests/Scalpel.Tests.csproj --filter "FullyQualifiedName~OwnedRegistryKeys_cover_all_owned_subtrees"`
Expected: FAIL — `Assert.Contains() Failure` (the key is not yet in the inventory).

- [ ] **Step 3: Add the key to the inventory**

In `Services/Installer.cs`, in the `OwnedRegistryKeys` collection initializer, immediately after the line `@"Software\Classes\Applications\Scalpel.exe",` (line 44), add:

```csharp
            @"Software\Classes\SystemFileAssociations\.pdf\shell\Scalpel.edit",
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `~/.dotnet/dotnet.exe test Scalpel.Tests/Scalpel.Tests.csproj --filter "FullyQualifiedName~OwnedRegistryKeys_cover_all_owned_subtrees"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Scalpel.Tests/InstallerInventoryTests.cs Services/Installer.cs
git commit -m "feat: add Edit-with-Scalpel context-menu verb to uninstall inventory"
```

---

### Task 2: Register the context-menu verb on install

Write the verb's registry keys in `App.RegisterFileHandler()` so they are created during a per-user install (the method only runs in the non-packaged install path).

**Files:**
- Modify: `App.xaml.cs` (`RegisterFileHandler()`, immediately before the final `SHChangeNotify(...)` call at line 1084)

**Interfaces:**
- Consumes: `Installer.InstallExe` (the installed exe path, `string`); the verb key path from Task 1.
- Produces: the registry verb whose command launches `"<InstallExe>" /edit "%1"`. Task 3 consumes the `/edit` flag contract.

- [ ] **Step 1: Add the verb registration**

In `App.xaml.cs`, inside `RegisterFileHandler()`, immediately BEFORE the final line `SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);` (line 1084), insert:

```csharp
            // "Edit with Scalpel PDF" context-menu verb for ALL .pdf files, independent of the
            // default handler. Per-user (HKCU), no admin. On Windows 11 it appears under
            // "Show more options"; on Windows 10 on the main context menu. The verb subkey is
            // namespaced "Scalpel.edit" (not the bare "edit") to avoid colliding with a built-in
            // edit verb. Removed on uninstall via Installer.OwnedRegistryKeys.
            using (var k = Registry.CurrentUser.CreateSubKey(
                @"Software\Classes\SystemFileAssociations\.pdf\shell\Scalpel.edit"))
            {
                k.SetValue("", "Edit with Scalpel PDF");
                k.SetValue("Icon", $"{Installer.InstallExe},0");
            }
            using (var k = Registry.CurrentUser.CreateSubKey(
                @"Software\Classes\SystemFileAssociations\.pdf\shell\Scalpel.edit\command"))
                k.SetValue("", $"\"{Installer.InstallExe}\" /edit \"%1\"");

```

(The blank line keeps one line of separation before the existing `SHChangeNotify` call, which already notifies the shell of the change.)

- [ ] **Step 2: Build to verify it compiles**

Run: `~/.dotnet/dotnet.exe build`
Expected: `Build succeeded`, 0 Errors. (If `NETSDK1047` appears after a prior publish, re-run build WITH restore — drop any `--no-restore`.)

- [ ] **Step 3: Manual verification (note in report; agent cannot run a real install)**

Confirm by code reading that: the two keys are created with `CreateSubKey` under `HKCU`; the verb `(default)` is `Edit with Scalpel PDF`; the `Icon` value is `<InstallExe>,0`; the `command` `(default)` is `"<InstallExe>" /edit "%1"`; and the block sits inside `RegisterFileHandler()` before `SHChangeNotify`. Note in the report that live install + right-click verification is deferred to the human. (Human check later: install → right-click a `.pdf` → "Edit with Scalpel PDF" appears with the Scalpel icon. Win11: under "Show more options".)

- [ ] **Step 4: Commit**

```bash
git add App.xaml.cs
git commit -m "feat: register 'Edit with Scalpel PDF' context-menu verb on install"
```

---

### Task 3: Handle the `/edit` flag — open and jump to Edit mode

Make `MainWindow`'s startup file-argument handling robust to a leading `/edit` flag, and switch to Edit mode after a successful open when the flag is present.

**Files:**
- Modify: `MainWindow.xaml.cs` (the `Loaded += (_, _) => { ... }` handler, lines 240–257)

**Interfaces:**
- Consumes: the `/edit` flag contract from Task 2; existing `OpenFile(string)`, the `_doc` field (non-null after a successful open), `SetMode(AppMode.Edit)`, and the `AppMode` enum — all members of `MainWindow`.
- Produces: nothing new.

- [ ] **Step 1: Replace the startup file-arg block**

In `MainWindow.xaml.cs`, inside the `Loaded` handler, replace this exact existing block (lines 240–257):

```csharp
                var args = Environment.GetCommandLineArgs();
                if (args.Length > 1 && System.IO.File.Exists(args[1]))
                {
                    OpenFile(args[1]);
                }
                else
                {
                    // Reopen the last file if no CLI arg was provided
                    var lastFile = App.GetSetting("LastFile");
                    if (!string.IsNullOrEmpty(lastFile) && System.IO.File.Exists(lastFile))
                    {
                        OpenFile(lastFile!);
                        // If the reopen didn't actually load a document (open failed, or the
                        // user declined the repair prompt), forget it — otherwise the same
                        // damaged file would re-prompt on every subsequent launch.
                        if (_doc is null)
                            App.SetSetting("LastFile", "");
                    }
                }
```

with:

```csharp
                var args = Environment.GetCommandLineArgs();
                // Find the first argument that is an existing file (skipping arg[0] = exe path
                // and flags like /edit), so flag-vs-path order doesn't matter. The "Edit with
                // Scalpel PDF" context-menu verb launches us as: <exe> /edit "<file>".
                string? fileArg = null;
                bool editMode = false;
                for (int i = 1; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "/edit", StringComparison.OrdinalIgnoreCase))
                        editMode = true;
                    else if (fileArg is null && System.IO.File.Exists(args[i]))
                        fileArg = args[i];
                }
                if (fileArg is not null)
                {
                    OpenFile(fileArg);
                    // Jump straight to Edit mode for the "Edit with Scalpel PDF" verb — but only
                    // once a document actually loaded (OpenFile runs synchronously; _doc is null
                    // on failure or a declined repair prompt).
                    if (editMode && _doc is not null)
                        SetMode(AppMode.Edit);
                }
                else
                {
                    // Reopen the last file if no file argument was provided
                    var lastFile = App.GetSetting("LastFile");
                    if (!string.IsNullOrEmpty(lastFile) && System.IO.File.Exists(lastFile))
                    {
                        OpenFile(lastFile!);
                        // If the reopen didn't actually load a document (open failed, or the
                        // user declined the repair prompt), forget it — otherwise the same
                        // damaged file would re-prompt on every subsequent launch.
                        if (_doc is null)
                            App.SetSetting("LastFile", "");
                    }
                }
```

- [ ] **Step 2: Build to verify it compiles**

Run: `~/.dotnet/dotnet.exe build`
Expected: `Build succeeded`, 0 Errors.

- [ ] **Step 3: Manual verification (note in report; agent cannot run the GUI)**

Confirm by code reading that: a plain file path (no flag) still opens normally and does NOT switch mode (`editMode` stays false); `<exe> /edit "<file>"` sets `editMode = true`, opens the file, and calls `SetMode(AppMode.Edit)` only when `_doc is not null`; and the no-file branch still reopens `LastFile` exactly as before. Note that live GUI verification (right-click → Edit mode) is deferred to the human.

- [ ] **Step 4: Commit**

```bash
git add MainWindow.xaml.cs
git commit -m "feat: open in Edit mode when launched with /edit (context-menu verb)"
```

---

## Self-Review

**Spec coverage:**
- Spec "Registry layout" (verb + command + icon under `SystemFileAssociations\.pdf\shell\Scalpel.edit`) → Task 2. ✓
- Spec "Click behavior — the `/edit` flag" (robust file-arg scan, `/edit` detect, `SetMode(AppMode.Edit)` only when `_doc` non-null, unchanged LastFile fallback) → Task 3. ✓
- Spec "Uninstall cleanup" (add key to `OwnedRegistryKeys`; `WipeAllData` unchanged) → Task 1. ✓
- Spec "Testing — Unit (TDD)" (assert verb key in `OwnedRegistryKeys`) → Task 1. ✓
- Spec "packaged gate" → satisfied implicitly: `RegisterFileHandler` runs only via `DoInstall`, reached only after `InstallAndRelaunch`'s `if (IsPackaged()) return;` (documented in the plan's Architecture). ✓

**Placeholder scan:** No TBD/TODO/"handle edge cases"; every code step shows exact content. ✓

**Type/name consistency:** The verb key string `Software\Classes\SystemFileAssociations\.pdf\shell\Scalpel.edit` is identical in Task 1 (inventory + test) and Task 2 (registration). The `/edit` flag and `Installer.InstallExe` are consistent across Tasks 2–3. `OpenFile`, `_doc`, `SetMode`, `AppMode.Edit` are all existing `MainWindow` members. ✓

**Note on TDD:** Only Task 1 (the WPF-free `Installer` inventory) is unit-testable and carries a real red→green test. Tasks 2–3 are registry/WPF side-effects with no unit harness in this repo (App.xaml.cs / MainWindow.xaml.cs are not compiled into the test project), so each uses a build gate plus code-reading verification, with live install/GUI checks deferred to the human — consistent with the repo's existing convention.
