// Runs one VCC verification entirely client-side:
//   mcpp (WebAssembly)  ->  VCC on .NET (WebAssembly)  <->  Z3 (WebAssembly)
// Used by the playground's worker and by the Node test driver (port/web/run-node.mjs).
// A .NET runtime keeps global state, so callers use a fresh worker (or process) per run.

/** VCC's default pointer size is 64 bits; /ps:32 (or /pointersize:32) selects 32. */
function pointerSize(args) {
  return args.some(a => /^[/-](ps|pointersize):32$/i.test(a)) ? 32 : 64;
}

function writeFile(FS, path, data) {
  const dir = path.substring(0, path.lastIndexOf('/'));
  if (dir) FS.mkdirTree(dir);
  FS.writeFile(path, data);
}

/**
 * @param {object} o
 * @param {string} o.base         URL (with trailing slash) of the directory holding
 *                                _framework/, wasm/ and headers/
 * @param {(url: string) => Promise<Uint8Array>} o.fetchBytes
 * @param {string} o.source       C source to verify
 * @param {string[]} [o.args]     VCC command-line options (e.g. "/f:foo", "/ps:32")
 * @param {string} [o.fileName]   name used in messages
 * @param {(stream: 1|2, text: string) => void} o.output
 * @param {(stage: string) => void} [o.onStage]
 * @param {Record<string, string>} [o.env]  extra environment variables for the .NET runtime
 * @param {object} [o.dotnet]     the `dotnet` export of _framework/dotnet.js, if already imported;
 *                                in a web worker it must be imported before the worker sets its
 *                                onmessage handler (otherwise dotnet.js takes the worker for one
 *                                of its own threads and never starts)
 * @returns {Promise<{exitCode: number, stage: string}>}
 */
export async function runVcc(o) {
  const args = o.args ?? [];
  const fileName = o.fileName ?? 'input.c';
  const stage = s => o.onStage?.(s);
  const text = async url => new TextDecoder().decode(await o.fetchBytes(url));

  // --- 1. preprocess with mcpp ---------------------------------------------------------
  stage('preprocess');
  const headerNames = JSON.parse(await text(o.base + 'headers/index.json'));
  const headers = await Promise.all(headerNames.map(async n => [n, await o.fetchBytes(o.base + 'headers/' + n)]));
  const ppOut = [], ppErr = [];
  const createMcpp = (await import(o.base + 'wasm/mcpp.js')).default;
  const mcpp = await createMcpp({ print: l => ppOut.push(l), printErr: l => ppErr.push(l) });
  for (const [n, data] of headers) writeFile(mcpp.FS, '/vcc/headers/' + n, data);
  writeFile(mcpp.FS, '/work/' + fileName, o.source);
  const ppArgs = ['-I-', '-DVERIFY', '-D_WIN32', '-D_MSC_VER=1600', '-DVCC_PORTABLE_PP'];
  if (pointerSize(args) === 64) ppArgs.push('-D_WIN64');
  for (const a of args) {                       // /p:<option> passes options to the preprocessor
    const m = /^[/-](?:p|preprocessor):(.*)$/i.exec(a);
    if (m) ppArgs.push(...m[1].split(',').map(x => x.replace(/^\//, '-')));
  }
  ppArgs.push('-I', '/vcc/headers', '-I', '/vcc/headers/portable', '/work/' + fileName);
  const ppRc = mcpp.callMain(ppArgs);
  for (const l of ppErr) o.output(2, l + '\n');
  if (ppRc !== 0) return { exitCode: ppRc, stage: 'preprocess' };
  const preprocessed = ppOut.join('\n') + '\n';

  // --- 2. Z3 --------------------------------------------------------------------------
  stage('load');
  const createZ3 = (await import(o.base + 'wasm/z3.js')).default;
  const z3 = await createZ3();
  const z3SetParam = z3.cwrap('vccz3_global_param_set', null, ['string', 'string']);
  const z3Open = z3.cwrap('vccz3_open', 'number', []);
  const z3Eval = z3.cwrap('vccz3_eval', 'string', ['number', 'string']);
  const z3Close = z3.cwrap('vccz3_close', null, ['number']);
  const contexts = new Map();
  let nextHandle = 1;

  // --- 3. VCC on .NET -----------------------------------------------------------------
  const dotnet = o.dotnet ?? (await import(o.base + '_framework/dotnet.js')).dotnet;
  const ppPath = '/work/' + fileName.replace(/\.[^.]*$/, '') + '.i';
  const passThrough = args.filter(a => !/^[/-](?:p|preprocessor):/i.test(a));
  const rt = await dotnet
    .withApplicationArguments('/n', ...passThrough, ppPath)
    // Mono's interpreter optimizer crashes on some of the (2010-era) CCI/VCC code paths
    // ("memory access out of bounds" in interp_optimize_bblocks); run unoptimized.
    .withEnvironmentVariable('MONO_INTERPRETER_OPTIONS', '-all')
    .withEnvironmentVariables(o.env ?? {})
    .create();
  rt.setModuleImports('vccio', { write: (stream, s) => o.output(stream === 2 ? 2 : 1, s) });
  rt.setModuleImports('vccz3', {
    setParam: (n, v) => z3SetParam(n, v),
    open: () => { const h = nextHandle++; contexts.set(h, z3Open()); return h; },
    eval: (h, cmds) => z3Eval(contexts.get(h), cmds),
    close: h => { const c = contexts.get(h); if (c) { z3Close(c); contexts.delete(h); } },
  });
  rt.Module.FS_createPath('/', 'work', true, true);
  rt.Module.FS_createDataFile('/work', ppPath.substring(6), new TextEncoder().encode(preprocessed), true, true);
  stage('verify');
  const exitCode = await rt.runMain();
  return { exitCode, stage: 'verify' };
}
