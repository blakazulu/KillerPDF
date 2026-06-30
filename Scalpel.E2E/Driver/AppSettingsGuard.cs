using Microsoft.Win32;

namespace Scalpel.E2E;

/// <summary>
/// The suites click theme / accent / language / view-mode controls, which the app PERSISTS to
/// <c>HKCU\Software\Scalpel\Settings</c>. This guard snapshots those values, writes a deterministic
/// baseline for the duration of the run, and restores the user's original values on dispose. Two
/// reasons: (1) running the E2E must never silently change the user's real Scalpel preferences;
/// (2) a value left by a prior run — e.g. <c>ViewMode=Grid</c> or an RTL <c>Locale</c> — must not
/// make the next run start from a different state.
/// </summary>
public sealed class AppSettingsGuard : IDisposable
{
    private const string KeyPath = @"Software\Scalpel\Settings";
    private static readonly string[] Managed = { "Theme", "Accent", "Locale", "ViewMode", "ZoomLevel" };
    private readonly Dictionary<string, object?> _saved = new();

    public static AppSettingsGuard SnapshotAndBaseline()
    {
        var guard = new AppSettingsGuard();
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            foreach (var name in Managed) guard._saved[name] = key.GetValue(name);
        }
        catch { }
        WriteBaseline();
        return guard;
    }

    /// <summary>
    /// Write the deterministic baseline (single-page, non-RTL English, non-HighContrast) to the
    /// registry. Suites mutate these by clicking theme/accent/language/view controls, and the app
    /// reads them at startup — so this MUST be re-applied before every app relaunch, or a relaunch
    /// inherits the contaminated state (e.g. an RTL locale that breaks canvas annotation placement).
    /// </summary>
    public static void WriteBaseline()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key.SetValue("Theme", "Dark");
            key.SetValue("Accent", "Amber");
            key.SetValue("Locale", "EnUS");
            key.SetValue("ViewMode", "Single");
            key.SetValue("ZoomLevel", "1"); // 100% — a prior suite's fractional zoom can leave the page un-rendered where canvas placement expects it
        }
        catch { }
    }

    public void Dispose()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            foreach (var name in Managed)
            {
                if (_saved.TryGetValue(name, out var v) && v != null) key.SetValue(name, v);
                else key.DeleteValue(name, throwOnMissingValue: false);
            }
        }
        catch { }
    }
}
