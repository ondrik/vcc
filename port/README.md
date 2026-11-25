# VCC on modern .NET (Linux and the browser)

SDK-style projects that build the sources in `../vcc` with .NET 10, so VCC runs on
Linux and, compiled to WebAssembly, in the browser (the playground). The original
VS2015 solution is unchanged.

## Build

    dotnet build port/VccPort.sln -c Release

Requirements: .NET 10 SDK, `z3` (tested with 4.13.3) and `cpp` on `PATH`. To preprocess
with mcpp instead (as the playground does), point `VCC_CPP` to an mcpp binary.
If .NET is installed outside the default location, set `DOTNET_ROOT`.

## Run

    dotnet port/Host/bin/Release/vcc.dll file.c
    dotnet port/Host/bin/Release/vcc.dll /s testcase      # test-suite mode

## How it works

- **CCI and Boogie** are used as the prebuilt .NET Framework binaries in `vcc/CCI` and
  `vcc/Boogie`. The only API they use that modern .NET lacks is `Debug.Listeners`
  in Boogie's `Core.dll`; `tools/PatchLibs` rewrites it to `Trace.Listeners` into
  `port/.libs/Boogie` at build time.
- **mscorlib**: VCC models the C type system on .NET metadata, so the host loads the
  .NET Framework 4.0 reference `mscorlib.dll` (copied to `ref/`) instead of the
  running runtime's core library. `VccRuntime` is likewise built for `net40`.
- **Preprocessor**: without `cl.exe`, VCC runs a GNU-compatible preprocessor
  (`cpp`, or `$VCC_CPP`) and uses the MSVC CRT stand-ins in `vcc/Headers/portable`.
- **Z3**: Boogie starts `z3.exe` next to its binaries. That is `Z3Shim`, which
  starts the real Z3 (`$VCC_Z3` or `z3`) and translates the pre-4.0 parameter
  names Boogie and VCC use (`CASE_SPLIT`, `SOFT_TIMEOUT`, ...) to current ones.

## The playground (WebAssembly)

Everything runs client-side in a Web Worker: the C source is preprocessed by
[mcpp](https://github.com/zeroc-ice/mcpp), then VCC (`Browser/`, .NET `browser-wasm`)
verifies it, with Boogie calling Z3 in-process. Z3 is built single-threaded with a polling
timer, so the page needs no `SharedArrayBuffer` or cross-origin isolation and can be served
from GitHub Pages. `.github/workflows/playground.yml` builds and deploys it.

Pieces:

- `wasm/build-z3.sh`, `wasm/vccz3.cpp`: Z3 with Emscripten. The wrapper sets up a
  `cmd_context` like the z3 executable does; `Z3_eval_smtlib2_string` turned out to be
  orders of magnitude slower on some of Boogie's queries.
- `wasm/build-mcpp.sh`: the C preprocessor (also usable natively via `VCC_CPP`).
- `ProverBridge/`: replaces the z3 process. `tools/PatchLibs --inproc` rewrites Boogie
  (`.libs/Boogie-inproc`): `SMTLibProcess` talks to the bridge, `ProcessStartInfo` and
  `Console.CancelKeyPress` (unavailable in the browser) are stubbed out,
  `Checker.BeginCheck` runs synchronously (no threads), and `Assembly.Location` falls back
  to `VCC_HOME`.
- `web/vcc-engine.mjs`: runs one verification (mcpp, then VCC with Z3); used by the
  page's worker and by `web/run-node.mjs` for testing under Node.
- `web/site/`: the page. `web/assemble.sh` puts everything together.

Build locally (needs emsdk 3.1.73 in `PATH`):

    port/wasm/build-z3.sh <z3-4.15.4-source> build/z3-wasm build/wasm
    port/wasm/build-mcpp.sh <mcpp-source> build/wasm
    port/web/assemble.sh _site build/wasm build/wasm
    node port/web/run-node.mjs _site vcc/Demo/Parity.c        # or serve _site over HTTP

Notes:

- Mono's interpreter optimizer crashes on some of this (2010-era) code, so the engine runs
  with `MONO_INTERPRETER_OPTIONS=-all`.
- In a worker, `dotnet.js` must be imported before `onmessage` is set (see `worker.js`).
- A .NET runtime cannot be restarted, so every run uses a fresh worker.
- `VCC_DEBUG=1` makes VCC print stack traces of internal errors; `VCC_SMT_TRACE=<file>` logs
  the SMT-LIB traffic (shim and bridge).

## Status

Regression suite (same set as `Test/testsuite/run-all.bat`, 1479 files), native, Z3 4.13.3:

- with mcpp (`VCC_CPP`, the playground's preprocessor): 1426 pass, 50 fail, 3 time out;
- with gcc's `cpp`: 1393 pass.

Most remaining differences are cosmetic: output lines in a different order (.NET Core
randomizes string hashing), error columns shifted by the preprocessor's spacing, and
preprocessor warnings. A handful are prover differences with the modern Z3 (memory-outs,
timeouts, `basics-40`). Known limitations:

- mcpp does not support `__COUNTER__` (test `arrays-15`);
- includes are case-sensitive outside Windows;
- the playground verifies a single file (no local headers besides VCC's own).
