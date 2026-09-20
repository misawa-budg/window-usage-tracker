[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = ("artifacts/release-{0}" -f (Get-Date -Format "yyyyMMdd-HHmmss")),
    [switch]$NoZip,
    [bool]$StopRunningApps = $false
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

function Invoke-DotNetPublish {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$PublishDir,
        [Parameter(Mandatory = $true)][bool]$SelfContained,
        [string[]]$ExtraMsbuildProps = @()
    )

    if (Test-Path -LiteralPath $PublishDir) {
        throw "Refusing to overwrite existing directory: $PublishDir. Choose a new OutputRoot."
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
        [Parameter(Mandatory = $true)][string]$BuildOutputDir,
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
        "-p:OutDir=$BuildOutputDir\\"
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

    if (Test-Path -LiteralPath $DestinationDir) {
        throw "Refusing to overwrite existing directory: $DestinationDir. Choose a new OutputRoot."
    }

    New-Item -Path $DestinationDir -ItemType Directory -Force | Out-Null
    Invoke-WithRetry `
        -Description "Copy files $SourceDir -> $DestinationDir" `
        -Action { Copy-Item -Path (Join-Path $SourceDir "*") -Destination $DestinationDir -Recurse -Force }
}

function Copy-PackageDocumentation {
    param([Parameter(Mandatory = $true)][string]$DestinationDir)

    foreach ($file in @("README.md", "LICENSE")) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $DestinationDir
    }
    $docsDir = Join-Path $DestinationDir "docs"
    New-Item -Path $docsDir -ItemType Directory -Force | Out-Null
    foreach ($file in @("architecture.md", "verification.md", "interview-notes.md", "browser-services.md")) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "docs/$file") -Destination $docsDir
    }
}

function Copy-DependencyNotices {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$PackageDir,
        [Parameter(Mandatory = $true)][string]$ApplicationName
    )

    $assetsPath = Join-Path (Split-Path $ProjectPath -Parent) "obj/project.assets.json"
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
    $packagePaths = @($assets.libraries.PSObject.Properties | Where-Object {
        $_.Value.type -eq "package"
    } | ForEach-Object { $_.Value.path })
    $runtimeConfig = Get-Content -LiteralPath (Join-Path $PackageDir "$ApplicationName.runtimeconfig.json") -Raw | ConvertFrom-Json
    if ($runtimeConfig.runtimeOptions.PSObject.Properties.Name -contains "includedFrameworks") {
        foreach ($framework in $runtimeConfig.runtimeOptions.includedFrameworks) {
            $packagePaths += "$($framework.name.ToLowerInvariant()).runtime.$Runtime/$($framework.version)"
        }
    }
    foreach ($packagePath in ($packagePaths | Sort-Object -Unique)) {
        $source = $assets.packageFolders.PSObject.Properties.Name | ForEach-Object {
            Join-Path $_ $packagePath
        } | Where-Object { Test-Path -LiteralPath $_ -PathType Container } | Select-Object -First 1
        if (-not $source) { throw "Dependency package not found: $packagePath" }
        $destination = Join-Path $PackageDir "licenses/$packagePath"
        New-Item -Path $destination -ItemType Directory -Force | Out-Null
        # Preserve publisher-provided notices and license metadata, without shipping caches.
        Get-ChildItem -LiteralPath $source -File | Where-Object {
            $_.Name -match '^(licen[sc]e|copying|copyright|third.party.notices)' -or $_.Extension -eq '.nuspec'
        } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $destination }
    }
}

function New-ZipPackage {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDir,
        [Parameter(Mandatory = $true)][string]$ZipPath
    )

    if (Test-Path -LiteralPath $ZipPath) {
        throw "Refusing to overwrite existing archive: $ZipPath"
    }

    # Never publish usage databases or local event/diagnostic logs.
    $privateFiles = @(Get-ChildItem -LiteralPath $SourceDir -Recurse -File -Force | Where-Object {
        $_.Name -match '\.(db|sqlite|sqlite3)(-wal|-shm|-journal)?$|\.(jsonl|log)$'
    })
    if ($privateFiles.Count -gt 0) {
        throw "Package contains database/log files. Refusing to create archive."
    }

    Invoke-WithRetry `
        -Description "Create zip $ZipPath" `
        -Action { Compress-Archive -Path (Join-Path $SourceDir "*") -DestinationPath $ZipPath -Force }
    Write-Host "Created: $ZipPath"
}

