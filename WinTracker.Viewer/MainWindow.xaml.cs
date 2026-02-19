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
        Tick00TextBlock.Text = compact ? "0" : "00:00";
        Tick06TextBlock.Text = compact ? "6" : "06:00";
        Tick12TextBlock.Text = compact ? "12" : "12:00";
        Tick18TextBlock.Text = compact ? "18" : "18:00";
        Tick24TextBlock.Text = compact ? "24" : "24:00";

        AppTick00TextBlock.Text = compact ? "0" : "00:00";
        AppTick06TextBlock.Text = compact ? "6" : "06:00";
        AppTick12TextBlock.Text = compact ? "12" : "12:00";
        AppTick18TextBlock.Text = compact ? "18" : "18:00";
        AppTick24TextBlock.Text = compact ? "24" : "24:00";

        WeekTick00TextBlock.Text = compact ? "0" : "00:00";
        WeekTick06TextBlock.Text = compact ? "6" : "06:00";
        WeekTick12TextBlock.Text = compact ? "12" : "12:00";
        WeekTick18TextBlock.Text = compact ? "18" : "18:00";
        WeekTick24TextBlock.Text = compact ? "24" : "24:00";

        AppWeekTick00TextBlock.Text = compact ? "0" : "00:00";
        AppWeekTick06TextBlock.Text = compact ? "6" : "06:00";
        AppWeekTick12TextBlock.Text = compact ? "12" : "12:00";
        AppWeekTick18TextBlock.Text = compact ? "18" : "18:00";
        AppWeekTick24TextBlock.Text = compact ? "24" : "24:00";

        UpdateTickOffsets();
    }

    private void UpdateTickOffsets()
    {
        double extra = Math.Max(0, AppWindow.Size.Width - MinWindowWidth);
        double offset = Math.Min(TickOffsetMax, TickOffsetBase + (extra / 120.0));
        Tick06TextBlock.Margin = new Thickness(-offset, 0, 0, 0);
        Tick18TextBlock.Margin = new Thickness(offset, 0, 0, 0);
        AppTick06TextBlock.Margin = new Thickness(-offset, 0, 0, 0);
        AppTick18TextBlock.Margin = new Thickness(offset, 0, 0, 0);
        WeekTick06TextBlock.Margin = new Thickness(-offset, 0, 0, 0);
        WeekTick18TextBlock.Margin = new Thickness(offset, 0, 0, 0);
        AppWeekTick06TextBlock.Margin = new Thickness(-offset, 0, 0, 0);
        AppWeekTick18TextBlock.Margin = new Thickness(offset, 0, 0, 0);
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

        string[] states = ["Active", "Open", "Minimized"];
        double hourWidth = DailyTrackWidth / 24.0;
        DateTimeOffset fromUtc = _currentWindow.FromUtc;

        foreach (string state in states)
        {
            var segments = new List<AbsoluteSegmentViewModel>();
            double laneTotalSeconds = 0;

            var byHour = _timelineRows
                .Where(x => string.Equals(x.State, state, StringComparison.OrdinalIgnoreCase))
                .Select(x => new
                {
                    HourIndex = (int)Math.Floor((x.BucketStartUtc - fromUtc).TotalHours),
                    x.ExeName,
                    x.Seconds
                })
                .Where(x => x.HourIndex >= 0 && x.HourIndex < 24)
                .GroupBy(x => x.HourIndex)
                .ToDictionary(
                    g => g.Key,
                    g => g.GroupBy(v => v.ExeName, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(v => v.Key, v => v.Sum(t => t.Seconds), StringComparer.OrdinalIgnoreCase));

            for (int hour = 0; hour < 24; hour++)
            {
                DateTimeOffset bucketStart = _currentWindow.FromUtc.AddHours(hour);
                DateTimeOffset bucketEnd = bucketStart.AddHours(1);

                if (!byHour.TryGetValue(hour, out Dictionary<string, double>? byApp))
                {
                    segments.Add(new AbsoluteSegmentViewModel(
                        0,
                        hourWidth,
                        TransparentBrush,
                        "No data"));
                    continue;
                }

                var normalizedByApp = byApp
                    .ToDictionary(
                        x => x.Key,
                        x => Math.Min((double)_currentWindow.BucketSeconds, x.Value),
                        StringComparer.OrdinalIgnoreCase);

                double rawHourSeconds = normalizedByApp.Values.Sum();
                double occupiedSeconds = Math.Min((double)_currentWindow.BucketSeconds, rawHourSeconds);
                laneTotalSeconds += occupiedSeconds;

                if (occupiedSeconds <= 0)
                {
                    segments.Add(new AbsoluteSegmentViewModel(
                        0,
                        hourWidth,
                        TransparentBrush,
                        "No data"));
                    continue;
                }

                double scale = rawHourSeconds > 0 ? occupiedSeconds / rawHourSeconds : 0;
                double occupiedWidth = 0;

                foreach ((string exeName, double seconds) in normalizedByApp
                             .OrderByDescending(x => x.Value)
                             .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                {
                    if (seconds <= 0)
                    {
                        continue;
                    }

                    double width = hourWidth * (seconds / _currentWindow.BucketSeconds) * scale;
                    if (width <= 0)
                    {
                        continue;
                    }

                    DateTimeOffset localStart = bucketStart.ToLocalTime();
                    DateTimeOffset localEnd = bucketEnd.ToLocalTime();
                    segments.Add(new AbsoluteSegmentViewModel(
                        0,
                        width,
                        CreateBrush(ColorForAppState(exeName, state)),
                        $"{state} | {exeName} | {localStart:HH\\:mm}-{localEnd:HH\\:mm} | {ToDuration(seconds)}"));
                    occupiedWidth += width;
                }

                double gapWidth = Math.Max(0, hourWidth - occupiedWidth);
                if (gapWidth > 0)
                {
                    segments.Add(new AbsoluteSegmentViewModel(
                        0,
                        gapWidth,
                        TransparentBrush,
                        "No data"));
                }
            }

            _overviewDailyLanes.Add(new StateLaneViewModel(
                state,
                ToDuration(laneTotalSeconds),
                segments));
        }
    }

    private void RebuildAppNames()
    {
        string? current = AppComboBox.SelectedItem as string;

        var totals = _timelineRows
            .GroupBy(r => r.ExeName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { ExeName = g.Key, Seconds = g.Sum(x => x.Seconds) })
            .Where(x => x.Seconds > 0)
            .OrderByDescending(x => x.Seconds)
            .ThenBy(x => x.ExeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _appNames.Clear();
        foreach (var app in totals)
        {
            _appNames.Add(app.ExeName);
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

        var apps = _timelineRows
            .GroupBy(x => x.ExeName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { ExeName = g.Key, Seconds = g.Sum(x => x.Seconds) })
            .Where(x => x.Seconds > 0)
            .OrderByDescending(x => x.Seconds)
            .ThenBy(x => x.ExeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var top = apps.Take(TopAppCount);

        foreach (var app in top)
        {
            _overviewLegendItems.Add(new LegendItemViewModel(
                CreateBrush(ColorForKey(app.ExeName)),
                app.ExeName));
        }

        if (apps.Count > TopAppCount)
        {
            _overviewLegendItems.Add(new LegendItemViewModel(
                CreateBrush("#F8B5B5"),
                "Other"));
        }
    }

    private void RebuildAppLegend()
    {
        _appLegendItems.Clear();
        _appLegendItems.Add(new LegendItemViewModel(
            CreateBrush("#BFA8FF"),
            "Active"));
        _appLegendItems.Add(new LegendItemViewModel(
            CreateBrush("#B8E9C7"),
            "Open"));
        _appLegendItems.Add(new LegendItemViewModel(
            CreateBrush("#F9E6A6"),
            "Minimized"));
    }

    private void BuildOverviewRows()
    {
        _overviewRows.Clear();

        var totalByApp = _timelineRows
            .GroupBy(x => x.ExeName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Seconds), StringComparer.OrdinalIgnoreCase);

        HashSet<string> topApps = totalByApp
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Take(TopAppCount)
            .Select(x => x.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (DateTimeOffset dayStart in EnumerateDayBuckets(_currentWindow))
        {
            DateTimeOffset dayEnd = dayStart.AddDays(1);
            var segments = new List<TimelineSegmentViewModel>();
            double dayTotalSeconds = 0;
            double hourWidth = BucketTrackWidth / 24.0;

            for (int hour = 0; hour < 24; hour++)
            {
                DateTimeOffset hourStart = dayStart.AddHours(hour);
                var hourRows = _timelineRows
                    .Where(x => x.BucketStartUtc == hourStart)
                    .ToList();

                if (hourRows.Count == 0)
                {
                    segments.Add(new TimelineSegmentViewModel(
                        hourWidth,
                        TransparentBrush,
                        "No data"));
                    continue;
                }

                var byApp = hourRows
                    .GroupBy(x => x.ExeName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        g => g.Key,
                        g => Math.Min((double)_currentWindow.BucketSeconds, g.Sum(v => v.Seconds)),
                        StringComparer.OrdinalIgnoreCase);

                double otherSeconds = byApp
                    .Where(x => !topApps.Contains(x.Key))
                    .Sum(x => x.Value);

                var displayByApp = byApp
                    .Where(x => topApps.Contains(x.Key))
                    .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

                if (otherSeconds > 0)
                {
                    displayByApp["Other"] = Math.Min((double)_currentWindow.BucketSeconds, otherSeconds);
                }

                double rawHourSeconds = displayByApp.Values.Sum();
                double occupiedSeconds = Math.Min((double)_currentWindow.BucketSeconds, rawHourSeconds);
                dayTotalSeconds += occupiedSeconds;

                if (occupiedSeconds <= 0)
                {
                    segments.Add(new TimelineSegmentViewModel(
                        hourWidth,
                        TransparentBrush,
                        "No data"));
                    continue;
                }

                double scale = rawHourSeconds > 0 ? occupiedSeconds / rawHourSeconds : 0;
                double occupiedWidth = 0;

                foreach ((string app, double seconds) in displayByApp
                             .OrderByDescending(x => x.Value)
                             .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                {
                    double width = hourWidth * (seconds / _currentWindow.BucketSeconds) * scale;
                    if (width <= 0)
                    {
                        continue;
                    }

                    Brush fill = string.Equals(app, "Other", StringComparison.OrdinalIgnoreCase)
                        ? CreateBrush("#F8B5B5")
                        : CreateBrush(ColorForKey(app));

                    segments.Add(new TimelineSegmentViewModel(
                        width,
                        fill,
                        $"{app} {ToDuration(seconds)} ({seconds:F0}s)"));
                    occupiedWidth += width;
                }

                double gapWidth = Math.Max(0, hourWidth - occupiedWidth);
                if (gapWidth > 0)
                {
                    segments.Add(new TimelineSegmentViewModel(
                        gapWidth,
                        TransparentBrush,
                        "No data"));
                }
            }

            _overviewRows.Add(new TimelineRowViewModel(
                FormatBucketLabel(dayStart, dayEnd),
                ToDuration(dayTotalSeconds),
                segments));
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

        foreach (DateTimeOffset dayStart in EnumerateDayBuckets(_currentWindow))
        {
            DateTimeOffset dayEnd = dayStart.AddDays(1);
            var segments = new List<TimelineSegmentViewModel>();
            double dayTotalSeconds = 0;
            double hourWidth = BucketTrackWidth / 24.0;

            for (int hour = 0; hour < 24; hour++)
            {
                DateTimeOffset hourStart = dayStart.AddHours(hour);
                var hourRows = _timelineRows
                    .Where(x => x.BucketStartUtc == hourStart && string.Equals(x.ExeName, app, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (hourRows.Count == 0)
                {
                    segments.Add(new TimelineSegmentViewModel(
                        hourWidth,
                        TransparentBrush,
                        "No data"));
                    continue;
                }

                var byState = hourRows
                    .GroupBy(x => x.State, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        g => g.Key,
                        g => Math.Min((double)_currentWindow.BucketSeconds, g.Sum(v => v.Seconds)),
                        StringComparer.OrdinalIgnoreCase);

                double activeSeconds = byState.GetValueOrDefault("Active", 0);
                double openSeconds = byState.GetValueOrDefault("Open", 0);
                double minimizedSeconds = byState.GetValueOrDefault("Minimized", 0);
                double rawHourSeconds = activeSeconds + openSeconds + minimizedSeconds;
                double occupiedSeconds = Math.Min((double)_currentWindow.BucketSeconds, rawHourSeconds);
                dayTotalSeconds += occupiedSeconds;

                if (occupiedSeconds <= 0)
                {
                    segments.Add(new TimelineSegmentViewModel(
                        hourWidth,
                        TransparentBrush,
                        "No data"));
                    continue;
                }

                double scale = rawHourSeconds > 0 ? occupiedSeconds / rawHourSeconds : 0;
                double occupiedWidth = 0;

                occupiedWidth += AddStateSegmentWeek(segments, "Active", activeSeconds, _currentWindow.BucketSeconds, "#BFA8FF", hourWidth, scale);
                occupiedWidth += AddStateSegmentWeek(segments, "Open", openSeconds, _currentWindow.BucketSeconds, "#B8E9C7", hourWidth, scale);
                occupiedWidth += AddStateSegmentWeek(segments, "Minimized", minimizedSeconds, _currentWindow.BucketSeconds, "#F9E6A6", hourWidth, scale);

                double gapWidth = Math.Max(0, hourWidth - occupiedWidth);
                if (gapWidth > 0)
                {
                    segments.Add(new TimelineSegmentViewModel(
                        gapWidth,
                        TransparentBrush,
                        "No data"));
                }
            }

            _appRows.Add(new TimelineRowViewModel(
                FormatBucketLabel(dayStart, dayEnd),
                ToDuration(dayTotalSeconds),
                segments));
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

        string[] states = ["Active", "Open", "Minimized"];
        double hourWidth = DailyTrackWidth / 24.0;
        DateTimeOffset fromUtc = _currentWindow.FromUtc;

        foreach (string state in states)
        {
            var segments = new List<AbsoluteSegmentViewModel>();
            double laneTotalSeconds = 0;

            var byHour = _timelineRows
                .Where(x =>
                    string.Equals(x.ExeName, app, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.State, state, StringComparison.OrdinalIgnoreCase))
                .Select(x => new
                {
                    HourIndex = (int)Math.Floor((x.BucketStartUtc - fromUtc).TotalHours),
                    x.Seconds
                })
                .Where(x => x.HourIndex >= 0 && x.HourIndex < 24)
                .GroupBy(x => x.HourIndex)
                .ToDictionary(g => g.Key, g => g.Sum(v => v.Seconds));

            for (int hour = 0; hour < 24; hour++)
            {
                DateTimeOffset bucketStart = _currentWindow.FromUtc.AddHours(hour);
                DateTimeOffset bucketEnd = bucketStart.AddHours(1);

                if (!byHour.TryGetValue(hour, out double seconds))
                {
                    segments.Add(new AbsoluteSegmentViewModel(
                        0,
                        hourWidth,
                        TransparentBrush,
                        "No data"));
                    continue;
                }

                double occupiedSeconds = Math.Min((double)_currentWindow.BucketSeconds, seconds);
                laneTotalSeconds += occupiedSeconds;
                if (occupiedSeconds <= 0)
                {
                    segments.Add(new AbsoluteSegmentViewModel(
                        0,
                        hourWidth,
                        TransparentBrush,
                        "No data"));
                    continue;
                }

                double width = hourWidth * (occupiedSeconds / _currentWindow.BucketSeconds);
                width = Math.Min(hourWidth, width);
                if (width <= 0)
                {
                    segments.Add(new AbsoluteSegmentViewModel(
                        0,
                        hourWidth,
                        TransparentBrush,
                        "No data"));
                    continue;
                }

                DateTimeOffset localStart = bucketStart.ToLocalTime();
                DateTimeOffset localEnd = bucketEnd.ToLocalTime();
                segments.Add(new AbsoluteSegmentViewModel(
                    0,
                    width,
                    CreateBrush(ColorForAppState(app, state)),
                    $"{state} | {app} | {localStart:HH\\:mm}-{localEnd:HH\\:mm} | {ToDuration(occupiedSeconds)}"));

                double gapWidth = Math.Max(0, hourWidth - width);
                if (gapWidth > 0)
                {
                    segments.Add(new AbsoluteSegmentViewModel(
                        0,
                        gapWidth,
                        TransparentBrush,
                        "No data"));
                }
            }

            _appDailyLanes.Add(new StateLaneViewModel(
                state,
                ToDuration(laneTotalSeconds),
                segments));
        }
    }

    private static double AddStateSegmentWeek(
        ICollection<TimelineSegmentViewModel> segments,
        string state,
        double seconds,
        int bucketSeconds,
        string colorHex,
        double hourWidth,
        double scale)
    {
        if (seconds <= 0 || bucketSeconds <= 0 || hourWidth <= 0 || scale <= 0)
        {
            return 0;
        }

        double width = hourWidth * (seconds / bucketSeconds) * scale;
        if (width <= 0)
        {
            return 0;
        }

        segments.Add(new TimelineSegmentViewModel(
            width,
            CreateBrush(colorHex),
            $"{state} {ToDuration(seconds)} ({seconds:F0}s)"));
        return width;
    }

    private static string FormatBucketLabel(DateTimeOffset bucketStartUtc, DateTimeOffset bucketEndUtc)
    {
        DateTimeOffset localStart = bucketStartUtc.ToLocalTime();
        DateTimeOffset localEnd = bucketEndUtc.ToLocalTime();
        TimeSpan bucket = bucketEndUtc - bucketStartUtc;
        if (bucket >= TimeSpan.FromHours(23))
        {
            return $"{localStart:MM/dd (ddd)}";
        }

        return $"{localStart:MM/dd HH:mm} - {localEnd:HH:mm}";
    }

    private static IEnumerable<DateTimeOffset> EnumerateDayBuckets(UsageQueryWindow window)
    {
        DateTimeOffset cursor = window.FromUtc;
        while (cursor < window.ToUtc)
        {
            yield return cursor;
            cursor = cursor.AddDays(1);
        }
    }

    private static string ToDuration(double seconds)
    {
        TimeSpan span = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds)));
        return $"{(int)span.TotalHours:D2}:{span.Minutes:D2}";
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

    private static string ColorForAppState(string appKey, string state)
    {
        return ColorForKey(appKey);
    }

    private static string ColorForKey(string key)
    {
        int hue = HashToHue(key);
        (byte r, byte g, byte b) = HslToRgb(hue / 360.0, 0.62, 0.55);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static int HashToHue(string key)
    {
        uint hash = 2166136261;
        foreach (char c in key.ToLowerInvariant())
        {
            hash ^= c;
            hash *= 16777619;
        }

        int[] palette = [268, 145, 8, 48];
        return palette[(int)(hash % (uint)palette.Length)];
    }

    private static (byte R, byte G, byte B) HslToRgb(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs((h * 6) % 2 - 1));
        double m = l - c / 2;

        double r1;
        double g1;
        double b1;

        if (h < 1.0 / 6.0)
        {
            r1 = c;
            g1 = x;
            b1 = 0d;
        }
        else if (h < 2.0 / 6.0)
        {
            r1 = x;
            g1 = c;
            b1 = 0d;
        }
        else if (h < 3.0 / 6.0)
        {
            r1 = 0d;
            g1 = c;
            b1 = x;
        }
        else if (h < 4.0 / 6.0)
        {
            r1 = 0d;
            g1 = x;
            b1 = c;
        }
        else if (h < 5.0 / 6.0)
        {
            r1 = x;
            g1 = 0d;
            b1 = c;
        }
        else
        {
            r1 = c;
            g1 = 0d;
            b1 = x;
        }

        byte r = (byte)Math.Round((r1 + m) * 255);
        byte g = (byte)Math.Round((g1 + m) * 255);
        byte b = (byte)Math.Round((b1 + m) * 255);
        return (r, g, b);
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
