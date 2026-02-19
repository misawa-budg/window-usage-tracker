using Microsoft.UI.Xaml.Media;

namespace WinTracker.Viewer;

public sealed class TimelineRowViewModel
{
    public TimelineRowViewModel(
        string bucketLabel,
        string totalLabel,
        IReadOnlyList<TimelineSegmentViewModel> segments)
    {
        BucketLabel = bucketLabel;
        TotalLabel = totalLabel;
        Segments = segments;
    }

    public string BucketLabel { get; }
    public string TotalLabel { get; }
    public IReadOnlyList<TimelineSegmentViewModel> Segments { get; }
}

public sealed class TimelineSegmentViewModel
{
    public TimelineSegmentViewModel(double width, Brush fill, string tooltip)
    {
        Width = width;
        Fill = fill;
        Tooltip = tooltip;
    }

    public double Width { get; }
    public Brush Fill { get; }
    public string Tooltip { get; }
}

public sealed class StateLaneViewModel
{
    public StateLaneViewModel(string label, string totalLabel, IReadOnlyList<AbsoluteSegmentViewModel> segments)
    {
        Label = label;
        TotalLabel = totalLabel;
        Segments = segments;
    }

    public string Label { get; }
    public string TotalLabel { get; }
    public IReadOnlyList<AbsoluteSegmentViewModel> Segments { get; }
}

public sealed class StateGroupViewModel
{
    public StateGroupViewModel(string label, string totalLabel, IReadOnlyList<StateLaneViewModel> lanes)
    {
        Label = label;
        TotalLabel = totalLabel;
        Lanes = lanes;
    }

    public string Label { get; }
    public string TotalLabel { get; }
    public IReadOnlyList<StateLaneViewModel> Lanes { get; }
}

public sealed class StateStackRowViewModel
{
    public StateStackRowViewModel(
        string label,
        string totalLabel,
        IReadOnlyList<StackedColumnViewModel> columns)
    {
        Label = label;
        TotalLabel = totalLabel;
        Columns = columns;
    }

    public string Label { get; }
    public string TotalLabel { get; }
    public IReadOnlyList<StackedColumnViewModel> Columns { get; }
}

public sealed class StackedColumnViewModel
{
    public StackedColumnViewModel(
        double width,
        bool isNoData,
        IReadOnlyList<StackedEntryViewModel> entries)
    {
        Width = width;
        IsNoData = isNoData;
        Entries = entries;
    }

    public double Width { get; }
    public bool IsNoData { get; }
    public IReadOnlyList<StackedEntryViewModel> Entries { get; }
}

public sealed class StackedEntryViewModel
{
    public StackedEntryViewModel(double height, Brush fill, string tooltip)
    {
        Height = height;
        Fill = fill;
        Tooltip = tooltip;
    }

    public double Height { get; }
    public Brush Fill { get; }
    public string Tooltip { get; }
}

public sealed class AbsoluteSegmentViewModel
{
    public AbsoluteSegmentViewModel(double width, Brush fill, string tooltip)
    {
        Width = width;
        Fill = fill;
        Tooltip = tooltip;
    }

    public double Width { get; }
    public Brush Fill { get; }
    public string Tooltip { get; }
}

public sealed class LegendItemViewModel
{
    public LegendItemViewModel(Brush fill, string label)
    {
        Fill = fill;
        Label = label;
    }

    public Brush Fill { get; }
    public string Label { get; }
}
