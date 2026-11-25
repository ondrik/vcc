// Runs the playground engine under Node, for testing an assembled site.
// usage: node run-node.mjs <site-dir> <file.c> [vcc options...]
import { readFile } from 'node:fs/promises';
import { pathToFileURL, fileURLToPath } from 'node:url';
import { basename, resolve } from 'node:path';

const [site, file, ...args] = process.argv.slice(2);
const { runVcc } = await import(pathToFileURL(resolve(site, 'vcc-engine.mjs')).href);
const t0 = Date.now();
const { exitCode, stage } = await runVcc({
  base: pathToFileURL(resolve(site, 'vcc')).href + '/',
  fetchBytes: async url => new Uint8Array(await readFile(fileURLToPath(url))),
  source: await readFile(file, 'utf8'),
  fileName: basename(file),
  args,
  output: (stream, text) => (stream === 2 ? process.stderr : process.stdout).write(text),
  onStage: s => process.stderr.write(`[${s} ${Date.now() - t0} ms]\n`),
  // VCC_DOTNET_ENV='A=1 B=2' passes environment variables to the .NET runtime (debugging)
  env: Object.fromEntries((process.env.VCC_DOTNET_ENV ?? '').split(' ').filter(Boolean).map(kv => kv.split(/=(.*)/s).slice(0, 2))),
});
process.stderr.write(`[exit ${exitCode} after ${stage}, ${Date.now() - t0} ms]\n`);
process.exit(exitCode);
