using PdfSharpCore.Drawing;
using PdfSharpCore.Drawing.Layout;
using PdfSharpCore.Pdf;

namespace Scalpel.Services
{
    /// <summary>
    /// Generates a believable 4-page sample PDF for the screenshot harness, so captured shots
    /// show a realistic document and each shot can display a visually distinct page. Windows-only
    /// (relies on PdfSharpCore's built-in font resolver). Not shipped behavior — used by /shoot.
    /// </summary>
    public static class SampleDocument
    {
        public static string Generate(string path)
        {
            using var doc = new PdfDocument();

            var titleFont   = new XFont("Arial", 26, XFontStyle.Bold);
            var headingFont = new XFont("Arial", 15, XFontStyle.Bold);
            var bodyFont    = new XFont("Arial", 11, XFontStyle.Regular);
            var slate       = new XSolidBrush(XColor.FromArgb(255, 30, 41, 59));
            var ink         = XBrushes.Black;

            string body =
                "This document is a sample used to demonstrate the application. It contains multiple " +
                "paragraphs of body text, headings, a table, a list, and a chart so the pages look " +
                "like a real document a user would view, annotate, and sign. The layout wraps across " +
                "the width of the page and continues onto the following pages.";

            // ── Page 1: header bar + title + summary table ──────────────────────
            {
                var page = doc.AddPage();
                using var gfx = XGraphics.FromPdfPage(page);
                var tf = new XTextFormatter(gfx);

                gfx.DrawRectangle(slate, new XRect(0, 0, page.Width, 70));
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
                    { "Region", "Result", "Change" },
                    { "North",  "1,204",  "+8%" },
                    { "South",  "  986",  "+3%" },
                    { "West",   "1,540",  "+12%" },
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

            // ── Page 2: Methodology heading + bulleted list + paragraph ─────────
            {
                var page = doc.AddPage();
                using var gfx = XGraphics.FromPdfPage(page);
                var tf = new XTextFormatter(gfx);

                gfx.DrawString("Methodology", headingFont, ink, new XPoint(40, 64));
                string[] bullets =
                [
                    "Collected regional figures across four zones.",
                    "Normalized results against the prior period.",
                    "Validated outliers with a second review pass.",
                    "Compiled the summary table shown on page one.",
                ];
                double by = 96;
                foreach (var b in bullets)
                {
                    gfx.DrawEllipse(slate, new XRect(44, by + 4, 5, 5));
                    tf.DrawString(b, bodyFont, ink, new XRect(60, by, page.Width - 100, 20));
                    by += 26;
                }
                tf.DrawString(body, bodyFont, ink, new XRect(40, by + 12, page.Width - 80, 240));
            }

            // ── Page 3: Results heading + bar chart + caption ───────────────────
            {
                var page = doc.AddPage();
                using var gfx = XGraphics.FromPdfPage(page);
                var tf = new XTextFormatter(gfx);

                gfx.DrawString("Results", headingFont, ink, new XPoint(40, 64));
                var bar = new XSolidBrush(XColor.FromArgb(255, 37, 99, 235));
                int[] heights = [120, 180, 90, 200, 150];
                double baseY = 360, barW = 44, gap = 26, x = 60;
                foreach (int h in heights)
                {
                    gfx.DrawRectangle(bar, new XRect(x, baseY - h, barW, h));
                    x += barW + gap;
                }
                gfx.DrawLine(new XPen(XColors.Gray, 1), 50, baseY, x, baseY);
                tf.DrawString("Figure 1. Regional results by zone.", bodyFont, ink,
                    new XRect(40, baseY + 18, page.Width - 80, 40));
            }

            // ── Page 4: Appendix heading + long text ────────────────────────────
            {
                var page = doc.AddPage();
                using var gfx = XGraphics.FromPdfPage(page);
                var tf = new XTextFormatter(gfx);
                gfx.DrawString("Appendix", headingFont, ink, new XPoint(40, 64));
                tf.DrawString(body + " " + body + " " + body, bodyFont, ink,
                    new XRect(40, 90, page.Width - 80, page.Height - 160));
            }

            doc.Save(path);
            return path;
        }
    }
}
