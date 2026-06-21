using PdfSharpCore.Drawing;
using PdfSharpCore.Drawing.Layout;
using PdfSharpCore.Pdf;

namespace Scalpel.Services
{
    /// <summary>
    /// Generates a believable multi-page sample PDF for the screenshot harness, so captured
    /// shots show a realistic document rather than a lorem-ipsum stub. Windows-only (relies on
    /// PdfSharpCore's built-in font resolver). Not shipped behavior — used by the dev /shoot path.
    /// </summary>
    public static class SampleDocument
    {
        public static string Generate(string path)
        {
            using var doc = new PdfDocument();

            var titleFont   = new XFont("Arial", 26, XFontStyle.Bold);
            var headingFont = new XFont("Arial", 15, XFontStyle.Bold);
            var bodyFont    = new XFont("Arial", 11, XFontStyle.Regular);
            var header      = new XSolidBrush(XColor.FromArgb(255, 30, 41, 59));   // slate header bar
            var ink         = XBrushes.Black;

            string body =
                "This document is a sample used to demonstrate the application. It contains " +
                "multiple paragraphs of body text, headings, and a small table so the page looks " +
                "like a real document a user would view, annotate, and sign. The layout wraps " +
                "across the width of the page and continues onto the following pages.";

            // ── Page 1: header bar + title + table ──────────────────────────────
            {
                var page = doc.AddPage();
                using var gfx = XGraphics.FromPdfPage(page);
                var tf = new XTextFormatter(gfx);

                gfx.DrawRectangle(header, new XRect(0, 0, page.Width, 70));
                gfx.DrawString("Quarterly Report", titleFont, XBrushes.White, new XPoint(40, 46));

                double y = 100;
                gfx.DrawString("Overview", headingFont, ink, new XPoint(40, y));
                y += 14;
                tf.DrawString(body, bodyFont, ink, new XRect(40, y, page.Width - 80, 120));
                y += 130;

                gfx.DrawString("Summary", headingFont, ink, new XPoint(40, y));
                y += 20;
                var pen = new XPen(XColors.Gray, 0.75);
                string[,] cells =
                {
                    { "Region",  "Result", "Change" },
                    { "North",   "1,204",  "+8%" },
                    { "South",   "  986",  "+3%" },
                    { "West",    "1,540",  "+12%" },
                };
                double colW = (page.Width - 80) / 3, rowH = 24;
                for (int r = 0; r < 4; r++)
                    for (int c = 0; c < 3; c++)
                    {
                        var cell = new XRect(40 + c * colW, y + r * rowH, colW, rowH);
                        gfx.DrawRectangle(pen, cell);
                        var f = r == 0 ? headingFont : bodyFont;
                        tf.DrawString(cells[r, c], f, ink,
                            new XRect(cell.X + 6, cell.Y + 5, cell.Width - 12, cell.Height));
                    }
            }

            // ── Pages 2-3: heading + wrapped body ───────────────────────────────
            for (int p = 2; p <= 3; p++)
            {
                var page = doc.AddPage();
                using var gfx = XGraphics.FromPdfPage(page);
                var tf = new XTextFormatter(gfx);
                gfx.DrawString($"Section {p}", headingFont, ink, new XPoint(40, 60));
                tf.DrawString(body + " " + body, bodyFont, ink,
                    new XRect(40, 80, page.Width - 80, page.Height - 140));
            }

            doc.Save(path);
            return path;
        }
    }
}
