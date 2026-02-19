[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "artifacts",
    [switch]$NoZip
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-DotNetPublish {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$PublishDir,
        [Parameter(Mandatory = $true)][bool]$SelfContained
    )

    if (Test-Path $PublishDir) {
        Remove-Item -Path $PublishDir -Recurse -Force
    }

    $selfContainedValue = if ($SelfContained) { "true" } else { "false" }
    Write-Host "Publishing $ProjectPath (self-contained=$selfContainedValue) -> $PublishDir"

    & dotnet publish $ProjectPath `
        -c $Configuration `
        -r $Runtime `
        --self-contained $selfContainedValue `
        -o $PublishDir

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed: $ProjectPath (self-contained=$selfContainedValue)"
    }
}

function New-ZipPackage {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDir,
        [Parameter(Mandatory = $true)][string]$ZipPath
    )

    if (Test-Path $ZipPath) {
        Remove-Item -Path $ZipPath -Force
    }

    Compress-Archive -Path (Join-Path $SourceDir "*") -DestinationPath $ZipPath -Force
    Write-Host "Created: $ZipPath"
}

$repoRoot = $PSScriptRoot
$resolvedOutputRoot = Join-Path $repoRoot $OutputRoot
New-Item -Path $resolvedOutputRoot -ItemType Directory -Force | Out-Null

$targets = @(
    @{
        Name = "collector"
        Project = Join-Path $repoRoot "WinTracker.Collector/WinTracker.Collector.csproj"
        SettingsFile = Join-Path $repoRoot "WinTracker.Collector/collector.settings.json"
    },
    @{
        Name = "viewer"
        Project = Join-Path $repoRoot "WinTracker.Viewer/WinTracker.Viewer.csproj"
        SettingsFile = $null
    }
)

$modes = @(
    @{ Suffix = "fd"; SelfContained = $false },
    @{ Suffix = "sc"; SelfContained = $true }
)

foreach ($target in $targets) {
    foreach ($mode in $modes) {
        $packageName = "{0}-{1}-{2}" -f $target.Name, $Runtime, $mode.Suffix
        $publishDir = Join-Path $resolvedOutputRoot $packageName
        Invoke-DotNetPublish -ProjectPath $target.Project -PublishDir $publishDir -SelfContained $mode.SelfContained

        if ($null -ne $target.SettingsFile -and (Test-Path $target.SettingsFile)) {
            Copy-Item -Path $target.SettingsFile -Destination (Join-Path $publishDir "collector.settings.json") -Force
            New-Item -Path (Join-Path $publishDir "data") -ItemType Directory -Force | Out-Null
        }

        if (-not $NoZip) {
            $zipPath = Join-Path $resolvedOutputRoot ("{0}.zip" -f $packageName)
            New-ZipPackage -SourceDir $publishDir -ZipPath $zipPath
        }
    }
}

Write-Host ""
Write-Host "Done."
Write-Host "Output: $resolvedOutputRoot"
