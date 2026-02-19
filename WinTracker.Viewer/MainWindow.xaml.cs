using System.Collections.ObjectModel;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using WinTracker.Shared.Analytics;
using Windows.Foundation;
using Windows.Graphics;

namespace WinTracker.Viewer;

public sealed partial class MainWindow : Window
{
    private const double BucketTrackWidth = 760.0;
    private const double DailyTrackWidth = 960.0;
    private const int TopAppCount = 8;
    private const string DefaultDatabasePath = "data/collector.db";
    private const int MinWindowWidth = 1100;
    private const int MinWindowHeight = 700;
    private const int CompactTickSwitchWidth = 1360;
    private const double TickOffsetBase = 100.0;
    private const double TickOffsetMax = 100.0;
    private static readonly SolidColorBrush RefreshButtonNormalBrush = CreateBrush("#BFA8FF");
    private static readonly SolidColorBrush RefreshButtonHoverBrush = CreateBrush("#A892F2");
    private static readonly SolidColorBrush RefreshButtonPressedBrush = CreateBrush("#967EE8");
    private static readonly SolidColorBrush TransparentBrush =
        new(Windows.UI.Color.FromArgb(0, 0, 0, 0));

    private readonly ObservableCollection<TimelineRowViewModel> _overviewRows = [];
    private readonly ObservableCollection<StateLaneViewModel> _overviewDailyLanes = [];
    private readonly ObservableCollection<StateLaneViewModel> _appDailyLanes = [];
    private readonly ObservableCollection<TimelineRowViewModel> _appRows = [];
    private readonly ObservableCollection<string> _appNames = [];
    private readonly ObservableCollection<LegendItemViewModel> _overviewLegendItems = [];
    private readonly ObservableCollection<LegendItemViewModel> _appLegendItems = [];
    private readonly TimelineLayoutBuilder _layoutBuilder = new(topAppCount: TopAppCount);

    private IReadOnlyList<TimelineUsageRow> _timelineRows = [];
    private UsageQueryWindow _currentWindow = CreateLocalDay24hWindow();
    private CancellationTokenSource? _reloadCts;
    private bool _isInitialized;
    private bool _isEnforcingMinSize;

    public MainWindow()
    {
        InitializeComponent();
        ConfigureWindowSizing();
        UpdateTimeTickLabels();

        OverviewListView.ItemsSource = _overviewRows;
        OverviewDailyListView.ItemsSource = _overviewDailyLanes;
        AppDailyListView.ItemsSource = _appDailyLanes;
        AppTimelineListView.ItemsSource = _appRows;
        AppComboBox.ItemsSource = _appNames;
        OverviewLegendItemsControl.ItemsSource = _overviewLegendItems;
        AppLegendItemsControl.ItemsSource = _appLegendItems;

        _isInitialized = true;
        _ = ReloadAsync();
    }

    private void ConfigureWindowSizing()
    {
        AppWindow.Changed += OnAppWindowChanged;
        EnforceMinimumWindowSize();
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        EnforceMinimumWindowSize();
        UpdateTimeTickLabels();
    }

    private void EnforceMinimumWindowSize()
    {
        if (_isEnforcingMinSize)
        {
            return;
        }

        SizeInt32 current = AppWindow.Size;
        int width = Math.Max(MinWindowWidth, current.Width);
        int height = Math.Max(MinWindowHeight, current.Height);
        if (width == current.Width && height == current.Height)
        {
            return;
        }

        _isEnforcingMinSize = true;
        AppWindow.Resize(new SizeInt32(width, height));
        _isEnforcingMinSize = false;
    }

    private void UpdateTimeTickLabels()
    {
        bool compact = AppWindow.Size.Width < CompactTickSwitchWidth;
        SetTickTexts(Tick00TextBlock, Tick06TextBlock, Tick12TextBlock, Tick18TextBlock, Tick24TextBlock, compact);
        SetTickTexts(AppTick00TextBlock, AppTick06TextBlock, AppTick12TextBlock, AppTick18TextBlock, AppTick24TextBlock, compact);
        SetTickTexts(WeekTick00TextBlock, WeekTick06TextBlock, WeekTick12TextBlock, WeekTick18TextBlock, WeekTick24TextBlock, compact);
        SetTickTexts(AppWeekTick00TextBlock, AppWeekTick06TextBlock, AppWeekTick12TextBlock, AppWeekTick18TextBlock, AppWeekTick24TextBlock, compact);

        UpdateTickOffsets();
    }

