# Branded Install / Uninstall Experience + Complete Data Wipe — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give Scalpel a branded install + uninstall experience and a zero-leftover uninstall (registry + all data dirs), driven by a single testable cleanup inventory, with publisher "Liraz Amir".

**Architecture:** Extract a WPF-free `Services/Installer.cs` holding the canonical cleanup inventory (registry keys/values + filesystem paths) and the wipe logic; add a self-contained branded `Services/InstallerUI.cs` (fixed dark+amber palette, Geist/Tabler fonts, custom chrome) for the install confirm and the uninstall confirm→progress→farewell flow; rewire `App.xaml.cs`/`MainWindow` to use both and remove the dead `ShowLauncher`.

**Tech Stack:** C# / .NET Framework 4.8, WPF, `Microsoft.Win32.Registry`, xUnit.

**Spec:** `docs/superpowers/specs/2026-06-21-install-uninstall-experience-design.md`

## Global Constraints

- Target `net48`; build/test with the .NET 8 SDK. `dotnet` is not on PATH — use `~/.dotnet/dotnet.exe` (e.g. `~/.dotnet/dotnet.exe build`).
- `LangVersion=latest`, `Nullable` + `ImplicitUsings` enabled. Match existing style: collection expressions (`[]`), target-typed `new`, switch expressions.
- All persistence is **HKCU + `%LOCALAPPDATA%` only** — never HKLM or roaming. No UAC/admin.
- Every registry/file deletion is individually wrapped in `try { } catch { }` that swallows and continues (codebase convention) — a locked/absent item must never abort the wipe.
- All self-install behavior stays gated behind `App.IsPackaged()` — packaged (MSIX/Store) builds never self-install or self-uninstall. Do not change the packaged path.
- Brand palette (fixed, used only by `InstallerUI`): Canvas `#0A0B0E`, Panel `#14161A`, Accent `#F2A93B`, AccentHover `#F6C170`, TextPrimary `#E7E9EE`, TextDim `#7C818C`, Danger `#EF4444`. Fonts: Geist (`pack://application:,,,/Resources/Fonts/#Geist`) and Tabler (`pack://application:,,,/Resources/Fonts/#tabler-icons`) — the embedded `Resource` TTFs.
- Publisher string in the Add/Remove Programs entry must be exactly `Liraz Amir`.
- The running EXE cannot delete its own directory or an open-log data dir, so directory removal is deferred to a post-exit `.bat`; registry, shortcuts, and `%TEMP%` scratch are removed in-process.

---

### Task 1: `Services/Installer.cs` — cleanup inventory + wipe logic (TDD)

Create the WPF-free logic unit: canonical path constants, the three cleanup inventories, and the wipe methods. This is purely additive — nothing references it yet, so the build stays green and the inventory is unit-tested in isolation.

**Files:**
- Create: `Services/Installer.cs`
- Modify: `Scalpel.Tests/Scalpel.Tests.csproj` (link the new file)
- Test: `Scalpel.Tests/InstallerInventoryTests.cs`

**Interfaces:**
- Produces:
  - `Scalpel.Services.Installer.InstallDir` / `InstallExe` / `DataDir` / `StartMenuDir` / `StartMenuLnk` / `DesktopLnk` (`string`)
  - `Installer.OwnedRegistryKeys` → `IReadOnlyList<string>` (HKCU-relative subtrees)
  - `Installer.OwnedRegistryValues` → `IReadOnlyList<(string KeyPath, string ValueName)>`
  - `Installer.OwnedPaths` → `IReadOnlyList<string>` (absolute dirs + the two `.lnk` files)
  - `Installer.WipeAllData()` → `void` (in-process: registry keys + values, shortcuts, `%TEMP%\scalpel_*.pdf`, `SHChangeNotify`)
  - `Installer.WriteDeferredDirWipeScript()` → `string` (writes + returns the `.bat` path; does not launch it)

- [ ] **Step 1: Link the (not-yet-created) source file into the test project**

In `Scalpel.Tests/Scalpel.Tests.csproj`, add to the linked-`<Compile>` `<ItemGroup>` (after the `ThemeMigration.cs` line):

```xml
    <Compile Include="..\Services\Installer.cs" Link="Services\Installer.cs" />
```

- [ ] **Step 2: Write the failing inventory tests**

