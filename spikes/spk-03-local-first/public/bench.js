// Bancada de medição do LocalStore (chamada pelo run-bench.mjs com a CPU já limitada via CDP).
import { IdbStore } from './store-idb.js';
import { SqliteStore } from './store-sqlite.js';
import { IdbChunkStore } from './store-idb-chunks.js';
import { Register } from './register.js';
import { stats } from './common.js';

const now = () => performance.now();

async function timeEach(list, fn, warm = 3) {
  const out = [];
  let hits = 0;
  for (let i = 0; i < list.length; i++) {
    const t0 = now();
    const r = await fn(list[i]);
    const dt = now() - t0;
    if (i >= warm) out.push(dt);
    hits += Array.isArray(r) ? r.length : r ? 1 : 0;
  }
  return { ...stats(out), avgHits: Math.round((hits / list.length) * 10) / 10 };
}

function mainCpu(n) {
  const t0 = now();
  let x = 0;
  for (let i = 0; i < n; i++) x = (x + Math.sqrt(i) * 1.0000001) % 1e9;
  return { ms: now() - t0, x };
}

async function estimate() {
  const e = await navigator.storage.estimate();
  return { usage: e.usage, details: e.usageDetails || null };
}

window.calib = async (n = 20_000_000) => {
  const main = mainCpu(n).ms;
  const w = new Worker('./calib-worker.js', { type: 'module' });
  const worker = await new Promise((res) => { w.onmessage = (e) => res(e.data.ms); w.postMessage(n); });
  w.terminate();
  return { mainMs: main, workerMs: worker };
};

window.persistCheck = async () => {
  const before = await navigator.storage.persisted();
  const granted = await navigator.storage.persist();
  return { before, granted, after: await navigator.storage.persisted() };
};

