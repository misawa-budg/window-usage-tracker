namespace WinTracker.Shared.Analytics;

public readonly record struct SegmentLayout(
    double Width,
    string Tooltip,
    string ColorHex,
    bool IsNoData = false);

public readonly record struct TimelineRowLayout(
    string BucketLabel,
    string TotalLabel,
    IReadOnlyList<SegmentLayout> Segments);

public readonly record struct StateLaneLayout(
    string Label,
    string TotalLabel,
    IReadOnlyList<SegmentLayout> Segments);

public readonly record struct StateGroupLayout(
    string Label,
    string TotalLabel,
    IReadOnlyList<StateLaneLayout> Lanes);

public readonly record struct StackedEntryLayout(
    string Label,
    string ColorHex,
    string Tooltip);

public readonly record struct StackedColumnLayout(
    double Width,
    bool IsNoData,
    IReadOnlyList<StackedEntryLayout> Entries);

public readonly record struct StateStackRowLayout(
    string Label,
    string TotalLabel,
    IReadOnlyList<StackedColumnLayout> Columns);

public readonly record struct LegendItemLayout(
    string Label,
    string ColorHex);