Create `Scalpel.Tests/InstallerInventoryTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class InstallerInventoryTests
    {
        private static string Local =>
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        [Fact]
        public void OwnedRegistryKeys_cover_all_owned_subtrees()
        {
            Assert.Contains(@"Software\Scalpel", Installer.OwnedRegistryKeys);
            Assert.Contains(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Scalpel",
                Installer.OwnedRegistryKeys);
            Assert.Contains(@"Software\Classes\Scalpel.pdf", Installer.OwnedRegistryKeys);
        }

        [Fact]
        public void OwnedRegistryValues_cover_shared_shell_keys()
        {
            Assert.Contains((@"Software\Classes\.pdf\OpenWithProgids", "Scalpel.pdf"),
                Installer.OwnedRegistryValues);
            Assert.Contains((@"Software\RegisteredApplications", "Scalpel"),
                Installer.OwnedRegistryValues);
        }

        [Fact]
        public void OwnedPaths_cover_install_dir_data_dir_and_shortcuts()
        {
            Assert.Contains(Path.Combine(Local, "Programs", "Scalpel"), Installer.OwnedPaths);
            Assert.Contains(Path.Combine(Local, "Scalpel"), Installer.OwnedPaths);
            Assert.Contains(Installer.StartMenuLnk, Installer.OwnedPaths);
            Assert.Contains(Installer.DesktopLnk, Installer.OwnedPaths);
        }

        [Fact]
        public void DataDir_is_the_parent_of_the_temp_and_logs_dirs()
        {
            // The data dir must be the whole %LOCALAPPDATA%\Scalpel tree, not just a subdir,
            // so signatures.json + logs + Temp are all removed.
            Assert.Equal(Path.Combine(Local, "Scalpel"), Installer.DataDir);
        }
    }
}
```

- [ ] **Step 3: Run tests to verify they fail to compile**

Run: `~/.dotnet/dotnet.exe test --filter "FullyQualifiedName~InstallerInventoryTests"`
Expected: FAIL — `Installer` does not exist.

- [ ] **Step 4: Write the minimal implementation**

