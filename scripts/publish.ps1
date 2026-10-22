<#
.SYNOPSIS
  Publishes a self-contained Windows build and, optionally, the installer.
.DESCRIPTION
  Produces dist\RadioWindows-Portable.zip, dist\RadioWindows-Setup.exe (with Inno
  Setup installed) and dist\SHA256SUMS.txt. Version defaults to the assembly version.
#>
param(
    [string]$Version = "",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    if ([string]::IsNullOrWhiteSpace($Version)) {
        [xml]$props = Get-Content "Directory.Build.props"
        $Version = $props.Project.PropertyGroup.Version
    }

    $publish = Join-Path $root "publish"
    $dist = Join-Path $root "dist"
    Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $dist | Out-Null

    dotnet publish "src/RadioWindows/RadioWindows.csproj" `
        -c Release -r win-x64 --self-contained true `
        -p:Version=$Version -p:FileVersion="$Version.0" -p:AssemblyVersion="$Version.0" `
        -p:PublishSingleFile=false -p:PublishTrimmed=false -p:PublishReadyToRun=true `
        -o $publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

    # The package ships both architectures; a win-x64 build never loads the x86 copy.
    $legacyVlc = Join-Path $publish "libvlc/win-x86"
    if (Test-Path $legacyVlc) { Remove-Item $legacyVlc -Recurse -Force }

    $zip = Join-Path $dist "RadioWindows-Portable.zip"
    Remove-Item $zip -Force -ErrorAction SilentlyContinue
    Compress-Archive -Path (Join-Path $publish "*") -DestinationPath $zip -CompressionLevel Optimal

    if (-not $SkipInstaller) {
        $iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
        if ($null -eq $iscc) {
            $candidates = @(
                "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
                "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
            )
            $iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
        }

        if ($null -ne $iscc) {
            $env:RADIO_VERSION = $Version
            $numeric = ($Version -split '-')[0]
            $exe = if ($iscc -is [string]) { $iscc } else { $iscc.Source }
            & $exe "/DMyAppVersion=$Version" "/DMyAppVersionInfo=$numeric" "installer/RadioWindows.iss"
            if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }
        }
        else {
            Write-Warning "Inno Setup 6 not found; only the portable archive was produced."
        }
    }

    $installer = Join-Path $dist "RadioWindows-Setup.exe"
    if (Test-Path $installer) {
        Copy-Item $installer (Join-Path $dist "RadioWindows-$Version-Setup.exe") -Force
    }

    Get-ChildItem $dist -File | Where-Object { $_.Name -ne "SHA256SUMS.txt" } | ForEach-Object {
        $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "{0}  {1}" -f $hash, $_.Name
    } | Set-Content (Join-Path $dist "SHA256SUMS.txt") -Encoding utf8

    Write-Host "Artifacts in $dist"
    Get-ChildItem $dist | Format-Table Name, Length
}
finally {
    Pop-Location
}
