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

        // Journey 4: settings overlay round-trip (open, toggle theme, close).
        report.Results.Add(runner.RunRaw("journeys", "settings:open",
            () => driver.Click("SettingsBtn"), "SettingsBtn", "settingsOverlayOpen"));
        report.Results.Add(runner.RunRaw("journeys", "settings:accent-red",
            () => driver.Click("AccentRedRadio"), "AccentRedRadio", null));
        report.Results.Add(runner.RunRaw("journeys", "settings:accent-amber",
            () => driver.Click("AccentAmberRadio"), "AccentAmberRadio", null));
    }
}
