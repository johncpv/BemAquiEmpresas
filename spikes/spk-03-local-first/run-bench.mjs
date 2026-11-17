// CA 1, 2 e 3: busca, carga inicial, delta, gravação de venda, tamanho e persist(), por tecnologia × perfil de CPU.
// O perfil de CPU é SIMULADO com Emulation.setCPUThrottlingRate do Chromium (1x, 4x, 6x).
import fs from 'node:fs';
import path from 'node:path';
import { startServer, tempProfile, launch, throttle, BASE, writeResult, dirSize, machineInfo } from './lib/harness.mjs';

const arg = (k, d) => (process.argv.find((a) => a.startsWith(`--${k}=`)) || '').split('=')[1] || d;
const RATES = arg('rates', '1,4,6').split(',').map(Number);
const TECHS = arg('techs', 'idb,sqlite').split(',');
const N = Number(arg('n', '50000'));
const LABEL = arg('label', ''); // ex.: job4x quando roda dentro do lib/job-throttle.ps1 (sem CDP)
const SALES = Number(arg('sales', '100'));
const SQLITE = { journal: arg('journal', 'wal'), sync: arg('sync', 'normal'), locking: arg('locking', 'exclusive') };

const server = await startServer();
const out = { when: new Date().toISOString(), machine: machineInfo(), n: N, sqlite: SQLITE, runs: [] };
try {
  for (const rate of RATES) {
    for (const tech of TECHS) {
      const dir = tempProfile(`${tech}-${rate}x`);
      const ctx = await launch(dir);
      const page = await ctx.newPage();
      page.on('console', (m) => { if (m.type() === 'error') console.log(`[${tech} ${rate}x] ${m.text()}`); });
      page.on('pageerror', (e) => console.log(`[${tech} ${rate}x] pageerror ${e.message}`));
      await page.goto(`${BASE}/bench.html`);
      await page.waitForSelector('body[data-ready="1"]');
      if (rate > 1) await throttle(ctx, page, rate, { workers: false });
      const t0 = Date.now();
      const calib = await page.evaluate(() => window.calib());
      const persist = await page.evaluate(() => window.persistCheck());
      const r = await page.evaluate((p) => window.bench(p), { tech, n: N, sales: SALES, sqlite: SQLITE });
      const browserVersion = ctx.browser()?.version() || null;
      await ctx.close();
      const disk = {
        indexedDb: dirSize(path.join(dir, 'Default', 'IndexedDB')),
        fileSystem: dirSize(path.join(dir, 'Default', 'File System')),
      };
      fs.rmSync(dir, { recursive: true, force: true });
      const run = { rate, label: LABEL || (rate > 1 ? `cdp${rate}x` : 'normal'), tech, wallSec: (Date.now() - t0) / 1000, calib, persist, browserVersion, disk, ...r };
      out.runs.push(run);
      console.log(`${tech} ${run.label}: carga ${Math.round(r.snapshot.totalMs)} ms, código p95 ${r.barcode.p95} ms, nome-prefixo p95 ${JSON.stringify(Object.fromEntries(Object.entries(r.search).map(([k, v]) => [k, v.p95])))}, disco ${disk.indexedDb + disk.fileSystem} B`);
    }
  }
} finally {
  server.kill();
}
const f = writeResult(arg('out', 'bench.json'), out);
console.log('resultado em', f);
