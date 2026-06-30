namespace Scalpel.E2E;

public static class JourneysSuite
{
    public static void Run(AppDriver driver, ActionRunner runner, RunReport report)
    {
        // Journey 1: view-mode + zoom tour.
        report.Results.Add(runner.RunRaw("journeys", "view:single",
            () => driver.Click("ModeViewTab"), "ModeViewTab", "modeViewActive"));
        foreach (var v in new[] { "ViewContinuousBtn", "ViewTwoPageBtn", "ViewGridBtn", "ViewSingleBtn", "ViewFitBtn" })
            report.Results.Add(runner.RunRaw("journeys", $"view:{v}", () => driver.Click(v), v, null));
        report.Results.Add(runner.RunRaw("journeys", "zoom:in",
            () => driver.Click("ZoomInBtn"), "ZoomInBtn", "zoomIncreased"));

        // Journey 2: edit tools tour.
        report.Results.Add(runner.RunRaw("journeys", "mode:edit",
            () => driver.Click("ModeEditTab"), "ModeEditTab", "modeEditActive"));
        foreach (var t in new[] { "ToolSelectBtn", "ToolTextBtn", "ToolHighlightBtn", "ToolDrawBtn", "ToolCropBtn" })
            report.Results.Add(runner.RunRaw("journeys", $"tool:{t}", () => driver.Click(t), t, null));

        // Journey 3: sign mode.
        report.Results.Add(runner.RunRaw("journeys", "mode:sign",
            () => driver.Click("ModeSignTab"), "ModeSignTab", "modeSignActive"));

        // Journey 4: settings overlay round-trip (open + expand sections, toggle theme/accent).
        // EnsureSurface opens the overlay AND expands the collapsible accordion sections so the
        // radios are in the tree. Select a non-HC theme first, because High Contrast disables
        // the accent picker (a prior suite may have left the theme on HC).
        report.Results.Add(runner.RunRaw("journeys", "settings:open",
            () => driver.EnsureSurface(Surface.SettingsOverlay), "SettingsBtn", "settingsOverlayOpen"));
        report.Results.Add(runner.RunRaw("journeys", "settings:theme-light",
            () => driver.Click("ThemeLightRadio"), "ThemeLightRadio", null));
        report.Results.Add(runner.RunRaw("journeys", "settings:accent-red",
            () => driver.Click("AccentRedRadio"), "AccentRedRadio", null));
        report.Results.Add(runner.RunRaw("journeys", "settings:accent-amber",
            () => driver.Click("AccentAmberRadio"), "AccentAmberRadio", null));

        // Journey 5: in-window overlays + Tools menu. These are excluded from the flat
        // singles scan (they raise overlays/popups that would cover later controls), so
        // exercise them here with an explicit open→dismiss pair. All Invoke-based, so no
        // physical-click foreground dependency. The close buttons only enter the UIA tree
        // once their overlay is visible, hence open-then-close ordering.
        report.Results.Add(runner.RunRaw("journeys", "whatsnew:open",
            () => driver.Click("ModeWhatsNewTab"), "ModeWhatsNewTab", null));
        report.Results.Add(runner.RunRaw("journeys", "whatsnew:close",
            () => driver.Click("WhatsNewOverlayCloseBtn"), "WhatsNewOverlayCloseBtn", null));
        report.Results.Add(runner.RunRaw("journeys", "about:open",
            () => driver.Click("ModeAboutTab"), "ModeAboutTab", null));
        report.Results.Add(runner.RunRaw("journeys", "about:close",
            () => driver.Click("AboutOverlayCloseBtn"), "AboutOverlayCloseBtn", null));
        report.Results.Add(runner.RunRaw("journeys", "tools:open",
            () => driver.Click("ToolsMenuBtn"), "ToolsMenuBtn", null));
        // The Tools button opens a context-menu popup; press Escape so it can't wedge the
        // next suite. No click is expected from the dismiss itself.
        report.Results.Add(runner.RunRaw("journeys", "tools:dismiss",
            () => driver.PressKey(FlaUI.Core.WindowsAPI.VirtualKeyShort.ESCAPE), null, null));
    }
}
