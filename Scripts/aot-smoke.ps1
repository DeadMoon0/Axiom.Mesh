<#
.SYNOPSIS
    Publishes the Host, the command line, the chat and the sync service as Native AOT for this machine and runs them together once.

.DESCRIPTION
    The smoke test of fundamentals plan §6 (install-tests.yml and release.yml run the same steps through Scripts/package.*, on linux-x64 and win-x64): both
    executables are published with PublishAot and IlcTreatWarningsAsErrors, the Host starts with a temporary home and its
    own pipe, and the command line creates an identity and a Channel, lists Packages, writes a message and reads the
    history, lists the services, then waits for the chat service the Host spawns and sends and shows a message through
    it, and waits for the sync service and adds a folder that becomes a Version. Any failure exits non-zero; the temporary home is removed either way.

.PARAMETER Runtime
    The runtime identifier to publish for; by default the one of this machine.

.PARAMETER Output
    Where to publish; by default a temporary directory.
#>
#Requires -Version 7.0
param(
    [string] $Runtime = "",
    [string] $Output = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ($Runtime -eq "") {
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    $os = if ($IsWindows -or $env:OS -eq "Windows_NT") { "win" } elseif ($IsMacOS) { "osx" } else { "linux" }
    $Runtime = "$os-$arch"
}
if ($Output -eq "") { $Output = Join-Path ([System.IO.Path]::GetTempPath()) "axiom-mesh-aot-$([guid]::NewGuid().ToString('N').Substring(0, 8))" }
$exe = if ($Runtime.StartsWith("win")) { ".exe" } else { "" }

# The Native AOT linker finds MSVC through vswhere, which the Visual Studio installer does not put on PATH.
$installer = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer"
if ($exe -eq ".exe" -and -not (Get-Command vswhere -ErrorAction SilentlyContinue) -and (Test-Path (Join-Path $installer "vswhere.exe"))) {
    $env:PATH = "$installer;$env:PATH"
}

function Publish([string] $project, [string] $into) {
    Write-Host "== publishing $project for $Runtime (Native AOT)"
    & dotnet publish (Join-Path $root $project) -c Release -f net10.0 -r $Runtime -o $into `
        -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TreatWarningsAsErrors=true -nologo
    if ($LASTEXITCODE -ne 0) { throw "publishing $project failed" }
}

Publish "Axiom.Mesh.Host/Axiom.Mesh.Host.csproj" (Join-Path $Output "host")
Publish "Apps/Axiom.Mesh.Cli/Axiom.Mesh.Cli.csproj" (Join-Path $Output "cli")
Publish "Services/Axiom.Mesh.Chat/Axiom.Mesh.Chat.csproj" (Join-Path $Output "chat")
Publish "Services/Axiom.Mesh.Sync/Axiom.Mesh.Sync.csproj" (Join-Path $Output "sync")
# Beside the daemon, where the default configuration's bare names axiom-mesh-chat and axiom-mesh-sync resolve (S-D11).
Copy-Item (Join-Path $Output "chat/axiom-mesh-chat$exe") (Join-Path $Output "host")
Copy-Item (Join-Path $Output "sync/axiom-mesh-sync$exe") (Join-Path $Output "host")
$hostExe = Join-Path $Output "host/axiom-meshd$exe"
$cliExe = Join-Path $Output "cli/axiom-mesh$exe"

$meshHome = Join-Path ([System.IO.Path]::GetTempPath()) "axiom-mesh-smoke-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
New-Item -ItemType Directory -Path $meshHome | Out-Null
# A port of the system's choice and no discovery: the smoke never meets a real Host on this machine.
Set-Content -Path (Join-Path $meshHome "config.json") -Value '{ "mesh": { "port": 0, "mdns": false, "broadcast": false } }'
$env:AXIOM_MESH_HOME = $meshHome
$env:AXIOM_MESH_PIPE = if ($exe -eq ".exe") { "axiom-mesh-smoke-$PID" } else { Join-Path $meshHome "pipe.sock" }

$daemon = $null
try {
    Write-Host "== starting the Host (home $meshHome, pipe $env:AXIOM_MESH_PIPE)"
    $daemon = Start-Process -FilePath $hostExe -ArgumentList "run" -PassThru -NoNewWindow `
        -RedirectStandardOutput (Join-Path $meshHome "host.out") -RedirectStandardError (Join-Path $meshHome "host.err")

    $ready = $false
    for ($i = 0; $i -lt 60 -and -not $ready; $i++) {
        & $cliExe status *> $null
        if ($LASTEXITCODE -eq 0) { $ready = $true } else { Start-Sleep -Milliseconds 500 }
        if ($daemon.HasExited) { throw "the Host exited with $($daemon.ExitCode): $(Get-Content (Join-Path $meshHome 'host.err') -Raw)" }
    }
    if (-not $ready) { throw "the Host did not answer 'status' within 30 seconds" }

    function Invoke-MeshCli([string[]] $arguments, [string] $expect = "") {
        Write-Host "== axiom-mesh $($arguments -join ' ')"
        $out = & $cliExe @arguments 2>&1 | Out-String
        Write-Host $out.TrimEnd()
        if ($LASTEXITCODE -ne 0) { throw "axiom-mesh $($arguments -join ' ') exited $LASTEXITCODE" }
        if ($expect -ne "" -and -not $out.Contains($expect)) { throw "axiom-mesh $($arguments -join ' ') did not print '$expect'" }
    }

    Invoke-MeshCli @("identity", "create", "--user", "smoke", "--device", "runner") "Created"
    Invoke-MeshCli @("package", "create", "Smoke") "Package"
    Invoke-MeshCli @("package", "list") "Smoke"
    Invoke-MeshCli @("write", "Smoke", "--text", "hello from the AOT smoke") "written"
    Invoke-MeshCli @("history", "Smoke") "hello from the AOT smoke"
    Invoke-MeshCli @("status", "--json") '"present": true'
    Invoke-MeshCli @("services", "--json") '"name": "cli"'

    # The chat service, spawned by the Host from beside axiom-meshd (S-D11): wait until it answers, then send and show.
    $chatReady = $false
    for ($i = 0; $i -lt 60 -and -not $chatReady; $i++) {
        & $cliExe chat list *> $null
        if ($LASTEXITCODE -eq 0) { $chatReady = $true } else { Start-Sleep -Milliseconds 500 }
    }
    if (-not $chatReady) { throw "the chat service did not answer 'chat list' within 30 seconds: $(Get-Content (Join-Path $meshHome 'host.out') -Raw)" }
    Invoke-MeshCli @("chat", "list") "Smoke"
    Invoke-MeshCli @("chat", "send", "Smoke", "--text", "hello from the chat service") "written"
    Invoke-MeshCli @("chat", "show", "Smoke") "hello from the chat service"
    Invoke-MeshCli @("services", "--json") '"name": "chat"'

    # The sync service, spawned likewise: a folder added with a new Archive becomes a Version (its binding gets a head).
    $syncReady = $false
    for ($i = 0; $i -lt 60 -and -not $syncReady; $i++) {
        & $cliExe sync list *> $null
        if ($LASTEXITCODE -eq 0) { $syncReady = $true } else { Start-Sleep -Milliseconds 500 }
    }
    if (-not $syncReady) { throw "the sync service did not answer 'sync list' within 30 seconds: $(Get-Content (Join-Path $meshHome 'host.out') -Raw)" }
    $shared = Join-Path $meshHome "shared"
    New-Item -ItemType Directory -Path $shared | Out-Null
    Set-Content -Path (Join-Path $shared "hello.txt") -Value "hello from the sync service"
    Invoke-MeshCli @("sync", "add", $shared, "--create", "SmokeFiles") "Bound"
    $versioned = $false
    for ($i = 0; $i -lt 60 -and -not $versioned; $i++) {
        $status = & $cliExe sync status $shared --json 2>&1 | Out-String
        if ($LASTEXITCODE -eq 0 -and $status.Contains('"pending": false') -and -not $status.Contains('"head": "0000000000000000000000000000000000000000000000000000000000000000"')) { $versioned = $true } else { Start-Sleep -Milliseconds 500 }
    }
    if (-not $versioned) { throw "the bound folder did not become a Version within 30 seconds: $status" }
    Invoke-MeshCli @("sync", "status", $shared) "in sync"
    Invoke-MeshCli @("history", "SmokeFiles") "version"
    Invoke-MeshCli @("services", "--json") '"name": "sync"'
    Write-Host "== AOT smoke passed ($Runtime)"
}
finally {
    # taskkill /T ends the daemon with the services it spawned; Process.Kill(bool) exists only in PowerShell 7.
    if ($null -ne $daemon -and -not $daemon.HasExited) { & taskkill /T /F /PID $daemon.Id | Out-Null; $daemon.WaitForExit() }
    Remove-Item Env:\AXIOM_MESH_HOME, Env:\AXIOM_MESH_PIPE -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force $meshHome -ErrorAction SilentlyContinue
}
