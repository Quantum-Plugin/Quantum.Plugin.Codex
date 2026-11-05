$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    dotnet clean src/Quantum.CodexPlugin/Quantum.CodexPlugin.csproj -c Release --verbosity quiet
    dotnet publish src/Quantum.CodexPlugin/Quantum.CodexPlugin.csproj -c Release --output artifacts/plugin -p:DebugType=None -p:DebugSymbols=false -p:ContinuousIntegrationBuild=true
    python .ci/package-plugin.py artifacts/plugin
} finally {
    Pop-Location
}
