<#
.SYNOPSIS
    Installs Axiom.Mesh for the current user: axiom-meshd (the Host) and axiom-mesh (the command line).

.DESCRIPTION
    One line, in PowerShell:

        irm https://raw.githubusercontent.com/DeadMoon0/Axiom.Mesh/main/install.ps1 | iex

    With options (a script piped into iex takes none):

        & ([scriptblock]::Create((irm https://raw.githubusercontent.com/DeadMoon0/Axiom.Mesh/main/install.ps1))) -Version 0.1.0 -NoAutostart

    The script finds the platform (win-x64; Windows on Arm runs it emulated), downloads the release zip and SHA256SUMS
    from GitHub (the latest release, or -Version), verifies the zip's SHA-256 against SHA256SUMS, unpacks it to
    %LOCALAPPDATA%\Programs\Axiom.Mesh, adds that directory to the user's PATH, registers the Host to start at every
    logon (a per-user scheduled task, through 'axiom-mesh host install'; -NoAutostart opts out), starts the Host and
    prints 'axiom-mesh status'. Nothing needs administrator rights. -Uninstall reverses it and keeps the Host's data.

    The script itself is fetched from the branch main, which can change; the checksum protects the binaries, not the
    script. For the strict option, fetch the script from a release tag instead of main:
        https://raw.githubusercontent.com/DeadMoon0/Axiom.Mesh/v0.1.0/install.ps1

.PARAMETER Version
    The release to install, such as 0.1.0; by default the latest. Also read from AXIOM_MESH_VERSION.

.PARAMETER Source
    A local release zip to install instead of downloading one (CI and offline installs). SHA256SUMS beside it is
    checked when it exists.

.PARAMETER NoAutostart
    Do not register the Host to start at logon; start it once now. Also set by AXIOM_MESH_NO_AUTOSTART=1.

.PARAMETER InstallDir
    Where the executables go; %LOCALAPPDATA%\Programs\Axiom.Mesh by default.

.PARAMETER Uninstall
    Stop the Host, remove its logon start, the executables and the PATH entry. The Host's home (keys, store,
    config.json) stays.
#>
param(
    [string] $Version = $env:AXIOM_MESH_VERSION,
    [string] $Source = "",
    [switch] $NoAutostart,
    [string] $InstallDir = "",
    [switch] $Uninstall
)

$ErrorActionPreference = "Stop"
$1# Windows PowerShell 5.1 does not offer TLS 1.2 by default; GitHub needs it.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$Repository = "DeadMoon0/Axiom.Mesh"

# A failure ends the script with one line; throw, not exit, so a session that ran the script through iex stays open.
function Fail([string] $line) {
    throw "axiom-mesh install: $line"
}

# Runs a program with stderr folded into the output: Windows PowerShell 5.1 turns a native program's stderr into errors,
# which ErrorActionPreference Stop would make fatal. Returns the exit code; the output goes to the host when -Show.
function Invoke-Native([string] $program, [string[]] $arguments, [switch] $Show) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & $program @arguments 2>&1 | ForEach-Object { "$_" }
        if ($Show -and $null -ne $output) { $output | ForEach-Object { Write-Host $_ } }
        return $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }
}

# The runtime identifier of this machine; Windows on Arm runs the x64 build emulated.
function Get-Rid {
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    switch ($arch) {
        "X64" { return "win-x64" }
        "Arm64" { Write-Host "note: no win-arm64 build yet; installing win-x64, which Windows on Arm runs emulated"; return "win-x64" }
        default { Fail "no build for Windows on $arch" }
    }
}

# The SHA-256 SHA256SUMS lists for a file name ("<hex>  <name>" per line, as sha256sum writes it).
function Get-ListedHash([string] $sums, [string] $name) {
    foreach ($line in $sums -split "`r?`n") {
        $parts = $line.Trim() -split "\s+\*?", 2
        if ($parts.Count -eq 2 -and $parts[1] -eq $name) { return $parts[0].ToLowerInvariant() }
    }
    return $null
}

function Get-Download([string] $url, [string] $file) {
    try { Invoke-WebRequest -Uri $url -OutFile $file -UseBasicParsing }
    catch { Fail "cannot download $url ($($_.Exception.Message))" }
}

function Add-ToUserPath([string] $directory) {
    $current = [Environment]::GetEnvironmentVariable("Path", "User")
    $entries = @($current -split ";" | Where-Object { $_ -ne "" })
    if ($entries -notcontains $directory) {
        [Environment]::SetEnvironmentVariable("Path", (($entries + $directory) -join ";"), "User")
        Write-Host "added $directory to your PATH (new terminals see it)"
    }
    if (($env:Path -split ";") -notcontains $directory) { $env:Path = "$env:Path;$directory" }
}

function Remove-FromUserPath([string] $directory) {
    $current = [Environment]::GetEnvironmentVariable("Path", "User")
    $entries = @($current -split ";" | Where-Object { $_ -ne "" -and $_ -ne $directory })
    [Environment]::SetEnvironmentVariable("Path", ($entries -join ";"), "User")
}

# Ends a Host that runs from the install directory, so its files can be replaced or removed.
function Stop-InstalledHost([string] $daemon) {
    Get-Process -Name "axiom-meshd" -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $daemon } |
        ForEach-Object { Stop-Process -Id $_.Id -Force; $_.WaitForExit(5000) | Out-Null }
}