window.bench = async ({ tech, n = 50000, sales = 100, sqlite = {} }) => {
  const R = { tech, n, ua: navigator.userAgent };
  const terms = await (await fetch(`/api/catalog/terms?n=${n}`)).json();
  const name = `bench-${tech}`;
  const isIdb = tech.startsWith('idb');
  const lean = tech === 'idb-lean' || tech === 'idb-chunks'; // só busca em memória
  const make = (dur = 'relaxed') => (tech === 'idb-chunks' ? new IdbChunkStore(name, { durability: dur }) : isIdb ? new IdbStore(name, { durability: dur, lean }) : new SqliteStore(name, sqlite));

  // 1. abrir
  let t0 = now();
  let store = make();
  const openInfo = await store.open();
  R.openMs = now() - t0;
  if (openInfo) R.engine = openInfo;

  // 2. carga inicial (foto)
  t0 = now();
  if (isIdb) {
    const res = await fetch(`/api/catalog/snapshot?n=${n}`);
    const data = await res.json();
    const tFetch = now() - t0;
    const t1 = now();
    await store.loadSnapshot(data.items);
    R.snapshot = { totalMs: now() - t0, fetchParseMs: tFetch, insertMs: now() - t1, rawBytes: Number(res.headers.get('X-Raw-Bytes')), gzBytes: Number(res.headers.get('X-Gz-Bytes')) };
  } else {
    const d = await store.loadSnapshotFromUrl(`/api/catalog/snapshot?n=${n}`);
    R.snapshot = { totalMs: now() - t0, ...d };
  }
  R.count = await store.countItems();
  R.sizeAfterSnapshot = await estimate();
  if (store.buildMemIndex) await store.buildMemIndex();
  // heap JS da página com o catálogo carregado (o worker do SQLite não entra nesta conta)
  R.heapMB = performance.memory ? Math.round(performance.memory.usedJSHeapSize / 2 ** 20) : null;
  if (tech === 'sqlite') R.sqliteDbBytes = await store.dbBytes();

  // 3. busca por código de barras
  R.barcode = await timeEach(terms.barcodes, (c) => store.findByBarcode(c));

  // 4. busca por nome
  R.search = {};
  if (isIdb) {
    if (!lean) R.search.prefix_index = await timeEach(terms.prefix, (t) => store.searchName(t, 'prefix'));
    if (!lean) R.search.middle_scan = await timeEach(terms.middle, (t) => store.searchName(t, 'scan'));
    const tm = now();
    await store.buildMemIndex();
    R.memIndexBuildMs = now() - tm;
    R.search.prefix_mem = await timeEach(terms.prefix, (t) => store.searchName(t, 'mem'));
    R.search.middle_mem = await timeEach(terms.middle, (t) => store.searchName(t, 'mem'));
  } else {
    R.search.prefix_like = await timeEach(terms.prefix, (t) => store.searchName(t, 'prefix'));
    R.search.middle_scan = await timeEach(terms.middle, (t) => store.searchName(t, 'scan'));
    R.search.prefix_fts = await timeEach(terms.prefix, (t) => store.searchName(t, 'fts'));
    R.search.middle_fts = await timeEach(terms.middle, (t) => store.searchName(t, 'fts'));
  }

  // 5. vendas: item = busca por código + gravação do carrinho; concluir = venda + evento + contador numa transação
  async function sell(st, label) {
    const reg = new Register(st, { deviceId: 'BENCH', epoch: 1 });
    await reg.open();
    const add = [];
    const done = [];
    for (let s = 0; s < sales; s++) {
      for (let k = 0; k < 3; k++) {
        const code = terms.barcodes[(s * 3 + k) % terms.barcodes.length];
        const t = now();
        const it = (await st.findByBarcode(code)) || (await st.findByBarcode(terms.barcodes[1]));
        await reg.addItem(it);
        add.push(now() - t);
      }
      const t = now();
      await reg.complete();
      done.push(now() - t);
    }
    R[`sale_${label}`] = { addItem: stats(add.slice(3)), complete: stats(done.slice(1)) };
  }
  await sell(store, isIdb ? 'relaxed' : 'sync_normal');
  if (isIdb) {
    await store.close();
    const strict = make('strict');
    await strict.open();
    await sell(strict, 'strict');
    store = strict;
  } else {
    await store.pragma('PRAGMA synchronous = FULL');
    await sell(store, 'sync_full');
    await store.pragma('PRAGMA synchronous = NORMAL');
  }

  // 6. delta (500 mudanças: 450 alterações, 50 removidos)
  t0 = now();
  if (isIdb) {
    const res = await fetch(`/api/catalog/changes?n=${n}`);
    const data = await res.json();
    const tF = now() - t0;
    const t1 = now();
    await store.applyDelta(data.changes);
    R.delta = { totalMs: now() - t0, fetchParseMs: tF, applyMs: now() - t1, rawBytes: Number(res.headers.get('X-Raw-Bytes')), gzBytes: Number(res.headers.get('X-Gz-Bytes')) };
  } else {
    const d = await store.applyDeltaFromUrl(`/api/catalog/changes?n=${n}`);
    R.delta = { totalMs: now() - t0, ...d };
  }
  R.countAfterDelta = await store.countItems();
  // confere que o delta valeu na busca (item removido some do FTS/índice)
  R.deltaCheck = { removedBarcodeFound: !!(await store.findByBarcode(terms.removedBarcode || '0')) };

  // 7. reabrir (caixa recarregado): até a primeira busca respondida
  await store.close();
  t0 = now();
  store = make();
  await store.open();
  await store.findByBarcode(terms.barcodes[1]);
  R.reopenToFirstLookupMs = now() - t0;
  if (isIdb) { const tm = now(); await store.buildMemIndex(); R.reopenMemIndexMs = now() - tm; }

  R.sizeFinal = await estimate();
  if (tech === 'sqlite') R.sqliteDbBytesFinal = await store.dbBytes();
  await store.close();
  return R;
};

document.body.dataset.ready = '1';
