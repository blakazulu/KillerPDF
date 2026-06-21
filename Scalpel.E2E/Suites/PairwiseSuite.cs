namespace Scalpel.E2E;

public static class PairwiseSuite
{
    public static void Run(AppDriver driver, ActionRunner runner, RunReport report)
    {
        // All ordered pairs of Edit tools (state-leak detection between tools).
        var editTools = Catalog.All.Where(c => c.Surface == Surface.EditMode).ToList();
        driver.EnsureSurface(Surface.EditMode);
        foreach (var a in editTools)
            foreach (var b in editTools)
            {
                if (a.AutomationId == b.AutomationId) continue;
                string label = $"{a.AutomationId}->{b.AutomationId}";
                report.Results.Add(runner.RunRaw("pairwise", label, () =>
                {
                    driver.Click(a.AutomationId);
                    driver.Click(b.AutomationId);
                }, b.AutomationId, null));
            }
    }
}