    private void UpdateTickOffsets()
    {
        double extra = Math.Max(0, AppWindow.Size.Width - MinWindowWidth);
        double offset = Math.Min(TickOffsetMax, TickOffsetBase + (extra / 120.0));
        SetTickOffsets(Tick06TextBlock, Tick18TextBlock, offset);
        SetTickOffsets(AppTick06TextBlock, AppTick18TextBlock, offset);
        SetTickOffsets(WeekTick06TextBlock, WeekTick18TextBlock, offset);
        SetTickOffsets(AppWeekTick06TextBlock, AppWeekTick18TextBlock, offset);
    }

    private static void SetTickTexts(
        TextBlock tick00,
        TextBlock tick06,
        TextBlock tick12,
        TextBlock tick18,
        TextBlock tick24,
        bool compact)
    {
        tick00.Text = compact ? "0" : "00:00";
        tick06.Text = compact ? "6" : "06:00";
        tick12.Text = compact ? "12" : "12:00";
        tick18.Text = compact ? "18" : "18:00";
        tick24.Text = compact ? "24" : "24:00";
    }

    private static void SetTickOffsets(TextBlock tick06, TextBlock tick18, double offset)
    {
        tick06.Margin = new Thickness(-offset, 0, 0, 0);
        tick18.Margin = new Thickness(offset, 0, 0, 0);
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        await ReloadAsync();
    }

