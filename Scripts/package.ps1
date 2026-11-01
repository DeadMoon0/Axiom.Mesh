<#
.SYNOPSIS
    Builds the release zip of one platform: Native AOT publish and smoke of the Host, the command line, the chat and the sync service, then
    axiom-mesh-<version>-<rid>.zip and its SHA256SUMS line.

.DESCRIPTION
    The release workflow (release.yml) and the install-script job (install-tests.yml) run this on win-x64; package.sh is
    the same on Linux. It runs Scripts/aot-smoke.ps1 (publish with IlcTreatWarningsAsErrors, then the smoke), puts
    axiom-meshd.exe, axiom-mesh.exe, axiom-mesh-chat.exe, axiom-mesh-sync.exe, LICENSE and a short README.txt into the zip, and writes SHA256SUMS beside it with
    one line in the format of sha256sum ("<hex>  <file>").

.PARAMETER Runtime
    The runtime identifier, win-x64 by default.

.PARAMETER Output
    Where the zip and SHA256SUMS go.

.PARAMETER Version
    The version in the zip's name; by default the <Version> of the projects.
#>
#Requires -Version 7.0
param(
    [string] $Runtime = "win-x64",
    [Parameter(Mandatory = $true)] [string] $Output,
    [string] $Version = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ($Version -eq "") {
    $Version = ([xml](Get-Content (Join-Path $root "Apps/Axiom.Mesh.Cli/Axiom.Mesh.Cli.csproj") -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
$exe = if ($Runtime.StartsWith("win")) { ".exe" } else { "" }
New-Item -ItemType Directory -Force -Path $Output | Out-Null
$Output = (Resolve-Path $Output).Path
$work = Join-Path $Output "publish-$Runtime"

& (Join-Path $PSScriptRoot "aot-smoke.ps1") -Runtime $Runtime -Output $work
if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) { throw "the AOT publish or smoke failed" }

$name = "axiom-mesh-$Version-$Runtime.zip"
$stage = Join-Path $Output "stage-$Runtime"
Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item (Join-Path $work "host/axiom-meshd$exe") $stage
Copy-Item (Join-Path $work "cli/axiom-mesh$exe") $stage
Copy-Item (Join-Path $work "chat/axiom-mesh-chat$exe") $stage
Copy-Item (Join-Path $work "sync/axiom-mesh-sync$exe") $stage
Copy-Item (Join-Path $root "LICENSE") $stage
Copy-Item (Join-Path $PSScriptRoot "zip-readme.txt") (Join-Path $stage "README.txt")

$zip = Join-Path $Output $name
Remove-Item -Force $zip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
$hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
Set-Content -Path (Join-Path $Output "SHA256SUMS") -Value "$hash  $name" -Encoding ascii
Remove-Item -Recurse -Force $stage
Write-Host "== packaged $zip ($hash)"