Create `Services/Installer.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Scalpel.Services
{
    /// <summary>
    /// Per-user install/uninstall logic and the canonical cleanup inventory.
    /// WPF-free so it is unit-testable. All paths are HKCU + %LOCALAPPDATA% only.
    /// </summary>
    internal static class Installer
    {
        private const string AppName = "Scalpel";
        private const string ExeName = "Scalpel.exe";

        private static string Local =>
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // ── Canonical paths ───────────────────────────────────────────────
        public static string InstallDir   => Path.Combine(Local, "Programs", AppName);
        public static string InstallExe   => Path.Combine(InstallDir, ExeName);
        public static string DataDir      => Path.Combine(Local, AppName);   // signatures, logs, Temp, crash logs
        public static string StartMenuDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName);
        public static string StartMenuLnk => Path.Combine(StartMenuDir, $"{AppName}.lnk");
        public static string DesktopLnk   => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{AppName}.lnk");

        // ── Cleanup inventory (single source of truth) ─────────────────────
        // HKCU subtrees removed wholesale.
        public static IReadOnlyList<string> OwnedRegistryKeys { get; } =
        [
            @"Software\Scalpel",
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Scalpel",
            @"Software\Classes\Scalpel.pdf",
        ];

        // Stray values under shared shell keys we must NOT delete wholesale.
        public static IReadOnlyList<(string KeyPath, string ValueName)> OwnedRegistryValues { get; } =
        [
            (@"Software\Classes\.pdf\OpenWithProgids", "Scalpel.pdf"),
            (@"Software\RegisteredApplications", "Scalpel"),
        ];

        // Filesystem dirs + shortcut files removed on uninstall.
        public static IReadOnlyList<string> OwnedPaths { get; } =
        [
            Path.Combine(Local, "Programs", AppName),  // == InstallDir
            Path.Combine(Local, AppName),              // == DataDir
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName,
                $"{AppName}.lnk"),                     // == StartMenuLnk
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                $"{AppName}.lnk"),                     // == DesktopLnk
        ];

        // ── Shell notify ──────────────────────────────────────────────────
        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
        private const uint SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_IDLIST       = 0x0000;

        // ── Wipe ──────────────────────────────────────────────────────────

        /// <summary>
        /// Removes everything that can be removed while the process is still running:
        /// registry subtrees + stray values, shortcut files + the Start-Menu dir, and
        /// %TEMP%\scalpel_*.pdf scratch. The install dir and data dir are NOT removed here
        /// (they may be locked) — defer those to WriteDeferredDirWipeScript().
        /// </summary>
        public static void WipeAllData()
        {
            foreach (var key in OwnedRegistryKeys)
                try { Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false); } catch { }

            foreach (var (keyPath, valueName) in OwnedRegistryValues)
                try
                {
                    using var k = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
                    k?.DeleteValue(valueName, throwOnMissingValue: false);
                }
                catch { }

            try { File.Delete(StartMenuLnk); } catch { }
            try { Directory.Delete(StartMenuDir, recursive: false); } catch { }
            try { File.Delete(DesktopLnk); } catch { }

            try
            {
                var temp = Path.GetTempPath();
                foreach (var f in Directory.GetFiles(temp, "scalpel_*.pdf"))
                    try { File.Delete(f); } catch { }
            }
            catch { }

            try { SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero); } catch { }
        }

        /// <summary>
        /// Writes a hidden batch file that (after a short delay, so the EXE can exit)
        /// removes both the install dir and the data dir, then deletes itself. Returns
        /// the .bat path. The caller is responsible for launching it.
        /// </summary>
        public static string WriteDeferredDirWipeScript()
        {
            string bat = Path.Combine(Path.GetTempPath(), "scalpel_uninstall.bat");
            File.WriteAllText(bat,
                "@echo off\r\n" +
                "ping -n 3 127.0.0.1 >nul\r\n" +
                $"rmdir /s /q \"{InstallDir}\"\r\n" +
                $"rmdir /s /q \"{DataDir}\"\r\n" +
                "del \"%~f0\"\r\n");
            return bat;
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `~/.dotnet/dotnet.exe test --filter "FullyQualifiedName~InstallerInventoryTests"`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```bash
git add Services/Installer.cs Scalpel.Tests/InstallerInventoryTests.cs Scalpel.Tests/Scalpel.Tests.csproj
git commit -m "Add Installer cleanup inventory + wipe logic (tested)"
```

---

### Task 2: `Services/InstallerUI.cs` — branded install + uninstall dialogs

Add the self-contained branded dialogs. Purely additive (no caller yet), so the build stays green. Brand palette is hardcoded — no dependency on theme dictionaries (these run before the main window).

**Files:**
- Create: `Services/InstallerUI.cs`

**Interfaces:**
- Produces:
  - `InstallerUI.ShowInstallConfirm(bool alreadyInstalled)` → `(bool proceed, bool wantDesktop)`
  - `InstallerUI.RunUninstallFlow(Action inProcessWipe, Func<string> writeDeferredScript, Action launchScript)` → `bool` (false = user cancelled; performs the full confirm→progress→farewell)

- [ ] **Step 1: Create the file with the brand chrome + both dialogs**

Create `Services/InstallerUI.cs`:

```csharp
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Scalpel.Services
{
    /// <summary>
    /// Branded, self-contained install/uninstall dialogs. Fixed dark+amber palette
    /// (no theme-dictionary dependency — these run before the main window). Custom
    /// borderless chrome with a draggable title bar.
    /// </summary>
    internal static class InstallerUI
    {
        // ── Brand palette ──────────────────────────────────────────────────
        private static SolidColorBrush B(byte r, byte g, byte b) =>
            new(Color.FromRgb(r, g, b));
        private static readonly Brush Canvas      = B(0x0A, 0x0B, 0x0E);
        private static readonly Brush Panel       = B(0x14, 0x16, 0x1A);
        private static readonly Brush Accent      = B(0xF2, 0xA9, 0x3B);
        private static readonly Brush AccentHover = B(0xF6, 0xC1, 0x70);
        private static readonly Brush TextPrimary = B(0xE7, 0xE9, 0xEE);
        private static readonly Brush TextDim     = B(0x7C, 0x81, 0x8C);
        private static readonly Brush Danger      = B(0xEF, 0x44, 0x44);
        private static readonly Brush DangerHover = B(0xF8, 0x71, 0x71);
        private static readonly FontFamily Geist  =
            new(new Uri("pack://application:,,,/"), "./Resources/Fonts/#Geist");

        // ── Public dialogs ─────────────────────────────────────────────────

        public static (bool proceed, bool wantDesktop) ShowInstallConfirm(bool alreadyInstalled)
        {
            bool proceed = false;
            bool desktop = true;

            var (win, content) = MakeWindow();

            content.Children.Add(Heading());
            content.Children.Add(Sub($"Version {VersionString()}"));
            content.Children.Add(Body(alreadyInstalled
                ? "Update Scalpel on this computer. Your settings are kept."
                : "Install Scalpel on this computer. Adds a Start-Menu entry and a PDF file association — no admin needed."));

            var desktopChk = new CheckBox
            {
                IsChecked = true,
                Margin    = new Thickness(0, 6, 0, 24),
                Foreground = TextPrimary,
                Content   = new TextBlock { Text = "Create desktop shortcut", Foreground = TextPrimary, FontFamily = Geist },
            };
            content.Children.Add(desktopChk);

            var primary = PrimaryButton(alreadyInstalled ? "Update" : "Install");
            var ghost   = GhostButton("Not now");
            primary.Click += (_, _) => { proceed = true; desktop = desktopChk.IsChecked == true; win.Close(); };
            ghost.Click   += (_, _) => { proceed = false; win.Close(); };
            content.Children.Add(ButtonRow(ghost, primary));

            win.ShowDialog();
            return (proceed, desktop);
        }

        public static bool RunUninstallFlow(Action inProcessWipe, Func<string> writeDeferredScript, Action launchScript)
        {
            bool proceed = false;
            var (win, content) = MakeWindow();

            content.Children.Add(Heading("Uninstall Scalpel"));
            content.Children.Add(Body(
                "Remove Scalpel and ALL of its data from this PC — the app, your settings, " +
                "saved signatures, and logs. Nothing is left behind. This cannot be undone."));

            var remove = DangerButton("Remove");
            var cancel = GhostButton("Cancel");
            cancel.Click += (_, _) => { proceed = false; win.Close(); };
            remove.Click += (_, _) =>
            {
                proceed = true;
                // Swap to the progress state in-place.
                content.Children.Clear();
                content.Children.Add(Heading("Removing Scalpel…"));
                content.Children.Add(Body("Cleaning up files and registry entries."));
                win.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    try { inProcessWipe(); } catch { }
                    try { writeDeferredScript(); launchScript(); } catch { }
                    // Farewell, then auto-close.
                    content.Children.Clear();
                    content.Children.Add(Heading("Done"));
                    content.Children.Add(Body("Thanks for using Scalpel."));
                    var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    t.Tick += (_, _) => { t.Stop(); win.Close(); };
                    t.Start();
                }));
            };
            content.Children.Add(ButtonRow(cancel, remove));

            win.ShowDialog();
            return proceed;
        }

        // ── Chrome + element factories ─────────────────────────────────────

        private static (Window win, StackPanel content) MakeWindow()
        {
            var win = new Window
            {
                Title                 = "Scalpel",
                Width                 = 420,
                SizeToContent         = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode            = ResizeMode.NoResize,
                WindowStyle           = WindowStyle.None,
                Background            = Canvas,
                AllowsTransparency    = false,
            };

            var root = new DockPanel();

            var titleBar = new DockPanel { Background = Panel, Height = 36 };
            DockPanel.SetDock(titleBar, Dock.Top);
            titleBar.MouseLeftButtonDown += (_, e) =>
            { if (e.ButtonState == MouseButtonState.Pressed) win.DragMove(); };

            var close = new Button
            {
                Content = "", // Tabler "x"
                FontFamily = new FontFamily(new Uri("pack://application:,,,/"), "./Resources/Fonts/#tabler-icons"),
                FontSize = 14, Width = 46, Foreground = TextDim,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Cursor = Cursors.Arrow,
            };
            close.Click += (_, _) => win.Close();
            DockPanel.SetDock(close, Dock.Right);
            titleBar.Children.Add(close);
            titleBar.Children.Add(new TextBlock
            {
                Text = "Scalpel", Foreground = TextDim, FontFamily = Geist, FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0),
            });
            root.Children.Add(titleBar);

            var content = new StackPanel { Margin = new Thickness(36, 26, 36, 30) };
            root.Children.Add(content);

            win.Content = root;
            return (win, content);
        }

        private static TextBlock Heading(string text = "Scalpel") => new()
        {
            Text = text, FontFamily = Geist, FontSize = 26, FontWeight = FontWeights.Bold,
            Foreground = Accent, Margin = new Thickness(0, 0, 0, 4),
        };
        private static TextBlock Sub(string text) => new()
        {
            Text = text, FontFamily = Geist, FontSize = 12, Foreground = TextDim,
            Margin = new Thickness(0, 0, 0, 16),
        };
        private static TextBlock Body(string text) => new()
        {
            Text = text, FontFamily = Geist, FontSize = 13, Foreground = TextPrimary,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18),
        };

        private static StackPanel ButtonRow(params UIElement[] buttons)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            foreach (var b in buttons) row.Children.Add(b);
            return row;
        }

        private static Button PrimaryButton(string text) =>
            StyledButton(text, Accent, AccentHover, B(0x0A, 0x0A, 0x0A), width: 120, semibold: true);
        private static Button DangerButton(string text) =>
            StyledButton(text, Danger, DangerHover, Brushes.White, width: 120, semibold: true);
        private static Button GhostButton(string text) =>
            StyledButton(text, B(0x23, 0x27, 0x2F), B(0x2A, 0x2E, 0x36), TextPrimary, width: 96, semibold: false);

        private static Button StyledButton(string text, Brush normal, Brush hover, Brush fg, double width, bool semibold)
        {
            var template = new ControlTemplate(typeof(Button));
            var border   = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent),
            });
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetValue(ContentPresenter.MarginProperty, new Thickness(0, 8, 0, 8));
            border.AppendChild(cp);
            template.VisualTree = border;

            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Button.BackgroundProperty, normal));
            style.Setters.Add(new Setter(Button.ForegroundProperty, fg));
            style.Setters.Add(new Setter(Button.TemplateProperty, template));
            style.Setters.Add(new Setter(Button.CursorProperty, Cursors.Hand));
            var trig = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            trig.Setters.Add(new Setter(Button.BackgroundProperty, hover));
            style.Triggers.Add(trig);

            return new Button
            {
                Content = text, Width = width, Margin = new Thickness(8, 0, 0, 0),
                FontFamily = Geist, FontWeight = semibold ? FontWeights.SemiBold : FontWeights.Normal,
                Style = style,
            };
        }

        private static string VersionString() =>
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
    }
}
```

- [ ] **Step 2: Build**

Run: `~/.dotnet/dotnet.exe build`
Expected: BUILD SUCCEEDED, 0 errors (pre-existing Fody warning OK). New file compiles but is not yet referenced.

> If the Tabler glyph `` (x/close) is not in the embedded subset, the close button shows a box — cosmetic; verified visually in Task 4 and swapped for a present glyph if needed. The font is subset to ~39 glyphs (see `CLAUDE.md`).

- [ ] **Step 3: Commit**

```bash
git add Services/InstallerUI.cs
git commit -m "Add branded install/uninstall dialogs (InstallerUI)"
```

---

### Task 3: Rewire `App.xaml.cs` + `MainWindow` to the new units; remove dead code

Wire the branded dialogs and the inventory-driven wipe into the real flows, point install-side constants at `Installer`, set the publisher, and delete the dead `ShowLauncher`. This is the integration task — the build goes green only when it's complete.

**Files:**
- Modify: `App.xaml.cs` (uninstall path ~103-109 + `Uninstall()` ~1393-1446; `InstallAndRelaunch` ~558-583; `DoInstall` publisher ~1318; constants ~30-39; `IsPortable` ~543-551; `TempDir` ~597-599; delete dead `ShowLauncher` ~732-908 and its only-by-ShowLauncher helpers if unused elsewhere)
- Modify: `MainWindow.xaml.cs` (`Install_Click` ~587-598)

**Interfaces:**
- Consumes: `Installer.*` (Task 1), `InstallerUI.ShowInstallConfirm` / `InstallerUI.RunUninstallFlow` (Task 2).

- [ ] **Step 1: Point the uninstall arg handler at the branded flow**

In `App.xaml.cs` `OnStartup`, replace the `Uninstall(); Shutdown(); return;` body (the call inside the `/uninstall` `if`, ~106-108) with:

```csharp
                InstallerUI.RunUninstallFlow(
                    Scalpel.Services.Installer.WipeAllData,
                    Scalpel.Services.Installer.WriteDeferredDirWipeScript,
                    () =>
                    {
                        var bat = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "scalpel_uninstall.bat");
                        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"")
                        {
                            WindowStyle = ProcessWindowStyle.Hidden, UseShellExecute = true,
                        });
                    });
                Shutdown();
                return;
