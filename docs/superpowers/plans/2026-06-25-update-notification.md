# Update Notification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Notify the user inside Scalpel when a newer version exists and send them to the right place to get it (Microsoft Store for Store builds, the website for installed/portable), opt-in and phone-home-free by default.

**Architecture:** A WPF-free, testable `Services/UpdateService.cs` does the version fetch/parse/compare and URL resolution. A `MainWindow.Update.cs` partial glues it to the registry settings, the opt-in dialog, the Settings toggle, and a branded overlay that mirrors the existing What's New overlay. The latest version is published as a static `version.json` on the website.

**Tech Stack:** C# / .NET Framework 4.8 (net48), WPF, `System.Net.Http.HttpClient`, `System.Text.Json`, xUnit (Scalpel.Tests).

## Global Constraints

- Target **net48**; build/test with the user-local SDK at `~/.dotnet/dotnet.exe` (dotnet is not on PATH).
- `UpdateService.cs` must be **WPF-free and registry-free** (no `App`, no `System.Windows.*`) so the test project can link it. Registry/UI glue lives in `MainWindow.Update.cs`.
- Defensive I/O: every network/parse path is wrapped in `try/catch` that swallows and returns null/no-op — never surface an error to the user (match `Services/OcrAssets.cs`).
- Settings live in `HKCU\Software\Scalpel\Settings` via `App.GetSetting(string)→string?` and `App.SetSetting(string,string)`.
- **No phone-home by default:** no network call unless `UpdateCheckEnabled == "1"`.
- Distribution detection: `App.IsPackaged()` (Store/MSIX), `App.IsPortable()`.
- Real links: site `https://scalpel-pdf.netlify.app`, store `https://apps.microsoft.com/detail/9n9hn8xw4lf3`.
- User-facing change → add a `Services/Changelog.cs` entry.
- Every locale key must exist in **all 9** `Strings/*.xaml` files (en-US, es, zh-TW, zh-CN, bn, tr-TR, he, ar, ru) or `DynamicResource`/`Loc()` blanks out.

---

### Task 1: Publish `version.json` on the website

**Files:**
- Create: `website/public/version.json`

**Interfaces:**
- Produces: the JSON document the app fetches. Shape: `{ version:string, siteUrl:string, storeUrl:string, notes:string[] }`.

- [ ] **Step 1: Create the file**

```json
{
  "version": "1.5.1",
  "siteUrl": "https://scalpel-pdf.netlify.app",
  "storeUrl": "https://apps.microsoft.com/detail/9n9hn8xw4lf3",
  "notes": []
}
```

(Set `version` to the current released version. Bump it — and optionally fill `notes` — at each release so older clients see an update.)

- [ ] **Step 2: Validate it is well-formed JSON**

Run: `node -e "JSON.parse(require('fs').readFileSync('website/public/version.json','utf8')); console.log('ok')"`
Expected: `ok`

- [ ] **Step 3: Commit**

```bash
git add website/public/version.json
git commit -m "feat(web): publish version.json for in-app update checks"
```

---

### Task 2: `UpdateService` — model, version compare, parse, URL resolution

**Files:**
- Create: `Services/UpdateService.cs`
- Test: `Scalpel.Tests/UpdateServiceTests.cs`
- Modify: `Scalpel.Tests/Scalpel.Tests.csproj` (add a `<Compile Include>` link)

**Interfaces:**
- Produces:
  - `sealed record UpdateInfo(string Version, string SiteUrl, string StoreUrl, string[] Notes)`
  - `static bool UpdateService.IsNewer(string latest, Version current)`
  - `static UpdateInfo? UpdateService.TryParse(string json)`
  - `static string UpdateService.ResolveUrl(UpdateInfo info, bool packaged)`

- [ ] **Step 1: Add the compile link to the test project**

In `Scalpel.Tests/Scalpel.Tests.csproj`, inside the existing `<ItemGroup>` that links source files, add:

```xml
<Compile Include="..\Services\UpdateService.cs" Link="Services\UpdateService.cs" />
```

- [ ] **Step 2: Write the failing tests**

Create `Scalpel.Tests/UpdateServiceTests.cs`:

