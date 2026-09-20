[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$OutputRoot)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

foreach ($mode in @("fd", "sc")) {
    foreach ($kind in @("collector", "viewer", "window-usage-tracker-portable")) {
        $name = "$kind-win-x64-$mode.zip"
        $path = Join-Path $OutputRoot $name
        $zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $path).Path)
        try {
            $names = @($zip.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
            $required = @("README.md", "LICENSE", "docs/verification.md", "docs/usage.md")
            if ($kind -eq "window-usage-tracker-portable") {
                $required += @("collector/WinTracker.Collector.exe", "viewer/WinTracker.Viewer.exe",
                    "browser-host/WinTracker.BrowserHost.exe", "browser-extension/manifest.json",
                    "browser-extension/background.js", "browser-extension/services.js",
                    "browser-extension/diagnostics.js", "browser-extension/popup.html", "browser-extension/popup.js",
                    "scripts/Register-BrowserHost.ps1", "docs/browser-services.md",
                    "collector.settings.json", "Run-Collector.cmd", "Run-Viewer.cmd", "Run-Demo.cmd", "Stop-Collector.cmd")
                $viewerPrefix = "viewer/"
            }
            else {
                $app = if ($kind -eq "viewer") { "Viewer" } else { "Collector" }
                $required += "WinTracker.$app.exe"
                $viewerPrefix = ""
            }
            foreach ($entry in $required) {
                if ($names -notcontains $entry) { throw "${name}: missing $entry" }
            }
            if ($names | Where-Object { $_ -match '\.(db|sqlite|sqlite3)(-wal|-shm|-journal)?$|\.(jsonl|log)$|(^|/)\.env($|\.)' }) {
                throw "${name}: database, log or environment file found"
            }
            if (-not ($names | Where-Object { $_ -match '(^|/)licenses/.+\.nuspec$' })) {
                throw "${name}: dependency notices missing"
            }
            if ($names | Where-Object { $_ -match '(^|/)native-host[^/]*\.json$' }) {
                throw "${name}: personal browser registration found"
            }
            foreach ($settingsEntry in @($zip.Entries | Where-Object {
                $_.FullName.Replace('\', '/') -match '(^|/)collector\.settings\.json$'
            })) {
                $reader = [IO.StreamReader]::new($settingsEntry.Open())
                try { $settings = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
                foreach ($flag in @('enableBrowserTracking', 'storeBrowserHostnames', 'storeWindowTitles')) {
                    if ($settings.PSObject.Properties.Name -notcontains $flag -or $settings.$flag -cne $false) {
                        throw "${name}: $flag must explicitly default to false"
                    }
                }
            }
            if ($kind -eq 'window-usage-tracker-portable') {
                $versions = foreach ($metadata in @('browser-extension/manifest.json',
                    'collector/WinTracker.Collector.deps.json', 'viewer/WinTracker.Viewer.deps.json',
                    'browser-host/WinTracker.BrowserHost.deps.json')) {
                    $entry = $zip.Entries | Where-Object { $_.FullName.Replace('\', '/') -eq $metadata }
                    if (-not $entry) { throw "${name}: missing $metadata" }
                    $reader = [IO.StreamReader]::new($entry.Open())
                    try { $data = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
                    if ($metadata -like '*.deps.json') {
                        $library = @($data.libraries.PSObject.Properties.Name | Where-Object { $_ -match '^WinTracker\.(Collector|Viewer|BrowserHost)/' })
                        if ($library.Count -ne 1) { throw "${name}: ambiguous application version" }
                        $library[0].Split('/')[1]
                    } else { $data.version }
                }
                if (@($versions | Select-Object -Unique).Count -ne 1) { throw "${name}: application and extension versions differ" }
            }
            if ($kind -ne "collector") {
                $entry = $zip.Entries | Where-Object { $_.FullName.Replace('\', '/') -eq "${viewerPrefix}WinTracker.Viewer.runtimeconfig.json" }
                $reader = [IO.StreamReader]::new($entry.Open())
                try { $config = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
                $included = $config.runtimeOptions.PSObject.Properties.Name -contains "includedFrameworks"
                if ($included -ne ($mode -eq "sc")) { throw "${name}: wrong runtime mode" }
                if ($mode -eq "sc" -and $names -notcontains "${viewerPrefix}Microsoft.ui.xaml.dll") {
                    throw "${name}: self-contained WinUI runtime missing"
                }
            }
            [pscustomobject]@{ Package = $name; Entries = $names.Count; SHA256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
        }
        finally { $zip.Dispose() }
    }
}
