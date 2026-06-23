using System;
using System.IO;
using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    [Collection("FontResolver")] // global GlobalFontSettings state — no parallel runs
    public class FontEmbeddingTests
    {
        private static void EnsureResolver()
        {
            if (GlobalFontSettings.FontResolver is null)
                GlobalFontSettings.FontResolver = PdfFontResolver.Instance;
        }

        /// <summary>True if any font in the saved PDF has an embedded font program.</summary>
        public static bool HasEmbeddedFontProgram(string path)
        {
            using var doc = PdfReader.Open(path, PdfDocumentOpenMode.ReadOnly);
            foreach (var obj in doc.Internals.GetAllObjects())
            {
                if (obj is PdfSharpCore.Pdf.PdfDictionary dict &&
                    (dict.Elements.ContainsKey("/FontFile2") ||
                     dict.Elements.ContainsKey("/FontFile3") ||
                     dict.Elements.ContainsKey("/FontFile")))
                    return true;
            }
            return false;
        }

        [Fact]
        public void DrawingSystemFont_EmbedsIt()
        {
            EnsureResolver();
            string path = Path.Combine(Path.GetTempPath(), $"scalpel_embed_{Guid.NewGuid():N}.pdf");
            try
            {
                using (var doc = new PdfDocument())
                {
                    var page = doc.AddPage();
                    using var gfx = XGraphics.FromPdfPage(page);
                    gfx.DrawString("Embedding check 12345", new XFont("Arial", 14), XBrushes.Black,
                        new XPoint(50, 50));
                    doc.Save(path);
                }
                Assert.True(File.Exists(path));
                Assert.True(HasEmbeddedFontProgram(path), "saved PDF should embed the Arial font program");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
