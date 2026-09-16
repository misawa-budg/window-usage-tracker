using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace WinTracker.Viewer;

// Explicitly opt-in; never records application names, titles, or database paths.
internal sealed class ReloadMeasurement
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly string _output;
    private double _queryMs;

    private ReloadMeasurement(string output) => _output = output;

    public static ReloadMeasurement? Start()
    {
        string? output = Environment.GetEnvironmentVariable("WINTRACKER_PROFILE_OUTPUT");
        return string.IsNullOrWhiteSpace(output) ? null : new(output);
    }

    public void QueryCompleted() => _queryMs = _clock.Elapsed.TotalMilliseconds;

    public async Task CompleteAsync(FrameworkElement root, string range, int intervals, CancellationToken token)
    {
        double modelsMs = _clock.Elapsed.TotalMilliseconds - _queryMs;
        var frame = new TaskCompletionSource<double>();
        void OnRendering(object? sender, object args) => frame.TrySetResult(_clock.Elapsed.TotalMilliseconds);
        CompositionTarget.Rendering += OnRendering;
        try
        {
            // Diagnostic-only forced layout: use identical instrumentation for comparisons.
            root.UpdateLayout();
            double layoutEnd = _clock.Elapsed.TotalMilliseconds;
            double renderingMs = await frame.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            int elements = CountElements(root);
            string line = JsonSerializer.Serialize(new
            {
                range, intervals,
                queryMs = Math.Round(_queryMs, 2),
                modelsMs = Math.Round(modelsMs, 2),
                layoutMs = Math.Round(layoutEnd - _queryMs - modelsMs, 2),
                renderingCallbackMs = Math.Round(renderingMs, 2),
                visualElements = elements
            });
            await File.AppendAllTextAsync(_output, line + Environment.NewLine, token);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
        {
            Debug.WriteLine($"Reload measurement unavailable: {ex.GetType().Name}");
        }
        finally
        {
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    private static int CountElements(DependencyObject root)
    {
        int count = 1;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            count += CountElements(VisualTreeHelper.GetChild(root, i));
        return count;
    }
}
