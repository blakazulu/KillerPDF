namespace Scalpel.E2E;

public static class MonkeySuite
{
    public static void Run(AppDriver driver, ActionRunner runner, RunReport report, int seed)
    {
        var rng = new Random(seed);
        var ids = Catalog.KnownIds.ToList();
        var sizes = new (int w, int h)[] { (800, 600), (1280, 800), (1920, 1080), (640, 480) };

        const int actions = 120;
        for (int i = 0; i < actions; i++)
        {
            int roll = rng.Next(0, 10);
            if (roll == 0)
            {
                var (w, h) = sizes[rng.Next(sizes.Length)];
                report.Results.Add(runner.RunRaw("monkey", $"resize:{w}x{h}",
                    () => driver.Resize(w, h), null, null));
            }
            else
            {
                string id = ids[rng.Next(ids.Count)];
                var spec = Catalog.Find(id)!;
                report.Results.Add(runner.RunControl("monkey", spec));
            }
            driver.DismissModals(); // keep the run from wedging on a stray dialog
        }
    }
}