```csharp
using System;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class UpdateServiceTests
    {
        [Theory]
        [InlineData("1.7.0", "1.5.1.36", true)]   // newer minor
        [InlineData("1.5.2", "1.5.1.36", true)]   // newer build
        [InlineData("2.0.0", "1.9.9.99", true)]   // newer major
        [InlineData("1.5.1", "1.5.1.36", false)]  // same 3-part
        [InlineData("1.5.0", "1.5.1.36", false)]  // older
        [InlineData("1.5", "1.5.1.36", false)]    // missing component, older-or-equal
        [InlineData("garbage", "1.5.1.36", false)]// unparseable => not newer
        public void IsNewer_compares_three_components(string latest, string current, bool expected)
        {
            Assert.Equal(expected, UpdateService.IsNewer(latest, Version.Parse(current)));
        }

        [Fact]
        public void TryParse_reads_full_document()
        {
            var json = "{\"version\":\"1.7.0\",\"siteUrl\":\"https://s\",\"storeUrl\":\"https://store\",\"notes\":[\"a\",\"b\"]}";
            var info = UpdateService.TryParse(json);
            Assert.NotNull(info);
            Assert.Equal("1.7.0", info!.Version);
            Assert.Equal("https://s", info.SiteUrl);
            Assert.Equal("https://store", info.StoreUrl);
            Assert.Equal(new[] { "a", "b" }, info.Notes);
        }

        [Fact]
        public void TryParse_tolerates_missing_optional_fields()
        {
            var info = UpdateService.TryParse("{\"version\":\"1.7.0\"}");
            Assert.NotNull(info);
            Assert.Equal("1.7.0", info!.Version);
            Assert.Equal("", info.StoreUrl);
            Assert.Empty(info.Notes);
        }

        [Fact]
        public void TryParse_returns_null_on_garbage()
        {
            Assert.Null(UpdateService.TryParse("not json"));
            Assert.Null(UpdateService.TryParse("{\"nope\":1}")); // no version
        }

        [Fact]
        public void ResolveUrl_picks_store_when_packaged_else_site()
        {
            var info = new UpdateInfo("1.7.0", "https://site", "https://store", Array.Empty<string>());
            Assert.Equal("https://store", UpdateService.ResolveUrl(info, packaged: true));
            Assert.Equal("https://site", UpdateService.ResolveUrl(info, packaged: false));
        }

        [Fact]
        public void ResolveUrl_falls_back_to_store_search_when_store_url_empty()
        {
            var info = new UpdateInfo("1.7.0", "https://site", "", Array.Empty<string>());
            Assert.Equal(UpdateService.StoreSearchUrl, UpdateService.ResolveUrl(info, packaged: true));
        }
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `~/.dotnet/dotnet.exe test --filter "FullyQualifiedName~UpdateServiceTests"`
Expected: FAIL — `UpdateService` / `UpdateInfo` do not exist.

- [ ] **Step 4: Implement `UpdateService` (this step's portion)**

Create `Services/UpdateService.cs`:

```csharp
using System;
using System.Linq;
using System.Text.Json;

namespace Scalpel.Services
{
    /// <summary>Latest-version metadata fetched from the website's version.json.</summary>
    public sealed record UpdateInfo(string Version, string SiteUrl, string StoreUrl, string[] Notes);

    /// <summary>
    /// Update-check logic: parse the published version.json, compare versions, and resolve the
    /// distribution-appropriate download URL. WPF-free and registry-free so it can be unit-tested.
    /// </summary>
    public static class UpdateService
    {
        /// <summary>Fallback when a packaged build has no explicit storeUrl.</summary>
        public const string StoreSearchUrl = "https://apps.microsoft.com/search?query=Scalpel+PDF";

        /// <summary>True when <paramref name="latest"/> is a strictly newer 3-part version.</summary>
        public static bool IsNewer(string latest, Version current)
        {
            if (!TryParseVersion(latest, out var l)) return false;
            var c = new Version(current.Major, current.Minor, Math.Max(0, current.Build));
            return l > c;
        }

        /// <summary>Parses a version.json document; returns null on any malformed/missing-version input.</summary>
        public static UpdateInfo? TryParse(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("version", out var v) || v.ValueKind != JsonValueKind.String)
                    return null;
                string version = v.GetString() ?? "";
                if (version.Length == 0) return null;

                string site = StringProp(root, "siteUrl");
                string store = StringProp(root, "storeUrl");
                string[] notes = Array.Empty<string>();
                if (root.TryGetProperty("notes", out var n) && n.ValueKind == JsonValueKind.Array)
                    notes = n.EnumerateArray()
                             .Where(e => e.ValueKind == JsonValueKind.String)
                             .Select(e => e.GetString() ?? "")
                             .Where(s => s.Length > 0)
                             .ToArray();

