# Recent Files (MRU) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let users reopen recently-opened PDFs from the empty-state DropZone and the Open-button right-click menu.

**Architecture:** A pure, unit-tested MRU service (`Services/RecentFiles.cs`) holds all list logic; thin `App` registry wrappers persist it under `HKCU\Software\Scalpel\Settings\RecentFiles`; capture happens in `FinishOpenFile`; two view surfaces (DropZone list + Open ContextMenu) render the filtered list on demand from new partial `MainWindow.Recent.cs`.

**Tech Stack:** C# / .NET Framework 4.8, WPF, xUnit. Build/test with `~/.dotnet/dotnet.exe` (not on PATH).

## Global Constraints

- New `Str_*` localization keys MUST be added to ALL 9 `Strings/*.xaml` files (en-US, es, zh-TW, zh-CN, bn, tr-TR, he, ar, ru) or a `DynamicResource` blanks out in that locale.
- All `.cs`/`.xaml` files are UTF-8-BOM + CRLF.
- I/O and registry access wrapped in defensive `try { } catch { }` that swallow and no-op (codebase convention).
- MRU cap = 10. Separator = `'|'` (illegal in Windows paths). Dedupe is case-insensitive.
- Build: `~/.dotnet/dotnet.exe build` (drop `--no-restore`). Test: `~/.dotnet/dotnet.exe test Scalpel.Tests/Scalpel.Tests.csproj`. A `pdfium.dll` MSB3027 copy error just means a running Scalpel.exe is locking it — close it and rebuild.
- User-facing change → add a Changelog bullet (Task 6).

---

### Task 1: RecentFiles pure service + unit tests

**Files:**
- Create: `Services/RecentFiles.cs`
- Create: `Scalpel.Tests/RecentFilesTests.cs`
- Modify: `Scalpel.Tests/Scalpel.Tests.csproj` (link the new source)

**Interfaces:**
- Produces: `Scalpel.Services.RecentFiles.Parse(string?) → List<string>`, `Serialize(IEnumerable<string>) → string`, `Add(IReadOnlyList<string> current, string path, int max = 10) → List<string>`, `Remove(IReadOnlyList<string> current, string path) → List<string>`.

- [ ] **Step 1: Write the failing tests**

Create `Scalpel.Tests/RecentFilesTests.cs` (UTF-8-BOM, CRLF):

```csharp
using System.Collections.Generic;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class RecentFilesTests
    {
        [Fact]
        public void Add_PrependsNewPath()
        {
            var result = RecentFiles.Add(new List<string> { @"C:\a.pdf" }, @"C:\b.pdf");
            Assert.Equal(new[] { @"C:\b.pdf", @"C:\a.pdf" }, result);
        }

        [Fact]
        public void Add_DedupesCaseInsensitive_AndMovesToFront()
        {
            var result = RecentFiles.Add(new List<string> { @"C:\a.pdf", @"C:\b.pdf" }, @"C:\A.PDF");
            Assert.Equal(new[] { @"C:\A.PDF", @"C:\b.pdf" }, result);
        }

        [Fact]
        public void Add_CapsAtMax_DroppingOldest()
        {
            var current = new List<string>();
            for (int i = 0; i < 10; i++) current.Add($@"C:\f{i}.pdf");
            var result = RecentFiles.Add(current, @"C:\new.pdf", max: 10);
            Assert.Equal(10, result.Count);
            Assert.Equal(@"C:\new.pdf", result[0]);
            Assert.DoesNotContain(@"C:\f9.pdf", result); // oldest dropped
        }

        [Fact]
        public void Remove_IsCaseInsensitive()
        {
            var result = RecentFiles.Remove(new List<string> { @"C:\a.pdf", @"C:\b.pdf" }, @"C:\A.PDF");
            Assert.Equal(new[] { @"C:\b.pdf" }, result);
        }

        [Fact]
        public void Parse_DropsEmptyAndWhitespaceEntries()
        {
            var result = RecentFiles.Parse(@"C:\a.pdf||  |C:\b.pdf");
            Assert.Equal(new[] { @"C:\a.pdf", @"C:\b.pdf" }, result);
        }

        [Fact]
        public void Parse_NullOrEmpty_ReturnsEmpty()
        {
            Assert.Empty(RecentFiles.Parse(null));
            Assert.Empty(RecentFiles.Parse(""));
        }

        [Fact]
        public void Serialize_RoundTrips_WithSpacesAndUnicode()
        {
            var list = new List<string> { @"C:\My Docs\contrato señor.pdf", @"C:\文件.pdf" };
            var round = RecentFiles.Parse(RecentFiles.Serialize(list));
            Assert.Equal(list, round);
        }
    }
}
```

