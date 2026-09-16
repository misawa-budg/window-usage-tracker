using System.Xml.Linq;
using Xunit;

namespace WinTracker.Viewer.Tests;

// These are markup contracts, not substitutes for rendered WinUI checks.
public sealed class TimelineMarkupTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static XDocument Load() => XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Ui", "MainWindow.xaml"));

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
        Assert.Equal(new[] { "0", "1", "2", "3", "3" }, ticks.Select(x => (string?)x.Attribute("Grid.Column")));
        Assert.Equal(new[] { "00:00", "06:00", "12:00", "18:00", "24:00" }, ticks.Select(x => (string?)x.Attribute("Text")));
        Assert.All(ticks, x => Assert.Null(x.Attribute("Margin")));
        Assert.Equal("Right", (string?)ticks[^1].Attribute("HorizontalAlignment"));
    }
}