                return new UpdateInfo(version, site, store, notes);
            }
            catch { return null; }
        }

        /// <summary>Store URL for packaged builds (search fallback if empty), else the site URL.</summary>
        public static string ResolveUrl(UpdateInfo info, bool packaged)
        {
            if (packaged)
                return string.IsNullOrWhiteSpace(info.StoreUrl) ? StoreSearchUrl : info.StoreUrl;
            return info.SiteUrl;
        }

        private static string StringProp(JsonElement root, string name) =>
            root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString() ?? "" : "";

        private static bool TryParseVersion(string s, out Version version)
        {
            version = new Version(0, 0, 0);
            if (string.IsNullOrWhiteSpace(s)) return false;
            var parts = s.Split('.');
            if (parts.Length == 0) return false;
            int Get(int i) => i < parts.Length && int.TryParse(parts[i], out var n) && n >= 0 ? n : 0;
            // Require at least the major to be a real number.
            if (!int.TryParse(parts[0], out _)) return false;
            version = new Version(Get(0), Get(1), Get(2));
            return true;
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `~/.dotnet/dotnet.exe test --filter "FullyQualifiedName~UpdateServiceTests"`
Expected: PASS (all UpdateServiceTests green).

- [ ] **Step 6: Commit**

```bash
git add Services/UpdateService.cs Scalpel.Tests/UpdateServiceTests.cs Scalpel.Tests/Scalpel.Tests.csproj
git commit -m "feat(update): version-compare, parse and URL resolution (UpdateService core)"
```

---

### Task 3: `UpdateService` — throttle decision + network fetch

**Files:**
- Modify: `Services/UpdateService.cs`
- Test: `Scalpel.Tests/UpdateServiceTests.cs` (add cases)

**Interfaces:**
- Consumes: `UpdateInfo`, `TryParse` (Task 2).
- Produces:
  - `static bool UpdateService.ShouldCheckNow(bool enabled, DateTime? lastCheck, DateTime now)`
  - `static TimeSpan UpdateService.CheckInterval` (24h)
  - `static string UpdateService.VersionJsonUrl`
  - `static Task<UpdateInfo?> UpdateService.CheckAsync(string url)` (network; not unit-tested)

- [ ] **Step 1: Write the failing tests (append to UpdateServiceTests.cs)**

```csharp
        [Fact]
        public void ShouldCheckNow_false_when_disabled()
        {
            var now = new DateTime(2026, 6, 25, 12, 0, 0, DateTimeKind.Utc);
            Assert.False(UpdateService.ShouldCheckNow(enabled: false, lastCheck: null, now: now));
        }

        [Fact]
        public void ShouldCheckNow_true_when_enabled_and_never_checked()
        {
            var now = new DateTime(2026, 6, 25, 12, 0, 0, DateTimeKind.Utc);
            Assert.True(UpdateService.ShouldCheckNow(enabled: true, lastCheck: null, now: now));
        }

        [Fact]
        public void ShouldCheckNow_respects_24h_throttle()
        {
            var now = new DateTime(2026, 6, 25, 12, 0, 0, DateTimeKind.Utc);
            Assert.False(UpdateService.ShouldCheckNow(true, now.AddHours(-1), now));   // too soon
            Assert.True(UpdateService.ShouldCheckNow(true, now.AddHours(-25), now));   // due
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `~/.dotnet/dotnet.exe test --filter "FullyQualifiedName~UpdateServiceTests.ShouldCheckNow"`
Expected: FAIL — `ShouldCheckNow` not defined.

- [ ] **Step 3: Implement the throttle + network members (add to UpdateService)**

Add `using System.Net.Http;`, `using System.Net;`, and `using System.Threading.Tasks;` at the top of `Services/UpdateService.cs`, then add these members to the class:

```csharp
        public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
        public const string VersionJsonUrl = "https://scalpel-pdf.netlify.app/version.json";

        /// <summary>Whether an automatic check is due: enabled and last check older than the interval.</summary>
        public static bool ShouldCheckNow(bool enabled, DateTime? lastCheck, DateTime now)
        {
            if (!enabled) return false;
            if (lastCheck is null) return true;
            return now - lastCheck.Value >= CheckInterval;
        }

        /// <summary>
        /// Fetches and parses version.json. Returns null on any failure (offline/timeout/malformed) —
        /// caller does nothing. Sends no data about the user. Does NOT touch settings.
        /// </summary>
        public static async Task<UpdateInfo?> CheckAsync(string url)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                http.DefaultRequestHeaders.Add("User-Agent", "Scalpel-UpdateCheck");
                string json = await http.GetStringAsync(url).ConfigureAwait(false);
                return TryParse(json);
            }
            catch { return null; }
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `~/.dotnet/dotnet.exe test --filter "FullyQualifiedName~UpdateServiceTests"`
Expected: PASS (all, including ShouldCheckNow).

