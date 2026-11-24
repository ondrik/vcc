# VCC on modern .NET (Linux)

SDK-style projects that build the sources in `../vcc` with .NET 10, so VCC runs on
Linux (and, later, in the browser). The original VS2015 solution is unchanged.

## Build

    dotnet build port/VccPort.sln -c Release

Requirements: .NET 10 SDK, `z3` (tested with 4.13.3) and `cpp` on `PATH`.
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

## Status

On the regression suite (same set as `Test/testsuite/run-all.bat`, 1479 files),
1393 pass as-is. Of the rest, 59 differ only in the order of output lines (.NET
Core randomizes string hashing); most others are cosmetic (error columns) or
preprocessor differences between gcc and MSVC. About 8 differ in prover behaviour
with the modern Z3 (memory-outs, timeouts, `basics-40`).