    private async void OnRangeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized)
        {
            return;
        }

        await ReloadAsync();
    }

    private void OnAppSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RebuildAppLegend();

        if (GetRangeLabel() == "24h")
        {
            BuildAppDailyLanes();
        }
        else
        {
            BuildAppTimelineRows();
        }
    }

    private async Task ReloadAsync()
    {
        _reloadCts?.Cancel();
        _reloadCts = new CancellationTokenSource();
        CancellationToken token = _reloadCts.Token;

        try
        {
            SetBusy(true, "Loading...");

            _currentWindow = GetWindowFromSelection();
            string dbPath = ResolveDatabasePath();
            if (!File.Exists(dbPath))
            {
                ClearRows();
                StatusTextBlock.Text = "DB not found.";
                return;
            }

            _timelineRows = await Task.Run(() =>
            {
                using var query = new SqliteTimelineQueryService(dbPath);
                return query.QueryTimeline(_currentWindow);
            }, token);

            if (GetRangeLabel() == "24h")
            {
                SetOverviewMode(isDaily24h: true);
                BuildDailyOverviewLanes();
                SetAppMode(isDaily24h: true);
            }
            else
            {
                SetOverviewMode(isDaily24h: false);
                BuildOverviewRows();
                SetAppMode(isDaily24h: false);
            }

            RebuildAppNames();
            RebuildOverviewLegend();
            RebuildAppLegend();
            if (GetRangeLabel() == "24h")
            {
                BuildAppDailyLanes();
            }
            else
            {
                BuildAppTimelineRows();
            }

            StatusTextBlock.Text = $"Loaded: {GetRangeLabel()} / events={_timelineRows.Count}";
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "Canceled";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Error: {ex.Message}";
        }
        finally
        {
            SetBusy(false, StatusTextBlock.Text);
        }
    }

    private void SetOverviewMode(bool isDaily24h)
    {
        OverviewDailyPanel.Visibility = isDaily24h ? Visibility.Visible : Visibility.Collapsed;
        OverviewWeekHeader.Visibility = isDaily24h ? Visibility.Collapsed : Visibility.Visible;
        OverviewListView.Visibility = isDaily24h ? Visibility.Collapsed : Visibility.Visible;

        if (isDaily24h)
        {
            _overviewRows.Clear();
        }
        else
        {
            _overviewDailyLanes.Clear();
        }
    }

    private void SetAppMode(bool isDaily24h)
    {
        AppDailyPanel.Visibility = isDaily24h ? Visibility.Visible : Visibility.Collapsed;
        AppWeekHeader.Visibility = isDaily24h ? Visibility.Collapsed : Visibility.Visible;
        AppTimelineListView.Visibility = isDaily24h ? Visibility.Collapsed : Visibility.Visible;

        if (isDaily24h)
        {
            _appRows.Clear();
        }
        else
        {
            _appDailyLanes.Clear();
        }
    }

    private UsageQueryWindow GetWindowFromSelection() =>
        GetRangeLabel() == "1week"
            ? CreateLocalWeekWindow()
            : CreateLocalDay24hWindow();

    private string GetRangeLabel() =>
        (RangeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() == "1week" ? "1week" : "24h";

    private static UsageQueryWindow CreateLocalDay24hWindow()
    {
        DateTimeOffset nowLocal = DateTimeOffset.Now;
        DateTimeOffset dayStartLocal = new(nowLocal.Year, nowLocal.Month, nowLocal.Day, 0, 0, 0, nowLocal.Offset);
        DateTimeOffset dayEndLocal = dayStartLocal.AddDays(1);
        return new UsageQueryWindow(
            dayStartLocal.ToUniversalTime(),
            dayEndLocal.ToUniversalTime(),
            TimeSpan.FromHours(1));
    }

    private static UsageQueryWindow CreateLocalWeekWindow()
    {
        DateTimeOffset nowLocal = DateTimeOffset.Now;
        DateTimeOffset dayStartLocal = new(nowLocal.Year, nowLocal.Month, nowLocal.Day, 0, 0, 0, nowLocal.Offset);
        DateTimeOffset fromLocal = dayStartLocal.AddDays(-6);
        DateTimeOffset toLocal = dayStartLocal.AddDays(1);
        return new UsageQueryWindow(
            fromLocal.ToUniversalTime(),
            toLocal.ToUniversalTime(),
            TimeSpan.FromHours(1));
    }

    private static string ResolveDatabasePath()
    {
        string candidate = DefaultDatabasePath;
        return Path.IsPathRooted(candidate)
            ? candidate
            : Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, candidate));
    }

    private void BuildDailyOverviewLanes()
    {
        _overviewDailyLanes.Clear();
        IReadOnlyList<StateLaneLayout> lanes = _layoutBuilder.BuildDailyOverviewLanes(
            _timelineRows,
            _currentWindow,
            DailyTrackWidth);

        foreach (StateLaneLayout lane in lanes)
        {
            _overviewDailyLanes.Add(new StateLaneViewModel(
                lane.Label,
                lane.TotalLabel,
                lane.Segments.Select(ToAbsoluteSegmentViewModel).ToList()));
        }
    }

    private void RebuildAppNames()
    {
        string? current = AppComboBox.SelectedItem as string;
        IReadOnlyList<string> appNames = _layoutBuilder.BuildAppNames(_timelineRows);

        _appNames.Clear();
        foreach (string app in appNames)
        {
            _appNames.Add(app);
        }

        if (_appNames.Count == 0)
        {
            AppComboBox.SelectedItem = null;
            return;
        }

        if (current is not null && _appNames.Contains(current, StringComparer.OrdinalIgnoreCase))
        {
            AppComboBox.SelectedItem = _appNames.First(x => string.Equals(x, current, StringComparison.OrdinalIgnoreCase));
            return;
        }

        AppComboBox.SelectedIndex = 0;
    }

    private void RebuildOverviewLegend()
    {
        _overviewLegendItems.Clear();
        IReadOnlyList<LegendItemLayout> legendItems = _layoutBuilder.BuildOverviewLegend(_timelineRows);
        foreach (LegendItemLayout item in legendItems)
        {
            _overviewLegendItems.Add(new LegendItemViewModel(
                CreateBrush(item.ColorHex),
                item.Label));
        }
    }

    private void RebuildAppLegend()
    {
        _appLegendItems.Clear();
        IReadOnlyList<LegendItemLayout> legendItems = _layoutBuilder.BuildAppLegend();
        foreach (LegendItemLayout item in legendItems)
        {
            _appLegendItems.Add(new LegendItemViewModel(
                CreateBrush(item.ColorHex),
                item.Label));
        }
    }

    private void BuildOverviewRows()
    {
        _overviewRows.Clear();
        IReadOnlyList<TimelineRowLayout> rows = _layoutBuilder.BuildOverviewRows(
            _timelineRows,
            _currentWindow,
            BucketTrackWidth);

        foreach (TimelineRowLayout row in rows)
        {
            _overviewRows.Add(new TimelineRowViewModel(
                row.BucketLabel,
                row.TotalLabel,
                row.Segments.Select(ToTimelineSegmentViewModel).ToList()));
        }
    }

    private void BuildAppTimelineRows()
    {
        _appRows.Clear();
        string? app = AppComboBox.SelectedItem as string;

        if (string.IsNullOrWhiteSpace(app))
        {
            return;
        }
        IReadOnlyList<TimelineRowLayout> rows = _layoutBuilder.BuildAppTimelineRows(
            _timelineRows,
            _currentWindow,
            app,
            BucketTrackWidth);

        foreach (TimelineRowLayout row in rows)
        {
            _appRows.Add(new TimelineRowViewModel(
                row.BucketLabel,
                row.TotalLabel,
                row.Segments.Select(ToTimelineSegmentViewModel).ToList()));
        }
    }

    private void BuildAppDailyLanes()
    {
        _appDailyLanes.Clear();
        string? app = AppComboBox.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(app))
        {
            return;
        }
        IReadOnlyList<StateLaneLayout> lanes = _layoutBuilder.BuildAppDailyLanes(
            _timelineRows,
            _currentWindow,
            app,
            DailyTrackWidth);

        foreach (StateLaneLayout lane in lanes)
        {
            _appDailyLanes.Add(new StateLaneViewModel(
                lane.Label,
                lane.TotalLabel,
                lane.Segments.Select(ToAbsoluteSegmentViewModel).ToList()));
        }
    }

    private static TimelineSegmentViewModel ToTimelineSegmentViewModel(SegmentLayout segment)
    {
        Brush fill = segment.IsNoData ? TransparentBrush : CreateBrush(segment.ColorHex);
        return new TimelineSegmentViewModel(segment.Width, fill, segment.Tooltip);
    }

    private static AbsoluteSegmentViewModel ToAbsoluteSegmentViewModel(SegmentLayout segment)
    {
        Brush fill = segment.IsNoData ? TransparentBrush : CreateBrush(segment.ColorHex);
        return new AbsoluteSegmentViewModel(segment.Width, fill, segment.Tooltip);
    }

    private static SolidColorBrush CreateBrush(string hex)
    {
        if (hex.Length != 7 || !hex.StartsWith("#", StringComparison.Ordinal))
        {
            return new SolidColorBrush(Colors.Gray);
        }

        byte r = Convert.ToByte(hex.Substring(1, 2), 16);
        byte g = Convert.ToByte(hex.Substring(3, 2), 16);
        byte b = Convert.ToByte(hex.Substring(5, 2), 16);
        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
    }

    private void ClearRows()
    {
        _overviewRows.Clear();
        _overviewDailyLanes.Clear();
        _appDailyLanes.Clear();
        _appRows.Clear();
        _appNames.Clear();
        _overviewLegendItems.Clear();
        _appLegendItems.Clear();
        AppComboBox.SelectedItem = null;
    }

    private void SetBusy(bool busy, string status)
    {
        RefreshButton.IsEnabled = !busy;
        RangeComboBox.IsEnabled = !busy;
        AppComboBox.IsEnabled = !busy;
        RefreshButton.Background = RefreshButtonNormalBrush;
        StatusTextBlock.Text = status;
    }

    private void OnRefreshButtonPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.Background = RefreshButtonHoverBrush;
        }
    }

    private void OnRefreshButtonPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.Background = RefreshButtonNormalBrush;
        }
    }

    private void OnRefreshButtonPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.Background = RefreshButtonPressedBrush;
        }
    }

    private void OnRefreshButtonPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.Background = RefreshButtonHoverBrush;
        }
    }

    private void OnCardPointerEntered(object sender, PointerRoutedEventArgs e) =>
        AnimateInteractive(sender as FrameworkElement, scale: 1.02, opacity: 1.0, durationMs: 140);

    private void OnCardPointerExited(object sender, PointerRoutedEventArgs e) =>
        AnimateInteractive(sender as FrameworkElement, scale: 1.00, opacity: 1.0, durationMs: 160);

    private void OnCardPointerPressed(object sender, PointerRoutedEventArgs e) =>
        AnimateInteractive(sender as FrameworkElement, scale: 0.995, opacity: 0.96, durationMs: 70);

    private void OnCardPointerReleased(object sender, PointerRoutedEventArgs e) =>
        AnimateInteractive(sender as FrameworkElement, scale: 1.02, opacity: 1.0, durationMs: 90);

    private static void AnimateInteractive(FrameworkElement? element, double scale, double opacity, int durationMs)
    {
        if (element is null)
        {
            return;
        }

        if (element.RenderTransform is not ScaleTransform transform)
        {
            transform = new ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 };
            element.RenderTransform = transform;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        var storyboard = new Storyboard();
        var duration = TimeSpan.FromMilliseconds(durationMs);

        var scaleX = new DoubleAnimation
        {
            To = scale,
            Duration = duration,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(scaleX, transform);
        Storyboard.SetTargetProperty(scaleX, "ScaleX");

        var scaleY = new DoubleAnimation
        {
            To = scale,
            Duration = duration,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(scaleY, transform);
        Storyboard.SetTargetProperty(scaleY, "ScaleY");

        var fade = new DoubleAnimation
        {
            To = opacity,
            Duration = duration,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(fade, element);
        Storyboard.SetTargetProperty(fade, "Opacity");

        storyboard.Children.Add(scaleX);
        storyboard.Children.Add(scaleY);
        storyboard.Children.Add(fade);
        storyboard.Begin();
    }
}