- [ ] **Step 5: Commit**

```bash
git add Services/UpdateService.cs Scalpel.Tests/UpdateServiceTests.cs
git commit -m "feat(update): 24h throttle decision + version.json fetch"
```

---

### Task 4: Localization keys (all 9 locale files)

**Files:**
- Modify: `Strings/en-US.xaml`, `Strings/es.xaml`, `Strings/zh-TW.xaml`, `Strings/zh-CN.xaml`, `Strings/bn.xaml`, `Strings/tr-TR.xaml`, `Strings/he.xaml`, `Strings/ar.xaml`, `Strings/ru.xaml`

**Interfaces:**
- Produces these resource keys (used by Tasks 5 & 6 via `Loc("…")`):
  `Str_Update_OptIn_Title`, `Str_Update_OptIn_Body`, `Str_Update_Title`,
  `Str_Update_Body_Prefix` (text before the version), `Str_Update_Body_Suffix` (text after, before "you have X"),
  `Str_Update_Get`, `Str_Update_Later`, `Str_Settings_CheckUpdates`.

- [ ] **Step 1: Add the keys to `Strings/en-US.xaml`**

Inside the root `<ResourceDictionary>` (next to other `Str_*` entries), add:

```xml
    <s:String x:Key="Str_Update_OptIn_Title">Check for updates?</s:String>
    <s:String x:Key="Str_Update_OptIn_Body">Scalpel can check scalpel-pdf.netlify.app once a day for a new version. No information about you or your files is sent. Enable update checks?</s:String>
    <s:String x:Key="Str_Update_Title">Update available</s:String>
    <s:String x:Key="Str_Update_Body_Prefix">Scalpel </s:String>
    <s:String x:Key="Str_Update_Body_Suffix"> is available.</s:String>
    <s:String x:Key="Str_Update_Get">Get the update</s:String>
    <s:String x:Key="Str_Update_Later">Later</s:String>
    <s:String x:Key="Str_Settings_CheckUpdates">Check for updates</s:String>
```

