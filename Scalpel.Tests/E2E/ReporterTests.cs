using System.IO;
using Scalpel.E2E;
using Xunit;

namespace Scalpel.Tests.E2E;

public class ReporterTests
{
    private static RunReport Sample()
    {
        var r = new RunReport();
        r.Results.Add(new ActionResult("singles", "ZoomInBtn", Outcome.Pass, null, []));
        r.Results.Add(new ActionResult("singles", "ToolDrawBtn", Outcome.Fail,
            "expected click 'ToolDrawBtn' not logged",
            [LogEntry.Parse("{\"ts\":\"2026-06-21T05:11:44.007Z\",\"level\":\"INFO\",\"cat\":\"UI\",\"event\":\"click\",\"msg\":\"ToolSelectBtn\"}")!]));
        r.UntestedControls.Add("MysteryButton");
        return r;
    }

    [Fact]
    public void ToMarkdown_ContainsSummaryFailureAndUntested()
    {
        var md = Reporter.ToMarkdown(Sample());
        Assert.Contains("singles", md);
        Assert.Contains("ToolDrawBtn", md);
        Assert.Contains("expected click 'ToolDrawBtn' not logged", md);
        Assert.Contains("ToolSelectBtn", md);          // the log-context line
        Assert.Contains("MysteryButton", md);          // untested control
    }

    [Fact]
    public void ToJson_IsParseableAndHasCounts()
    {
        var json = Reporter.ToJson(Sample());
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(2, doc.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("failed").GetInt32());
    }

    [Fact]
    public void Write_ProducesBothFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"rep-{Guid.NewGuid():N}");
        var (md, json) = Reporter.Write(Sample(), dir, "20260621-090000");
        Assert.True(File.Exists(md));
        Assert.True(File.Exists(json));
        Directory.Delete(dir, true);
    }
}
