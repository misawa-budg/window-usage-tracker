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

public sealed class AbsoluteSegmentViewModel
{
    public AbsoluteSegmentViewModel(double left, double width, Brush fill, string tooltip)
    {
        Left = left;
        Width = width;
        Fill = fill;
        Tooltip = tooltip;
    }

    public double Left { get; }
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