(If the file's string element prefix differs, match the existing `Str_*` lines in that same file exactly — use the same element/namespace they use.)

- [ ] **Step 2: Add the same 8 keys to each of the other 8 locale files, translated**

For each of `es, zh-TW, zh-CN, bn, tr-TR, he, ar, ru`, add the same 8 `x:Key` entries with values translated to that language, matching the tone and element syntax of the existing `Str_*` entries already in that file. Brand/product names stay Latin (Scalpel, scalpel-pdf.netlify.app). Hebrew (`he`) values, for reference:

```xml
    <s:String x:Key="Str_Update_OptIn_Title">לבדוק עדכונים?</s:String>
    <s:String x:Key="Str_Update_OptIn_Body">Scalpel יכול לבדוק פעם ביום ב-scalpel-pdf.netlify.app אם יש גרסה חדשה. שום מידע עליך או על הקבצים שלך לא נשלח. להפעיל בדיקת עדכונים?</s:String>
    <s:String x:Key="Str_Update_Title">עדכון זמין</s:String>
    <s:String x:Key="Str_Update_Body_Prefix">Scalpel </s:String>
    <s:String x:Key="Str_Update_Body_Suffix"> זמין.</s:String>
    <s:String x:Key="Str_Update_Get">קבלת העדכון</s:String>
    <s:String x:Key="Str_Update_Later">אחר כך</s:String>
    <s:String x:Key="Str_Settings_CheckUpdates">בדיקת עדכונים</s:String>
```

- [ ] **Step 3: Verify each file has all 8 keys**

Run: `grep -c "Str_Update_Title\|Str_Update_OptIn_Title\|Str_Update_OptIn_Body\|Str_Update_Body_Prefix\|Str_Update_Body_Suffix\|Str_Update_Get\|Str_Update_Later\|Str_Settings_CheckUpdates" Strings/*.xaml`
Expected: each `Strings/*.xaml` reports `8`.

- [ ] **Step 4: Build to confirm XAML still parses**

Run: `~/.dotnet/dotnet.exe build` (close any running `Scalpel.exe` first; a pdfium copy lock is not a code error)
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add Strings/*.xaml
git commit -m "i18n: add update-notification strings to all locales"
```

---

### Task 5: Update overlay UI + settings glue + opt-in + startup wiring

**Files:**
- Modify: `MainWindow.xaml` (add the overlay markup near `WhatsNewOverlay`)
- Create: `MainWindow.Update.cs` (new `partial class MainWindow` — settings wrappers, opt-in, orchestration, handlers)
- Modify: `MainWindow.xaml.cs:192` constructor (wire the startup check)

**Interfaces:**
- Consumes: `UpdateService.{ShouldCheckNow,CheckAsync,IsNewer,ResolveUrl,VersionJsonUrl}`, `UpdateInfo` (Tasks 2-3); `App.GetSetting/SetSetting`, `App.IsPackaged()`; `Loc(string)`; `ScalpelDialog.Show(Window?, string, string, MessageBoxButton)`; the `Str_Update_*` keys (Task 4).
- Produces: `UpdateOverlay` (XAML), `MainWindow.CheckForUpdatesAsync()`, `MainWindow.EnsureUpdateOptIn()`, and the overlay click handlers referenced by the XAML.

- [ ] **Step 1: Add the overlay markup to `MainWindow.xaml`**

Immediately AFTER the closing `</Grid>` of `WhatsNewOverlay` (after line ~1360; find the `</Grid>` that closes `x:Name="WhatsNewOverlay"`), add:

```xml
        <!-- Update-available overlay -->
        <Grid x:Name="UpdateOverlay"
              Grid.RowSpan="5"
              Panel.ZIndex="10002"
              Visibility="Collapsed"
              Background="#88000000"
              MouseLeftButtonDown="UpdateOverlay_MouseLeftButtonDown">
            <Border Style="{StaticResource StudioOverlayCard}"
                    Padding="28,22,28,22"
                    VerticalAlignment="Center"
                    Width="480" MaxHeight="520"
                    MouseLeftButtonDown="UpdateOverlayCard_MouseLeftButtonDown">
                <StackPanel>
                    <TextBlock Text="{DynamicResource Str_Update_Title}"
                               FontSize="18" FontWeight="SemiBold"
                               Foreground="{DynamicResource TextPrimary}"/>
                    <TextBlock x:Name="UpdateBodyText"
                               Margin="0,10,0,0" TextWrapping="Wrap"
                               Foreground="{DynamicResource TextSecondary}"/>
                    <StackPanel x:Name="UpdateNotesPanel" Margin="0,12,0,0"/>
                    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,20,0,0">
                        <Button Content="{DynamicResource Str_Update_Later}"
                                Style="{StaticResource StudioToolButton}"
                                MinWidth="92" Margin="0,0,10,0"
                                Click="UpdateLater_Click"/>
                        <Button Content="{DynamicResource Str_Update_Get}"
                                Style="{StaticResource StudioPrimaryButton}"
                                MinWidth="120"
                                Click="UpdateGet_Click"/>
                    </StackPanel>
                </StackPanel>
            </Border>
        </Grid>
```

(If `StudioPrimaryButton`/`StudioToolButton` aren't the names used elsewhere in this file, match the styles used by the What's New / About overlay buttons in the same file.)

- [ ] **Step 2: Create `MainWindow.Update.cs`**

```csharp
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Scalpel.Services;

namespace Scalpel
{
    public partial class MainWindow
    {
        private const string KeyUpdateEnabled = "UpdateCheckEnabled";
        private const string KeyUpdateLastCheck = "LastUpdateCheck";
        private const string KeyUpdateDismissed = "UpdateDismissedVersion";

        private UpdateInfo? _pendingUpdate;

        /// <summary>One-time opt-in prompt the first time the setting is unset. Stores the choice.</summary>
        private void EnsureUpdateOptIn()
        {
            try
            {
                if (App.GetSetting(KeyUpdateEnabled) != null) return; // already answered
                var res = ScalpelDialog.Show(this,
                    Loc("Str_Update_OptIn_Body"),
                    Loc("Str_Update_OptIn_Title"),
                    MessageBoxButton.YesNo);
                App.SetSetting(KeyUpdateEnabled, res == MessageBoxResult.Yes ? "1" : "0");
            }
            catch { /* never block startup on the opt-in */ }
        }

        /// <summary>If enabled and due, fetch version.json and show the overlay for a newer version.</summary>
        private async Task CheckForUpdatesAsync()
        {
            try
            {
                bool enabled = App.GetSetting(KeyUpdateEnabled) == "1";
                DateTime? last = null;
                if (DateTime.TryParse(App.GetSetting(KeyUpdateLastCheck), out var parsed))
                    last = parsed.ToUniversalTime();
                if (!UpdateService.ShouldCheckNow(enabled, last, DateTime.UtcNow)) return;

                var info = await UpdateService.CheckAsync(UpdateService.VersionJsonUrl).ConfigureAwait(true);
                App.SetSetting(KeyUpdateLastCheck, DateTime.UtcNow.ToString("o")); // after every attempt
                if (info is null) return;

                var current = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version
                              ?? new Version(0, 0, 0);
                if (!UpdateService.IsNewer(info.Version, current)) return;
                if (App.GetSetting(KeyUpdateDismissed) == info.Version) return; // dismissed this version

                ShowUpdateOverlay(info, current);
            }
            catch { /* offline / any failure: silent */ }
        }

        private void ShowUpdateOverlay(UpdateInfo info, Version current)
        {
            _pendingUpdate = info;
            UpdateBodyText.Text =
                $"{Loc("Str_Update_Body_Prefix")}{info.Version}{Loc("Str_Update_Body_Suffix")} " +
                $"(v{current.Major}.{current.Minor}.{Math.Max(0, current.Build)})";

            UpdateNotesPanel.Children.Clear();
            foreach (var note in info.Notes)
            {
                UpdateNotesPanel.Children.Add(new TextBlock
                {
                    Text = "• " + note,
                    Margin = new Thickness(0, 2, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary"),
                });
            }
            UpdateOverlay.Visibility = Visibility.Visible;
        }

        private void UpdateGet_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_pendingUpdate is { } info)
                {
                    string url = UpdateService.ResolveUrl(info, App.IsPackaged());
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
            }
            catch { /* ignore launch failure */ }
            UpdateOverlay.Visibility = Visibility.Collapsed;
        }

        private void UpdateLater_Click(object sender, RoutedEventArgs e)
        {
            try { if (_pendingUpdate is { } info) App.SetSetting(KeyUpdateDismissed, info.Version); }
            catch { }
            UpdateOverlay.Visibility = Visibility.Collapsed;
        }

        private void UpdateOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => UpdateOverlay.Visibility = Visibility.Collapsed;

        private void UpdateOverlayCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => e.Handled = true;
    }
}
```

- [ ] **Step 3: Wire startup check into the constructor**

In `MainWindow.xaml.cs`, in the `public MainWindow()` constructor (ends around line 232), add at the END of the constructor body:

```csharp
            Loaded += async (_, _) =>
            {
                EnsureUpdateOptIn();
                await CheckForUpdatesAsync();
            };
