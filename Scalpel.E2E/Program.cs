using Scalpel.E2E;

internal static class Program
{
    private static int Main(string[] args)
    {
        string suite = ArgVal(args, "--suite") ?? "all";
        string? appPath = ArgVal(args, "--app") ?? FindDefaultApp();
        string reportDir = ArgVal(args, "--report-dir") ?? Path.Combine(Directory.GetCurrentDirectory(), "e2e-reports");
        int seed = int.TryParse(ArgVal(args, "--seed"), out var s) ? s : 1234;
        string stamp = ArgVal(args, "--stamp") ?? "run"; // caller can pass a timestamp; scripts can't use DateTime.Now

        if (appPath == null || !File.Exists(appPath))
        {
            Console.Error.WriteLine("Scalpel.exe not found. Pass --app <path>.");
            return 2;
        }

        // 1. Fixtures.
        string corpusDir = Path.Combine(Path.GetTempPath(), "scalpel-e2e-corpus");
        var corpus = Corpus.Generate(corpusDir);
        string openWith = corpus.First(c => c.Key == "simple-1p").Path;

        // 2. Launch + locate this run's log (newest after launch).
        using var driver = AppDriver.Launch(appPath, openWith);
        System.Threading.Thread.Sleep(800); // let app.start + open.success flush
        string? logPath = LogReader.FindLatestLog(LogReader.DefaultLogDir());
        if (logPath == null)
        {
            Console.Error.WriteLine("No session log found — is logging enabled?");
            return 3;
        }
        var log = new LogReader(logPath);
        var runner = new ActionRunner(driver, log, openWith);
        var report = new RunReport();

        // 3. Run the requested suite(s).
        bool all = suite == "all";
        if (all || suite == "singles")  SinglesSuite.Run(driver, runner, report);
        if (all || suite == "journeys") JourneysSuite.Run(driver, runner, report);
        if (all || suite == "pairwise") PairwiseSuite.Run(driver, runner, report);
        if (all || suite == "monkey")   MonkeySuite.Run(driver, runner, report, seed);

        // 4. Report.
        var (md, json) = Reporter.Write(report, reportDir, stamp);
        Console.WriteLine(Reporter.ToMarkdown(report));
        Console.WriteLine($"\nReport written:\n  {md}\n  {json}");

        // 5. Exit code: fail the run on any failure or any uncatalogued control.
        return (report.Failed() == 0 && report.UntestedControls.Count == 0) ? 0 : 1;
    }

    private static string? ArgVal(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return (i >= 0 && i + 1 < args.Length) ? args[i + 1] : null;
    }

    private static string? FindDefaultApp()
    {
        // Newest published Scalpel.exe under bin/.
        try
        {
            var root = Directory.GetCurrentDirectory();
            return Directory.GetFiles(root, "Scalpel.exe", SearchOption.AllDirectories)
                .Where(p => p.Contains("publish") || p.Contains("Release") || p.Contains("Debug"))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch { return null; }
    }
}
