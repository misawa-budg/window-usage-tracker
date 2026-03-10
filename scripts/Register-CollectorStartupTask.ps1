[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CollectorExePath,

    [string]$TaskName = "WindowUsageTracker.Collector",

    [string]$WorkingDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$resolvedExePath = (Resolve-Path $CollectorExePath).Path
if (-not (Test-Path $resolvedExePath -PathType Leaf)) {
    throw "Collector executable not found: $CollectorExePath"
}

$collectorDirectory = Split-Path -Path $resolvedExePath -Parent
$bundleRoot = Split-Path -Path $collectorDirectory -Parent

if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) {
    $portableMarker = Join-Path $bundleRoot "Run-Collector.cmd"
    if (Test-Path $portableMarker) {
        $resolvedWorkingDirectory = $bundleRoot
    }
    else {
        $resolvedWorkingDirectory = $collectorDirectory
    }
}
else {
    $resolvedWorkingDirectory = (Resolve-Path $WorkingDirectory).Path
}
$currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name

$existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($null -ne $existing) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

$action = New-ScheduledTaskAction `
    -Execute $resolvedExePath `
    -Argument "--background" `
    -WorkingDirectory $resolvedWorkingDirectory

$trigger = New-ScheduledTaskTrigger -AtLogOn -User $currentUser

$principal = New-ScheduledTaskPrincipal `
    -UserId $currentUser `
    -LogonType Interactive `
    -RunLevel Limited

$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -MultipleInstances IgnoreNew

Register-ScheduledTask `
    -TaskName $TaskName `
    -Action $action `
    -Trigger $trigger `
    -Principal $principal `
    -Settings $settings `
    -Description "Start WindowUsageTracker collector at user logon."

Write-Host "Registered task: $TaskName"
Write-Host "Collector: $resolvedExePath"
Write-Host "Mode: --background"
Write-Host "WorkingDirectory: $resolvedWorkingDirectory"
