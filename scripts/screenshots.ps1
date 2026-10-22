<#
.SYNOPSIS
  Renders UI screenshots headlessly into docs/screenshots.
#>
param([string]$Output = "")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Output)) { $Output = Join-Path $root "docs/screenshots" }
dotnet run --project (Join-Path $root "tools/ScreenshotTool/ScreenshotTool.csproj") -c Release -- $Output
