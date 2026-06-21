using System.Linq;
using Scalpel.E2E;
using Xunit;

namespace Scalpel.Tests.E2E;

public class CatalogTests
{
    [Fact]
    public void All_ContainsCoreControlsWithCorrectSurfaces()
    {
        Assert.Contains(Catalog.All, c => c.AutomationId == "ZoomInBtn" && c.Surface == Surface.AlwaysVisible);
        Assert.Contains(Catalog.All, c => c.AutomationId == "ToolDrawBtn" && c.Surface == Surface.EditMode);
        Assert.Contains(Catalog.All, c => c.AutomationId == "ViewGridBtn" && c.Surface == Surface.ViewMode);
        Assert.Contains(Catalog.All, c => c.AutomationId == "AccentRedRadio" && c.Surface == Surface.SettingsOverlay);
    }

    [Fact]
    public void Find_ReturnsSpecOrNull()
    {
        Assert.NotNull(Catalog.Find("SettingsBtn"));
        Assert.Null(Catalog.Find("NoSuchControl"));
    }

    [Fact]
    public void KnownIds_AreUnique()
    {
        Assert.Equal(Catalog.KnownIds.Count, Catalog.KnownIds.Distinct().Count());
    }
}
