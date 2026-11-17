// CA 6: 20 caixas simulados, 200 vendas cada, com respostas perdidas (reenvio) e falhas do servidor.
// Confere no servidor: nenhuma venda duplicada, nenhuma perdida, cadeia de hash íntegra.
import fs from 'node:fs';
import { startServer, tempProfile, launch, BASE, writeResult, machineInfo } from './lib/harness.mjs';

const arg = (k, d) => (process.argv.find((a) => a.startsWith(`--${k}=`)) || '').split('=')[1] || d;
const TECH = arg('tech', 'idb');
const CAIXAS = Number(arg('caixas', '20'));
const SALES = Number(arg('sales', '200'));
const DROP = Number(arg('drop', '0.3'));
const FAIL = Number(arg('fail', '0.05'));
const RATE = Number(arg('rate', '1'));

const server = await startServer();
const dir = tempProfile('sync');
const ctx = await launch(dir);
const t0 = Date.now();
const pages = [];
try {
  for (let i = 1; i <= CAIXAS; i++) {
    const device = `L1-C${String(i).padStart(2, '0')}`;
    const page = await ctx.newPage();
    page.on('console', (m) => { const t = m.text(); if (t.startsWith('ERROR')) console.log(device, t); });
    if (RATE > 1) { const cdp = await ctx.newCDPSession(page); await cdp.send('Emulation.setCPUThrottlingRate', { rate: RATE }); }
    await page.goto(`${BASE}/pos.html?tech=${TECH}&device=${device}&sales=${SALES}&pace=5&drop=${DROP}&fail=${FAIL}&batch=25&syncEvery=150&n=2000`);
    pages.push({ device, page });
  }
  const tick = setInterval(async () => {
    try { const v = await (await fetch(`${BASE}/api/sync/verify`)).json(); console.log(`progresso: ${v.totals.ev} eventos, ${v.stats.duplicate || 0} reenvios descartados`); } catch { /* servidor ocupado */ }
  }, 15_000);
  const done = [];
  for (const { device, page } of pages) {
    await page.waitForFunction(() => window.__done, null, { timeout: 15 * 60_000, polling: 500 });
    done.push({ device, local: await page.evaluate(() => window.__done), sync: await page.evaluate(() => window.summary()) });
  }
  clearInterval(tick);
  const wallSec = (Date.now() - t0) / 1000;
  const verify = await (await fetch(`${BASE}/api/sync/verify`)).json();
  const expected = CAIXAS * SALES;
  const perDeviceOk = verify.perDevice.length === CAIXAS && verify.perDevice.every((d) => d.c === SALES && d.mn === 1 && d.mx === SALES);
  const allRtt = done.flatMap((d) => [d.sync.rtt.p50]);
  const result = {
    when: new Date().toISOString(), machine: machineInfo(), tech: TECH, caixas: CAIXAS, salesPerCaixa: SALES, dropProb: DROP, failProb: FAIL, cpuRate: RATE,
    wallSec,
    expected,
    serverEvents: verify.totals.ev, serverSales: verify.totals.sales, distinctNumbers: verify.totals.numbers,
    duplicatesRejected: verify.stats.duplicate || 0, accepted: verify.stats.accepted || 0,
    chainBreaks: verify.chainBreaks, perDeviceOk,
    batchesTotal: done.reduce((s, d) => s + d.sync.batches, 0),
    responsesDropped: done.reduce((s, d) => s + d.sync.dropped, 0),
    serverFailures: done.reduce((s, d) => s + d.sync.failed, 0),
    clientRttP50ByCaixa: { min: Math.min(...allRtt), max: Math.max(...allRtt) },
    clientRttP95Worst: Math.max(...done.map((d) => d.sync.rtt.p95)),
    serverBatchMs: verify.serverBatchMs,
    dbBytes: verify.dbBytes,
    dbBytesPerSale: Math.round(verify.dbBytes / Math.max(1, verify.totals.ev)),
    localAllContiguous: done.every((d) => d.local.contiguous && d.local.uniqueSeqs && d.local.acked === SALES),
    devices: done.map((d) => ({ device: d.device, acked: d.local.acked, batches: d.sync.batches, dropped: d.sync.dropped, failed: d.sync.failed, rtt: d.sync.rtt })),
  };
  result.ok = result.serverEvents === expected && result.serverSales === expected && result.distinctNumbers === expected && result.chainBreaks === 0 && perDeviceOk && result.localAllContiguous;
  const f = writeResult(arg('out', `sync-${TECH}.json`), result);
  console.log(JSON.stringify({ ok: result.ok, wallSec, expected, serverEvents: result.serverEvents, duplicatesRejected: result.duplicatesRejected, chainBreaks: result.chainBreaks, batches: result.batchesTotal, dropped: result.responsesDropped, failures: result.serverFailures, serverBatchMs: result.serverBatchMs, rttP95Worst: result.clientRttP95Worst, dbBytes: result.dbBytes }));
  console.log('resultado em', f);
} finally {
  await ctx.close();
  fs.rmSync(dir, { recursive: true, force: true });
  server.kill();
}
