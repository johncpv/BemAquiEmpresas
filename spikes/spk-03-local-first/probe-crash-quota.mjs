// Sonda: depois de matar o Chromium (taskkill /F), o IndexedDB do mesmo perfil volta a aceitar gravação?
import { spawn, execSync } from 'node:child_process';
import fs from 'node:fs';
import { chromium } from 'playwright-core';
import { startServer, tempProfile, BASE } from './lib/harness.mjs';

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const WAIT = Number(process.argv[2] || 500);
let port = 9600;
async function start(dir) {
  const p = ++port;
  const child = spawn(chromium.executablePath(), ['--headless=new', `--user-data-dir=${dir}`, `--remote-debugging-port=${p}`, '--no-first-run', 'about:blank'], { stdio: 'ignore' });
  let b;
  for (let i = 0; i < 100 && !b; i++) { try { b = await chromium.connectOverCDP(`http://127.0.0.1:${p}`); } catch { await sleep(200); } }
  return { child, b, ctx: b.contexts()[0] };
}
const step = async (ctx) => {
  const page = await ctx.newPage();
  for (let i = 0; ; i++) { try { await page.goto(`${BASE}/bench.html`); break; } catch (e) { console.log('goto falhou', i, e.message.split('\n')[0]); if (i > 4) throw e; await sleep(500); } }
  return page.evaluate(async () => {
    const est = await navigator.storage.estimate();
    try {
      const db = await new Promise((res, rej) => { const r = indexedDB.open('probe', 1); r.onupgradeneeded = () => r.result.createObjectStore('s'); r.onsuccess = () => res(r.result); r.onerror = () => rej(r.error); });
      await new Promise((res, rej) => { const tx = db.transaction('s', 'readwrite', { durability: 'strict' }); tx.objectStore('s').put(new Array(1000).fill('x').join(''), Date.now()); tx.oncomplete = res; tx.onerror = () => rej(tx.error); tx.onabort = () => rej(tx.error); });
      return { ok: true, quota: est.quota, usage: est.usage };
    } catch (e) { return { ok: false, err: String(e && e.name + ' ' + e.message), quota: est.quota, usage: est.usage }; }
  });
};
const server = await startServer();
const dir = tempProfile('quota');
let s = await start(dir);
console.log('1ª abertura', JSON.stringify(await step(s.ctx)));
execSync(`taskkill /F /T /PID ${s.child.pid}`, { stdio: 'ignore' });
await sleep(WAIT);
s = await start(dir);
console.log(`depois do kill (espera ${WAIT} ms)`, JSON.stringify(await step(s.ctx)));
await s.b.close().catch(() => {});
execSync(`taskkill /F /T /PID ${s.child.pid}`, { stdio: 'ignore' });
await sleep(1000);
s = await start(dir);
console.log('terceira abertura (fechamento limpo antes)', JSON.stringify(await step(s.ctx)));
await s.b.close().catch(() => {});
execSync(`taskkill /F /T /PID ${s.child.pid}`, { stdio: 'ignore' });
server.kill();
fs.rmSync(dir, { recursive: true, force: true, maxRetries: 5 });
