using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WinTracker.Shared.Analytics;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace WinTracker.Viewer;

// One visual per color, not one nested ItemsControl/Border/ToolTip per interval.
// Geometry retains the original fractional coordinates; no time buckets are discarded.
public sealed class TimelineTrack : Canvas
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(IReadOnlyList<StackedColumnViewModel>), typeof(TimelineTrack),
        new PropertyMetadata(null, (sender, _) => ((TimelineTrack)sender).Rebuild()));

    private readonly ToolTip _tooltip = new();
    private double[] _rightEdges = [];
    private int _keyboardColumn = -1;
    private int _keyboardEntry;

    public IReadOnlyList<StackedColumnViewModel>? Columns
    {
        get => (IReadOnlyList<StackedColumnViewModel>?)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public TimelineTrack()
    {
        Background = new SolidColorBrush(Colors.Transparent);
        UseLayoutRounding = false;
        IsTabStop = true;
        ToolTipService.SetToolTip(this, _tooltip);
        PointerMoved += OnPointerMoved;
        PointerExited += (_, _) => _tooltip.IsOpen = false;
        LostFocus += (_, _) => _tooltip.IsOpen = false;
        KeyDown += OnKeyDown;
        AutomationProperties.SetName(this, "タイムライン。左右キーで区間、上下キーで状態の詳細を確認できます。");
    }

    private void Rebuild()
    {
        Children.Clear();
        _tooltip.IsOpen = false;
        _keyboardColumn = -1;
        _keyboardEntry = 0;
        IReadOnlyList<StackedColumnViewModel> columns = Columns ?? [];
        _rightEdges = new double[columns.Count];
        var colors = new Dictionary<Color, GeometryGroup>();
        double x = 0;
        for (int i = 0; i < columns.Count; i++)
        {
            StackedColumnViewModel column = columns[i];
            double y = 2;
            foreach (StackedEntryViewModel entry in column.Entries)
            {
                if (!column.IsNoData && column.Width > 0 && entry.Fill is SolidColorBrush brush && brush.Color.A > 0)
                {
                    if (!colors.TryGetValue(brush.Color, out GeometryGroup? geometry))
                    {
                        geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
                        colors.Add(brush.Color, geometry);
                    }
                    geometry.Children.Add(new RectangleGeometry { Rect = new Rect(x, y, column.Width, entry.Height) });
                }
                y += entry.Height;
            }
            x += column.Width;
            _rightEdges[i] = x;
        }
        foreach ((Color color, GeometryGroup geometry) in colors)
            Children.Add(new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = geometry, Fill = new SolidColorBrush(color),
                IsHitTestVisible = false, UseLayoutRounding = false
            });
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        Point point = e.GetCurrentPoint(this).Position;
        int index = TrackHitTest.FindColumn(_rightEdges, point.X);
        if (Columns is null || index < 0 || point.Y < 2 || point.Y >= 48)
        {
            _tooltip.IsOpen = false;
            _tooltip.Content = null;
            return;
        }

        StackedColumnViewModel column = Columns[index];
        double bottom = 2;
        foreach (StackedEntryViewModel entry in column.Entries)
        {
            bottom += entry.Height;
            if (point.Y < bottom)
            {
                _tooltip.Content = entry.Tooltip;
                return;
            }
        }
        _tooltip.IsOpen = false;
        _tooltip.Content = null;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (Columns is not { Count: > 0 } columns) return;
        if (e.Key == VirtualKey.Escape) { _tooltip.IsOpen = false; return; }
        if (e.Key is not (VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down)) return;
        _keyboardColumn = Math.Clamp(_keyboardColumn + (e.Key == VirtualKey.Left ? -1 : e.Key == VirtualKey.Right ? 1 : 0), 0, columns.Count - 1);
        IReadOnlyList<StackedEntryViewModel> entries = columns[_keyboardColumn].Entries;
        _keyboardEntry = Math.Clamp(_keyboardEntry + (e.Key == VirtualKey.Up ? -1 : e.Key == VirtualKey.Down ? 1 : 0), 0, Math.Max(0, entries.Count - 1));
        string detail = entries.Count == 0 ? "記録なし" : entries[_keyboardEntry].Tooltip;
        _tooltip.Content = detail;
        AutomationProperties.SetName(this, detail);
        _tooltip.IsOpen = true;
        e.Handled = true;
    }
}