```

> `WriteDeferredDirWipeScript` already returns the path, but the launch closure re-derives the well-known path to keep `RunUninstallFlow`'s `launchScript` parameter parameterless. Both compute `%TEMP%\scalpel_uninstall.bat` — identical.

- [ ] **Step 2: Delete the old `Uninstall()` method**

Remove the entire `private static void Uninstall()` method from `App.xaml.cs` (the `// Uninstall` section, ~1389-1446). Its registry/shortcut/self-delete logic now lives in `Installer.WipeAllData` + `Installer.WriteDeferredDirWipeScript`.

- [ ] **Step 3: Branded install confirm in `MainWindow.Install_Click`**

Replace the body of `Install_Click` (`MainWindow.xaml.cs:587-598`) with:

```csharp
        private void Install_Click(object sender, RoutedEventArgs e)
        {
            var (proceed, wantDesktop) = Scalpel.Services.InstallerUI.ShowInstallConfirm(alreadyInstalled: false);
            if (!proceed) return;

            // Hide the badge immediately so it doesn't flash if relaunch is slow
            _portableBadge.Visibility = Visibility.Collapsed;

            App.InstallAndRelaunch(_currentFile, wantDesktop);
        }
```

> The branded dialog now owns the confirm + the desktop-shortcut choice, so `wantDesktop` is real instead of the previous hardcoded `true`. The old `ScalpelDialog.Show(... Str_Dlg_InstallMsg ...)` call is removed; those locale strings can remain unused.