- [ ] **Step 2: Link the source into the test project**

In `Scalpel.Tests/Scalpel.Tests.csproj`, add after line 35 (`SignatureStore.cs` link):

```xml
    <Compile Include="..\Services\RecentFiles.cs" Link="Services\RecentFiles.cs" />
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `~/.dotnet/dotnet.exe test Scalpel.Tests/Scalpel.Tests.csproj --nologo`
Expected: FAIL — `RecentFiles` does not exist (compile error).

- [ ] **Step 4: Write the implementation**

Create `Services/RecentFiles.cs` (UTF-8-BOM, CRLF):

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Scalpel.Services
{
    /// <summary>
    /// Pure most-recently-used list logic for recently-opened files. No registry, no WPF — the
    /// caller persists the serialized string. Entries are de-duplicated case-insensitively (Windows
    /// paths are case-insensitive), most-recent first, capped to a maximum. The list is serialized
    /// as a '|'-joined string; '|' is illegal in Windows paths, so it is a safe separator.
    /// </summary>
    public static class RecentFiles
    {
        public const int DefaultMax = 10;

        public static List<string> Parse(string? raw) =>
            string.IsNullOrEmpty(raw)
                ? new List<string>()
                : raw!.Split('|').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        public static string Serialize(IEnumerable<string> list) => string.Join("|", list);

        public static List<string> Add(IReadOnlyList<string> current, string path, int max = DefaultMax)
        {
            var result = new List<string> { path };
            result.AddRange(current.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)));
            if (result.Count > max) result.RemoveRange(max, result.Count - max);
            return result;
        }

        public static List<string> Remove(IReadOnlyList<string> current, string path) =>
            current.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `~/.dotnet/dotnet.exe test Scalpel.Tests/Scalpel.Tests.csproj --nologo`
Expected: PASS — all RecentFilesTests green, existing tests still pass.

- [ ] **Step 6: Commit**

```bash
git add Services/RecentFiles.cs Scalpel.Tests/RecentFilesTests.cs Scalpel.Tests/Scalpel.Tests.csproj
git commit -m "feat(recent): pure RecentFiles MRU service + tests"
```

---

### Task 2: App registry wrappers + capture on open

**Files:**
- Modify: `App.xaml.cs` (add wrappers near `GetSetting`/`SetSetting`, ~line 810)
- Modify: `MainWindow.FileOps.cs` (`FinishOpenFile`, ~line 547)

**Interfaces:**
- Consumes: `Scalpel.Services.RecentFiles.*` (Task 1).
- Produces: `App.GetRecentFiles() → List<string>`, `App.AddRecentFile(string)`, `App.RemoveRecentFile(string)`, `App.ClearRecentFiles()`.

- [ ] **Step 1: Add the wrappers**

In `App.xaml.cs`, immediately after the `SetSetting` method (the block ending `catch { /* best-effort */ } }` near line 810), add:

```csharp
        // ── Recent files (most-recent first, capped, de-duplicated) ──────────
        internal static System.Collections.Generic.List<string> GetRecentFiles() =>
            Scalpel.Services.RecentFiles.Parse(GetSetting("RecentFiles"));

        internal static void AddRecentFile(string path)
        {
            try { SetSetting("RecentFiles", Scalpel.Services.RecentFiles.Serialize(
                Scalpel.Services.RecentFiles.Add(GetRecentFiles(), path))); }
            catch { }
        }

        internal static void RemoveRecentFile(string path)
        {
            try { SetSetting("RecentFiles", Scalpel.Services.RecentFiles.Serialize(
                Scalpel.Services.RecentFiles.Remove(GetRecentFiles(), path))); }
            catch { }
        }

        internal static void ClearRecentFiles() => SetSetting("RecentFiles", "");
