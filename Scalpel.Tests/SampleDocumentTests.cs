using System.IO;
using Scalpel.Services;
using UglyToad.PdfPig;
using Xunit;

namespace Scalpel.Tests
{
    public class SampleDocumentTests
    {
        [Fact]
        public void Generate_WritesValidFourPagePdf()
        {
            string path = Path.Combine(Path.GetTempPath(), $"scalpel_sample_test_{System.Guid.NewGuid():N}.pdf");
            try
            {
                string returned = SampleDocument.Generate(path);

                Assert.Equal(path, returned);
                Assert.True(File.Exists(path));

                using var pdf = PdfDocument.Open(path);
                Assert.Equal(4, pdf.NumberOfPages);

                // Page 1 carries the title; page 3 carries the results heading — proves distinct,
                // non-blank pages were drawn.
                Assert.Contains("Quarterly", pdf.GetPage(1).Text);
                Assert.Contains("Results", pdf.GetPage(3).Text);
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }
    }
}
