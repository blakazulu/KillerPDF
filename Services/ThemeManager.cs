using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Scalpel.Services
{
    internal static class ThemeManager
    {
        // ── P/Invoke ──────────────────────────────────────────────────────

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        // ── State ─────────────────────────────────────────────────────────

        private static Theme  _theme  = Theme.Dark;
        private static Accent _accent = Accent.Amber;

        public static Theme  CurrentTheme  => _theme;
        public static Accent CurrentAccent => _accent;

        /// <summary>Fired after the theme/accent dictionary has been updated.</summary>
        public static event Action? ThemeChanged;

        // ── Public API ───────────────────────────────────────────────────

        /// <summary>
        /// Call once at startup (before MainWindow is created) to restore the saved
        /// theme + accent, migrating legacy single-axis values. DWM title bar is
        /// applied later via ApplyDwm(hwnd) from SourceInitialized.
        /// </summary>
        public static void Initialize()
        {
            var (theme, accent) = ThemeMigration.Resolve(App.GetSetting("Theme"), App.GetSetting("Accent"));
            _theme  = theme;
            _accent = accent;
            // Normalize persisted values so later loads are clean (drops legacy names).
            App.SetSetting("Theme",  _theme.ToString());
            App.SetSetting("Accent", _accent.ToString());
            ApplyInternal(applyDwm: false);
        }

        /// <summary>Change the base theme, keep the current accent, persist, update DWM.</summary>
        public static void ApplyTheme(Theme theme)
        {
            _theme = theme;
            App.SetSetting("Theme", theme.ToString());
            ApplyInternal(applyDwm: true);
            ThemeChanged?.Invoke();
        }

        /// <summary>Change the accent, keep the current base theme, persist.</summary>
        public static void ApplyAccent(Accent accent)
        {
            _accent = accent;
            App.SetSetting("Accent", accent.ToString());
            ApplyInternal(applyDwm: false);
            ThemeChanged?.Invoke();
        }

        /// <summary>Called from Window.SourceInitialized to set the native title bar colour.</summary>
        public static void ApplyDwm(IntPtr hwnd)
        {
            SetDwm(hwnd, _theme != Theme.Light);
        }

        // ── Internal ─────────────────────────────────────────────────────

        private static void ApplyInternal(bool applyDwm)
        {
            LoadDict(_theme, _accent);

            if (applyDwm)
            {
                var win = Application.Current?.MainWindow;
                if (win != null)
                {
                    var hwnd = new WindowInteropHelper(win).Handle;
                    if (hwnd != IntPtr.Zero)
                        SetDwm(hwnd, _theme != Theme.Light);
                }
            }
        }

        private static Uri BaseUri(Theme theme) => theme switch
        {
            Theme.Light        => new Uri("pack://application:,,,/Themes/Light.xaml"),
            Theme.HighContrast => new Uri("pack://application:,,,/Themes/HighContrast.xaml"),
            _                  => new Uri("pack://application:,,,/Themes/Dark.xaml"),
        };

        // Accent overlay exists only for Dark/Light and only for non-Amber accents.
        // Amber is the base file's built-in default; High Contrast has a fixed accent.
        private static Uri? AccentUri(Theme theme, Accent accent)
        {
            if (theme == Theme.HighContrast || accent == Accent.Amber) return null;
            return new Uri($"pack://application:,,,/Themes/Accents/{theme}_{accent}.xaml");
        }

        private static void LoadDict(Theme theme, Accent accent)
        {
            var merged = Application.Current.Resources.MergedDictionaries;

            var baseDict = new ResourceDictionary { Source = BaseUri(theme) };

            // In-place per-key update of the theme slot [0]. Structural add/remove fires a
            // synchronous ResourcesChanged that can invoke FindResource() before the new dict
            // is in place (e.g. SwitchSidebarToPagesTab), causing ResourceReferenceKeyNotFoundException.
            if (merged.Count > 0)
            {
                var existing = merged[0];
                foreach (object key in baseDict.Keys)
                    existing[key] = baseDict[key];

                // Overlay the accent's keys on top (Dark/Light + non-Amber only).
                var accentUri = AccentUri(theme, accent);
                if (accentUri != null)
                {
                    var accentDict = new ResourceDictionary { Source = accentUri };
                    foreach (object key in accentDict.Keys)
                        existing[key] = accentDict[key];
                }
            }
            else
            {
                // First load before the slot exists: merge base, then accent overlay.
                merged.Add(baseDict);
                var accentUri = AccentUri(theme, accent);
                if (accentUri != null)
                    merged.Add(new ResourceDictionary { Source = accentUri });
            }

            // One SystemIdle pass to nudge elements whose effective value didn't auto-update
            // (e.g. ControlTemplate trigger bindings with TargetName that missed the per-key signal).
            Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.SystemIdle, (Action)RefreshIcons);
        }

        /// <summary>
        /// Call from MainWindow.ContentRendered to fix icon colours on initial load
        /// when the theme was restored from settings (no switch event fires).
        /// </summary>
        public static void RefreshIcons()
        {
            if (Application.Current == null) return;
            foreach (Window w in Application.Current.Windows)
                ForceRender(w);
        }

        private static void ForceRender(DependencyObject node)
        {
            if (node is System.Windows.Controls.Primitives.ToggleButton tb)
            {
                // ClearValue + InvalidateProperty forces style-setter DynamicResources to
                // re-resolve from the updated dictionary without firing Checked/Unchecked
                // event handlers (which would re-trigger Apply and cause an infinite loop).
                tb.ClearValue(Control.ForegroundProperty);
                tb.InvalidateProperty(Control.ForegroundProperty);
            }
            if (node is Control ctrl)
            {
                ctrl.InvalidateProperty(Control.ForegroundProperty);
                ctrl.InvalidateProperty(Control.BackgroundProperty);
                ctrl.InvalidateProperty(Control.BorderBrushProperty);
            }
            if (node is UIElement el) el.InvalidateVisual();
            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
                ForceRender(VisualTreeHelper.GetChild(node, i));
        }

        private static void SetDwm(IntPtr hwnd, bool dark)
        {
            try
            {
                int value = dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
            }
            catch { /* DWMWA not supported on older Windows builds */ }
        }
    }
}