```

- [ ] **Step 2: Capture on successful open**

In `MainWindow.FileOps.cs`, find `private void FinishOpenFile(string displayPath, string workingPath)` (~line 547). Add as the FIRST line inside the method body:

```csharp
            try { if (System.IO.File.Exists(displayPath)) App.AddRecentFile(displayPath); } catch { }
```

- [ ] **Step 3: Build to verify it compiles**

Run: `~/.dotnet/dotnet.exe build Scalpel.csproj -c Debug`
Expected: `0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add App.xaml.cs MainWindow.FileOps.cs
git commit -m "feat(recent): App registry wrappers + capture in FinishOpenFile"
```

---

### Task 3: Localization keys (all 9 locales)

**Files:**
- Modify: `Strings/en-US.xaml`, `Strings/es.xaml`, `Strings/zh-TW.xaml`, `Strings/zh-CN.xaml`, `Strings/bn.xaml`, `Strings/tr-TR.xaml`, `Strings/he.xaml`, `Strings/ar.xaml`, `Strings/ru.xaml`

**Interfaces:**
- Produces: resource keys `Str_Recent`, `Str_Recent_Clear`, `Str_Recent_Remove`, `Str_Recent_NotFound`.

- [ ] **Step 1: Add the four keys to every locale file**

In EACH of the 9 `Strings/*.xaml` files, add these four lines immediately before the closing `</ResourceDictionary>` (use the translation for that locale; English shown, then per-locale values):

en-US:
```xml
    <sys:String x:Key="Str_Recent">Recent</sys:String>
    <sys:String x:Key="Str_Recent_Clear">Clear recent</sys:String>
    <sys:String x:Key="Str_Recent_Remove">Remove from recent</sys:String>
    <sys:String x:Key="Str_Recent_NotFound">File not found — removed from Recent</sys:String>
```

Per-locale string values (same keys):
- **es:** `Recientes` / `Borrar recientes` / `Quitar de recientes` / `Archivo no encontrado: eliminado de Recientes`
- **zh-TW:** `最近` / `清除最近` / `從最近移除` / `找不到檔案 — 已從最近移除`
- **zh-CN:** `最近` / `清除最近` / `从最近移除` / `找不到文件 — 已从最近移除`
- **bn:** `সাম্প্রতিক` / `সাম্প্রতিক মুছুন` / `সাম্প্রতিক থেকে সরান` / `ফাইল পাওয়া যায়নি — সাম্প্রতিক থেকে সরানো হয়েছে`
- **tr-TR:** `Son kullanılanlar` / `Son kullanılanları temizle` / `Son kullanılanlardan kaldır` / `Dosya bulunamadı — Son kullanılanlardan kaldırıldı`
- **he:** `אחרונים` / `נקה אחרונים` / `הסר מאחרונים` / `הקובץ לא נמצא — הוסר מהאחרונים`
- **ar:** `الأخيرة` / `مسح الأخيرة` / `إزالة من الأخيرة` / `الملف غير موجود — تمت إزالته من الأخيرة`
- **ru:** `Недавние` / `Очистить недавние` / `Убрать из недавних` / `Файл не найден — удалён из недавних`

- [ ] **Step 2: Build to verify XAML still parses**

Run: `~/.dotnet/dotnet.exe build Scalpel.csproj -c Debug`
Expected: `0 Error(s)`.

- [ ] **Step 3: Verify all 9 files have all 4 keys**

Run: `for f in Strings/*.xaml; do echo "$f: $(grep -c 'Str_Recent' "$f")"; done`
Expected: each file prints `4`.

- [ ] **Step 4: Commit**

```bash
git add Strings/
git commit -m "feat(recent): localization keys for Recent files (9 locales)"
```

---

### Task 4: Empty-state Recent list (DropZone)

**Files:**
- Create: `MainWindow.Recent.cs`
- Modify: `MainWindow.xaml` (DropZone card, ~line 871-877)
- Modify: `MainWindow.xaml.cs` (constructor, after `SetMode(AppMode.View)` ~line 230)
- Modify: `MainWindow.DirtyTracking.cs` (`CloseFile`, after `DropZone.Visibility = Visibility.Visible` ~line 72)

**Interfaces:**
- Consumes: `App.GetRecentFiles()`, `App.RemoveRecentFile(string)` (Task 2); `OpenFile(string)` and `ShowToast(string, string?)` (existing in `MainWindow.FileOps.cs`); `Loc(string)` (existing in `MainWindow.FileOps.cs`).
- Produces: `PopulateRecentList()` (called by ctor + CloseFile + menu/row handlers).

- [ ] **Step 1: Add the Recent section to the DropZone XAML**

In `MainWindow.xaml`, replace the inner `StackPanel` of the DropZone card (lines ~871-876, the one containing "Drop PDF here") so the Recent section sits below the prompt:

```xml
                            <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" MinWidth="320">
                                <TextBlock Text="Drop PDF here" FontFamily="{DynamicResource FontUI}" FontSize="20"
                                           Foreground="{DynamicResource TextSecondary}" HorizontalAlignment="Center"/>
                                <TextBlock Text="or click to browse" FontFamily="{DynamicResource FontUI}" FontSize="13"
                                           Foreground="{DynamicResource TextSecondary}" HorizontalAlignment="Center" Margin="0,8,0,0"/>
                                <TextBlock x:Name="RecentHeader" Text="{DynamicResource Str_Recent}" Visibility="Collapsed"
                                           FontFamily="{DynamicResource FontUI}" FontSize="12" FontWeight="SemiBold"
                                           Foreground="{DynamicResource TextDim}" HorizontalAlignment="Left" Margin="0,22,0,6"/>
                                <ItemsControl x:Name="RecentList" Visibility="Collapsed" HorizontalAlignment="Stretch">
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate>
                                            <Border Background="Transparent" Padding="6,4" Margin="0,1" CornerRadius="5"
                                                    MouseLeftButtonDown="RecentRow_Click" Cursor="Hand" Tag="{Binding Path}">
                                                <DockPanel LastChildFill="True">
                                                    <TextBlock DockPanel.Dock="Left" Text="{StaticResource Ico_Open}"
                                                               FontFamily="{DynamicResource FontIcon}" FontSize="14"
                                                               Foreground="{DynamicResource TextDim}" VerticalAlignment="Center" Margin="0,0,8,0"/>
                                                    <Button DockPanel.Dock="Right" Content="{StaticResource Ico_WinClose}"
                                                            FontFamily="{DynamicResource FontIcon}" Style="{StaticResource StudioIconButton}"
                                                            ToolTip="{DynamicResource Str_Recent_Remove}" Tag="{Binding Path}"
                                                            Click="RecentRemove_Click" VerticalAlignment="Center"/>
                                                    <TextBlock DockPanel.Dock="Right" Text="{Binding Dir}" Margin="10,0,8,0"
                                                               FontFamily="{DynamicResource FontUI}" FontSize="11"
                                                               Foreground="{DynamicResource TextDim}" VerticalAlignment="Center"
                                                               TextTrimming="CharacterEllipsis" MaxWidth="140"/>
                                                    <TextBlock Text="{Binding FileName}" FontFamily="{DynamicResource FontUI}" FontSize="13"
                                                               Foreground="{DynamicResource TextPrimary}" VerticalAlignment="Center"
                                                               TextTrimming="CharacterEllipsis"/>
                                                </DockPanel>
                                            </Border>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </StackPanel>
```

- [ ] **Step 2: Create `MainWindow.Recent.cs`**

Create `MainWindow.Recent.cs` (UTF-8-BOM, CRLF):

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Scalpel
{
    public partial class MainWindow
    {
        // View-model row for the empty-state recent list.
        private sealed record RecentItemVm(string Path, string FileName, string Dir);

        // Returns the recent files that still exist on disk (most-recent first).
        private List<string> ExistingRecentFiles() =>
            App.GetRecentFiles().Where(p => { try { return File.Exists(p); } catch { return false; } }).ToList();

        // Rebuilds the empty-state recent list; hides the header/list when there is nothing to show.
        private void PopulateRecentList()
        {
            if (RecentList is null || RecentHeader is null) return;
            var items = ExistingRecentFiles()
                .Select(p => new RecentItemVm(p, Path.GetFileName(p), TrimDir(Path.GetDirectoryName(p) ?? "")))
                .ToList();
            RecentList.ItemsSource = items;
            var vis = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            RecentList.Visibility = vis;
            RecentHeader.Visibility = vis;
        }

        // Shortens a directory to its last segment for compact display ("...\Docs").
        private static string TrimDir(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return "";
            var name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));
            return string.IsNullOrEmpty(name) ? dir : name;
        }

        // Opens a recent file; if it has vanished, toasts and drops it from the list.
        private void OpenRecent(string path)
        {
            if (File.Exists(path)) { OpenFile(path); return; }
            ShowToast(Loc("Str_Recent_NotFound"));
            App.RemoveRecentFile(path);
            PopulateRecentList();
        }

        private void RecentRow_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true; // do not also trigger the DropZone browse handler
            if (sender is FrameworkElement fe && fe.Tag is string path) OpenRecent(path);
        }

        private void RecentRemove_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is FrameworkElement fe && fe.Tag is string path)
            {
                App.RemoveRecentFile(path);
                PopulateRecentList();
            }
        }
    }
}
```

- [ ] **Step 3: Call PopulateRecentList from the constructor and CloseFile**

In `MainWindow.xaml.cs`, immediately after `SetMode(AppMode.View);` (~line 230) add:

```csharp
            PopulateRecentList();
```

In `MainWindow.DirtyTracking.cs`, immediately after `DropZone.Visibility = Visibility.Visible;` (~line 72) add:

```csharp
            PopulateRecentList();
```

- [ ] **Step 4: Build and run a manual smoke**

Run: `~/.dotnet/dotnet.exe build Scalpel.csproj -c Debug`
Expected: `0 Error(s)`.
Manual: launch `bin/Debug/net48/Scalpel.exe`, open 2-3 PDFs, then Close the file — the start screen shows them under "Recent"; clicking a row reopens it; ✕ removes one.

- [ ] **Step 5: Commit**

```bash
git add MainWindow.Recent.cs MainWindow.xaml MainWindow.xaml.cs MainWindow.DirtyTracking.cs
git commit -m "feat(recent): empty-state Recent list in the DropZone"
```

---

### Task 5: Open-button context-menu recent items

**Files:**
- Modify: `MainWindow.xaml` (Open `Button.ContextMenu`, ~line 456-461)
- Modify: `MainWindow.Recent.cs` (add `RebuildOpenRecentMenu` + handlers)

**Interfaces:**
- Consumes: `App.GetRecentFiles()`, `App.ClearRecentFiles()` (Task 2); `OpenRecent(string)`, `ExistingRecentFiles()` (Task 4); `Loc(string)`.
- Produces: `RebuildOpenRecentMenu` (wired to the menu's `Opened` event).

- [ ] **Step 1: Name the menu and wire its Opened event**

In `MainWindow.xaml`, change the Open button's `<ContextMenu>` open tag (line ~457) to:

```xml
                            <ContextMenu x:Name="OpenContextMenu" Opened="OpenContextMenu_Opened">
```

(Leave the existing `Open`/`New`/`Close` `MenuItem`s as the first three children.)

- [ ] **Step 2: Add the rebuild logic to `MainWindow.Recent.cs`**

Add these members inside the `MainWindow` partial class in `MainWindow.Recent.cs`:

```csharp
        // Rebuilds the dynamic "recent files" tail of the Open context menu on each open. The first
        // three items (Open / New / Close) are static; everything we add is tagged "recent" so it
        // can be cleared and rebuilt without disturbing them.
        private void OpenContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (OpenContextMenu is null) return;
            // Remove previously-added dynamic items.
            for (int i = OpenContextMenu.Items.Count - 1; i >= 0; i--)
                if (OpenContextMenu.Items[i] is FrameworkElement fe && (fe.Tag as string) == "recent")
                    OpenContextMenu.Items.RemoveAt(i);

            var files = ExistingRecentFiles();
            if (files.Count == 0) return;

            OpenContextMenu.Items.Add(new Separator { Tag = "recent" });
            foreach (var path in files)
            {
                var item = new MenuItem
                {
                    Header = System.IO.Path.GetFileName(path),
                    ToolTip = path,
                    Tag = "recent",
                };
                string captured = path;
                item.Click += (_, _) => OpenRecent(captured);
                OpenContextMenu.Items.Add(item);
            }
            OpenContextMenu.Items.Add(new Separator { Tag = "recent" });
            var clear = new MenuItem { Header = Loc("Str_Recent_Clear"), Tag = "recent" };
            clear.Click += (_, _) => { App.ClearRecentFiles(); PopulateRecentList(); };
            OpenContextMenu.Items.Add(clear);
        }
```

- [ ] **Step 3: Build and manual smoke**

Run: `~/.dotnet/dotnet.exe build Scalpel.csproj -c Debug`
Expected: `0 Error(s)`.
Manual: with a document open, right-click the Open button — recent files appear below Open/New/Close, clicking one opens it, "Clear recent" empties the list.

- [ ] **Step 4: Commit**

```bash
git add MainWindow.xaml MainWindow.Recent.cs
git commit -m "feat(recent): recent files in the Open context menu + Clear recent"
```

---

### Task 6: Changelog entry

**Files:**
- Modify: `Services/Changelog.cs` (1.9.0 release block, ~line 15-28)

- [ ] **Step 1: Prepend the bullet**

In `Services/Changelog.cs`, inside the `new Release("1.9.0", "June 2026", new[] { ... })` array, add as the FIRST string element (before the OCR bullet on line 17):

```csharp
                "Recent files: reopen a recently-opened PDF from the start screen or by right-clicking the Open button; missing files clean themselves out of the list.",
```

- [ ] **Step 2: Build to verify it compiles**

Run: `~/.dotnet/dotnet.exe build Scalpel.csproj -c Debug`
Expected: `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add Services/Changelog.cs
git commit -m "feat(recent): changelog entry for Recent files"
```

---

## Final verification

- [ ] `~/.dotnet/dotnet.exe test Scalpel.Tests/Scalpel.Tests.csproj --nologo` → all green (RecentFilesTests + existing 192).
- [ ] `~/.dotnet/dotnet.exe build Scalpel.csproj -c Debug` → `0 Error(s)`.
- [ ] `for f in Strings/*.xaml; do grep -c Str_Recent "$f"; done` → all `4`.
- [ ] Manual: open files → appear in both surfaces; ✕ / Clear / missing-file self-heal all work.
