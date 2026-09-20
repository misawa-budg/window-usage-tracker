using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Channels;
using WinTracker.Shared.Browser;

internal sealed class BrowserServiceHub : IAsyncDisposable
{
    private sealed record Client(Guid Id, string Browser, Channel<BrowserMessage> Outgoing);
    private readonly object _gate = new();
    private readonly BrowserServiceState _state = new();
    private readonly Dictionary<Guid, Client> _clients = [];
    private readonly List<Task> _connections = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly Action _changed;
    private readonly Func<BrowserForeground> _foreground;
    private readonly string _pipeName;
    private readonly Task _acceptLoop;
    private readonly Task _refreshLoop;

    public BrowserServiceHub(Action changed, Func<BrowserForeground> foreground, string? pipeName = null)
    {
        _changed = changed;
        _foreground = foreground;
        _pipeName = pipeName ?? BrowserWire.PipeName;
        _acceptLoop = AcceptAsync();
        _refreshLoop = RefreshAsync();
    }

    public void ForegroundChanged()
    {
        lock (_gate)
        {
            if (!_state.SetForeground(_foreground())) return;
            foreach (Client client in _clients.Values) Probe(client);
        }
        _changed();
    }

    public void Reset()
    {
        lock (_gate) _state.Reset();
    }

    public string? GetService(AppSnapshot app, DateTimeOffset now)
    {
        lock (_gate) return app.State == "Active"
            ? _state.GetService(new(app.ExeName, app.Hwnd), now) : null;
    }

    private void Probe(Client client)
    {
        string? id = _state.BeginProbe(client.Id, client.Browser, DateTimeOffset.UtcNow);
        if (id is not null) client.Outgoing.Writer.TryWrite(new("probe", RequestId: id));
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 8,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try { await pipe.WaitForConnectionAsync(_stop.Token); }
                catch { pipe.Dispose(); throw; }
                _connections.RemoveAll(task => task.IsCompleted);
                _connections.Add(HandleAsync(pipe));
                // Bound open handles, handshakes and per-profile tasks.
                if (_connections.Count >= 7) await Task.WhenAny(_connections);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (IOException) { Console.Error.WriteLine("Browser bridge unavailable; app recording continues."); }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe)
    {
        using (pipe)
        using (var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            Client? client = null;
            Task? sending = null;
            try
            {
                using var handshake = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                handshake.CancelAfter(TimeSpan.FromSeconds(5));
                BrowserMessage? hello = await BrowserWire.ReadAsync(pipe, handshake.Token);
                if (hello?.Kind != "hello" || hello.Browser is not ("edge" or "chrome")) return;
                var outgoing = Channel.CreateBounded<BrowserMessage>(new BoundedChannelOptions(1)
                    { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
                client = new(Guid.NewGuid(), hello.Browser, outgoing);
                lock (_gate)
                {
                    _clients.Add(client.Id, client);
                    _state.SetForeground(_foreground());
                    client.Outgoing.Writer.TryWrite(new("ready"));
                    Probe(client);
                }
                sending = SendAsync();
                while (true)
                {
                    BrowserMessage? message = await BrowserWire.ReadAsync(pipe, lifetime.Token);
                    if (message is null) break;
                    lock (_gate)
                    {
                        _state.SetForeground(_foreground());
                        if (message.Kind == "changed")
                        {
                            _state.ClientChanged(client.Id);
                            Probe(client);
                        }
                        else if (message.Kind == "sample")
                            _state.Accept(client.Id, message.RequestId, message.Focused, message.ServiceId, DateTimeOffset.UtcNow);
                        else throw new InvalidDataException("Unexpected browser message.");
                    }
                    _changed();
                }

                async Task SendAsync()
                {
                    try
                    {
                        await foreach (BrowserMessage message in outgoing.Reader.ReadAllAsync(lifetime.Token))
                            await BrowserWire.WriteAsync(pipe, message, lifetime.Token);
                    }
                    finally { lifetime.Cancel(); }
                }
            }
            catch (Exception error) when (error is IOException or JsonException or OperationCanceledException)
            {
                // Never log the message body (it is untrusted and could contain private text).
            }
            finally
            {
                lifetime.Cancel();
                if (sending is not null)
                    try { await sending; } catch (Exception error) when (error is IOException or OperationCanceledException) { }
                if (client is not null)
                {
                    lock (_gate) { _clients.Remove(client.Id); _state.ClientChanged(client.Id); }
                    _changed();
                }
            }
        }
    }

    private async Task RefreshAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                lock (_gate)
                {
                    _state.SetForeground(_foreground());
                    foreach (Client client in _clients.Values) Probe(client);
                }
                _changed(); // Expired/disconnected sources become unknown, never extrapolated indefinitely.
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        await Task.WhenAll(_acceptLoop, _refreshLoop);
        await Task.WhenAll(_connections);
        _stop.Dispose();
    }
}
