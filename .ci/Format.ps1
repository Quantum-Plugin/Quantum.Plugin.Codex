param([switch]$Verify)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$solution = Join-Path (Split-Path $PSScriptRoot -Parent) 'Quantum.Plugin.Codex.slnx'
$arguments = @('--no-restore')
if ($Verify) { $arguments += '--verify-no-changes' }
dotnet format whitespace $solution @arguments
dotnet format style $solution --severity info --diagnostics IDE0011 IDE0007 IDE0022 IDE0040 IDE0044 IDE0005 IDE0161 @arguments
