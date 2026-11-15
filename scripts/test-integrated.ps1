$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $env:DOTNET_CLI_HOME = "$root/tools/dotnet"
    & dotnet build smoke/IntegratedSmoke/IntegratedSmoke.csproj -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Integrated smoke build failed.' }
    & dotnet smoke/IntegratedSmoke/bin/Release/net10.0-windows/IntegratedSmoke.dll
    if ($LASTEXITCODE -ne 0) { throw 'Integrated smoke verification failed.' }
} finally { Pop-Location }
