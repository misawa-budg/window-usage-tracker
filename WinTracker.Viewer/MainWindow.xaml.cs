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

    private readonly ObservableCollection<TimelineRowViewModel> _overviewRows = [];
    private readonly ObservableCollection<StateLaneViewModel> _overviewDailyLanes = [];
    private readonly ObservableCollection<TimelineRowViewModel> _appRows = [];
    private readonly ObservableCollection<string> _appNames = [];

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
        AppTimelineListView.ItemsSource = _appRows;
        AppComboBox.ItemsSource = _appNames;

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
        UpdateTickOffsets();
    }

    private void UpdateTickOffsets()
    {
        double extra = Math.Max(0, AppWindow.Size.Width - MinWindowWidth);
        double offset = Math.Min(TickOffsetMax, TickOffsetBase + (extra / 120.0));
        Tick06TextBlock.Margin = new Thickness(-offset, 0, 0, 0);
        Tick18TextBlock.Margin = new Thickness(offset, 0, 0, 0);
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
        BuildAppTimelineRows();
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
            }
            else
            {
                SetOverviewMode(isDaily24h: false);
                BuildOverviewRows();
            }

            RebuildAppNames();
            BuildAppTimelineRows();

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
            TimeSpan.FromDays(1));
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

        foreach (string state in states)
        {
            var segments = new List<AbsoluteSegmentViewModel>();
            double laneTotalSeconds = 0;

            for (int hour = 0; hour < 24; hour++)
            {
                DateTimeOffset bucketStart = _currentWindow.FromUtc.AddHours(hour);
                DateTimeOffset bucketEnd = bucketStart.AddHours(1);

                Dictionary<string, double> byApp = _timelineRows
                    .Where(x =>
                        string.Equals(x.State, state, StringComparison.OrdinalIgnoreCase) &&
                        x.BucketStartUtc == bucketStart)
                    .GroupBy(x => x.ExeName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Sum(v => v.Seconds), StringComparer.OrdinalIgnoreCase);

                double bucketTotalSeconds = byApp.Values.Sum();
                laneTotalSeconds += bucketTotalSeconds;

                if (bucketTotalSeconds <= 0)
                {
                    continue;
                }

                double bucketStartX = hour * hourWidth;
                double cursorX = bucketStartX;
                double bucketEndX = bucketStartX + hourWidth;

                foreach ((string exeName, double seconds) in byApp
                             .OrderByDescending(x => x.Value)
                             .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                {
                    if (seconds <= 0)
                    {
                        continue;
                    }

                    double width = hourWidth * (seconds / bucketTotalSeconds);
                    if (width <= 0)
                    {
                        continue;
                    }

                    // keep each hour's fragments within its hour cell
                    double clampedWidth = Math.Min(width, bucketEndX - cursorX);
                    if (clampedWidth <= 0)
                    {
                        break;
                    }

                    DateTimeOffset localStart = bucketStart.ToLocalTime();
                    DateTimeOffset localEnd = bucketEnd.ToLocalTime();
                    segments.Add(new AbsoluteSegmentViewModel(
                        cursorX,
                        clampedWidth,
                        CreateBrush(ColorForAppState(exeName, state)),
                        $"{state} | {exeName} | {localStart:HH\\:mm}-{localEnd:HH\\:mm} | {ToDuration(seconds)}"));

                    cursorX += clampedWidth;
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

        foreach (DateTimeOffset bucketStart in EnumerateBuckets(_currentWindow))
        {
            DateTimeOffset bucketEnd = bucketStart + _currentWindow.BucketSize;

            var bucketRows = _timelineRows
                .Where(x => x.BucketStartUtc == bucketStart)
                .GroupBy(x => x.ExeName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Sum(v => v.Seconds), StringComparer.OrdinalIgnoreCase);

            double bucketTotalSeconds = bucketRows.Values.Sum();
            var segments = new List<TimelineSegmentViewModel>();

            foreach (string app in topApps.OrderByDescending(x => totalByApp[x]).ThenBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                if (!bucketRows.TryGetValue(app, out double seconds) || seconds <= 0)
                {
                    continue;
                }

                double width = CalculateShareWidth(seconds, bucketTotalSeconds, BucketTrackWidth);
                segments.Add(new TimelineSegmentViewModel(
                    width,
                    CreateBrush(ColorForKey(app)),
                    $"{app} {ToDuration(seconds)} ({seconds:F0}s)"));
            }

            double otherSeconds = bucketRows
                .Where(x => !topApps.Contains(x.Key))
                .Sum(x => x.Value);

            if (otherSeconds > 0)
            {
                segments.Add(new TimelineSegmentViewModel(
                    CalculateShareWidth(otherSeconds, bucketTotalSeconds, BucketTrackWidth),
                    CreateBrush("#F8B5B5"),
                    $"Other {ToDuration(otherSeconds)} ({otherSeconds:F0}s)"));
            }

            _overviewRows.Add(new TimelineRowViewModel(
                FormatBucketLabel(bucketStart, bucketEnd),
                ToDuration(bucketTotalSeconds),
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

        foreach (DateTimeOffset bucketStart in EnumerateBuckets(_currentWindow))
        {
            DateTimeOffset bucketEnd = bucketStart + _currentWindow.BucketSize;
            var grouped = _timelineRows
                .Where(x => x.BucketStartUtc == bucketStart && string.Equals(x.ExeName, app, StringComparison.OrdinalIgnoreCase))
                .GroupBy(x => x.State, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Sum(v => v.Seconds), StringComparer.OrdinalIgnoreCase);

            double activeSeconds = grouped.GetValueOrDefault("Active", 0);
            double openSeconds = grouped.GetValueOrDefault("Open", 0);
            double minimizedSeconds = grouped.GetValueOrDefault("Minimized", 0);

            var segments = new List<TimelineSegmentViewModel>();
            AddStateSegment(segments, "Active", activeSeconds, _currentWindow.BucketSeconds, "#BFA8FF");
            AddStateSegment(segments, "Open", openSeconds, _currentWindow.BucketSeconds, "#B8E9C7");
            AddStateSegment(segments, "Minimized", minimizedSeconds, _currentWindow.BucketSeconds, "#F9E6A6");

            double total = activeSeconds + openSeconds + minimizedSeconds;
            _appRows.Add(new TimelineRowViewModel(
                FormatBucketLabel(bucketStart, bucketEnd),
                ToDuration(total),
                segments));
        }
    }

    private static void AddStateSegment(
        ICollection<TimelineSegmentViewModel> segments,
        string state,
        double seconds,
        int bucketSeconds,
        string colorHex)
    {
        if (seconds <= 0 || bucketSeconds <= 0)
        {
            return;
        }

        double width = CalculateShareWidth(seconds, bucketSeconds, BucketTrackWidth);
        segments.Add(new TimelineSegmentViewModel(
            width,
            CreateBrush(colorHex),
            $"{state} {ToDuration(seconds)} ({seconds:F0}s)"));
    }

    private static double CalculateShareWidth(double part, double whole, double width)
    {
        if (part <= 0 || whole <= 0 || width <= 0)
        {
            return 0;
        }

        double raw = width * (part / whole);
        return Math.Max(1, raw);
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

    private static IEnumerable<DateTimeOffset> EnumerateBuckets(UsageQueryWindow window)
    {
        DateTimeOffset cursor = window.FromUtc;
        while (cursor < window.ToUtc)
        {
            yield return cursor;
            cursor = cursor.Add(window.BucketSize);
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
        int hue = HashToHue(appKey);
        double saturation = state switch
        {
            "Active" => 0.74,
            "Open" => 0.56,
            "Minimized" => 0.38,
            _ => 0.52
        };
        double lightness = state switch
        {
            "Active" => 0.52,
            "Open" => 0.50,
            "Minimized" => 0.48,
            _ => 0.50
        };

        (byte r, byte g, byte b) = HslToRgb(hue / 360.0, saturation, lightness);
        return $"#{r:X2}{g:X2}{b:X2}";
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
        _appRows.Clear();
        _appNames.Clear();
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