- [ ] **Step 4: Set publisher to "Liraz Amir"**

In `App.xaml.cs` `DoInstall`, change the ARP publisher value (currently `key.SetValue("Publisher", "Your Name");`, ~1318) to:

```csharp
                    key.SetValue("Publisher",            "Liraz Amir");
```

- [ ] **Step 5: Point install-side constants at `Installer` (DRY) and remove the duplicates**

In `App.xaml.cs`, the install/portable code uses `InstallDir`, `InstallExe`, `StartMenuDir`, `StartMenuLnk`, `DesktopLnk`. Delete App's own definitions of these (~30-39) and replace each in-file reference with the `Installer` equivalent (`Installer.InstallExe`, etc.). Specifically:
- `IsPortable` (~550): `Installer.InstallExe`.
- `DoInstall` / `CreateShortcut` references to `InstallDir`/`InstallExe`/`StartMenuDir`/`StartMenuLnk`/`DesktopLnk` → `Installer.*`.
- `TempDir` (~597) becomes `Path.Combine(Installer.DataDir, "Temp")` (same path, now derived from the single source).

Keep `AppName`/`ExeName` in `App` if still referenced elsewhere; otherwise leave them.

> Do this as a find/replace of the bare identifiers to `Installer.<name>` within `App.xaml.cs`, then delete the now-unused `private static readonly string` constant declarations. The compiler will flag any missed reference.

