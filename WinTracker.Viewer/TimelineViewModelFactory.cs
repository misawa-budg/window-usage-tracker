using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinTracker.Shared.Analytics;

namespace WinTracker.Viewer;

// The only conversion from pure layout records to WinUI brushes and view models.
internal static class TimelineViewModelFactory
{
    private static readonly SolidColorBrush TransparentBrush = new(Windows.UI.Color.FromArgb(0, 0, 0, 0));

    public static StateStackRowViewModel FromOverview(StateStackRowLayout row) =>
        new(row.Label == "Active" ? "最前面" : row.Label, row.TotalLabel,
            row.Columns.Select(column => new StackedColumnViewModel(column.Width, column.IsNoData,
                ToStackedEntries(column))).ToList());

    public static StateLaneViewModel FromApp(StateLaneLayout row) =>
        new(AppChoice.FormatDisplayName(row.Label), row.TotalLabel, row.Segments.Select(ToAbsoluteSegmentViewModel).ToList());

    public static StateLaneViewModel FromDay(TimelineRowLayout row) =>
        new(row.BucketLabel, row.TotalLabel, row.Segments.Select(ToAbsoluteSegmentViewModel).ToList());

    public static LegendItemViewModel FromLegend(LegendItemLayout item) => new(CreateBrush(item.ColorHex), item.Label);

    private static IReadOnlyList<StackedEntryViewModel> ToStackedEntries(StackedColumnLayout column)
    {
        if (column.Entries.Count == 0)
        {
            return [];
        }

        double entryHeight = 46.0 / column.Entries.Count;
        return column.Entries.Select(entry =>
            new StackedEntryViewModel(
                entryHeight,
                CreateBrush(entry.ColorHex),
                entry.Tooltip))
            .ToList();
    }

    private static AbsoluteSegmentViewModel ToAbsoluteSegmentViewModel(SegmentLayout segment)
    {
        Brush fill = segment.IsNoData ? TransparentBrush : CreateBrush(segment.ColorHex);
        return new AbsoluteSegmentViewModel(segment.Width, fill, segment.Tooltip);
    }

    private static SolidColorBrush CreateBrush(string hexOrKey)
    {
        if (Application.Current.Resources.TryGetValue(hexOrKey, out object resource))
        {
            if (resource is SolidColorBrush brush)
            {
                return brush;
            }
            if (resource is Windows.UI.Color color)
            {
                return new SolidColorBrush(color);
            }
        }

        if (hexOrKey.Length != 7 || !hexOrKey.StartsWith("#", StringComparison.Ordinal))
        {
            return new SolidColorBrush(Colors.Gray);
        }

        try
        {
            byte r = Convert.ToByte(hexOrKey.Substring(1, 2), 16);
            byte g = Convert.ToByte(hexOrKey.Substring(3, 2), 16);
            byte b = Convert.ToByte(hexOrKey.Substring(5, 2), 16);
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
        }
        catch
        {
            return new SolidColorBrush(Colors.Gray);
        }
    }

}