function Wait-ForHost([string] $cli, [int] $seconds) {
    for ($i = 0; $i -lt $seconds * 2; $i++) {
        if ((Invoke-Native $cli @("status")) -eq 0) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

if (-not ($IsWindows -or $env:OS -eq "Windows_NT")) { Fail "this script is for Windows; on Linux run install.sh" }
if ($InstallDir -eq "") { $InstallDir = Join-Path $env:LOCALAPPDATA "Programs\Axiom.Mesh" }
$daemon = Join-Path $InstallDir "axiom-meshd.exe"
$cli = Join-Path $InstallDir "axiom-mesh.exe"
if ($env:AXIOM_MESH_NO_AUTOSTART -eq "1") { $NoAutostart = $true }

if ($Uninstall) {
    if (Test-Path $cli) { Invoke-Native $cli @("host", "uninstall", "--daemon", $daemon) -Show | Out-Null }
    Stop-InstalledHost $daemon
    Remove-FromUserPath $InstallDir
    Remove-Item -Recurse -Force $InstallDir -ErrorAction SilentlyContinue
    Write-Host "Axiom.Mesh removed from $InstallDir. The Host's home (keys, store, config.json) is kept; delete it yourself to start anew."
    return
}

$rid = Get-Rid
$work = Join-Path ([System.IO.Path]::GetTempPath()) "axiom-mesh-install-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
New-Item -ItemType Directory -Path $work | Out-Null
try {
    if ($Source -ne "") {
        if (-not (Test-Path $Source)) { Fail "no file at $Source" }
        $zip = (Resolve-Path $Source).Path
        $name = Split-Path -Leaf $zip
        $sumsFile = Join-Path (Split-Path -Parent $zip) "SHA256SUMS"
        $sums = if (Test-Path $sumsFile) { Get-Content $sumsFile -Raw } else { $null }
    }
    else {
        if ([string]::IsNullOrWhiteSpace($Version)) {
            try { $Version = (Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/releases/latest" -UseBasicParsing).tag_name.TrimStart("v") }
            catch { Fail "cannot ask GitHub for the latest release ($($_.Exception.Message)); name one with -Version" }
        }
        $Version = $Version.TrimStart("v")
        $name = "axiom-mesh-$Version-$rid.zip"
        $base = "https://github.com/$Repository/releases/download/v$Version"
        $zip = Join-Path $work $name
        Write-Host "downloading Axiom.Mesh $Version for $rid"
        Get-Download "$base/$name" $zip
        Get-Download "$base/SHA256SUMS" (Join-Path $work "SHA256SUMS")
        $sums = Get-Content (Join-Path $work "SHA256SUMS") -Raw
    }

    $actual = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
    if ($null -ne $sums) {
        $listed = Get-ListedHash $sums $name
        if ($null -eq $listed) { Fail "SHA256SUMS lists no $name" }
        if ($listed -ne $actual) { Fail "$name does not match SHA256SUMS (expected $listed, got $actual); nothing was installed" }
        Write-Host "verified $name against SHA256SUMS"
    }
    else {
        Write-Host "note: no SHA256SUMS beside $name; the local zip is not verified"
    }

    $unpacked = Join-Path $work "unpacked"
    Expand-Archive -Path $zip -DestinationPath $unpacked
    if (-not (Test-Path (Join-Path $unpacked "axiom-meshd.exe")) -or -not (Test-Path (Join-Path $unpacked "axiom-mesh.exe"))) {
        Fail "$name holds no axiom-meshd.exe and axiom-mesh.exe"
    }

    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    $task = "\Axiom.Mesh\axiom-meshd"
    Invoke-Native "schtasks" @("/End", "/TN", $task) | Out-Null
    Stop-InstalledHost $daemon
    Copy-Item -Path (Join-Path $unpacked "*") -Destination $InstallDir -Force
    Write-Host "installed axiom-meshd and axiom-mesh to $InstallDir"
    Add-ToUserPath $InstallDir

    if ($NoAutostart) {
        Write-Host "not registering a start at logon (-NoAutostart); starting the Host once"
        Start-Process -FilePath $daemon -ArgumentList "run" -WindowStyle Hidden | Out-Null
    }
    else {
        if ((Invoke-Native $cli @("host", "install", "--daemon", $daemon) -Show) -ne 0) { Fail "registering the Host failed; run 'axiom-meshd run' yourself, or try 'axiom-mesh host install' again" }
    }

    if (-not (Wait-ForHost $cli 20)) {
        # The scheduled task may be refused by policy or delayed; the user still gets a running Host.
        Write-Host "note: the Host did not answer within 20 seconds; starting it directly"
        Start-Process -FilePath $daemon -ArgumentList "run" -WindowStyle Hidden | Out-Null
        if (-not (Wait-ForHost $cli 20)) { Fail "the Host does not answer; run 'axiom-meshd run' in a terminal to see why" }
    }

    Write-Host ""
    Invoke-Native $cli @("status") -Show | Out-Null
    Write-Host ""
    Write-Host "Next: axiom-mesh identity create --user <your name> --device <this device>   (then 'axiom-mesh help')"
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
