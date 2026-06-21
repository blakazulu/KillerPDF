using System.IO;
using Scalpel.Services;
using UglyToad.PdfPig;
using Xunit;

namespace Scalpel.Tests
{
    public class SampleDocumentTests
    {
        [Fact]
        public void Generate_WritesValidThreePagePdf()
        {
            string path = Path.Combine(Path.GetTempPath(), $"scalpel_sample_test_{System.Guid.NewGuid():N}.pdf");
            try
            {
                string returned = SampleDocument.Generate(path);

                Assert.Equal(path, returned);
                Assert.True(File.Exists(path));

                using var pdf = PdfDocument.Open(path);
                Assert.Equal(3, pdf.NumberOfPages);

                // Page 1 carries the title text, proving content (not a blank page) was drawn.
                string page1 = pdf.GetPage(1).Text;
                Assert.Contains("Quarterly", page1);
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }
    }
}
