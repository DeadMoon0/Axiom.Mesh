#!/bin/sh
# Installs Axiom.Mesh for the current user: axiom-meshd (the Host) and axiom-mesh (the command line).
#
#   curl -fsSL https://raw.githubusercontent.com/DeadMoon0/Axiom.Mesh/main/install.sh | sh
#   curl -fsSL https://raw.githubusercontent.com/DeadMoon0/Axiom.Mesh/main/install.sh | sh -s -- --version 0.1.0 --no-autostart
#
# It finds the platform (linux-x64), downloads the release zip and SHA256SUMS from GitHub (the latest release, or
# --version), verifies the zip's SHA-256 against SHA256SUMS, puts both executables into ~/.local/bin, makes sure that
# directory is on your PATH, registers the Host to start at every logon (a systemd user unit, enabled and started,
# through 'axiom-mesh host install'; --no-autostart opts out), starts the Host and prints 'axiom-mesh status'. Nothing
# needs root. --uninstall reverses it and keeps the Host's data.
#
# The script itself is fetched from the branch main, which can change; the checksum protects the binaries, not the
# script. For the strict option, fetch the script from a release tag instead of main:
#   https://raw.githubusercontent.com/DeadMoon0/Axiom.Mesh/v0.1.0/install.sh
#
# Options:
#   --version <x.y.z>   the release to install (default: the latest; also AXIOM_MESH_VERSION)
#   --source <zip>      install a local release zip instead of downloading one; SHA256SUMS beside it is checked when present
#   --no-autostart      do not register a start at logon; start the Host once now (also AXIOM_MESH_NO_AUTOSTART=1)
#   --bin-dir <dir>     where the executables go (default ~/.local/bin)
#   --uninstall         stop the Host, remove its logon start and the executables; the Host's home stays
set -eu

repository="DeadMoon0/Axiom.Mesh"
version="${AXIOM_MESH_VERSION:-}"
source_zip=""
autostart=1
[ "${AXIOM_MESH_NO_AUTOSTART:-}" = "1" ] && autostart=0
bin_dir="${HOME:?HOME is not set}/.local/bin"
uninstall=0

fail() {
    echo "axiom-mesh install: $*" >&2
    exit 1
}

while [ $# -gt 0 ]; do
    case "$1" in
        --version) version="${2:?--version needs a value}"; shift 2 ;;
        --source) source_zip="${2:?--source needs a file}"; shift 2 ;;
        --no-autostart) autostart=0; shift ;;
        --bin-dir) bin_dir="${2:?--bin-dir needs a directory}"; shift 2 ;;
        --uninstall) uninstall=1; shift ;;
        *) fail "unknown option $1 (see the comment at the top of install.sh)" ;;
    esac
done

daemon="$bin_dir/axiom-meshd"
cli="$bin_dir/axiom-mesh"
doc_dir="${XDG_DATA_HOME:-$HOME/.local/share}/axiom-mesh/doc"

# The runtime identifier of this machine.
rid() {
    case "$(uname -s)" in
        Linux) os=linux ;;
        Darwin) fail "no macOS build yet; build from source (Scripts/aot-smoke.sh)" ;;
        *) fail "no build for $(uname -s); on Windows run install.ps1" ;;
    esac
    case "$(uname -m)" in
        x86_64 | amd64) echo "$os-x64" ;;
        aarch64 | arm64) fail "no linux-arm64 build yet; build from source (Scripts/aot-smoke.sh linux-arm64)" ;;
        *) fail "no build for $(uname -m)" ;;
    esac
}

download() {
    if command -v curl > /dev/null 2>&1; then
        curl -fsSL "$1" -o "$2" || fail "cannot download $1"
    elif command -v wget > /dev/null 2>&1; then
        wget -q "$1" -O "$2" || fail "cannot download $1"
    else
        fail "neither curl nor wget is installed"
    fi
}

sha256() {
    if command -v sha256sum > /dev/null 2>&1; then
        sha256sum "$1" | cut -d ' ' -f 1
    elif command -v shasum > /dev/null 2>&1; then
        shasum -a 256 "$1" | cut -d ' ' -f 1
    else
        fail "neither sha256sum nor shasum is installed"
    fi
}

unpack() {
    if command -v unzip > /dev/null 2>&1; then
        unzip -q -o "$1" -d "$2"
    elif command -v python3 > /dev/null 2>&1; then
        python3 -I -m zipfile -e "$1" "$2"
    else
        fail "neither unzip nor python3 is installed to unpack $1"
    fi
}

# Ends a Host that runs from the install directory and its unit, so its files can be replaced or removed.
stop_host() {
    if command -v systemctl > /dev/null 2>&1; then systemctl --user stop axiom-meshd.service > /dev/null 2>&1 || true; fi
    if command -v pkill > /dev/null 2>&1; then pkill -x -f "$daemon run.*" > /dev/null 2>&1 || true; fi
}