```

- [ ] **Step 4: Build**

Run: `~/.dotnet/dotnet.exe build` (close any running `Scalpel.exe` first)
Expected: Build succeeded (no missing-handler / missing-resource errors).

- [ ] **Step 5: Commit**

```bash
git add MainWindow.xaml MainWindow.Update.cs MainWindow.xaml.cs
git commit -m "feat(update): opt-in, startup check, and update-available overlay"
```

---

### Task 6: Settings toggle for update checks

**Files:**
- Modify: `MainWindow.xaml` (Settings panel — add a toggle near the other settings)
- Modify: `MainWindow.Update.cs` (toggle handler + sync on open)

**Interfaces:**
- Consumes: `App.GetSetting/SetSetting`, `KeyUpdateEnabled`, `Str_Settings_CheckUpdates` (Task 4).
- Produces: `UpdateCheckToggle` control + `UpdateCheckToggle_Click` handler; `SyncUpdateToggle()`.

- [ ] **Step 1: Add the toggle to the Settings panel in `MainWindow.xaml`**

Find the Settings overlay/panel (search `SettingsOverlay` or the panel holding the Theme/Language options) and add, alongside the existing setting rows, a labeled checkbox matching the file's existing toggle style:

```xml
        <CheckBox x:Name="UpdateCheckToggle"
                  Content="{DynamicResource Str_Settings_CheckUpdates}"
                  Margin="0,8,0,0"
                  Click="UpdateCheckToggle_Click"/>
