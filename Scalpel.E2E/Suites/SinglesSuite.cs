using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace Scalpel.E2E;

public static class SinglesSuite
{
    // Buttons in the live tree that are deliberately NOT auto-clicked:
    // window-chrome buttons (clicking Close kills the app) and InstallBtn
    // (would self-install the portable app). They are excluded from the
    // "untested control" coverage gap rather than reported as a miss.
    //
    // The second group opens content that is incompatible with the flat,
    // single-pass singles scan: ToolsMenuBtn opens a context-menu popup;
    // ModeAboutTab/ModeWhatsNewTab raise *in-window* overlays (DismissModals,
    // which only closes separate top-level windows, cannot dismiss them, so
    // they would cover every control clicked after them). Those three are
    // instead exercised with explicit open→dismiss pairs in JourneysSuite
    // (Journey 5). The three Settings section-header toggles collapse the very
    // theme/accent/language groups whose radios the harness needs visible, so
    // they stay excluded entirely — same rationale as LogEnabledCheck/
    // ClearLogsBtn sabotaging the harness's own observability.
    private static readonly HashSet<string> ExcludedFromCoverage =
        ["Close", "Minimize", "Maximize", "Restore", "SystemMenuBar", "InstallBtn",
         "LogEnabledCheck", "ClearLogsBtn", "OpenMenuBtn",
         "ToolsMenuBtn", "ModeAboutTab", "ModeWhatsNewTab",
         "ThemeHeaderToggle", "AccentHeaderToggle", "LangHeaderToggle"];

    public static void Run(AppDriver driver, ActionRunner runner, RunReport report)
    {
        // Exercise every catalogued control once.
        foreach (var spec in Catalog.All)
            report.Results.Add(runner.RunControl("singles", spec));

        // Coverage cross-check: any button in the live tree not in the catalog
        // (and not a deliberately-excluded chrome/install button).
        try
        {
            var buttons = driver.MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Button));
            foreach (var b in buttons)
            {
                string id = b.Properties.AutomationId.ValueOrDefault ?? "";
                if (string.IsNullOrEmpty(id)) continue;
                if (ExcludedFromCoverage.Contains(id)) continue;
                if (!Catalog.KnownIds.Contains(id) && !report.UntestedControls.Contains(id))
                    report.UntestedControls.Add(id);
            }
        }
        catch { }
    }
}
