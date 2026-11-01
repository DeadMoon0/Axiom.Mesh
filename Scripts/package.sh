#!/bin/sh
# Builds the release zip of one platform: Native AOT publish and smoke of the Host and the command line (aot-smoke.sh),
# then axiom-mesh-<version>-<rid>.zip with axiom-meshd, axiom-mesh, axiom-mesh-chat, LICENSE and README.txt, and SHA256SUMS beside it
# with one line in the format of sha256sum. The release workflow and the install-script job run it on linux-x64;
# package.ps1 is the same on Windows.
#
# Usage: Scripts/package.sh <runtime-identifier> <output-directory> [version]
set -eu

root=$(cd "$(dirname "$0")/.." && pwd)
runtime=${1:?runtime identifier, such as linux-x64}
output=${2:?output directory}
version=${3:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Apps/Axiom.Mesh.Cli/Axiom.Mesh.Cli.csproj" | head -n 1)}
mkdir -p "$output"
output=$(cd "$output" && pwd)
work="$output/publish-$runtime"

sh "$root/Scripts/aot-smoke.sh" "$runtime" "$work"

name="axiom-mesh-$version-$runtime.zip"
stage="$output/stage-$runtime"
rm -rf "$stage"
mkdir -p "$stage"
cp "$work/host/axiom-meshd" "$work/cli/axiom-mesh" "$work/chat/axiom-mesh-chat" "$stage/"
chmod 755 "$stage/axiom-meshd" "$stage/axiom-mesh" "$stage/axiom-mesh-chat"
cp "$root/LICENSE" "$stage/"
cp "$root/Scripts/zip-readme.txt" "$stage/README.txt"

rm -f "$output/$name"
(cd "$stage" && zip -q -X "$output/$name" axiom-meshd axiom-mesh axiom-mesh-chat LICENSE README.txt)
(cd "$output" && sha256sum "$name" > SHA256SUMS)
rm -rf "$stage"
echo "== packaged $output/$name ($(cut -d ' ' -f 1 "$output/SHA256SUMS"))"
