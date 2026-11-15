param([switch] $IconTools)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    New-Item -ItemType Directory -Force .cache/third_party, .cache/tools | Out-Null
    function Invoke-Git([string[]] $Arguments) {
        & git @Arguments
        if ($LASTEXITCODE -ne 0) { throw "git failed: $Arguments" }
    }
    $spsSource = 'https://github.com/hrntsm/sequential-gallery.git'
    $spsCommit = 'c1adc392e8a455d9fc3994fe19852a080f79401a'
    if (!(Test-Path .cache/third_party/sequential-gallery/.git)) {
        Invoke-Git -Arguments @('clone', $spsSource, '.cache/third_party/sequential-gallery')
    }
    Invoke-Git -Arguments @('-C', '.cache/third_party/sequential-gallery', 'remote', 'set-url', 'origin', $spsSource)
    & git -C .cache/third_party/sequential-gallery cat-file -e "$spsCommit^{commit}" 2>$null
    if ($LASTEXITCODE -ne 0) {
        Invoke-Git -Arguments @('-C', '.cache/third_party/sequential-gallery', 'fetch', 'origin', $spsCommit)
    }
    Invoke-Git -Arguments @('-C', '.cache/third_party/sequential-gallery', 'checkout', $spsCommit)
    Invoke-Git -Arguments @('-C', '.cache/third_party/sequential-gallery', 'submodule', 'update', '--init', 'external/sequential-line-search')
    Invoke-Git -Arguments @('-C', '.cache/third_party/sequential-gallery/external/sequential-line-search', 'submodule', 'update', '--init',
        'external/timer', 'external/mathtoolbox', 'external/nlopt', 'external/nlopt-util', 'external/parallel-util')
    if (!(Test-Path .cache/third_party/eigen/.git)) {
        Invoke-Git -Arguments @('clone', 'https://github.com/eigenteam/eigen-git-mirror.git', '.cache/third_party/eigen')
    }
    Invoke-Git -Arguments @('-C', '.cache/third_party/eigen', 'checkout', 'cf794d3b741a6278df169e58461f8529f43bce5d')
    if (!(Test-Path .cache/tools/cmake-3.31.6-windows-x86_64/bin/cmake.exe)) {
        Invoke-WebRequest 'https://github.com/Kitware/CMake/releases/download/v3.31.6/cmake-3.31.6-windows-x86_64.zip' -OutFile .cache/tools/cmake.zip
        Expand-Archive .cache/tools/cmake.zip -DestinationPath .cache/tools -Force
    }
    if ($IconTools) {
        & npm install --prefix .cache/tools/icon-renderer --no-save --package-lock=false '@resvg/resvg-js@2.6.2'
        if ($LASTEXITCODE -ne 0) { throw 'Icon renderer installation failed. Install Node.js and retry.' }
    }
} finally { Pop-Location }
