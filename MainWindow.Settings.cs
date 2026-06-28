using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.Win32;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Scalpel.Services;
using PdfPigDoc = UglyToad.PdfPig.PdfDocument;

namespace Scalpel
{
    public partial class MainWindow
    {
        // ============================================================
        // Settings panel
        // ============================================================

        private void SettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            // Sync radio buttons to current theme/accent before showing
            var curTheme  = ThemeManager.CurrentTheme;
            ThemeDarkRadio.IsChecked  = curTheme == Theme.Dark;
            ThemeLightRadio.IsChecked = curTheme == Theme.Light;
            ThemeHCRadio.IsChecked    = curTheme == Theme.HighContrast;

            var curAccent = ThemeManager.CurrentAccent;
            AccentAmberRadio.IsChecked = curAccent == Accent.Amber;
            AccentRedRadio.IsChecked   = curAccent == Accent.Red;
            AccentGreenRadio.IsChecked = curAccent == Accent.Green;
            AccentCyanRadio.IsChecked  = curAccent == Accent.Cyan;
            UpdateAccentRadioState();
            // Sync language radios
            var curLoc = Scalpel.Services.LocaleManager.Current;
            LangEnRadio.IsChecked   = curLoc == Scalpel.Services.Locale.EnUS;
            LangEsRadio.IsChecked   = curLoc == Scalpel.Services.Locale.Es;
            LangZhTWRadio.IsChecked = curLoc == Scalpel.Services.Locale.ZhTW;
            LangZhCNRadio.IsChecked = curLoc == Scalpel.Services.Locale.ZhCN;
            LangBnRadio.IsChecked   = curLoc == Scalpel.Services.Locale.Bn;
            LangTrRadio.IsChecked   = curLoc == Scalpel.Services.Locale.TrTR;
            LangHeRadio.IsChecked   = curLoc == Scalpel.Services.Locale.He;
            LangArRadio.IsChecked   = curLoc == Scalpel.Services.Locale.Ar;
            LangRuRadio.IsChecked   = curLoc == Scalpel.Services.Locale.Ru;
            // Sync without re-triggering the toggle handler (would log a spurious
            // logging.toggle and re-save the setting on every Settings open).
            _suppressLogToggleEvent = true;
            LogEnabledCheck.IsChecked = Scalpel.Services.Logger.Enabled;
            _suppressLogToggleEvent = false;
            SyncUpdateToggle();
            SettingsOverlay.Visibility = Visibility.Visible;
        }

        private void SettingsOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => SettingsOverlay.Visibility = Visibility.Collapsed;

        private void SettingsOverlayCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => e.Handled = true;

        private void SettingsOverlayClose_Click(object sender, RoutedEventArgs e)
            => SettingsOverlay.Visibility = Visibility.Collapsed;

