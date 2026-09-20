using System.IO.Pipes;
using System.Text.Json;
using WinTracker.Shared.Browser;

// No settings/DB access and no stdout logging: stdout belongs to native messaging.
if (args.Length < 1 || !System.Text.RegularExpressions.Regex.IsMatch(args[0], "^chrome-extension://[a-p]{32}/$"))
{
    Console.Error.WriteLine("This program is started by the WinTracker browser extension.");
    return 2;
}
using var stop = new CancellationTokenSource();
using var pipe = new NamedPipeClientStream(".", BrowserWire.PipeName, PipeDirection.InOut,
    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
try
{
    await pipe.ConnectAsync(3000, stop.Token);
    using Stream input = Console.OpenStandardInput();
    using Stream output = Console.OpenStandardOutput();
    Task incoming = RelayAsync(input, pipe);
    Task outgoing = RelayAsync(pipe, output);
    Task completed = await Task.WhenAny(incoming, outgoing);
    await completed;
    // Console stdin may be a blocking OS pipe that cannot cancel an in-flight read.
    // Exit this relay process when either side ends, rather than waiting on stdin.
    return 0;
}
catch (Exception error) when (error is IOException or InvalidDataException or TimeoutException or JsonException or OperationCanceledException)
{
    Console.Error.WriteLine("WinTracker browser bridge disconnected. Check the Collector and browser tracking setting.");
    return 1;
}
finally { stop.Cancel(); }

async Task RelayAsync(Stream source, Stream destination)
{
    while (await BrowserWire.ReadAsync(source, stop.Token) is { } message)
        await BrowserWire.WriteAsync(destination, message, stop.Token);
}
