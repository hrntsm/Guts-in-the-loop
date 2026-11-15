param([string] $LiveReferencePath)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$previousKey = $env:OPENAI_API_KEY
Push-Location $root
try {
    $env:DOTNET_CLI_HOME = "$root/.cache/tools/dotnet"
    & dotnet build tests/RenderingSmoke/RenderingSmoke.csproj -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Rendering smoke build failed.' }
    $check = 'tests/RenderingSmoke/bin/Release/net10.0-windows/RenderingSmoke.dll'
    if ($LiveReferencePath) {
        $registeredKey = [Environment]::GetEnvironmentVariable('OPENAI_API_KEY', 'User')
        if (![string]::IsNullOrWhiteSpace($registeredKey)) { $env:OPENAI_API_KEY = $registeredKey }
        & dotnet $check --live $LiveReferencePath
    } else {
        & dotnet $check
    }
    if ($LASTEXITCODE -ne 0) { throw 'Rendering smoke verification failed.' }
} finally {
    $env:OPENAI_API_KEY = $previousKey
    Pop-Location
}