        private void LogEnabledCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressLogToggleEvent) return;
            bool on = LogEnabledCheck.IsChecked == true;
            Scalpel.Services.Logger.SetEnabled(on);
            App.SetSetting("LoggingEnabled", on ? "1" : "0");
            Scalpel.Services.Logger.Info("Settings", "logging.toggle", on ? "enabled" : "disabled");
        }

        private void OpenLogsBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Scalpel.Services.Logger.LogDirectory);
                System.Diagnostics.Process.Start("explorer.exe", Scalpel.Services.Logger.LogDirectory);
            }
            catch { }
        }

        private void ClearLogsBtn_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(this,
                "Delete all log files except the current session?",
                "Clear logs", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
                Scalpel.Services.Logger.ClearLogs();
        }

        private void OnThemeChanged()
        {
            // Refresh snapshot FindResource calls that were set as local values.
            // SetResourceReference bindings update automatically; sidebar tabs and
            // active tool button background still need an explicit refresh.
            SetTool(_currentTool);
            if (_sidebarShowingOutlines)
                SwitchSidebarToOutlinesTab();
            else
                SwitchSidebarToPagesTab();
            RefreshSelectionAccent();
        }

        private void ThemeDarkRadio_Checked(object sender, RoutedEventArgs e)
        {
            ThemeManager.ApplyTheme(Theme.Dark);
            UpdateAccentRadioState();
        }

        private void ThemeLightRadio_Checked(object sender, RoutedEventArgs e)
        {
            ThemeManager.ApplyTheme(Theme.Light);
            UpdateAccentRadioState();
        }

        private void ThemeHCRadio_Checked(object sender, RoutedEventArgs e)
        {
            ThemeManager.ApplyTheme(Theme.HighContrast);
            UpdateAccentRadioState();
        }

        private void AccentAmberRadio_Checked(object sender, RoutedEventArgs e)
            => ThemeManager.ApplyAccent(Accent.Amber);

        private void AccentRedRadio_Checked(object sender, RoutedEventArgs e)
            => ThemeManager.ApplyAccent(Accent.Red);

        private void AccentGreenRadio_Checked(object sender, RoutedEventArgs e)
            => ThemeManager.ApplyAccent(Accent.Green);

        private void AccentCyanRadio_Checked(object sender, RoutedEventArgs e)
            => ThemeManager.ApplyAccent(Accent.Cyan);

        // High Contrast owns its accent; the accent picker is inert while it is active.
        private void UpdateAccentRadioState()
        {
            bool enabled = ThemeManager.CurrentTheme != Theme.HighContrast;
            AccentAmberRadio.IsEnabled = enabled;
            AccentRedRadio.IsEnabled   = enabled;
            AccentGreenRadio.IsEnabled = enabled;
            AccentCyanRadio.IsEnabled  = enabled;
        }

        private void LangEnRadio_Checked(object sender, RoutedEventArgs e)
            => Scalpel.Services.LocaleManager.Apply(Scalpel.Services.Locale.EnUS);

        private void LangEsRadio_Checked(object sender, RoutedEventArgs e)
            => Scalpel.Services.LocaleManager.Apply(Scalpel.Services.Locale.Es);

        private void LangZhTWRadio_Checked(object sender, RoutedEventArgs e)
            => Scalpel.Services.LocaleManager.Apply(Scalpel.Services.Locale.ZhTW);

        private void LangZhCNRadio_Checked(object sender, RoutedEventArgs e)
            => Scalpel.Services.LocaleManager.Apply(Scalpel.Services.Locale.ZhCN);

        private void LangBnRadio_Checked(object sender, RoutedEventArgs e)
            => Scalpel.Services.LocaleManager.Apply(Scalpel.Services.Locale.Bn);

        private void LangTrRadio_Checked(object sender, RoutedEventArgs e)
            => Scalpel.Services.LocaleManager.Apply(Scalpel.Services.Locale.TrTR);

        private void LangHeRadio_Checked(object sender, RoutedEventArgs e)
            => Scalpel.Services.LocaleManager.Apply(Scalpel.Services.Locale.He);

        private void LangArRadio_Checked(object sender, RoutedEventArgs e)
            => Scalpel.Services.LocaleManager.Apply(Scalpel.Services.Locale.Ar);

        private void LangRuRadio_Checked(object sender, RoutedEventArgs e)
            => Scalpel.Services.LocaleManager.Apply(Scalpel.Services.Locale.Ru);

        private void ViewSingle_Click(object sender, RoutedEventArgs e)     { SetViewMode(ViewMode.Single);     UpdateViewModeButtons(); }
        private void ViewContinuous_Click(object sender, RoutedEventArgs e) { SetViewMode(ViewMode.Continuous); UpdateViewModeButtons(); }
        private void ViewTwoPage_Click(object sender, RoutedEventArgs e)    { SetViewMode(ViewMode.TwoPage);    UpdateViewModeButtons(); }
        private void ViewGrid_Click(object sender, RoutedEventArgs e)       { SetViewMode(ViewMode.Grid);       UpdateViewModeButtons(); }
        private void ViewFit_Click(object sender, RoutedEventArgs e)        => FitToWidth();

        private void UpdateViewModeButtons()
        {
            ViewSingleBtn.IsChecked     = _viewMode == ViewMode.Single;
            ViewContinuousBtn.IsChecked = _viewMode == ViewMode.Continuous;
            ViewTwoPageBtn.IsChecked    = _viewMode == ViewMode.TwoPage;
            ViewGridBtn.IsChecked       = _viewMode == ViewMode.Grid;
        }

        private const int  WM_GETMINMAXINFO   = 0x0024;
        private const int  WM_DPICHANGED      = 0x02E0;
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
        private const uint SWP_NOZORDER       = 0x0004;
        private const uint SWP_NOACTIVATE     = 0x0010;

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_GETMINMAXINFO)
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            else if (msg == WM_DPICHANGED)
            {
                // Apply Windows' suggested rect so the window's apparent size is preserved
                // on the new monitor. handled stays false so WPF's HwndSource also processes
                // the message — updating its internal DPI scale and firing Window.DpiChanged.
                var r = Marshal.PtrToStructure<RECT>(lParam);
                SetWindowPos(hwnd, IntPtr.Zero, r.left, r.top,
                             r.right - r.left, r.bottom - r.top,
                             SWP_NOZORDER | SWP_NOACTIVATE);
                // Re-render at the new DPI. DispatcherPriority.Loaded fires after WPF has
                // finished its own DPI update, so VisualTreeHelper.GetDpi already reflects
                // the new scale factor when RenderPage calls it.
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                    (Action)(() =>
                    {
                        int idx = PageList.SelectedIndex;
                        if (idx >= 0) RenderPage(idx);
                    }));
            }
            else if (msg == WM_NCHITTEST && WindowState == WindowState.Normal)
            {
                int ht = WmNcHitTest(hwnd, lParam);
                if (ht != 0) { handled = true; return new IntPtr(ht); }
            }
            return IntPtr.Zero;
        }

        private int WmNcHitTest(IntPtr hwnd, IntPtr lParam)
        {
            // lParam is screen coords: lo-word = X, hi-word = Y.
            // Cast through short to preserve sign (handles negative coords on left/above primary monitor).
            long lp  = lParam.ToInt64();
            int  mx  = unchecked((short)(lp & 0xFFFF));
            int  my  = unchecked((short)((lp >> 16) & 0xFFFF));

            if (!GetWindowRect(hwnd, out RECT rc)) return 0;

            bool onLeft   = mx < rc.left   + ResizeBorder;
            bool onRight  = mx >= rc.right  - ResizeBorder;
            bool onTop    = my < rc.top    + ResizeBorder;
            bool onBottom = my >= rc.bottom - ResizeBorder;

            if (onTop    && onLeft)  return HTTOPLEFT;
            if (onTop    && onRight) return HTTOPRIGHT;
            if (onBottom && onLeft)  return HTBOTTOMLEFT;
            if (onBottom && onRight) return HTBOTTOMRIGHT;
            if (onLeft)              return HTLEFT;
            if (onRight)             return HTRIGHT;
            if (onTop)               return HTTOP;
            if (onBottom)            return HTBOTTOM;

            return 0;
        }

        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                GetMonitorInfo(monitor, ref info);
                RECT work = info.rcWork;
                RECT mon = info.rcMonitor;
                mmi.ptMaxPosition.x = Math.Abs(work.left - mon.left);
                mmi.ptMaxPosition.y = Math.Abs(work.top - mon.top);
                mmi.ptMaxSize.x = Math.Abs(work.right - work.left);
                mmi.ptMaxSize.y = Math.Abs(work.bottom - work.top);
                mmi.ptMaxTrackSize.x = mmi.ptMaxSize.x;
                mmi.ptMaxTrackSize.y = mmi.ptMaxSize.y;
                Marshal.StructureToPtr(mmi, lParam, true);
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int WM_NCHITTEST     = 0x0084;
        private const int HTCAPTION        = 2;
        private const int HTLEFT           = 10;
        private const int HTRIGHT          = 11;
        private const int HTTOP            = 12;
        private const int HTTOPLEFT        = 13;
        private const int HTTOPRIGHT       = 14;
        private const int HTBOTTOM         = 15;
        private const int HTBOTTOMLEFT     = 16;
        private const int HTBOTTOMRIGHT    = 17;
        private const int ResizeBorder     = 8;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        // ============================================================
        // Settings persistence (window size, zoom, last file)
        // ============================================================

        private void SaveWindowSettings()
        {
            try
            {
                App.SetSetting("WindowState", WindowState.ToString());
                if (WindowState == WindowState.Normal)
                {
                    App.SetSetting("WindowWidth",  ((int)ActualWidth).ToString());
                    App.SetSetting("WindowHeight", ((int)ActualHeight).ToString());
                    App.SetSetting("WindowTop",  ((int)Top).ToString());
                    App.SetSetting("WindowLeft", ((int)Left).ToString());
                }
                App.SetSetting("FitMode",   _fitMode.ToString());
                App.SetSetting("ZoomLevel", _zoomLevel.ToString(System.Globalization.CultureInfo.InvariantCulture));
                // Persist as "last file" only for real, durable documents — never a
                // transient temp/repaired copy (those get swept and would re-prompt).
                if (_currentFile is not null && !IsTransientPath(_currentFile))
                    App.SetSetting("LastFile", _currentFile);
            }
            catch { /* best-effort */ }
        }

        /// <summary>True if the path lives under a temp location (system %TEMP% or our
        /// own session temp dir) — such files are transient and must not be remembered
        /// as the last opened file.</summary>
        private static bool IsTransientPath(string path)
        {
            try
            {
                var full = System.IO.Path.GetFullPath(path);
                return full.StartsWith(System.IO.Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)
                    || full.StartsWith(App.TempDir, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private void RestoreWindowSettings()
        {
            try
            {
                if (int.TryParse(App.GetSetting("WindowWidth"),  out int w) &&
                    int.TryParse(App.GetSetting("WindowHeight"), out int h) && w > 200 && h > 200)
                {
                    Width  = w;
                    Height = h;
                }
                if (int.TryParse(App.GetSetting("WindowTop"),  out int savedTop) &&
                    int.TryParse(App.GetSetting("WindowLeft"), out int savedLeft))
                {
                    // Verify the saved position is visible on the virtual desktop
                    // (covers all monitors). Falls back to CenterScreen (XAML default)
                    // if the monitor it was on is no longer connected.
                    double vLeft   = SystemParameters.VirtualScreenLeft;
                    double vTop    = SystemParameters.VirtualScreenTop;
                    double vRight  = vLeft + SystemParameters.VirtualScreenWidth;
                    double vBottom = vTop  + SystemParameters.VirtualScreenHeight;
                    bool onScreen  = savedLeft + 100 < vRight  && savedLeft + Width  > vLeft
                                  && savedTop  + 50  < vBottom && savedTop  + Height > vTop;
                    if (onScreen)
                    {
                        Left = savedLeft;
                        Top  = savedTop;
                    }
                }
                if (Enum.TryParse<WindowState>(App.GetSetting("WindowState"), out var ws) &&
                    ws == WindowState.Maximized)
                {
                    WindowState = WindowState.Maximized;
                }
                if (double.TryParse(App.GetSetting("ZoomLevel"),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double z) && z > 0)
                {
                    _zoomLevel = Math.Max(ZoomMin, Math.Min(ZoomMax, z));
                }
                if (Enum.TryParse<FitMode>(App.GetSetting("FitMode"), out var fm))
                    _fitMode = fm;
            }
            catch { /* best-effort */ }
        }

    }
}
