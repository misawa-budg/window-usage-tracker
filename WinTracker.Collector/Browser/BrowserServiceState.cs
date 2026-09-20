using WinTracker.Shared.Analytics;

internal readonly record struct BrowserForeground(string ExeName, string Hwnd);

// Pure state machine. Browser IDs are not HWNDs: ask for a fresh focused-tab sample
// while the OS foreground remains unchanged, and reject late/replayed responses.
// The hub serializes access; no full URLs/titles enter this class.
internal sealed class BrowserServiceState(bool storeBrowserHostnames = false)
{
    private sealed record Probe(string Id, BrowserForeground Foreground, DateTimeOffset SentAt);
    private sealed record Observation(Guid Client, BrowserForeground Foreground, string Service, DateTimeOffset At);
    private readonly Dictionary<Guid, Probe> _pending = [];
    private BrowserForeground _foreground = new("", "");
    public BrowserForeground Foreground => _foreground;
    private Observation? _confirmed;
    private bool _conflicted;
    internal static readonly TimeSpan Lease = TimeSpan.FromSeconds(25);

    public bool SetForeground(BrowserForeground foreground)
    {
        if (_foreground == foreground) return false;
        Reset();
        _foreground = foreground;
        return true;
    }

    public void Reset()
    {
        _pending.Clear();
        _confirmed = null;
        _conflicted = false;
    }

    public string? BeginProbe(Guid client, string browser, DateTimeOffset now)
    {
        if (!MatchesBrowser(browser, _foreground.ExeName)) return null;
        string id = Guid.NewGuid().ToString("N");
        _pending[client] = new(id, _foreground, now);
        return id;
    }

    public void ClientChanged(Guid client)
    {
        _pending.Remove(client);
        if (_confirmed?.Client == client) _confirmed = null;
    }

    public bool Accept(Guid client, string? requestId, bool focused, string? service, DateTimeOffset now)
    {
        if (!_pending.TryGetValue(client, out Probe? probe) || probe.Id != requestId) return false;
        _pending.Remove(client);
        if (probe.Foreground != _foreground || now < probe.SentAt || now - probe.SentAt > TimeSpan.FromSeconds(2)) return false;
        if (!focused || !BrowserServices.IsAllowed(service, storeBrowserHostnames))
        {
            if (_confirmed?.Client == client) _confirmed = null;
            return true;
        }
        if (_confirmed is not null && _confirmed.Client != client && now - _confirmed.At <= Lease)
        {
            _conflicted = true;
            _confirmed = null;
        }
        if (!_conflicted) _confirmed = new(client, _foreground, service!, now);
        return true;
    }

    public string? GetService(BrowserForeground foreground, DateTimeOffset now) =>
        !_conflicted && _confirmed is { } value && value.Foreground == foreground &&
        now >= value.At && now - value.At <= Lease ? value.Service : null;

    internal static bool MatchesBrowser(string browser, string exe) => browser switch
    {
        "edge" => string.Equals(exe, "msedge.exe", StringComparison.OrdinalIgnoreCase),
        "chrome" => string.Equals(exe, "chrome.exe", StringComparison.OrdinalIgnoreCase),
        _ => false
    };
}
