[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "artifacts",
    [switch]$NoZip,
    [bool]$StopRunningApps = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-WithRetry {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Action,
        [Parameter(Mandatory = $true)][string]$Description,
        [int]$MaxAttempts = 8,
        [int]$DelaySeconds = 2
    )

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        try {
            & $Action
            return
        }
        catch {
            if ($attempt -ge $MaxAttempts) {
                throw
            }

            Write-Warning "$Description failed (attempt $attempt/$MaxAttempts): $($_.Exception.Message)"
            Start-Sleep -Seconds $DelaySeconds
        }
    }
}

function Stop-WinTrackerProcessesUnderPath {
    param(
        [Parameter(Mandatory = $true)][string]$RootPath
    )

    if (-not (Test-Path $RootPath)) {
        return
    }

    $resolvedRoot = (Resolve-Path $RootPath).Path.TrimEnd('\') + '\'
    $targetNames = @("WinTracker.Viewer", "WinTracker.Collector")
    $processes = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -in $targetNames }

    foreach ($process in $processes) {
        $exePath = $null
        try {
            $exePath = $process.MainModule.FileName
        }
        catch {
            continue
        }

        if ([string]::IsNullOrWhiteSpace($exePath)) {
            continue
        }

        if ($exePath.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) {
            Write-Host "Stopping process: $($process.ProcessName) (PID=$($process.Id))"
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-DotNetPublish {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$PublishDir,
        [Parameter(Mandatory = $true)][bool]$SelfContained,
        [string[]]$ExtraMsbuildProps = @()
    )

    if (Test-Path $PublishDir) {
        if ($StopRunningApps) {
            Stop-WinTrackerProcessesUnderPath -RootPath $PublishDir
        }

        Invoke-WithRetry `
            -Description "Remove publish directory $PublishDir" `
            -Action { Remove-Item -Path $PublishDir -Recurse -Force }
    }

    $selfContainedValue = if ($SelfContained) { "true" } else { "false" }
    Write-Host "Publishing $ProjectPath (self-contained=$selfContainedValue) -> $PublishDir"

    $publishArgs = @(
        "publish"
        $ProjectPath
        "-c"
        $Configuration
        "-r"
        $Runtime
        "--self-contained"
        $selfContainedValue
        "-o"
        $PublishDir
    )

    foreach ($prop in $ExtraMsbuildProps) {
        $publishArgs += "-p:$prop"
    }

    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed: $ProjectPath (self-contained=$selfContainedValue)"
    }

    if ($ExtraMsbuildProps.Count -gt 0) {
        Write-Host "MSBuild props: $($ExtraMsbuildProps -join ', ')"
    }
}

function Invoke-DotNetBuildViewer {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$RuntimeIdentifier,
        [Parameter(Mandatory = $true)][bool]$SelfContained,
        [string[]]$ExtraMsbuildProps = @()
    )

    $selfContainedValue = if ($SelfContained) { "true" } else { "false" }
    Write-Host "Building viewer $ProjectPath (self-contained=$selfContainedValue)"

    $buildArgs = @(
        "build"
        $ProjectPath
        "-c"
        $Configuration
        "-r"
        $RuntimeIdentifier
        "--self-contained"
        $selfContainedValue
    )

    foreach ($prop in $ExtraMsbuildProps) {
        $buildArgs += "-p:$prop"
    }

    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed: $ProjectPath (self-contained=$selfContainedValue)"
    }

    if ($ExtraMsbuildProps.Count -gt 0) {
        Write-Host "MSBuild props: $($ExtraMsbuildProps -join ', ')"
    }
}

function Copy-DirectoryContents {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDir,
        [Parameter(Mandatory = $true)][string]$DestinationDir
    )

    if (-not (Test-Path $SourceDir)) {
        throw "Source directory not found: $SourceDir"
    }

    if (Test-Path $DestinationDir) {
        if ($StopRunningApps) {
            Stop-WinTrackerProcessesUnderPath -RootPath $DestinationDir
        }

        Invoke-WithRetry `
            -Description "Remove directory $DestinationDir" `
            -Action { Remove-Item -Path $DestinationDir -Recurse -Force }
    }

    New-Item -Path $DestinationDir -ItemType Directory -Force | Out-Null
    Invoke-WithRetry `
        -Description "Copy files $SourceDir -> $DestinationDir" `
        -Action { Copy-Item -Path (Join-Path $SourceDir "*") -Destination $DestinationDir -Recurse -Force }
}

function New-ZipPackage {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDir,
        [Parameter(Mandatory = $true)][string]$ZipPath
    )

    if ($StopRunningApps) {
        Stop-WinTrackerProcessesUnderPath -RootPath $SourceDir
    }

    if (Test-Path $ZipPath) {
        Invoke-WithRetry `
            -Description "Remove existing zip $ZipPath" `
            -Action { Remove-Item -Path $ZipPath -Force }
    }

    Invoke-WithRetry `
        -Description "Create zip $ZipPath" `
        -Action { Compress-Archive -Path (Join-Path $SourceDir "*") -DestinationPath $ZipPath -Force }
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
        $extraProps = @()
        if ($target.Name -eq "viewer") {
            $extraProps += "WindowsAppSDKSelfContained=$(([bool]$mode.SelfContained).ToString().ToLowerInvariant())"
        }

        if ($target.Name -eq "viewer") {
            Invoke-DotNetBuildViewer `
                -ProjectPath $target.Project `
                -RuntimeIdentifier $Runtime `
                -SelfContained $mode.SelfContained `
                -ExtraMsbuildProps $extraProps

            $viewerBuildOutput = Join-Path $repoRoot "WinTracker.Viewer\bin\$Configuration\net8.0-windows10.0.19041.0\$Runtime"
            Copy-DirectoryContents -SourceDir $viewerBuildOutput -DestinationDir $publishDir
        }
        else {
            Invoke-DotNetPublish `
                -ProjectPath $target.Project `
                -PublishDir $publishDir `
                -SelfContained $mode.SelfContained `
                -ExtraMsbuildProps $extraProps
        }

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