function New-PortableBundle {
    param(
        [Parameter(Mandatory = $true)][string]$ResolvedOutputRoot,
        [Parameter(Mandatory = $true)][string]$RuntimeIdentifier,
        [Parameter(Mandatory = $true)][string]$ModeSuffix
    )

    $collectorPackageDir = Join-Path $ResolvedOutputRoot ("collector-{0}-{1}" -f $RuntimeIdentifier, $ModeSuffix)
    $viewerPackageDir = Join-Path $ResolvedOutputRoot ("viewer-{0}-{1}" -f $RuntimeIdentifier, $ModeSuffix)
    if (-not (Test-Path $collectorPackageDir)) {
        throw "Collector package directory not found: $collectorPackageDir"
    }

    if (-not (Test-Path $viewerPackageDir)) {
        throw "Viewer package directory not found: $viewerPackageDir"
    }

    $bundleName = "window-usage-tracker-portable-{0}-{1}" -f $RuntimeIdentifier, $ModeSuffix
    $bundleDir = Join-Path $ResolvedOutputRoot $bundleName
    if (Test-Path -LiteralPath $bundleDir) {
        throw "Refusing to overwrite existing directory: $bundleDir. Choose a new OutputRoot."
    }

    New-Item -Path $bundleDir -ItemType Directory -Force | Out-Null
    Copy-DirectoryContents -SourceDir $collectorPackageDir -DestinationDir (Join-Path $bundleDir "collector")
    Copy-DirectoryContents -SourceDir $viewerPackageDir -DestinationDir (Join-Path $bundleDir "viewer")
    $hostProject = Join-Path $PSScriptRoot "WinTracker.BrowserHost/WinTracker.BrowserHost.csproj"
    $hostDir = Join-Path $bundleDir "browser-host"
    Invoke-DotNetPublish -ProjectPath $hostProject -PublishDir $hostDir -SelfContained ($ModeSuffix -eq "sc")
    Copy-DependencyNotices -ProjectPath $hostProject -PackageDir $hostDir -ApplicationName "WinTracker.BrowserHost"
    $extensionDir = Join-Path $bundleDir "browser-extension"
    New-Item -Path $extensionDir -ItemType Directory | Out-Null
    foreach ($file in @("manifest.json", "background.js", "services.js")) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "browser-extension/$file") -Destination $extensionDir
    }
    $scriptsDir = Join-Path $bundleDir "scripts"
    New-Item -Path $scriptsDir -ItemType Directory | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "scripts/Register-BrowserHost.ps1") -Destination $scriptsDir
    New-Item -Path (Join-Path $bundleDir "data") -ItemType Directory -Force | Out-Null

    $settingsSource = Join-Path $collectorPackageDir "collector.settings.json"
    if (Test-Path $settingsSource) {
        Copy-Item -Path $settingsSource -Destination (Join-Path $bundleDir "collector.settings.json") -Force
    }

    $collectorLauncher = @(
        "@echo off"
        "setlocal"
        "set ""WINTRACKER_HOME=%~dp0"""
        "cd /d ""%~dp0"""
        ".\collector\WinTracker.Collector.exe"
    )
    Set-Content -Path (Join-Path $bundleDir "Run-Collector.cmd") -Value $collectorLauncher -Encoding ASCII

    $viewerLauncher = @(
        "@echo off"
        "setlocal"
        "set ""WINTRACKER_HOME=%~dp0"""
        "cd /d ""%~dp0"""
        "start """" .\viewer\WinTracker.Viewer.exe"
    )
    Set-Content -Path (Join-Path $bundleDir "Run-Viewer.cmd") -Value $viewerLauncher -Encoding ASCII

    $stopLauncher = @(
        "@echo off"
        "cd /d ""%~dp0"""
        ".\collector\WinTracker.Collector.exe --stop"
        "pause"
    )
    Set-Content -Path (Join-Path $bundleDir "Stop-Collector.cmd") -Value $stopLauncher -Encoding ASCII

    $demoLauncher = @(
        "@echo off"
        "setlocal"
        "set ""WINTRACKER_HOME=%~dp0"""
        "cd /d ""%~dp0"""
        ".\collector\WinTracker.Collector.exe seed 1week services --demo --replace"
        "if errorlevel 1 exit /b %errorlevel%"
        "start """" .\viewer\WinTracker.Viewer.exe --demo"
    )
    Set-Content -Path (Join-Path $bundleDir "Run-Demo.cmd") -Value $demoLauncher -Encoding ASCII

    Copy-PackageDocumentation -DestinationDir $bundleDir

    if (-not $NoZip) {
        $zipPath = Join-Path $ResolvedOutputRoot ("{0}.zip" -f $bundleName)
        New-ZipPackage -SourceDir $bundleDir -ZipPath $zipPath
    }
}

$repoRoot = $PSScriptRoot
$resolvedOutputRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))
if ($StopRunningApps) { throw "Automatic process termination is disabled. Stop the collector gracefully with --stop." }
if ((Test-Path -LiteralPath $resolvedOutputRoot) -and
    (Get-ChildItem -LiteralPath $resolvedOutputRoot -Force | Select-Object -First 1)) {
    throw "OutputRoot is not empty. Existing packages and usage data will not be overwritten: $resolvedOutputRoot"
}
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
            $viewerBuildOutput = Join-Path $resolvedOutputRoot ("_build/viewer-{0}" -f $mode.Suffix)
            Invoke-DotNetBuildViewer `
                -ProjectPath $target.Project `
                -RuntimeIdentifier $Runtime `
                -BuildOutputDir $viewerBuildOutput `
                -SelfContained $mode.SelfContained `
                -ExtraMsbuildProps $extraProps

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

        Copy-PackageDocumentation -DestinationDir $publishDir
        $applicationName = if ($target.Name -eq "viewer") { "WinTracker.Viewer" } else { "WinTracker.Collector" }
        Copy-DependencyNotices -ProjectPath $target.Project -PackageDir $publishDir -ApplicationName $applicationName

        if (-not $NoZip) {
            $zipPath = Join-Path $resolvedOutputRoot ("{0}.zip" -f $packageName)
            New-ZipPackage -SourceDir $publishDir -ZipPath $zipPath
        }
    }
}

foreach ($mode in $modes) {
    New-PortableBundle `
        -ResolvedOutputRoot $resolvedOutputRoot `
        -RuntimeIdentifier $Runtime `
        -ModeSuffix $mode.Suffix
}

Write-Host ""
Write-Host "Done."
Write-Host "Output: $resolvedOutputRoot"

