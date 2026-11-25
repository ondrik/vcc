#!/bin/bash
# Assembles the static playground site.
# usage: assemble.sh <out-dir> <z3-wasm-dir> <mcpp-wasm-dir>
#   <z3-wasm-dir>   holds z3.js/z3.wasm    (port/wasm/build-z3.sh)
#   <mcpp-wasm-dir> holds mcpp.js/mcpp.wasm (port/wasm/build-mcpp.sh)
set -euo pipefail
here=$(dirname "$(realpath "$0")"); port=$(dirname "$here"); repo=$(dirname "$port")
out=$(realpath -m "$1"); z3=$(realpath "$2"); mcpp=$(realpath "$3")
pub=$(mktemp -d)
trap 'rm -r -- "$pub"' EXIT

dotnet publish "$port/Browser/Browser.csproj" -c Release -o "$pub" -p:PublishTrimmed=false \
  -p:SatelliteResourceLanguages=en --nologo -v quiet

mkdir -p "$out/vcc/wasm" "$out/vcc/headers/portable"
# precompressed copies are left out: they would double the size, and the server compresses anyway
rsync -a --delete --delete-excluded --exclude '*.gz' --exclude '*.br' "$pub/wwwroot/_framework/" "$out/vcc/_framework/"
cp "$z3/z3.js" "$z3/z3.wasm" "$mcpp/mcpp.js" "$mcpp/mcpp.wasm" "$out/vcc/wasm/"
cp "$repo"/vcc/Headers/*.h "$out/vcc/headers/"
cp "$repo"/vcc/Headers/portable/*.h "$out/vcc/headers/portable/"
(cd "$out/vcc/headers" && find . -name '*.h' | sed 's#^\./##' | sort | python3 -c 'import json,sys; print(json.dumps([l.strip() for l in sys.stdin]))' > index.json)
cp "$here/vcc-engine.mjs" "$out/"
rsync -a "$here/site/" "$out/"
python3 "$here/make-examples.py" "$repo" "$out/examples.js"
echo "assembled $out ($(du -sh "$out" | cut -f1))"
