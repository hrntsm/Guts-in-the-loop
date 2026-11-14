$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    function Invoke-Git([string[]] $Arguments) {
        & git @Arguments
        if ($LASTEXITCODE -ne 0) { throw "git failed: $Arguments" }
    }
    if (!(Test-Path third_party/sequential-gallery/.git)) {
        Invoke-Git -Arguments @('clone', 'https://github.com/yuki-koyama/sequential-gallery.git', 'third_party/sequential-gallery')
    }
    Invoke-Git -Arguments @('-C', 'third_party/sequential-gallery', 'checkout', 'cfbf7955ad4b47bb3d3a6854d0b93f643bf20f61')
    Invoke-Git -Arguments @('-C', 'third_party/sequential-gallery', 'submodule', 'update', '--init', 'external/sequential-line-search')
    Invoke-Git -Arguments @('-C', 'third_party/sequential-gallery/external/sequential-line-search', 'submodule', 'update', '--init',
        'external/timer', 'external/mathtoolbox', 'external/nlopt', 'external/nlopt-util', 'external/parallel-util')
    if (!(Test-Path third_party/eigen/.git)) {
        Invoke-Git -Arguments @('clone', 'https://github.com/eigenteam/eigen-git-mirror.git', 'third_party/eigen')
    }
    Invoke-Git -Arguments @('-C', 'third_party/eigen', 'checkout', 'cf794d3b741a6278df169e58461f8529f43bce5d')
    if (!(Test-Path tools/cmake-3.31.6-windows-x86_64/bin/cmake.exe)) {
        New-Item -ItemType Directory -Force tools | Out-Null
        Invoke-WebRequest 'https://github.com/Kitware/CMake/releases/download/v3.31.6/cmake-3.31.6-windows-x86_64.zip' -OutFile tools/cmake.zip
        Expand-Archive tools/cmake.zip -DestinationPath tools -Force
    }
} finally { Pop-Location }
