param([string] $RhinoInstallDir = 'C:\Program Files\Rhino 8', [switch] $CreateSample)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$plugin = Join-Path $root 'artifacts/SequentialGallery'
if (!(Test-Path "$plugin/SequentialGallery.gha")) { throw 'Build the plugin first.' }
$previous = $env:RHINO_PACKAGE_DIRS
try {
    $env:RHINO_PACKAGE_DIRS = if ($previous) { "$plugin;$previous" } else { $plugin }
    $command = if ($CreateSample) {
        '_-RunPythonScript (' + (Join-Path $root 'samples/open_sample.py') + ')'
    } else { '_Grasshopper' }
    # Rhino is the interactive application being developed and debugged, so show its window.
    $process = Start-Process -FilePath "$RhinoInstallDir/System/Rhino.exe" -ArgumentList @('/netcore', '/nosplash', ('/runscript="' + $command + '"')) -WorkingDirectory $root -WindowStyle Normal -PassThru
    Write-Host "Rhino PID: $($process.Id)"
} finally { $env:RHINO_PACKAGE_DIRS = $previous }
