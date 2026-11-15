param([string] $RhinoInstallDir = 'C:\Program Files\Rhino 8', [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release', [switch] $Smoke)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $env:DOTNET_CLI_HOME = "$root/tools/dotnet"
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $cmake = Join-Path $root 'tools/cmake-3.31.6-windows-x86_64/bin/cmake.exe'
    if (!(Test-Path $cmake)) { throw 'Run scripts/bootstrap.ps1 first.' }
    $vswhere = "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe"
    $vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (!$vs) { throw 'Install Visual Studio with Desktop development with C++ and a Windows SDK.' }
    $vcvars = Join-Path $vs 'VC/Auxiliary/Build/vcvars64.bat'
    $command = "`"$vcvars`" >nul && `"$cmake`" -S native -B build/native -G `"NMake Makefiles`" -DCMAKE_BUILD_TYPE=Release && `"$cmake`" --build build/native --target SequentialGallery.Native"
    & cmd /d /c $command
    if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }
    & dotnet build src/SequentialGallery/SequentialGallery.csproj -c $Configuration --nologo "-p:RhinoInstallDir=$RhinoInstallDir"
    if ($LASTEXITCODE -ne 0) { throw 'Managed build failed.' }
    New-Item -ItemType Directory -Force artifacts/SequentialGallery | Out-Null
    Copy-Item "src/SequentialGallery/bin/$Configuration/net10.0-windows/SequentialGallery.gha" artifacts/SequentialGallery
    Copy-Item "src/SequentialGallery/bin/$Configuration/net10.0-windows/SequentialGallery.pdb" artifacts/SequentialGallery
    Copy-Item build/native/out/SequentialGallery.Native.dll artifacts/SequentialGallery
    Copy-Item THIRD_PARTY_NOTICES.md artifacts/SequentialGallery
    $dependencies = @{
        'sps' = 'third_party/sequential-gallery'
        'eigen' = 'third_party/eigen'
        'sequential-line-search' = 'third_party/sequential-gallery/external/sequential-line-search'
    }
    foreach ($name in @('mathtoolbox', 'nlopt', 'nlopt-util', 'parallel-util', 'timer')) {
        $dependencies[$name] = "third_party/sequential-gallery/external/sequential-line-search/external/$name"
    }
    foreach ($entry in $dependencies.GetEnumerator()) {
        $destination = "artifacts/SequentialGallery/licenses/$($entry.Key)"
        New-Item -ItemType Directory -Force $destination | Out-Null
        Get-ChildItem -LiteralPath $entry.Value -File | Where-Object { $_.Name -match '^(LICENSE|COPYING|COPYRIGHT)' } |
            Copy-Item -Destination $destination
    }
    if ($Smoke) {
        $windowCheck = "`"$vcvars`" >nul && `"$cmake`" --build build/native --target SequentialGallery.WindowSmoke"
        & cmd /d /c $windowCheck
        if ($LASTEXITCODE -ne 0) { throw 'Bounded-window smoke build failed.' }
        & ./build/native/out/SequentialGallery.WindowSmoke.exe
        if ($LASTEXITCODE -ne 0) { throw 'Bounded-window smoke check failed.' }
        & dotnet build smoke/NativeSmoke.csproj -c Release --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Smoke build failed.' }
        Copy-Item build/native/out/SequentialGallery.Native.dll smoke/bin/Release/net10.0-windows
        & dotnet smoke/bin/Release/net10.0-windows/NativeSmoke.dll
        if ($LASTEXITCODE -ne 0) { throw 'SPS smoke check failed.' }
    }
    Write-Host 'Plugin ready in artifacts/SequentialGallery'
} finally { Pop-Location }