```

(If settings use a custom toggle style such as `SettingsSectionToggle` or `StudioToolToggle`, apply that same `Style` so it matches — mirror an existing settings row in this file.)

- [ ] **Step 2: Add the handler + sync to `MainWindow.Update.cs`**

```csharp
        /// <summary>Reflects the stored setting onto the toggle; call when opening Settings.</summary>
        private void SyncUpdateToggle()
        {
            if (UpdateCheckToggle != null)
                UpdateCheckToggle.IsChecked = App.GetSetting(KeyUpdateEnabled) == "1";
        }

        private void UpdateCheckToggle_Click(object sender, RoutedEventArgs e)
        {
            App.SetSetting(KeyUpdateEnabled, UpdateCheckToggle.IsChecked == true ? "1" : "0");
        }
```

Add `using System.Windows.Controls;` if not already present (it is, from Task 5).

- [ ] **Step 3: Call `SyncUpdateToggle()` when Settings opens**

Find the existing handler that shows the Settings panel (e.g. `SettingsBtn_Click`) and add a call to `SyncUpdateToggle();` where it makes the panel visible (mirror how it syncs the Theme/Language radios there).

- [ ] **Step 4: Build**

Run: `~/.dotnet/dotnet.exe build` (close any running `Scalpel.exe` first)
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add MainWindow.xaml MainWindow.Update.cs
git commit -m "feat(update): Settings toggle to enable/disable update checks"
```

---

### Task 7: Changelog entry

**Files:**
- Modify: `Services/Changelog.cs:13` (prepend a new `Release` to the list head)

**Interfaces:**
- Consumes: existing `Release(string Version, string Date, string[] Changes)` record.

- [ ] **Step 1: Prepend a release entry**

In `Services/Changelog.cs`, add as the FIRST element of the `Releases` array (before the `1.7.0` entry):

```csharp
            new Release("1.8.0", "June 2026", new[]
            {
                "Optional update notifications: Scalpel can now let you know when a new version is out. It's off until you turn it on, sends no information about you or your files, and you can toggle it anytime in Settings.",
                "The notification links you straight to the right place to update — the Microsoft Store for Store installs, the website for portable and installed copies.",
            }),
```

(Use the next version above the current head entry; adjust the version string if the head has moved.)

- [ ] **Step 2: Build**

Run: `~/.dotnet/dotnet.exe build` (close any running `Scalpel.exe` first)
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add Services/Changelog.cs
git commit -m "docs(changelog): note optional update notifications"
```

---

## Self-Review

**Spec coverage:**
- version.json source → Task 1. ✓
- UpdateService (parse/compare/resolve) → Task 2; throttle/fetch → Task 3. ✓
- Opt-in gate + first-run dialog → Task 5 (`EnsureUpdateOptIn`). ✓
- Settings toggle → Task 6. ✓
- 24h throttle, LastUpdateCheck after every attempt → Task 3 + Task 5 (`CheckForUpdatesAsync`). ✓
- Distribution-aware URL (Store vs site) → Task 2 `ResolveUrl` + Task 5 `UpdateGet_Click`. ✓
- Branded overlay on startup, dismiss-per-version → Task 5. ✓
- Localization in all 9 locales → Task 4. ✓
- Changelog entry → Task 7. ✓
- Tests for IsNewer/parse/ResolveUrl/ShouldCheckNow → Tasks 2-3. ✓

**Type consistency:** `UpdateInfo(Version, SiteUrl, StoreUrl, Notes)`, `IsNewer(string,Version)`, `TryParse(string)→UpdateInfo?`, `ResolveUrl(UpdateInfo,bool)`, `ShouldCheckNow(bool,DateTime?,DateTime)`, `CheckAsync(string)`, `VersionJsonUrl`, `StoreSearchUrl`, `CheckInterval` — referenced identically across Tasks 2-6. ✓

**Manual verification (not unit-tested):** the network fetch, the opt-in dialog, both overlays, and the Settings toggle are verified by building and running the app — open it twice (first run shows opt-in; with checks enabled and a higher `version.json`, the overlay appears; "Later" suppresses re-prompt for that version; toggling Settings persists).
