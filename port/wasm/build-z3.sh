#!/bin/bash
# Builds z3.js/z3.wasm for the playground: Z3 (single-threaded, polling timer, so no
# SharedArrayBuffer and no cross-origin isolation needed) plus the vccz3.cpp wrapper.
# usage: build-z3.sh <z3-source-dir> <build-dir> <out-dir>     (needs emsdk in PATH)
set -euo pipefail
src=$(realpath "$1"); build=$(realpath -m "$2"); out=$(realpath -m "$3")
here=$(dirname "$(realpath "$0")")
mkdir -p "$build" "$out"
if [ ! -f "$build/libz3.a" ]; then
  emcmake cmake -S "$src" -B "$build" -G "Unix Makefiles" -DCMAKE_BUILD_TYPE=Release \
    -DZ3_SINGLE_THREADED=ON -DZ3_POLLING_TIMER=ON -DZ3_BUILD_LIBZ3_SHARED=OFF \
    -DZ3_BUILD_EXECUTABLE=OFF -DZ3_BUILD_TEST_EXECUTABLES=OFF -DZ3_ENABLE_EXAMPLE_TARGETS=OFF \
    -DZ3_BUILD_PYTHON_BINDINGS=OFF -DCMAKE_CXX_FLAGS="-fexceptions"
  emmake make -C "$build" -j"$(nproc)" libz3
fi
# same definitions as the libz3 build (CMake: -DZ3_SINGLE_THREADED=ON -DZ3_POLLING_TIMER=ON)
em++ -O3 -std=gnu++20 -fexceptions -DPOLLING_TIMER -DSINGLE_THREAD -D_EXTERNAL_RELEASE -D_MP_INTERNAL -DNDEBUG "$here/vccz3.cpp" "$build/libz3.a" -I "$src/src" -I "$build/src" \
  -o "$out/z3.js" \
  -sMODULARIZE=1 -sEXPORT_ES6=1 -sEXPORT_NAME=createZ3 \
  -sENVIRONMENT=web,worker,node \
  -sALLOW_MEMORY_GROWTH=1 -sINITIAL_MEMORY=64MB -sMAXIMUM_MEMORY=4GB -sSTACK_SIZE=16MB \
  -sEXPORTED_FUNCTIONS=_vccz3_global_param_set,_vccz3_open,_vccz3_eval,_vccz3_close,_malloc,_free \
  -sEXPORTED_RUNTIME_METHODS=cwrap,UTF8ToString
echo "built $out/z3.js $out/z3.wasm"