- [ ] **Step 6: Delete the dead `ShowLauncher`**

`ShowLauncher` (`App.xaml.cs` ~728-908) has no callers (verified: only its definition exists in the repo). Delete the method. If `MakeLauncherButtonStyle` is still referenced by the crash dialog / `ScalpelDialog` (it is — two other call sites), KEEP it; only delete `ShowLauncher` itself.

- [ ] **Step 7: Build the whole solution**

Run: `~/.dotnet/dotnet.exe build`
Expected: BUILD SUCCEEDED, 0 errors. Resolve any leftover reference to a deleted constant by pointing it at `Installer.*`.

- [ ] **Step 8: Grep for stragglers**

Run: `grep -rn "Your Name\|private static void Uninstall\|ShowLauncher" App.xaml.cs MainWindow.xaml.cs`
Expected: no matches.

- [ ] **Step 9: Commit**

```bash
git add App.xaml.cs MainWindow.xaml.cs
git commit -m "Wire branded install/uninstall + zero-leftover wipe; publisher=Liraz Amir; drop dead ShowLauncher"
```

---

### Task 4: Full build, test, manual verification, docs

**Files:**
- Modify: `docs/OVERVIEW.md` and/or `CLAUDE.md` install/uninstall description; `App.xaml.cs` is no longer the sole owner of install logic.

- [ ] **Step 1: Build + full test suite**

