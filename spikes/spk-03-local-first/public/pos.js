// Caixa simulado: vende sem parar (carrinho gravado a cada item) e sincroniza em lote ao mesmo tempo.
// Usado pelos roteiros de sincronização (20 caixas) e de robustez (aba fechada, navegador morto).
import { IdbStore } from './store-idb.js';
import { SqliteStore } from './store-sqlite.js';
import { IdbChunkStore } from './store-idb-chunks.js';
import { Register } from './register.js';
import { stats } from './common.js';

const q = new URLSearchParams(location.search);
const P = {
  tech: q.get('tech') || 'idb',
  device: q.get('device') || 'D01',
  epoch: Number(q.get('epoch') || 1),
  n: Number(q.get('n') || 2000),
  sales: Number(q.get('sales') || 200),
  pace: Number(q.get('pace') || 10),
  batch: Number(q.get('batch') || 25),
  drop: Number(q.get('drop') || 0),
  fail: Number(q.get('fail') || 0),
  syncEvery: Number(q.get('syncEvery') || 100),
  autostart: q.get('autostart') !== '0',
};
const status = document.getElementById('status');
const say = (t) => { status.textContent = t; };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const S = { rtt: [], serverMs: [], batches: 0, dropped: 0, failed: 0, dupReturned: 0, sold: 0 };
window.__stats = S;
let store, reg, lockRelease;

// Um só escritor por aparelho (Web Locks). A segunda aba avisa e não escreve.
async function takeLock() {
  return new Promise((resolve) => {
    navigator.locks.request(`pdv-writer-${P.device}`, { ifAvailable: true }, (lock) => {
      if (!lock) { resolve(false); return undefined; }
      resolve(true);
      return new Promise((rel) => { lockRelease = rel; });
    });
  });
}

async function boot() {
  const got = await takeLock();
  window.__lock = got ? 'owner' : 'busy';
  if (!got) { say('O caixa já está aberto em outra janela.'); console.log('LOCK busy'); return false; }
  const name = `pos-${P.device}`;
  const dur = q.get('durability') || 'strict';
  store = P.tech === 'idb-chunks' ? new IdbChunkStore(name, { durability: dur })
    : P.tech === 'idb' ? new IdbStore(name, { durability: dur, lean: true })
    : new SqliteStore(name, { sync: q.get('sync') || 'full' });
  await store.open();
  if ((await store.countItems()) === 0) {
    if (P.tech !== 'sqlite') {
      const data = await (await fetch(`/api/catalog/snapshot?n=${P.n}`)).json();
      await store.loadSnapshot(data.items);
    } else {
      await store.loadSnapshotFromUrl(`/api/catalog/snapshot?n=${P.n}`);
    }
  }
  reg = new Register(store, { deviceId: P.device, epoch: P.epoch });
  const opened = await reg.open();
  console.log('OPENED ' + JSON.stringify(opened));
  window.__opened = opened;
  return true;
}

window.inspect = async () => {
  const seqs = await store.outboxSeqs();
  let contiguous = true;
  for (let i = 0; i < seqs.length; i++) if (seqs[i] !== i + 1) { contiguous = false; break; }
  const cart = await store.cartGet();
  return {
    seq: reg.seq, acked: reg.acked, outbox: seqs.length, outboxMax: seqs.at(-1) || 0, contiguous,
    uniqueSeqs: new Set(seqs).size === seqs.length, sales: await store.salesCount(),
    cartLines: cart.length, cartSaleId: cart[0]?.saleId || null,
  };
};

async function sellLoop() {
  const codes = Array.from({ length: P.n }, (_, i) => i);
  for (let s = 0; s < P.sales; s++) {
    for (let k = 0; k < 3; k++) {
      const idx = codes[Math.floor(Math.random() * codes.length)];
      const item = await store.findByBarcode(ean(idx));
      const line = await reg.addItem(item);
      console.log(`ADDED ${line.saleId} ${k + 1}`);
      await sleep(P.pace);
    }
    const ev = await reg.complete();
    S.sold++;
    console.log(`COMMITTED ${ev.seq} ${ev.payload.sale_id}`);
    await sleep(P.pace);
  }
}

function ean(i) {
  const s = String(789000000000 + i).padStart(12, '0');
  let sum = 0;
  for (let j = 0; j < 12; j++) sum += Number(s[j]) * (j % 2 === 0 ? 1 : 3);
  return s + ((10 - (sum % 10)) % 10);
}

let selling = true;
async function syncLoop() {
  while (true) {
    const r = await reg.syncOnce({ batch: P.batch, dropProb: P.drop, failProb: P.fail });
    if (r.sent) {
      S.batches++;
      if (r.rtt != null) S.rtt.push(r.rtt);
      if (r.serverMs != null) S.serverMs.push(r.serverMs);
      if (r.dropped) S.dropped++;
      if (r.error) S.failed++;
      S.dupReturned += r.dup || 0;
      if (!r.dropped && !r.error) continue; // há mais a enviar: segue sem esperar
    } else if (!selling) {
      break;
    }
    await sleep(P.syncEvery);
  }
}

window.drain = async (drop = 0) => { P.drop = drop; P.fail = 0; selling = false; await syncLoop(); return window.inspect(); };
window.summary = () => ({ ...S, rtt: stats(S.rtt), serverMs: stats(S.serverMs) });
window.retryLock = async () => { const ok = await boot(); return ok; };
window.releaseLock = () => lockRelease?.();

window.start = () => run();

async function run() {
  try {
    const sync = syncLoop();
    await sellLoop();
    selling = false;
    await sync;
    window.__done = await window.inspect();
    console.log('DONE ' + JSON.stringify(window.__done));
  } catch (e) {
    console.log('ERROR ' + (e && e.stack || e));
  }
}

(async () => {
  try {
    if (!(await boot())) return;
    say(`Caixa ${P.device} (${P.tech}) pronto`);
    console.log('READY');
    if (P.autostart) run();
  } catch (e) {
    console.log('ERROR ' + (e && e.stack || e));
  }
})();
