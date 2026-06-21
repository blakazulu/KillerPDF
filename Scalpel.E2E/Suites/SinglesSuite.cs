using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace Scalpel.E2E;

public static class SinglesSuite
{
    public static void Run(AppDriver driver, ActionRunner runner, RunReport report)
    {
        // Exercise every catalogued control once.
        foreach (var spec in Catalog.All)
            report.Results.Add(runner.RunControl("singles", spec));

        // Coverage cross-check: any button in the live tree not in the catalog.
        try
        {
            var buttons = driver.MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Button));
            foreach (var b in buttons)
            {
                string id = b.Properties.AutomationId.ValueOrDefault ?? "";
                if (string.IsNullOrEmpty(id)) continue;
                if (!Catalog.KnownIds.Contains(id) && !report.UntestedControls.Contains(id))
                    report.UntestedControls.Add(id);
            }
        }
        catch { }
    }
}
