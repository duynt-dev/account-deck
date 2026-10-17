<#
.SYNOPSIS
    Builds the AccountDeck release packages for GitHub Releases.

.DESCRIPTION
    Produces, in artifacts\<version>\:
      AccountDeck-<version>-win-x64.zip           framework-dependent (needs .NET 9 Desktop Runtime)
      AccountDeck-<version>-win-x64-portable.zip  self-contained, runs anywhere
      SHA256SUMS.txt                              checksums of both zips
    The version is read from src\AccountDeck\AccountDeck.csproj.

.EXAMPLE
    .\scripts\build-release.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\AccountDeck\AccountDeck.csproj'
$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> found in $project" }

$out = Join-Path $root "artifacts\$version"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

$variants = @(
    @{ Name = "AccountDeck-$version-$Runtime";          SelfContained = 'false' },
    @{ Name = "AccountDeck-$version-$Runtime-portable"; SelfContained = 'true' }
)

foreach ($v in $variants) {
    $stage = Join-Path $out "stage\$($v.Name)"
    Write-Host "Publishing $($v.Name)..." -ForegroundColor Cyan
    dotnet publish $project -c $Configuration -r $Runtime -nologo -v q `
        -p:SelfContained=$($v.SelfContained) `
        -p:PublishSingleFile=true `
        -p:EnableCompressionInSingleFile=$($v.SelfContained) `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=none `
        -o $stage
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $($v.Name)" }

    Copy-Item (Join-Path $root 'LICENSE') $stage
    Copy-Item (Join-Path $root 'README.md') $stage
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath (Join-Path $out "$($v.Name).zip") -CompressionLevel Optimal
}

Remove-Item (Join-Path $out 'stage') -Recurse -Force

$sums = Get-ChildItem $out -Filter *.zip | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}
$sums | Set-Content (Join-Path $out 'SHA256SUMS.txt') -Encoding ascii

Write-Host "`nRelease $version ready in $out" -ForegroundColor Green
Get-ChildItem $out | ForEach-Object { '  {0,-48} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }
