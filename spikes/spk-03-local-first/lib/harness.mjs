// Apoio dos roteiros: sobe o servidor do spike e abre o Chromium do Playwright com perfil próprio.
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright-core';

export const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
export const PORT = Number(process.env.PORT || 5317);
export const BASE = `http://127.0.0.1:${PORT}`;
export const RESULTS = path.join(ROOT, 'results');
fs.mkdirSync(RESULTS, { recursive: true });

export async function startServer({ reset = true } = {}) {
  // SPK03_EXTERNAL_SERVER=1: o servidor já está rodando fora (ex.: fora do Job Object que limita a CPU)
  if (process.env.SPK03_EXTERNAL_SERVER === '1') {
    if (reset) await fetch(`${BASE}/api/reset`, { method: 'POST' });
    return { kill() {} };
  }
  const child = spawn(process.execPath, ['--no-warnings', path.join(ROOT, 'server.mjs'), ...(reset ? ['--reset'] : [])], {
    cwd: ROOT, env: { ...process.env, PORT: String(PORT) }, stdio: ['ignore', 'pipe', 'inherit'],
  });
  await new Promise((resolve, reject) => {
    child.stdout.on('data', (d) => { const s = String(d); if (s.includes('spk-03 server')) resolve(); if (process.env.VERBOSE) process.stdout.write(s); });
    child.on('exit', (c) => reject(new Error('servidor saiu ' + c)));
  });
  return child;
}

export function tempProfile(tag) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), `spk03-${tag}-`));
  return dir;
}

export function dirSize(dir) {
  let total = 0;
  if (!fs.existsSync(dir)) return 0;
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    total += e.isDirectory() ? dirSize(p) : fs.statSync(p).size;
  }
  return total;
}

// Chromium completo do Playwright em modo headless novo (mesmo motor de armazenamento do Chrome com janela).
export async function launch(userDataDir) {
  return chromium.launchPersistentContext(userDataDir, {
    channel: 'chromium',
    headless: true,
    viewport: { width: 1280, height: 800 },
    // cada caixa real é a aba em primeiro plano do seu aparelho; aqui várias abas dividem um navegador,
    // então o adiamento de timers de aba em segundo plano é desligado
    args: ['--disable-background-timer-throttling', '--disable-renderer-backgrounding', '--disable-backgrounding-occluded-windows'],
  });
}

// Throttling de CPU do Chromium. Pela sessão da página ele só pega na thread da página; com workers=true
// a mesma taxa é aplicada também em cada worker dedicado (auto-attach), que é onde roda o SQLite-WASM.
export async function throttle(context, page, rate, { workers = true } = {}) {
  const cdp = await context.newCDPSession(page);
  await cdp.send('Emulation.setCPUThrottlingRate', { rate });
  if (workers) {
    let id = 0;
    const replies = [];
    cdp.on('Target.receivedMessageFromTarget', ({ message }) => { const m = JSON.parse(message); if (m.error) { replies.push(m.error); console.log('worker cdp erro', JSON.stringify(m.error)); } });
    cdp.on('Target.attachedToTarget', async ({ sessionId, targetInfo }) => {
      const send = (method, params = {}) => cdp.send('Target.sendMessageToTarget', { sessionId, message: JSON.stringify({ id: ++id, method, params }) }).catch((e) => console.log('send', e.message));
      if (targetInfo.type === 'worker') await send('Emulation.setCPUThrottlingRate', { rate });
      await send('Runtime.runIfWaitingForDebugger');
    });
    await cdp.send('Target.setAutoAttach', { autoAttach: true, waitForDebuggerOnStart: true, flatten: false });
    cdp.workerErrors = replies;
  }
  return cdp;
}

export function chromiumPath() { return chromium.executablePath(); }

export function writeResult(name, obj) {
  const f = path.join(RESULTS, name);
  fs.writeFileSync(f, JSON.stringify(obj, null, 2));
  return f;
}

export function machineInfo() {
  return {
    cpu: os.cpus()[0]?.model, cores: os.cpus().length, ramGb: Math.round(os.totalmem() / 2 ** 30),
    os: `${os.type()} ${os.release()}`, node: process.version,
  };
}
