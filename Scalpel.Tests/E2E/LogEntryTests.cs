using System.Text.Json;
using Scalpel.E2E;
using Xunit;

namespace Scalpel.Tests.E2E;

public class LogEntryTests
{
    [Fact]
    public void Parse_ValidLine_PopulatesFields()
    {
        var line = "{\"ts\":\"2026-06-21T05:11:44.007Z\",\"level\":\"INFO\",\"cat\":\"File\",\"event\":\"open.success\",\"msg\":\"PDF opened\",\"data\":{\"pages\":2}}";
        var e = LogEntry.Parse(line);
        Assert.NotNull(e);
        Assert.Equal("INFO", e!.Level);
        Assert.Equal("File", e.Cat);
        Assert.Equal("open.success", e.Event);
        Assert.Equal(2, e.Data!.Value.GetProperty("pages").GetInt32());
        Assert.False(e.IsFailure);
    }

    [Fact]
    public void Parse_BlankOrGarbage_ReturnsNull()
    {
        Assert.Null(LogEntry.Parse(""));
        Assert.Null(LogEntry.Parse("   "));
        Assert.Null(LogEntry.Parse("not json"));
    }

    [Theory]
    [InlineData("ERROR", "GetPageFormFields", true)]
    [InlineData("INFO", "crash.dispatcher", true)]
    [InlineData("INFO", "save.fail", true)]
    [InlineData("INFO", "click", false)]
    [InlineData("INFO", "open.success", false)]
    public void IsFailure_ClassifiesCorrectly(string level, string ev, bool expected)
    {
        var line = $"{{\"ts\":\"2026-06-21T05:11:44.007Z\",\"level\":\"{level}\",\"cat\":\"X\",\"event\":\"{ev}\",\"msg\":\"m\"}}";
        Assert.Equal(expected, LogEntry.Parse(line)!.IsFailure);
    }
}
