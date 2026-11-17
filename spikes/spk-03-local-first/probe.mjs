// Sonda rápida: modos de journal do SQLite-WASM (opfs-sahpool) e custo por gravação. Usada na exploração do spike.
import fs from 'node:fs';
import { startServer, tempProfile, launch, BASE } from './lib/harness.mjs';

const server = await startServer();
const dir = tempProfile('probe');
const ctx = await launch(dir);
const page = await ctx.newPage();
await page.goto(`${BASE}/bench.html`);
await page.waitForSelector('body[data-ready="1"]');
const out = await page.evaluate(async () => {
  const { SqliteStore } = await import('./store-sqlite.js');
  const s = new SqliteStore('probe', { fts: false });
  const info = await s.open();
  const res = { info };
  for (const [label, pragmas] of [
    ['delete/full', ['PRAGMA journal_mode=delete', 'PRAGMA synchronous=full']],
    ['truncate/full', ['PRAGMA journal_mode=truncate', 'PRAGMA synchronous=full']],
    ['persist/full', ['PRAGMA journal_mode=persist', 'PRAGMA synchronous=full']],
    ['exclusive+persist/full', ['PRAGMA locking_mode=exclusive', 'PRAGMA journal_mode=persist', 'PRAGMA synchronous=full']],
    ['exclusive+wal/full', ['PRAGMA locking_mode=exclusive', 'PRAGMA journal_mode=wal', 'PRAGMA synchronous=full']],
    ['exclusive+wal/normal', ['PRAGMA synchronous=normal']],
  ]) {
    const got = [];
    for (const p of pragmas) got.push(JSON.stringify(await s.pragma(p)));
    const t = [];
    for (let i = 0; i < 40; i++) {
      const t0 = performance.now();
      await s.cartPut({ lineId: 'x' + i, saleId: 's', price: 1, qty: 1 });
      t.push(performance.now() - t0);
    }
    t.sort((a, b) => a - b);
    res[label] = { got, p50: t[20], p95: t[38] };
  }
  return res;
});
console.log(JSON.stringify(out, null, 1));
await ctx.close();
fs.rmSync(dir, { recursive: true, force: true });
server.kill();
