using System.Xml.Linq;
using Xunit;

namespace WinTracker.Viewer.Tests;

// These are markup contracts, not substitutes for rendered WinUI checks.
public sealed class TimelineMarkupTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static XDocument Load() => XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Ui", "MainWindow.xaml"));

    [Fact]
    public void SectionsGroupChartsWithoutRestoringIndividualRowCards()
    {
        var doc = Load();
        Assert.Equal("Dark", (string?)doc.Root!.Elements().Single().Attribute("RequestedTheme"));
        Assert.Equal(2, doc.Descendants().Count(x => (string?)x.Attribute("Style") == "{StaticResource TimelineSection}"));
        var rowStyle = doc.Descendants().Single(x => (string?)x.Attribute(Xaml + "Key") == "TimelineRow");
        Assert.DoesNotContain(rowStyle.Elements(), x => (string?)x.Attribute("Property") is "Background" or "CornerRadius");
    }

    [Fact]
    public void TimeGuidesStayBehindDataAndDoNotInterceptInput()
    {
        var doc = Load();
        var template = doc.Descendants().Single(x => (string?)x.Attribute(Xaml + "Key") == "TimeGuidesTemplate");
        Assert.Equal(new[] { "1", "2", "3" }, template.Descendants().Where(x => x.Name.LocalName == "Border")
            .Select(x => (string?)x.Attribute("Grid.Column")));
        var guides = doc.Descendants().Where(x => (string?)x.Attribute("ContentTemplate") == "{StaticResource TimeGuidesTemplate}").ToArray();
        Assert.Equal(2, guides.Length);
        Assert.All(guides, x =>
        {
            Assert.Equal("False", (string?)x.Attribute("IsHitTestVisible"));
            Assert.Equal(x, x.Parent!.Elements().First());
            Assert.Null(x.ElementsAfterSelf().Single().Attribute("Background"));
        });
    }

    [Theory]
    [InlineData("RangeComboBox", "24h", "1week")]
    [InlineData("AppDisplayModeComboBox", "Running", "StateDetails")]
    public void SelectionKeepsStableTagsIndependentOfDisplayText(string name, string first, string second)
    {
        XElement control = Load().Descendants().Single(x => (string?)x.Attribute(Xaml + "Name") == name);
        Assert.Equal(new[] { first, second }, control.Elements().Select(x => (string?)x.Attribute("Tag")));
        Assert.All(control.Elements(), x => Assert.NotEqual((string?)x.Attribute("Tag"), (string?)x.Attribute("Content")));
    }

    [Fact]
    public void AxesAndBothRowTypesShareColumnWidths()
    {
        var definitions = Load().Descendants().Where(x => x.Name.LocalName == "Grid.ColumnDefinitions")
            .Select(x => x.Elements().Select(c => (string?)c.Attribute("Width")).ToArray())
            .Where(x => x.Length == 3).ToArray();
        Assert.Equal(4, definitions.Length);
        Assert.All(definitions, x => Assert.Equal(new[] { "140", "*", "104" }, x));
    }

    [Fact]
    public void SharedTimeAxisUsesQuarterDayBoundariesWithoutPixelOffsets()
    {
        XElement template = Load().Descendants().Single(x => (string?)x.Attribute(Xaml + "Key") == "TimeAxisTemplate");
        var columns = template.Descendants().Where(x => x.Name.LocalName == "ColumnDefinition").ToArray();
        Assert.Equal(4, columns.Length);
        Assert.All(columns, x => Assert.Equal("*", (string?)x.Attribute("Width")));
        var ticks = template.Descendants().Where(x => x.Name.LocalName == "TextBlock").ToArray();
        Assert.Equal(new[] { "0", "0", "1", "2", "3" }, ticks.Select(x => (string?)x.Attribute("Grid.Column")));
        Assert.Equal(new[] { "00:00", "06:00", "12:00", "18:00", "24:00" }, ticks.Select(x => (string?)x.Attribute("Text")));
        Assert.All(ticks, x => Assert.Null(x.Attribute("Margin")));
        Assert.Equal("Right", (string?)ticks[^1].Attribute("HorizontalAlignment"));
    }

    [Theory]
    [InlineData(375.5)]
    [InlineData(720)]
    [InlineData(1086)]
    public void InteriorTickCentersMatchTimeGuidesAtAnyTrackWidth(double trackWidth)
    {
        var doc = Load();
        var axis = doc.Descendants().Single(x => (string?)x.Attribute(Xaml + "Key") == "TimeAxisTemplate");
        var guides = doc.Descendants().Single(x => (string?)x.Attribute(Xaml + "Key") == "TimeGuidesTemplate");
        var ticks = axis.Descendants().Where(x => (string?)x.Attribute("Text") is "06:00" or "12:00" or "18:00").ToArray();
        var lines = guides.Descendants().Where(x => x.Name.LocalName == "Border").ToArray();
        Assert.Equal(3, ticks.Length);
        Assert.Equal(3, lines.Length);
        for (int i = 0; i < ticks.Length; i++)
        {
            Assert.Equal("Center", (string?)ticks[i].Attribute("HorizontalAlignment"));
            Assert.Equal("2", (string?)ticks[i].Attribute("Grid.ColumnSpan"));
            double center = ((int)ticks[i].Attribute("Grid.Column")! + 1) * trackWidth / 4;
            double line = (int)lines[i].Attribute("Grid.Column")! * trackWidth / 4;
            Assert.Equal(line, center, precision: 6);
        }
        Assert.Equal(2, doc.Descendants().Count(x =>
            (string?)x.Attribute("ContentTemplate") == "{StaticResource TimeAxisTemplate}"));
    }
}
