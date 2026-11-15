param([string] $RhinoInstallDir = 'C:\Program Files\Rhino 8')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$yak = Join-Path $RhinoInstallDir 'System/Yak.exe'
if (!(Test-Path -LiteralPath $yak)) { throw "Yak was not found at $yak. Install Rhino or pass -RhinoInstallDir." }
$yak = (Get-Item -LiteralPath $yak).FullName

Push-Location $root
try {
    & ./scripts/build.ps1 -RhinoInstallDir $RhinoInstallDir -Configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    $stage = Join-Path $root ('build/yak/' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    Copy-Item -LiteralPath 'packaging/manifest.yml' -Destination $stage
    Copy-Item -LiteralPath 'artifacts/SequentialGallery/SequentialGallery.gha', 'artifacts/SequentialGallery/SequentialGallery.Native.dll', 'artifacts/SequentialGallery/THIRD_PARTY_NOTICES.md' -Destination $stage
    Copy-Item -LiteralPath 'artifacts/SequentialGallery/licenses' -Destination $stage -Recurse
    Copy-Item -LiteralPath 'LICENSE' -Destination (Join-Path $stage 'LICENSE.txt')
    Copy-Item -LiteralPath 'src/SequentialGallery/Resources/Gallery.png' -Destination (Join-Path $stage 'icon.png')
    Push-Location $stage
    try {
        & $yak build --platform win
        if ($LASTEXITCODE -ne 0) { throw 'Yak package build failed.' }
        $packages = @(Get-ChildItem -LiteralPath $stage -Filter '*.yak' -File)
        if ($packages.Count -ne 1) { throw 'Expected one Yak package in the clean staging directory.' }
        $output = Join-Path $root 'artifacts/yak'
        New-Item -ItemType Directory -Path $output -Force | Out-Null
        Copy-Item -LiteralPath $packages[0].FullName -Destination $output -Force
        Write-Host ('Created ' + (Join-Path $output $packages[0].Name))
    } finally { Pop-Location }
} finally { Pop-Location }
