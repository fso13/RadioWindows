<#
.SYNOPSIS
  Produces release notes from CHANGELOG.md plus download links and checksums.
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$OutFile
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$changelog = Join-Path $root "CHANGELOG.md"
$repo = "fso13/RadioWindows"
$base = "https://github.com/$repo/releases/latest/download"

$notes = @()
$found = $false
if (Test-Path $changelog) {
    foreach ($line in Get-Content $changelog) {
        if ($line -match "^##\s*\[?$([regex]::Escape($Version))\]?") { $found = $true; $notes += "## История изменений"; continue }
        if ($found -and $line -match "^##\s*\[") { break }
        if ($found) { $notes += $line }
    }
}

if (-not $found) {
    $notes += "Полный список изменений: [CHANGELOG.md](https://github.com/$repo/blob/main/CHANGELOG.md)."
}

$downloads = @(
    "",
    "## Скачать для Windows",
    "",
    "| Файл | Что это |",
    "| --- | --- |",
    "| [RadioWindows-Setup.exe]($base/RadioWindows-Setup.exe) | Установщик (рекомендуется) |",
    "| [RadioWindows-Portable.zip]($base/RadioWindows-Portable.zip) | Портативная сборка без установки |",
    "| [SHA256SUMS.txt]($base/SHA256SUMS.txt) | Контрольные суммы |",
    "",
    "Установщик не требует прав администратора: приложение ставится в профиль пользователя.",
    "Сборка self-contained: отдельно ставить .NET не нужно.",
    "",
    "Если SmartScreen предупреждает о неизвестном издателе, нажмите «Подробнее» → «Выполнить в любом случае»:",
    "сборка не подписана сертификатом EV."
)

$content = (@($notes) + @($downloads)) -join [Environment]::NewLine
Set-Content -Path $OutFile -Value $content -Encoding utf8
Write-Host "Release notes written to $OutFile"