wait_for_host() {
    i=0
    while [ $i -lt $(($1 * 2)) ]; do
        if "$cli" status > /dev/null 2>&1; then return 0; fi
        sleep 0.5
        i=$((i + 1))
    done
    return 1
}

start_directly() {
    nohup "$daemon" run > /dev/null 2>&1 &
}

if [ $uninstall -eq 1 ]; then
    if [ -x "$cli" ]; then "$cli" host uninstall --daemon "$daemon" || true; fi
    stop_host
    rm -f "$daemon" "$cli"
    rm -rf "$doc_dir"
    echo "Axiom.Mesh removed from $bin_dir. The Host's home (keys, store, config.json) is kept; delete it yourself to start anew."
    exit 0
fi

target=$(rid)
work=$(mktemp -d "${TMPDIR:-/tmp}/axiom-mesh-install.XXXXXX")
trap 'rm -rf "$work"' EXIT

if [ -n "$source_zip" ]; then
    [ -f "$source_zip" ] || fail "no file at $source_zip"
    zip="$source_zip"
    name=$(basename "$zip")
    sums="$(dirname "$zip")/SHA256SUMS"
    [ -f "$sums" ] || sums=""
else
    if [ -z "$version" ]; then
        download "https://api.github.com/repos/$repository/releases/latest" "$work/latest.json"
        version=$(sed -n 's/.*"tag_name"[[:space:]]*:[[:space:]]*"v\{0,1\}\([^"]*\)".*/\1/p' "$work/latest.json" | head -n 1)
        [ -n "$version" ] || fail "cannot tell the latest release; name one with --version"
    fi
    version=${version#v}
    name="axiom-mesh-$version-$target.zip"
    base="https://github.com/$repository/releases/download/v$version"
    echo "downloading Axiom.Mesh $version for $target"
    zip="$work/$name"
    download "$base/$name" "$zip"
    download "$base/SHA256SUMS" "$work/SHA256SUMS"
    sums="$work/SHA256SUMS"
fi

actual=$(sha256 "$zip")
if [ -n "$sums" ]; then
    listed=$(awk -v n="$name" '{ f = $2; sub(/^\*/, "", f); if (f == n) { print tolower($1); exit } }' "$sums")
    [ -n "$listed" ] || fail "SHA256SUMS lists no $name"
    [ "$listed" = "$actual" ] || fail "$name does not match SHA256SUMS (expected $listed, got $actual); nothing was installed"
    echo "verified $name against SHA256SUMS"
else
    echo "note: no SHA256SUMS beside $name; the local zip is not verified"
fi

unpack "$zip" "$work/unpacked"
[ -f "$work/unpacked/axiom-meshd" ] && [ -f "$work/unpacked/axiom-mesh" ] || fail "$name holds no axiom-meshd and axiom-mesh"

mkdir -p "$bin_dir" "$doc_dir"
stop_host
# A rename replaces an executable that is still running; copying over it would fail with "text file busy".
for file in axiom-meshd axiom-mesh; do
    cp "$work/unpacked/$file" "$bin_dir/.$file.new"
    chmod 755 "$bin_dir/.$file.new"
    mv -f "$bin_dir/.$file.new" "$bin_dir/$file"
done
cp "$work/unpacked/LICENSE" "$work/unpacked/README.txt" "$doc_dir/" 2> /dev/null || true
echo "installed axiom-meshd and axiom-mesh to $bin_dir"

case ":$PATH:" in
    *":$bin_dir:"*) ;;
    *)
        line="export PATH=\"$bin_dir:\$PATH\""
        for profile in "$HOME/.profile" "$HOME/.bashrc" "$HOME/.zshrc"; do
            if [ -f "$profile" ] || [ "$profile" = "$HOME/.profile" ]; then
                grep -qsF "$line" "$profile" || printf '\n# Added by the Axiom.Mesh installer\n%s\n' "$line" >> "$profile"
            fi
        done
        export PATH="$bin_dir:$PATH"
        echo "added $bin_dir to your PATH in your shell profile (new shells see it)"
        ;;
esac

if [ $autostart -eq 1 ]; then
    if ! "$cli" host install --daemon "$daemon"; then
        echo "note: the Host could not be registered to start at logon (no systemd user manager?); starting it once now"
        start_directly
    fi
else
    echo "not registering a start at logon (--no-autostart); starting the Host once"
    start_directly
fi

if ! wait_for_host 20; then
    echo "note: the Host did not answer within 20 seconds; starting it directly"
    start_directly
    wait_for_host 20 || fail "the Host does not answer; run 'axiom-meshd run' in a terminal to see why"
fi

echo
"$cli" status
echo
echo "Next: axiom-mesh identity create --user <your name> --device <this device>   (then 'axiom-mesh help')"
