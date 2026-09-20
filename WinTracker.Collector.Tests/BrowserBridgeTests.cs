using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using WinTracker.Shared.Browser;
using Xunit;

namespace WinTracker.Collector.Tests;

public sealed class BrowserBridgeTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-20T00:00:00Z");
    private static readonly BrowserForeground Edge = new("msedge.exe", "0x1");

    [Fact]
    public void ChangedWindowRejectsLateResponseEvenWithinTheSameBrowser()
    {
        var state = new BrowserServiceState();
        Guid client = Guid.NewGuid();
        state.SetForeground(Edge);
        string? old = state.BeginProbe(client, "edge", Now);
        state.SetForeground(Edge with { Hwnd = "0x2" });
        Assert.False(state.Accept(client, old, true, "youtube", Now.AddMilliseconds(20)));
        Assert.Null(state.GetService(Edge, Now));
    }

    [Fact]
    public void ConfirmedServiceExpiresAndDisconnectImmediatelyInvalidatesIt()
    {
        var state = new BrowserServiceState();
        Guid client = Guid.NewGuid();
        state.SetForeground(Edge);
        string? request = state.BeginProbe(client, "edge", Now);
        Assert.True(state.Accept(client, request, true, "youtube", Now));
        Assert.Equal("youtube", state.GetService(Edge, Now.AddSeconds(24)));
        Assert.Null(state.GetService(Edge, Now.AddSeconds(26)));
        Assert.Null(state.GetService(Edge, Now.AddSeconds(-1)));
        state.ClientChanged(client);
        Assert.Null(state.GetService(Edge, Now));
    }

    [Theory]
    [InlineData(false, "youtube", 0)]
    [InlineData(true, "https://secret.example", 0)]
    [InlineData(true, "gmail", 3)]
    public void UnfocusedInvalidAndLateSamplesDoNotAttributeTime(bool focused, string service, int delay)
    {
        var state = new BrowserServiceState();
        state.SetForeground(Edge);
        Guid client = Guid.NewGuid();
        string? request = state.BeginProbe(client, "edge", Now);
        state.Accept(client, request, focused, service, Now.AddSeconds(delay));
        Assert.Null(state.GetService(Edge, Now.AddSeconds(delay)));
        Assert.Null(state.BeginProbe(client, "chrome", Now));
    }

    [Fact]
    public void ConflictingProfilesAndObservationGapsFailClosed()
    {
        var state = new BrowserServiceState();
        state.SetForeground(Edge);
        Guid first = Guid.NewGuid(), second = Guid.NewGuid();
        state.Accept(first, state.BeginProbe(first, "edge", Now), true, "youtube", Now);
        state.Accept(second, state.BeginProbe(second, "edge", Now), true, "gmail", Now);
        Assert.Null(state.GetService(Edge, Now));
        state.Reset();
        state.Accept(second, state.BeginProbe(second, "edge", Now), true, "gmail", Now);
        Assert.Equal("gmail", state.GetService(Edge, Now));
        state.Reset();
        Assert.Null(state.GetService(Edge, Now));
    }

    [Fact]
    public async Task WireRejectsOversizedTruncatedAndUnexpectedPrivateFields()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => BrowserWire.ReadAsync(new MemoryStream(BitConverter.GetBytes(4097)), default));
        await Assert.ThrowsAsync<EndOfStreamException>(() => BrowserWire.ReadAsync(new MemoryStream([1, 0]), default));
        byte[] text = Encoding.UTF8.GetBytes("{\"kind\":\"sample\",\"url\":\"https://private.example\"}");
        using var frame = new MemoryStream();
        frame.Write(BitConverter.GetBytes(text.Length));
        frame.Write(text);
        frame.Position = 0;
        await Assert.ThrowsAsync<JsonException>(() => BrowserWire.ReadAsync(frame, default));
    }

    [Fact]
    public async Task BadClientDoesNotPreventASecondClientFromConnecting()
    {
        string name = "WinTracker-test-" + Guid.NewGuid().ToString("N");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var hub = new BrowserServiceHub(() => { }, () => Edge, name);
        using (var bad = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await bad.ConnectAsync(timeout.Token);
            await bad.WriteAsync(BitConverter.GetBytes(4097), timeout.Token);
        }
        using var good = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await good.ConnectAsync(timeout.Token);
        await BrowserWire.WriteAsync(good, new("hello", Browser: "edge"), timeout.Token);
        Assert.Equal("probe", (await BrowserWire.ReadAsync(good, timeout.Token))?.Kind);
    }

    [Fact]
    public async Task RealPipeExchangesProbesAndDropsDisconnectedClients()
    {
        string pipeName = "WinTracker-test-" + Guid.NewGuid().ToString("N");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var changed = new SemaphoreSlim(0);
        await using var hub = new BrowserServiceHub(() => changed.Release(), () => Edge, pipeName);
        using (var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            await pipe.ConnectAsync(timeout.Token);
            await BrowserWire.WriteAsync(pipe, new("hello", Browser: "edge"), timeout.Token);
            var probe = await BrowserWire.ReadAsync(pipe, timeout.Token);
            Assert.Equal("probe", probe?.Kind);
            await BrowserWire.WriteAsync(pipe, new("sample", RequestId: probe!.RequestId, Focused: true, ServiceId: "youtube"), timeout.Token);
            await changed.WaitAsync(timeout.Token);
            Assert.Equal("youtube", hub.GetService(new("msedge.exe", 1, "0x1", "", "Active"), DateTimeOffset.UtcNow));
            Assert.Null(hub.GetService(new("msedge.exe", 1, "0x1", "", "Open"), DateTimeOffset.UtcNow));
        }
        await changed.WaitAsync(timeout.Token);
        Assert.Null(hub.GetService(new("msedge.exe", 1, "0x1", "", "Active"), DateTimeOffset.UtcNow));
    }
}
