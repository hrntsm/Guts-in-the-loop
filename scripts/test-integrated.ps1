$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $env:DOTNET_CLI_HOME = "$root/.cache/tools/dotnet"
    & dotnet build tests/IntegratedSmoke/IntegratedSmoke.csproj -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Integrated smoke build failed.' }
    & dotnet tests/IntegratedSmoke/bin/Release/net10.0-windows/IntegratedSmoke.dll
    if ($LASTEXITCODE -ne 0) { throw 'Integrated smoke verification failed.' }
} finally { Pop-Location }
