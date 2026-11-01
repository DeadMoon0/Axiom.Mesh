#!/bin/sh
# Publishes the Host, the command line and both services as Native AOT for this machine and runs them together once: the smoke test of
# fundamentals plan §6 (install-tests.yml and release.yml run the same steps through Scripts/package.*, on linux-x64 and win-x64). Both executables are published with
# PublishAot and IlcTreatWarningsAsErrors, the Host starts with a temporary home and its own socket, and the command line
# creates an identity and a Channel, lists Packages, writes a message and reads the history, sends a message through the chat
# service and adds a folder to the sync service until it is a Version. Any failure exits non-zero.
#
# Usage: Scripts/aot-smoke.sh [runtime-identifier] [output-directory]
set -eu

root=$(cd "$(dirname "$0")/.." && pwd)
case "$(uname -s)" in
    Linux) os=linux ;;
    Darwin) os=osx ;;
    *) os=linux ;;
esac
case "$(uname -m)" in
    x86_64 | amd64) arch=x64 ;;
    aarch64 | arm64) arch=arm64 ;;
    *) arch=$(uname -m) ;;
esac
runtime=${1:-$os-$arch}
output=${2:-$(mktemp -d "${TMPDIR:-/tmp}/axiom-mesh-aot.XXXXXX")}

publish() {
    echo "== publishing $1 for $runtime (Native AOT)"
    dotnet publish "$root/$1" -c Release -f net10.0 -r "$runtime" -o "$2" \
        -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TreatWarningsAsErrors=true -nologo
}

publish Axiom.Mesh.Host/Axiom.Mesh.Host.csproj "$output/host"
publish Apps/Axiom.Mesh.Cli/Axiom.Mesh.Cli.csproj "$output/cli"
publish Services/Axiom.Mesh.Chat/Axiom.Mesh.Chat.csproj "$output/chat"
publish Services/Axiom.Mesh.Sync/Axiom.Mesh.Sync.csproj "$output/sync"
# Beside the daemon, where the default configuration's bare names axiom-mesh-chat and axiom-mesh-sync resolve (S-D11).
cp "$output/chat/axiom-mesh-chat" "$output/host/"
cp "$output/sync/axiom-mesh-sync" "$output/host/"
host_exe="$output/host/axiom-meshd"
cli_exe="$output/cli/axiom-mesh"

home=$(mktemp -d "${TMPDIR:-/tmp}/axiom-mesh-smoke.XXXXXX")
chmod 700 "$home"
# A port of the system's choice and no discovery: the smoke never meets a real Host on this machine.
printf '{ "mesh": { "port": 0, "mdns": false, "broadcast": false } }\n' > "$home/config.json"
export AXIOM_MESH_HOME="$home"
export AXIOM_MESH_PIPE="$home/pipe.sock"

daemon=""
cleanup() {
    if [ -n "$daemon" ]; then kill "$daemon" 2>/dev/null || true; wait "$daemon" 2>/dev/null || true; fi
    rm -rf "$home"
}
trap cleanup EXIT

echo "== starting the Host (home $home, socket $AXIOM_MESH_PIPE)"
"$host_exe" run > "$home/host.out" 2> "$home/host.err" &
daemon=$!

ready=0
i=0
while [ $i -lt 60 ]; do
    if "$cli_exe" status > /dev/null 2>&1; then ready=1; break; fi
    if ! kill -0 "$daemon" 2>/dev/null; then echo "the Host exited:"; cat "$home/host.err"; exit 1; fi
    sleep 0.5
    i=$((i + 1))
done
[ $ready -eq 1 ] || { echo "the Host did not answer 'status' within 30 seconds"; exit 1; }

cli() {
    expect=$1
    shift
    echo "== axiom-mesh $*"
    out=$("$cli_exe" "$@" 2>&1) || { echo "$out"; echo "axiom-mesh $* failed"; exit 1; }
    echo "$out"
    case "$out" in
        *"$expect"*) ;;
        *) echo "axiom-mesh $* did not print '$expect'"; exit 1 ;;
    esac
}

cli "Created" identity create --user smoke --device runner
cli "Package" package create Smoke
cli "Smoke" package list
cli "written" write Smoke --text "hello from the AOT smoke"
cli "hello from the AOT smoke" history Smoke
cli '"present": true' status --json
cli '"name": "cli"' services --json
# The chat service, spawned by the Host from beside axiom-meshd: wait until it answers, then send and show.
chat_ready=0
for _ in $(seq 1 60); do
    if "$cli_exe" chat list > /dev/null 2>&1; then chat_ready=1; break; fi
    sleep 0.5
done
if [ "$chat_ready" -ne 1 ]; then echo "the chat service did not answer chat list within 30 seconds:"; cat "$home/host.out"; exit 1; fi
cli "Smoke" chat list
cli "written" chat send Smoke --text "hello from the chat service"
cli "hello from the chat service" chat show Smoke
cli '"name": "chat"' services --json
# The sync service, spawned likewise: a folder added with a new Archive becomes a Version (its binding gets a head).
sync_ready=0
for _ in $(seq 1 60); do
    if "$cli_exe" sync list > /dev/null 2>&1; then sync_ready=1; break; fi
    sleep 0.5
done
if [ "$sync_ready" -ne 1 ]; then echo "the sync service did not answer sync list within 30 seconds:"; cat "$home/host.out"; exit 1; fi
mkdir -p "$home/shared"
printf 'hello from the sync service\n' > "$home/shared/hello.txt"
cli "Bound" sync add "$home/shared" --create SmokeFiles
versioned=0
for _ in $(seq 1 60); do
    status=$("$cli_exe" sync status "$home/shared" --json 2>&1 || true)
    case "$status" in
        *'"head": "0000000000000000000000000000000000000000000000000000000000000000"'*) ;;
        *'"pending": false'*) versioned=1; break ;;
    esac
    sleep 0.5
done
if [ "$versioned" -ne 1 ]; then echo "the bound folder did not become a Version within 30 seconds: $status"; exit 1; fi
cli "in sync" sync status "$home/shared"
cli "version" history SmokeFiles
cli '"name": "sync"' services --json
echo "== AOT smoke passed ($runtime)"
