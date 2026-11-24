'use strict';
// VCC playground UI. Verification runs in worker.js (one fresh worker per run).

(() => {
  const $ = id => document.getElementById(id);
  const el = {
    source: $('source'), highlight: $('highlight'), gutter: $('gutter'), editor: $('editor'),
    examples: $('examples'), filename: $('filename'), share: $('share'), restore: $('restore'),
    sharePanel: $('share-panel'), shareLink: $('share-link'), shareNote: $('share-note'),
    ptrsize: $('ptrsize'), functions: $('functions'), timeout: $('timeout'), extra: $('extra'),
    run: $('run'), stop: $('stop'), clear: $('clear'), status: $('status'), verdict: $('verdict'),
    output: $('output'),
  };
  const STORAGE_KEY = 'vcc-playground.v1';
  const MAX_SHARE = 8 << 20;

  // ---------------------------------------------------------------- highlighting --------
  const C_KEYWORDS = new Set(('auto break case char const continue default do double else enum extern float for goto if ' +
    'inline int long register restrict return short signed sizeof static struct switch typedef union unsigned void ' +
    'volatile while __int8 __int16 __int32 __int64 __declspec _Bool bool').split(' '));
  const VCC_KEYWORDS = new Set(('requires ensures writes reads maintains returns decreases invariant assert assume ' +
    'ghost out unwrap wrap unwrapping atomic begin_update pure def abstract logic axiom frameenough frameaxiom ' +
    'dynamic_owns volatile_owns claimable record datatype typedef always updates by_claim known isolated_only ' +
    'admissibility no_admissibility inline_spec').split(' '));
  const TOKEN = /(\/\*[\s\S]*?(?:\*\/|$)|\/\/[^\n]*)|("(?:[^"\\\n]|\\.)*"?|'(?:[^'\\\n]|\\.)*'?)|(^[ \t]*#[^\n]*)|(\\[A-Za-z_]\w*)|\b(0[xX][0-9a-fA-F]+[uUlLi0-9]*|\d+(?:\.\d+)?[uUlLi0-9]*)\b|([A-Za-z_]\w*)/gm;

  const escapeHtml = s => s.replace(/[&<>]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c]));

  function highlight(text) {
    let html = '', last = 0;
    for (const m of text.matchAll(TOKEN)) {
      html += escapeHtml(text.slice(last, m.index));
      last = m.index + m[0].length;
      const t = escapeHtml(m[0]);
      if (m[1]) html += `<span class="hl-comment">${t}</span>`;
      else if (m[2]) html += `<span class="hl-string">${t}</span>`;
      else if (m[3]) html += `<span class="hl-pp">${t}</span>`;
      else if (m[4]) html += `<span class="hl-spec">${t}</span>`;
      else if (m[5]) html += `<span class="hl-number">${t}</span>`;
      else if (m[6] && C_KEYWORDS.has(m[6])) html += `<span class="hl-keyword">${t}</span>`;
      else if (m[6] && (VCC_KEYWORDS.has(m[6]) || m[6] === '_')) html += `<span class="hl-spec">${t}</span>`;
      else html += t;
    }
    return html + escapeHtml(text.slice(last)) + '\n';
  }

  let errorLines = new Set();

  function refreshEditor() {
    const text = el.source.value;
    el.highlight.innerHTML = highlight(text);
    const n = text.split('\n').length;
    let g = '';
    for (let i = 1; i <= n; i++) g += (errorLines.has(i) ? `<span class="mark">${i}</span>` : i) + '\n';
    el.gutter.innerHTML = g;
    // size the overlay to the content so the editor container scrolls both together
    el.source.style.height = '0';
    const h = Math.max(el.source.scrollHeight, el.highlight.scrollHeight);
    el.source.style.height = h + 'px';
    el.source.parentElement.style.height = h + 'px';
    el.source.style.width = Math.max(el.source.scrollWidth, el.highlight.scrollWidth) + 'px';
  }

  el.source.addEventListener('input', () => { errorLines = new Set(); refreshEditor(); save(); });
  el.source.addEventListener('keydown', e => {
    if (e.key === 'Tab' && !e.ctrlKey && !e.metaKey && !e.altKey) {
      e.preventDefault();
      document.execCommand('insertText', false, '  ');
    }
  });

  function goToLine(line, col) {
    const lines = el.source.value.split('\n');
    let pos = 0;
    for (let i = 0; i < Math.min(line - 1, lines.length); i++) pos += lines[i].length + 1;
    pos += Math.max(0, Math.min((col || 1) - 1, (lines[line - 1] || '').length));
    el.source.focus();
    el.source.setSelectionRange(pos, pos);
    const top = (line - 1) * 20;
    if (top < el.editor.scrollTop || top > el.editor.scrollTop + el.editor.clientHeight - 40)
      el.editor.scrollTop = Math.max(0, top - el.editor.clientHeight / 3);
  }

  // ---------------------------------------------------------------- state ---------------
  function currentState() {
    return {
      v: 1, code: el.source.value, file: el.filename.value, p: el.ptrsize.value,
      f: el.functions.value, t: el.timeout.value, x: el.extra.value,
    };
  }

  function applyState(s) {
    if (typeof s.code === 'string') el.source.value = s.code;
    if (s.file) el.filename.value = s.file;
    if (s.p) el.ptrsize.value = s.p === '32' ? '32' : '64';
    if (typeof s.f === 'string') el.functions.value = s.f;
    if (s.t) el.timeout.value = s.t;
    if (typeof s.x === 'string') el.extra.value = s.x;
    errorLines = new Set();
    refreshEditor();
  }

  function save() {
    try { localStorage.setItem(STORAGE_KEY, JSON.stringify(currentState())); } catch { /* storage unavailable */ }
  }

  function load() {
    try { return JSON.parse(localStorage.getItem(STORAGE_KEY) || 'null'); } catch { return null; }
  }

  for (const input of [el.filename, el.ptrsize, el.functions, el.timeout, el.extra]) input.addEventListener('change', save);

  // ---------------------------------------------------------------- examples ------------
  const examples = window.VCC_EXAMPLES || [];
  examples.forEach((ex, i) => el.examples.add(new Option(ex.title, String(i))));
  el.examples.addEventListener('change', () => {
    const ex = examples[+el.examples.value];
    el.examples.value = '';
    if (!ex) return;
    applyState({ code: ex.source, file: ex.file, x: (ex.args || []).join(' '), f: '' });
    save();
  });

  // ---------------------------------------------------------------- sharing -------------
  const b64url = bytes => {
    let s = '';
    for (let i = 0; i < bytes.length; i += 0x8000) s += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
    return btoa(s).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  };
  const unb64url = s => Uint8Array.from(atob(s.replace(/-/g, '+').replace(/_/g, '/')), c => c.charCodeAt(0));

  async function pipe(bytes, stream) {
    return new Uint8Array(await new Response(new Blob([bytes]).stream().pipeThrough(stream)).arrayBuffer());
  }

  async function makeShareLink() {
    const json = new TextEncoder().encode(JSON.stringify(currentState()));
    let frag;
    if (typeof CompressionStream === 'function') frag = 'z=' + b64url(await pipe(json, new CompressionStream('deflate-raw')));
    else frag = 'p=' + b64url(json);
    return location.href.split('#')[0] + '#' + frag;
  }

  async function readShareFragment() {
    const m = /^#([zp])=(.+)$/.exec(location.hash);
    if (!m) return null;
    try {
      let bytes = unb64url(m[2]);
      if (m[1] === 'z') {
        const out = await pipe(bytes, new DecompressionStream('deflate-raw'));
        if (out.length > MAX_SHARE) throw new Error('too large');
        bytes = out;
      }
      return JSON.parse(new TextDecoder().decode(bytes));
    } catch (e) {
      print(`Could not open the shared link (${e.message}).\n`, 'err');
      return null;
    }
  }

  el.share.addEventListener('click', async () => {
    const link = await makeShareLink();
    el.sharePanel.hidden = false;
    el.shareLink.value = link;
    el.shareLink.select();
    try {
      await navigator.clipboard.writeText(link);
      el.shareNote.textContent = 'Copied. The program is stored in the link itself.';
    } catch {
      el.shareNote.textContent = 'The program is stored in the link itself.';
    }
  });

  // ---------------------------------------------------------------- output --------------
  function print(text, cls) {
    const span = document.createElement('span');
    if (cls) span.className = cls;
    span.textContent = text;
    el.output.appendChild(span);
    el.output.scrollTop = el.output.scrollHeight;
    return span;
  }

  // file(line,col) : error VCnnnn: ...  (VCC)   /   file:line: error: ...  (preprocessor)
  const LOCATION = /^(.*?)\((\d+),(\d+)\)\s*:\s*(error|warning)\b|^(.*?):(\d+): (error|warning)\b/;
  let counts = { ok: 0, bad: 0 };

  function printLine(line, stream) {
    const shown = line.replace(/\/work\//g, '');
    let cls = stream === 2 ? 'err' : '';
    if (/^Verification of .* succeeded/.test(shown)) { cls = 'ok'; counts.ok++; }
    else if (/^Verification of .* (failed|was inconclusive|ran out of memory|timed out)/.test(shown)) { cls = 'err'; counts.bad++; }
    const m = LOCATION.exec(shown);
    if (m) {
      const isError = (m[4] || m[7]) === 'error';
      cls = isError ? 'err' : 'warn';
      const ln = +(m[2] || m[6]), col = +(m[3] || 1);
      const file = (m[1] || m[5]).trim();
      const span = document.createElement('span');
      span.className = cls;
      const a = document.createElement('a');
      a.className = 'loc';
      const locText = shown.slice(0, (m.index || 0) + m[0].length - (m[4] || m[7]).length).replace(/\s*:\s*$/, '');
      a.textContent = locText;
      if (file === el.filename.value || file.endsWith('/' + el.filename.value)) {
        a.addEventListener('click', () => goToLine(ln, col));
        if (isError) errorLines.add(ln);
      }
      span.append(a, document.createTextNode(shown.slice(locText.length)));
      el.output.appendChild(span);
    } else {
      print(shown, cls);
    }
  }

  let pending = '';
  function onOutput(stream, text) {
    pending += text;
    let i;
    while ((i = pending.indexOf('\n')) >= 0) {
      printLine(pending.slice(0, i + 1), stream);
      pending = pending.slice(i + 1);
    }
  }

  function showVerdict() {
    el.verdict.innerHTML = '';
    const tag = (n, word, cls) => {
      if (!n) return;
      const s = document.createElement('span');
      s.className = 'tag ' + cls;
      s.textContent = `${n} ${word}`;
      el.verdict.appendChild(s);
    };
    tag(counts.ok, 'verified', 'ok');
    tag(counts.bad, 'failed', 'bad');
  }

  el.clear.addEventListener('click', () => { el.output.textContent = ''; el.verdict.innerHTML = ''; el.status.textContent = ''; });

  // ---------------------------------------------------------------- running -------------
  let worker = null, timer = null, clock = null, started = 0;

  const STAGES = { preprocess: 'Preprocessing…', load: 'Loading VCC, Boogie and Z3…', verify: 'Verifying…' };

  function parseArgs() {
    const args = [];
    if (el.ptrsize.value === '32') args.push('/ps:32');
    const f = el.functions.value.trim();
    if (f) args.push('/f:' + f.split(/[\s,]+/).filter(Boolean).join(','));
    const t = Math.max(5, Math.min(3600, parseInt(el.timeout.value, 10) || 120));
    args.push('/b:/timeLimit:' + t);
    const extra = el.extra.value.trim();
    if (extra) {
      for (const a of extra.match(/"[^"]*"|\S+/g)) {
        if (!/^[/-]/.test(a)) throw new Error(`"${a}" is not an option (options start with / or -)`);
        args.push(a.replace(/^"|"$/g, ''));
      }
    }
    return { args, timeout: t };
  }

  function finish(message, cls) {
    if (pending) { printLine(pending, 1); pending = ''; }
    if (worker) { worker.terminate(); worker = null; }
    clearTimeout(timer); clearInterval(clock);
    const secs = ((performance.now() - started) / 1000).toFixed(1);
    el.status.textContent = `${message} (${secs} s)`;
    if (cls) print(`${message}\n`, cls);
    el.run.disabled = false;
    el.stop.disabled = true;
    showVerdict();
    refreshEditor();
  }

  function run() {
    if (worker) return;
    let opts;
    try { opts = parseArgs(); } catch (e) { print(e.message + '\n', 'err'); return; }
    const fileName = (el.filename.value.trim() || 'input.c').replace(/[^\w.-]/g, '_');
    el.filename.value = fileName;
    el.output.textContent = '';
    el.verdict.innerHTML = '';
    counts = { ok: 0, bad: 0 };
    errorLines = new Set();
    pending = '';
    print(`vcc ${opts.args.join(' ')} ${fileName}\n`, 'info');
    el.run.disabled = true;
    el.stop.disabled = false;
    started = performance.now();
    el.status.textContent = 'Starting…';
    let stage = 'start';
    clock = setInterval(() => {
      el.status.textContent = `${STAGES[stage] || 'Starting…'} ${((performance.now() - started) / 1000).toFixed(0)} s`;
    }, 500);

    worker = new Worker('worker.js', { type: 'module' });
    worker.onmessage = ({ data }) => {
      if (data.type === 'output') onOutput(data.stream, data.text);
      else if (data.type === 'stage') stage = data.stage;
      else if (data.type === 'done') {
        const ok = data.exitCode === 0;
        finish(data.stage === 'preprocess' ? 'Preprocessing failed'
          : ok ? 'Verification succeeded' : `VCC exited with code ${data.exitCode}`, null);
      } else if (data.type === 'error') {
        print(data.message + '\n', 'err');
        finish('Internal error', null);
      }
    };
    worker.onerror = e => { print((e.message || 'worker error') + '\n', 'err'); finish('Internal error', null); };
    worker.postMessage({ source: el.source.value, fileName, args: opts.args });
    // per-proof time limits are enforced by Boogie/Z3; this is a backstop for the whole run
    timer = setTimeout(() => finish(`Stopped after ${opts.timeout * 2} s`, 'err'), opts.timeout * 2000 + 30000);
    save();
  }

  el.run.addEventListener('click', run);
  el.stop.addEventListener('click', () => { if (worker) finish('Stopped', 'info'); });
  document.addEventListener('keydown', e => {
    if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') { e.preventDefault(); run(); }
  });

  // ---------------------------------------------------------------- startup -------------
  (async () => {
    const saved = load();
    const shared = await readShareFragment();
    if (shared) {
      applyState(shared);
      history.replaceState(null, '', location.pathname + location.search);
      if (saved && saved.code !== shared.code) {
        el.restore.hidden = false;
        el.restore.addEventListener('click', () => { applyState(saved); save(); el.restore.hidden = true; });
      }
    } else if (saved) {
      applyState(saved);
    } else if (examples.length) {
      applyState({ code: examples[1].source, file: examples[1].file });
    } else {
      refreshEditor();
    }
  })();
})();
