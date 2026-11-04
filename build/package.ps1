<#
.SYNOPSIS
  Собираетъ выпускъ «Аза»: испытанія → az.exe и Skriptorij.exe (самодостаточные, win-x64)
  → PDF-руководство → архивъ dist/Az-<версія>-win-x64.zip.
.EXAMPLE
  pwsh build/package.ps1 -Version 2.1
#>
param(
    [string]$Version = "2.0",
    [string]$OutDir = "dist",
    [switch]$SkipTests
)
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$name  = "Az-$Version"
$out   = Join-Path $root $OutDir
$stage = Join-Path $out $name
$tmp   = Join-Path $out "_publish"
Remove-Item $stage, $tmp -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage, $tmp | Out-Null

function Step($t) { Write-Host "`n☦ $t" -ForegroundColor DarkYellow }

if (-not $SkipTests) {
    Step "Эталонныя испытанія"
    dotnet run --project tests/Az.Tests -c Release "-p:Version=$Version"
}

$common = @("-c", "Release", "-r", "win-x64", "--self-contained", "true",
            "-p:PublishSingleFile=true", "-p:EnableCompressionInSingleFile=true",
            "-p:DebugType=none", "-p:Version=$Version", "--nologo")

Step "Трансляторъ az.exe"
dotnet publish src/az/az.csproj @common "-p:PublishTrimmed=true" -o "$tmp/az"   # консольный — можно обрѣзать (≈ 11 МБ вмѣсто 36)

Step "Среда Skriptorij.exe"
dotnet publish src/Skriptorij/Skriptorij.csproj @common "-p:IncludeNativeLibrariesForSelfExtract=true" -o "$tmp/ide"

Step "Руководство (PDF)"
New-Item -ItemType Directory -Force "$stage/docs" | Out-Null
$pdf = Join-Path $stage "docs/Азъ — руководство.pdf"
dotnet run --project tools/Manual -c Release "-p:Version=$Version" -- $pdf

Step "Составленіе выпуска"
Copy-Item "$tmp/az/az.exe" $stage
Copy-Item "$tmp/ide/Skriptorij.exe" $stage
Copy-Item "docs/синтаксисъ.md", "docs/иконы_и_покровители.md" "$stage/docs"
Copy-Item "examples" $stage -Recurse
Get-ChildItem "$stage/examples" -Recurse -Directory -Filter "собрано" | Remove-Item -Recurse -Force
New-Item -ItemType Directory -Force "$stage/licenses" | Out-Null
Copy-Item "src/Skriptorij/Assets/Fonts/OFL.txt" "$stage/licenses/Ponomar-OFL.txt"
Copy-Item "src/Skriptorij/Assets/Icons/CREDITS.md" "$stage/licenses/icons-CREDITS.md"
Copy-Item "build/licenses/AvalonEdit-MIT.txt" "$stage/licenses"
# Лицензіи .NET (вшитъ въ оба .exe) — изъ пакета среды выполненія, которымъ шла сборка
$pk = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $HOME ".nuget/packages" }
$rt = Get-ChildItem "$pk/microsoft.netcore.app.runtime.win-x64" -Directory |
      Sort-Object { [version]($_.Name -replace '-.*$', '') } | Select-Object -Last 1
Copy-Item "$($rt.FullName)/LICENSE.TXT" "$stage/licenses/dotnet-LICENSE.txt"
Copy-Item "$($rt.FullName)/THIRD-PARTY-NOTICES.TXT" "$stage/licenses/dotnet-THIRD-PARTY-NOTICES.txt"
Copy-Item "LICENSE" "$stage/LICENSE.txt"
(Get-Content "build/ПРОЧТИ.txt" -Raw -Encoding utf8).Replace("{{VERSION}}", $Version) |
    Set-Content "$stage/ПРОЧТИ.txt" -Encoding utf8

Step "Провѣрка: выпущенный az.exe собираетъ и запускаетъ примѣръ"
$hello = & "$stage/az.exe" пусти "$stage/examples/Адонаи/ядро/здравствꙋй_мїре.азъ"
if ($LASTEXITCODE -ne 0 -or -not ($hello -match "мі")) { throw "az.exe изъ выпуска не работаетъ: $hello" }
Write-Host $hello

Step "Архивъ"
$zip = Join-Path $out "$name-win-x64.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
"$hash  $(Split-Path $zip -Leaf)" | Set-Content "$zip.sha256" -Encoding ascii
Remove-Item $tmp -Recurse -Force

$size = "{0:N1} МБ" -f ((Get-Item $zip).Length / 1MB)
Write-Host "`nГотово: $zip ($size). Слава Богу!" -ForegroundColor Green
if ($env:GITHUB_OUTPUT) { "zip=$zip" >> $env:GITHUB_OUTPUT }
