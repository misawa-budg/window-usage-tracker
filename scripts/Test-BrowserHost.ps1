[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$HostExecutable, [switch]$MalformedFrame)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $HostExecutable).Path
if ([IO.Path]::GetFileName($exe) -ne 'WinTracker.BrowserHost.exe') { throw 'Select the test build of BrowserHost.' }
$pipeName = "WinTracker.Browser.v1.$([Diagnostics.Process]::GetCurrentProcess().SessionId)"
# FirstPipeInstance prevents accidentally attaching the relay to a running real Collector.
# No registry entries, browser, real database or user UI are touched.
$options = [IO.Pipes.PipeOptions]::Asynchronous -bor [IO.Pipes.PipeOptions]::CurrentUserOnly -bor [IO.Pipes.PipeOptions]::FirstPipeInstance
$pipe = [IO.Pipes.NamedPipeServerStream]::new($pipeName, [IO.Pipes.PipeDirection]::InOut, 1, [IO.Pipes.PipeTransmissionMode]::Byte, $options)
$process = [Diagnostics.Process]::new()
$started = $false

function Write-Frame([IO.Stream]$Stream, [hashtable]$Message) {
    $body = [Text.Encoding]::UTF8.GetBytes(($Message | ConvertTo-Json -Compress))
    $header = [BitConverter]::GetBytes([int]$body.Length)
    $Stream.Write($header, 0, 4)
    $Stream.Write($body, 0, $body.Length)
    $Stream.Flush()
}
function Read-Bytes([IO.Stream]$Stream, [int]$Length) {
    $buffer = [byte[]]::new($Length)
    $offset = 0
    while ($offset -lt $Length) {
        $read = $Stream.ReadAsync($buffer, $offset, $Length - $offset)
        if (-not $read.Wait(5000)) { throw 'Timed out waiting for relay frame.' }
        if ($read.Result -eq 0) { throw 'Unexpected EOF.' }
        $offset += $read.Result
    }
    return ,$buffer
}
function Read-Frame([IO.Stream]$Stream) {
    $header = Read-Bytes $Stream 4
    $length = [BitConverter]::ToInt32($header, 0)
    if ($length -le 0 -or $length -gt 4096) { throw 'Invalid frame size.' }
    $body = Read-Bytes $Stream $length
    return [Text.Encoding]::UTF8.GetString($body) | ConvertFrom-Json
}
try {
    $process.StartInfo.FileName = $exe
    $process.StartInfo.ArgumentList.Add('chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/')
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.CreateNoWindow = $true
    $process.StartInfo.RedirectStandardInput = $true
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    $started = $process.Start()
    if (-not $started) { throw 'Could not start relay.' }
    if (-not $pipe.WaitForConnectionAsync().Wait(5000)) { throw 'Relay did not connect.' }
    if ($MalformedFrame) {
        $header = [BitConverter]::GetBytes([int]4097)
        $process.StandardInput.BaseStream.Write($header, 0, 4)
        $process.StandardInput.BaseStream.Flush()
        if (-not $process.WaitForExit(5000) -or $process.ExitCode -ne 1) { throw 'Malformed frame was not rejected.' }
        Write-Output 'PASS: oversized native frame rejected without a crash dump or payload log.'
        return
    }
    Write-Frame $process.StandardInput.BaseStream @{ kind = 'hello'; browser = 'edge' }
    $hello = Read-Frame $pipe
    if ($hello.kind -ne 'hello' -or $hello.browser -ne 'edge') { throw 'Bad browser-to-Collector relay.' }
    $nonce = [Guid]::NewGuid().ToString('N')
    Write-Frame $pipe @{ kind = 'probe'; requestId = $nonce }
    $probe = Read-Frame $process.StandardOutput.BaseStream
    if ($probe.kind -ne 'probe' -or $probe.requestId -ne $nonce) { throw 'Bad Collector-to-browser relay.' }
    Write-Frame $process.StandardInput.BaseStream @{ kind = 'sample'; requestId = $nonce; focused = $true; serviceId = 'youtube' }
    $sample = Read-Frame $pipe
    if ($sample.serviceId -ne 'youtube' -or $sample.requestId -ne $nonce) { throw 'Bad sample relay.' }
    $pipe.Dispose()
    # Keep stdin open, just like a browser after the Collector has stopped.
    if (-not $process.WaitForExit(5000)) { throw 'Relay hung on open browser stdin after Collector disconnected.' }
    if ($process.ExitCode -ne 0) { throw "Relay failed: exit $($process.ExitCode)." }
    if ($process.StandardError.ReadToEnd()) { throw 'Unexpected relay diagnostics.' }
    Write-Output 'PASS: real native host bidirectional framing and exit on Collector disconnect.'
}
finally {
    $pipe.Dispose()
    if ($started -and -not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $process.Dispose()
}
