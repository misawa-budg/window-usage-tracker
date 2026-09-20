[CmdletBinding()]
param(
    [string]$OutputRoot = (Join-Path $PSScriptRoot '../artifacts/edge-user-diagnostics'),
    [switch]$CheckOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Run manually from the desktop, so the check uses the user's launch context.
# No registry/policy changes, process termination, remote debugging, or profile copy.
if (-not $CheckOnly -and @(Get-Process -Name msedge -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'Edge is still running (possibly in the background). Save your work and fully exit Edge, then run this file again. No process was stopped.'
}

$edgePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft/Edge/Application/msedge.exe'
if (-not (Test-Path -LiteralPath $edgePath -PathType Leaf)) {
    $edgePath = Join-Path $env:ProgramFiles 'Microsoft/Edge/Application/msedge.exe'
}
if (-not (Test-Path -LiteralPath $edgePath -PathType Leaf)) { throw 'Microsoft Edge was not found.' }

$runName = '{0}-{1}' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), ([Guid]::NewGuid().ToString('N').Substring(0, 6))
$output = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) $runName
New-Item -ItemType Directory -Path $output | Out-Null
$registrations = foreach ($hive in @([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryHive]::LocalMachine)) {
    foreach ($view in @([Microsoft.Win32.RegistryView]::Registry32, [Microsoft.Win32.RegistryView]::Registry64)) {
        $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey($hive, $view)
        $key = $null
        try {
            $key = $baseKey.OpenSubKey('Software\Microsoft\Edge\NativeMessagingHosts\com.wintracker.browser')
            $manifestPath = if ($key) { $key.GetValue('') } else { $null }
            $entry = [ordered]@{ hive = "$hive"; view = "$view"; registered = ($null -ne $key);
                manifestPath = $manifestPath; manifestReadable = $false; hostExists = $false }
            if ($manifestPath -is [string] -and $manifestPath.Length -gt 0) {
                try {
                    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
                    $entry.manifestReadable = $true
                    $entry.hostExists = Test-Path -LiteralPath $manifest.path -PathType Leaf
                } catch { # Report only the boolean failure, not an arbitrary file payload.
                }
            }
            [pscustomobject]$entry
        } finally {
            if ($key) { $key.Dispose() }
            $baseKey.Dispose()
        }
    }
}
$report = [ordered]@{
    capturedAt = [DateTimeOffset]::Now.ToString('o')
    userSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    edgeVersion = (Get-Item -LiteralPath $edgePath).VersionInfo.ProductVersion
    registrations = @($registrations)
}
[IO.File]::WriteAllText((Join-Path $output 'registration.json'), ($report | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
Write-Host "Diagnostic folder: $output"
if ($CheckOnly) {
    Write-Host 'Registration check only. Edge was not started.'
    return
}

Write-Host 'The local browser log may contain URLs and paths. Do not upload the full log.'
Write-Host 'In WinTracker, click Retry, wait a few seconds, then Refresh. Afterwards fully exit this Edge instance to stop logging.'
Write-Host 'Use your normal Edge shortcut after the test. No permanent logging setting is changed.'
# An interactive launch explicitly requested by the user; uses their existing profile.
$edgeArguments = @('--enable-logging', '--log-level=1', ('--log-file="' + (Join-Path $output 'edge.log') + '"'))
Start-Process -FilePath $edgePath -ArgumentList $edgeArguments | Out-Null
