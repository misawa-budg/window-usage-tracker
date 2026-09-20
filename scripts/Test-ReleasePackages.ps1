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
            $required = @("README.md", "LICENSE", "docs/verification.md")
            if ($kind -eq "window-usage-tracker-portable") {
                $required += @("collector/WinTracker.Collector.exe", "viewer/WinTracker.Viewer.exe",
                    "browser-host/WinTracker.BrowserHost.exe", "browser-extension/manifest.json",
                    "browser-extension/background.js", "browser-extension/services.js",
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
