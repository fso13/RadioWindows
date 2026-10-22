<#
.SYNOPSIS
  Builds and runs RadioWindows from source (window is shown immediately).
#>
param([switch]$Release)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$configuration = if ($Release) { "Release" } else { "Debug" }
dotnet run --project (Join-Path $root "src/RadioWindows/RadioWindows.csproj") -c $configuration -- --show
