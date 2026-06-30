using System.Collections.Generic;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class RecentFilesTests
    {
        [Fact]
        public void Add_PrependsNewPath()
        {
            var result = RecentFiles.Add(new List<string> { @"C:\a.pdf" }, @"C:\b.pdf");
            Assert.Equal(new[] { @"C:\b.pdf", @"C:\a.pdf" }, result);
        }

        [Fact]
        public void Add_DedupesCaseInsensitive_AndMovesToFront()
        {
            var result = RecentFiles.Add(new List<string> { @"C:\a.pdf", @"C:\b.pdf" }, @"C:\A.PDF");
            Assert.Equal(new[] { @"C:\A.PDF", @"C:\b.pdf" }, result);
        }

        [Fact]
        public void Add_CapsAtMax_DroppingOldest()
        {
            var current = new List<string>();
            for (int i = 0; i < 10; i++) current.Add($@"C:\f{i}.pdf");
            var result = RecentFiles.Add(current, @"C:\new.pdf", max: 10);
            Assert.Equal(10, result.Count);
            Assert.Equal(@"C:\new.pdf", result[0]);
            Assert.DoesNotContain(@"C:\f9.pdf", result); // oldest dropped
        }

        [Fact]
        public void Remove_IsCaseInsensitive()
        {
            var result = RecentFiles.Remove(new List<string> { @"C:\a.pdf", @"C:\b.pdf" }, @"C:\A.PDF");
            Assert.Equal(new[] { @"C:\b.pdf" }, result);
        }

        [Fact]
        public void Parse_DropsEmptyAndWhitespaceEntries()
        {
            var result = RecentFiles.Parse(@"C:\a.pdf||  |C:\b.pdf");
            Assert.Equal(new[] { @"C:\a.pdf", @"C:\b.pdf" }, result);
        }

        [Fact]
        public void Parse_NullOrEmpty_ReturnsEmpty()
        {
            Assert.Empty(RecentFiles.Parse(null));
            Assert.Empty(RecentFiles.Parse(""));
        }

        [Fact]
        public void Serialize_RoundTrips_WithSpacesAndUnicode()
        {
            var list = new List<string> { @"C:\My Docs\contrato señor.pdf", @"C:\文件.pdf" };
            var round = RecentFiles.Parse(RecentFiles.Serialize(list));
            Assert.Equal(list, round);
        }
    }
}
