[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)][ValidateSet('Edge', 'Chrome')][string]$Browser,
    [Parameter(Mandatory = $true)][string]$HostExecutable,
    [ValidatePattern('^[a-p]{32}$')][string[]]$ExtensionId,
    [switch]$Unregister
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $HostExecutable).Path
if ([IO.Path]::GetFileName($exe) -ne 'WinTracker.BrowserHost.exe') { throw 'Select WinTracker.BrowserHost.exe.' }
$directory = Split-Path -Parent $exe
$manifest = Join-Path $directory "native-host-$($Browser.ToLowerInvariant()).json"
$vendor = if ($Browser -eq 'Edge') { 'Microsoft\Edge' } else { 'Google\Chrome' }
$key = "HKCU:\Software\$vendor\NativeMessagingHosts\com.wintracker.browser"
$existing = if (Test-Path -LiteralPath $key) { (Get-Item -LiteralPath $key).GetValue('') } else { $null }
if ($existing -and $existing -ne $manifest) {
    throw "Another installation is registered at $existing. Unregister that installation first."
}
if ($Unregister) {
    if ($existing -and $PSCmdlet.ShouldProcess($key, 'Remove this user-only WinTracker registration')) {
        Remove-Item -LiteralPath $key
    }
    # Retain the manifest and binaries; never remove user folders or data.
    return
}
if (-not $ExtensionId -or $ExtensionId.Count -eq 0) { throw 'Supply the extension ID shown on the browser extensions page.' }
if ($PSCmdlet.ShouldProcess($key, "Register WinTracker for $Browser and write $manifest")) {
    $json = @{
        name = 'com.wintracker.browser'
        description = 'WinTracker local browser service bridge'
        path = $exe
        type = 'stdio'
        allowed_origins = @($ExtensionId | Sort-Object -Unique | ForEach-Object { "chrome-extension://$_/" })
    } | ConvertTo-Json -Depth 3
    [IO.File]::WriteAllText($manifest, $json, [Text.UTF8Encoding]::new($false))
    New-Item -Path $key -Force | Out-Null
    Set-Item -LiteralPath $key -Value $manifest
    Write-Output "Registered for the current user only: $Browser. Restart/reload the extension to connect."
}
