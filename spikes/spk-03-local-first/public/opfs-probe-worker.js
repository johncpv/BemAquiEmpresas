// Sonda: custo cru do OPFS (diretório, criar arquivo, abrir SyncAccessHandle, gravar e flush), sem SQLite.
self.onmessage = async () => {
  const t = {};
  let t0 = performance.now();
  const root = await navigator.storage.getDirectory();
  t.getDirectory = performance.now() - t0;
  const handles = [];
  t0 = performance.now();
  for (let i = 0; i < 6; i++) {
    const fh = await root.getFileHandle(`probe-${i}`, { create: true });
    handles.push(await fh.createSyncAccessHandle());
  }
  t.open6Handles = performance.now() - t0;
  const buf = new Uint8Array(4096);
  const w = [];
  const wf = [];
  for (let i = 0; i < 40; i++) {
    let a = performance.now();
    handles[0].write(buf, { at: i * 4096 });
    w.push(performance.now() - a);
    a = performance.now();
    handles[0].write(buf, { at: i * 4096 });
    handles[0].flush();
    wf.push(performance.now() - a);
  }
  w.sort((a, b) => a - b); wf.sort((a, b) => a - b);
  t.write4k_p50 = w[20]; t.writeFlush4k_p50 = wf[20]; t.writeFlush4k_p95 = wf[38];
  t0 = performance.now();
  for (const h of handles) h.close();
  t.close = performance.now() - t0;
  self.postMessage(t);
};