Run: `~/.dotnet/dotnet.exe build` then `~/.dotnet/dotnet.exe test`
Expected: BUILD SUCCEEDED; all tests pass, including `InstallerInventoryTests` (4) and the existing 46.

- [ ] **Step 2: Manual install verification**

Build a signed/dev EXE, run it portable, click the **Install** badge:
- Branded confirm card appears (dark + amber, Geist), desktop-shortcut toggle works.
- After install, app relaunches from `%LOCALAPPDATA%\Programs\Scalpel`.
- `reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Scalpel" /v Publisher` → `Liraz Amir`.
- `.pdf` "Open with" lists Scalpel.

- [ ] **Step 3: Manual uninstall verification (the zero-leftover gate)**

Trigger uninstall (Add/Remove Programs, or run `"%LOCALAPPDATA%\Programs\Scalpel\Scalpel.exe" /uninstall`):
- Confirm → "Removing Scalpel…" → "Done. Thanks for using Scalpel." auto-closes.
- After ~3 s, verify NOTHING remains:

```powershell
reg query "HKCU\Software\Scalpel"                                                   # -> ERROR (absent)
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Scalpel"        # -> ERROR
reg query "HKCU\Software\Classes\Scalpel.pdf"                                        # -> ERROR
reg query "HKCU\Software\RegisteredApplications" /v Scalpel                          # -> ERROR (value gone)
Test-Path "$env:LOCALAPPDATA\Scalpel"                                               # -> False
Test-Path "$env:LOCALAPPDATA\Programs\Scalpel"                                      # -> False
Test-Path "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Scalpel"             # -> False
```

All must be absent. If any remains, add it to the `Installer` inventory and re-run.

- [ ] **Step 4: Update docs**

In `docs/OVERVIEW.md` (and the `App.xaml.cs` installer note in `CLAUDE.md` if present), note that install/uninstall logic now lives in `Services/Installer.cs` (cleanup inventory + wipe) and `Services/InstallerUI.cs` (branded dialogs), uninstall is zero-leftover (removes `%LOCALAPPDATA%\Scalpel` data dir too), and publisher is "Liraz Amir".

- [ ] **Step 5: Commit**

```bash
git add docs/ CLAUDE.md
git commit -m "Docs: install/uninstall moved to Installer/InstallerUI; zero-leftover uninstall"
```

---

## Self-Review

**Spec coverage:**
- Branded install dialog → Task 2 (`ShowInstallConfirm`) + Task 3 Step 3. ✓
- Branded uninstall confirm→progress→farewell → Task 2 (`RunUninstallFlow`) + Task 3 Step 1. ✓
- Fixed brand palette, no theme dependency, Geist/Tabler → Task 2. ✓
- Zero-leftover wipe incl. `%LOCALAPPDATA%\Scalpel` data dir → Task 1 (`WipeAllData` + `WriteDeferredDirWipeScript` rmdir both dirs) + Task 4 Step 3 gate. ✓
- Canonical inventory single source of truth + guard test → Task 1. ✓
- Publisher "Liraz Amir" → Task 3 Step 4. ✓
- Extract out of App.xaml.cs monolith → Tasks 1–3. ✓
- Packaged-mode gate preserved → unchanged `if (!IsPackaged() …)` in OnStartup (Task 3 edits only the body inside it). ✓
- Trust gate + downgrade guard preserved → `DoInstall` retained (only publisher line changes). ✓

**Placeholder scan:** No TBD/TODO; all code blocks complete; exact registry paths, palette hexes, and verification commands provided.

**Type consistency:** `Installer.WipeAllData()`/`WriteDeferredDirWipeScript()` (Task 1) consumed by `OnStartup` and by `RunUninstallFlow` callbacks (Task 3). `InstallerUI.ShowInstallConfirm(bool) -> (bool, bool)` consumed by `Install_Click` (Task 3 Step 3). `InstallerUI.RunUninstallFlow(Action, Func<string>, Action)` consumed in Task 3 Step 1 with matching argument types. Inventory property names (`OwnedRegistryKeys`/`OwnedRegistryValues`/`OwnedPaths`) consistent between Task 1 implementation and tests.

**Note on TDD scope:** Only `Installer`'s inventory is unit-tested (the wipe's real registry/FS deletions and all WPF dialogs are verified by the Task 4 manual gate), consistent with the repo's test boundaries and the spec.
```
