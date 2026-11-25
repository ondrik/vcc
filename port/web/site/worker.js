// Runs one verification. The page starts a fresh worker for every run (a .NET runtime cannot
// be restarted) and terminates it to stop a run.
// dotnet.js must be imported before onmessage is set: in a worker without a message handler it
// runs as a "sidecar" (.NET hosted in a worker); with one, it assumes it is a runtime thread.
import { dotnet } from './vcc/_framework/dotnet.js';
import { runVcc } from './vcc-engine.mjs';

self.onmessage = async ({ data }) => {
  const post = m => self.postMessage(m);
  try {
    const result = await runVcc({
      base: new URL('vcc/', self.location.href).href,
      fetchBytes: async url => {
        const r = await fetch(url);
        if (!r.ok) throw new Error(`cannot load ${url}: ${r.status}`);
        return new Uint8Array(await r.arrayBuffer());
      },
      dotnet,
      source: data.source,
      fileName: data.fileName,
      args: data.args,
      output: (stream, text) => post({ type: 'output', stream, text }),
      onStage: stage => post({ type: 'stage', stage }),
    });
    post({ type: 'done', ...result });
  } catch (e) {
    post({ type: 'error', message: String(e && (e.stack || e.message) || e) });
  }
};
