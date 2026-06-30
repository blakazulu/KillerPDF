using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using UglyToad.PdfPig;

namespace Scalpel.E2E;

/// <summary>
/// End-to-end proof that an edit actually persists through Save: place a text annotation, save
/// in-place (Ctrl+S), then REOPEN the saved file from disk and verify the edit is present. Uses an
/// ASCII marker (rendered with bundled Geist) so PdfPig extraction is reliable — isolating "did the
/// edit persist" from the Hebrew search limitation. Save As shares this exact burn+save code path
/// (SaveAs_Click → DrawAnnotationsOnDocument → _doc.Save(newPath)); only the target path differs.
/// </summary>
public static class SaveVerifySuite
{
    private const string InPlaceMarker = "SAVECHECK7788";

    public static void Run(AppDriver driver, RunReport report, string openWithPath)
    {
        const string Suite = "save";

        // Isolate from prior suites: the fonts suite relaunches the app onto its own
        // fixtures (and leaves it open on missingfont-1p), so without a relaunch here we
        // would edit one file but assert another. Open a fresh private copy of the corpus
        // doc and both edit AND verify THAT file.
        string saveDoc = Path.Combine(Path.GetDirectoryName(openWithPath)!, "save-verify-inplace.pdf");
        try { File.Copy(openWithPath, saveDoc, overwrite: true); } catch { }
        driver.Relaunch(saveDoc);
        System.Threading.Thread.Sleep(1200);

        // Edit → in-place Save (Ctrl+S) → reopen the file → verify the edit persisted.
        PlaceTextAnnotation(driver, InPlaceMarker);
        driver.WithForeground(() =>
        {
            System.Threading.Thread.Sleep(200);
            using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
                Keyboard.Press(VirtualKeyShort.KEY_S);
        });
        System.Threading.Thread.Sleep(2200); // flush to disk
        report.Results.Add(AssertPdfContains(Suite, "inplace-save:persisted", saveDoc, InPlaceMarker));
    }

    // Edit-mode → Text tool → click canvas → type marker → commit (re-click the tool).
    private static void PlaceTextAnnotation(AppDriver driver, string text)
    {
        driver.EnsureSurface(Surface.EditMode); // relaunch re-applied the single-page baseline; just switch ribbon mode
        System.Threading.Thread.Sleep(200);
        driver.Click("ToolTextBtn");
        System.Threading.Thread.Sleep(200);
        driver.WithForeground(() =>
        {
            driver.PlaceAndSetAnnotationText(text);
            System.Threading.Thread.Sleep(200);
            driver.Click("ToolTextBtn"); // re-click commits the active TextBox
            System.Threading.Thread.Sleep(300);
        });
        System.Threading.Thread.Sleep(400);
    }

    private static ActionResult AssertPdfContains(string suite, string action, string pdfPath, string needle)
    {
        var noLogs = Array.Empty<LogEntry>();
        try
        {
            if (!File.Exists(pdfPath))
                return new ActionResult(suite, action, Outcome.Fail, $"PDF not found: {pdfPath}", noLogs);

            using var doc = PdfDocument.Open(pdfPath);
            var all = new System.Text.StringBuilder();
            foreach (var page in doc.GetPages()) all.Append(page.Text ?? "");
            // Normalize: PdfPig can interleave spaces between glyphs, so compare with whitespace removed.
            string flat = new string(all.ToString().Where(c => !char.IsWhiteSpace(c)).ToArray());
            bool found = flat.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
            string preview = flat.Length > 120 ? flat.Substring(0, 120) : flat;
            return new ActionResult(suite, action, found ? Outcome.Pass : Outcome.Fail,
                found ? null : $"'{needle}' not found in saved PDF (text-len={flat.Length}, preview='{preview}')",
                noLogs);
        }
        catch (Exception ex)
        {
            return new ActionResult(suite, action, Outcome.Fail, $"Exception reading PDF: {ex.Message}", noLogs);
        }
    }
}
