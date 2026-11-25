#!/bin/bash
# Builds mcpp.js/mcpp.wasm (the C preprocessor used by the playground).
# usage: build-mcpp.sh <mcpp-source-dir> <out-dir>     (needs emsdk in PATH)
set -euo pipefail
src=$(realpath "$1"); out=$(realpath -m "$2"); here=$(dirname "$(realpath "$0")")
mkdir -p "$out"
emcc -O2 -w -DMCPP_LIB=1 -I "$src" "$here/mcpp-driver.c" \
  "$src"/{directive,eval,expand,mbchar,mcpp_main,support,system}.c \
  -o "$out/mcpp.js" \
  -sMODULARIZE=1 -sEXPORT_ES6=1 -sEXPORT_NAME=createMcpp -sENVIRONMENT=web,worker,node \
  -sINVOKE_RUN=0 -sEXIT_RUNTIME=0 -sALLOW_MEMORY_GROWTH=1 -sFORCE_FILESYSTEM=1 \
  -sEXPORTED_RUNTIME_METHODS=callMain,FS
echo "built $out/mcpp.js $out/mcpp.wasm"
