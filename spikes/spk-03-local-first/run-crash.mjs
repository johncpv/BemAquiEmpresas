// CA 4 e 5: navegador morto (taskkill /F no processo inteiro) e aba fechada no meio da venda; duas abas no mesmo caixa.
// Corte de energia do aparelho NÃO é simulado aqui (ver relatório): matar o processo não descarta o cache do sistema operacional.
import { execSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { startServer, tempProfile, BASE, writeResult, machineInfo, launch } from './lib/harness.mjs';

const arg = (k, d) => (process.argv.find((a) => a.startsWith(`--${k}=`)) || '').split('=')[1] || d;
const VARIANTS = arg('variants', 'idb-chunks:strict,idb-chunks:relaxed,sqlite:full,sqlite:normal').split(',');
const KILLS = Number(arg('kills', '6'));
const CLOSES = Number(arg('closes', '3'));
const N = Number(arg('n', '5000'));
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// Chromium do Playwright com o perfil persistente; o pid do processo principal vem do CDP (SystemInfo),
// para o taskkill /F /T derrubar o navegador inteiro (inclusive o processo de armazenamento).
// (Na primeira versão o Chromium era lançado à mão; sem os argumentos do Playwright, a gravação inicial
// no IndexedDB falhava às vezes com QuotaExceededError e o roteiro travava.)
async function startChromium(dir) {
  const context = await launch(dir);
  const browser = context.browser();
  const s = await browser.newBrowserCDPSession();
  const info = await s.send('SystemInfo.getProcessInfo');
  const pid = info.processInfo.find((p) => p.type === 'browser').id;
  return { child: { pid }, browser, context };
}

async function gotoRetry(page, u) {
  for (let i = 0; ; i++) {
    try { return await page.goto(u); } catch (e) { if (i >= 4) throw e; await sleep(500); }
  }
}

// Espera até nenhum processo do Chromium usar mais o perfil (o taskkill /T pode deixar filhos por alguns ms).
async function waitProfileFree(dir) {
  const t0 = Date.now();
  for (;;) {
    const ps = `$ProgressPreference='SilentlyContinue'; (Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'chrome.exe' -and $_.CommandLine -like '*${path.basename(dir)}*' }).Count`;
    const out = execSync(`powershell -NoProfile -EncodedCommand ${Buffer.from(ps, 'utf16le').toString('base64')}`).toString().trim();
    if (out === '0' || out === '' || Date.now() - t0 > 15000) return Date.now() - t0;
    await sleep(250);
  }
}

function killTree(pid) {
  try { execSync(`taskkill /F /T /PID ${pid}`, { stdio: 'ignore' }); } catch { /* já morreu */ }
}

function watch(page, log) {
  page.on('console', (m) => {
    const t = m.text();
    if (process.env.VERBOSE) console.log('    [page]', t.slice(0, 200));
    if (t.startsWith('ADDED ')) { const [, sale, k] = t.split(' '); log.added.set(sale, Number(k)); log.lastSale = sale; }
    else if (t.startsWith('COMMITTED ')) { const [, seq, sale] = t.split(' '); log.lastCommitted = Math.max(log.lastCommitted, Number(seq)); log.committedSales.add(sale); }
    else if (t.startsWith('OPENED ')) log.opened = JSON.parse(t.slice(7));
    else if (t.startsWith('ERROR')) { log.errors.push(t); console.log('  ', t); }
  });
}

const url = (tech, dur, device, autostart) =>
  `${BASE}/pos.html?tech=${tech}&device=${device}&n=${N}&sales=1000000&pace=25&drop=0.3&fail=0.05&batch=20&syncEvery=200&autostart=${autostart ? 1 : 0}&${tech === 'sqlite' ? 'sync' : 'durability'}=${dur}`;

// Abre a página de verificação (sem vender), confere o estado contra o que foi observado antes da queda.
async function verifyAfter(context, tech, dur, device, before) {
  if (process.env.VERBOSE) console.log('    páginas abertas:', context.pages().map((p) => p.url()));
  const page = await context.newPage();
  page.on('pageerror', (e) => console.log('    pageerror', e.message));
  const log = { added: new Map(), committedSales: new Set(), lastCommitted: 0, errors: [] };
  watch(page, log);
  const t0 = Date.now();
  await gotoRetry(page, url(tech, dur, device, false));
  // Se a abertura do armazenamento falhar logo depois da queda, recarrega a página (como o app faria) e conta.
  const bootErrors = [];
  for (let i = 0; ; i++) {
    await page.waitForFunction(() => window.__opened || window.__lock === 'busy' || window.__bootError, null, { timeout: 120_000 });
    const err = await page.evaluate(() => window.__bootError || null);
    if (!err) break;
    bootErrors.push({ atMs: Date.now() - t0, err });
    if (i >= 8) throw new Error('armazenamento não abriu depois de 9 tentativas: ' + err);
    await sleep(2000);
    await gotoRetry(page, url(tech, dur, device, false));
  }
  const reopenMs = Date.now() - t0;
  const st = await page.evaluate(() => window.inspect());
  const pend = before.lastSale && !before.committedSales.has(before.lastSale) ? before.lastSale : null;
  const linesSeen = pend ? before.added.get(pend) : 0;
  const checks = {
    noCommittedLost: st.seq >= before.lastCommitted,
    outboxContiguous: st.contiguous && st.uniqueSeqs && st.outbox === st.seq,
    salesMatchSeq: st.sales === st.seq,
    // carrinho: a venda em andamento volta com pelo menos os itens já confirmados na tela,
    // ou já tinha sido concluída depois do último log observado
    cartIntact: !pend || (st.cartSaleId === pend && st.cartLines >= linesSeen) || st.seq > before.lastCommitted,
  };
  return { page, st, checks, reopenMs, pend, linesSeen, lastCommittedSeen: before.lastCommitted, bootErrors };
}

const server = await startServer();
const out = { when: new Date().toISOString(), machine: machineInfo(), n: N, variants: [] };
try {
  for (const v of VARIANTS) {
    const [tech, dur] = v.split(':');
    const device = `CRASH-${tech}-${dur}`;
    const dir = tempProfile(`crash-${tech}-${dur}`);
    const V = { tech, durability: dur, cycles: [], errors: [] };
    console.log(`== ${v}`);
    // 1. carga inicial e primeira venda
    let { child, context, browser } = await startChromium(dir);
    let page = await context.newPage();
    let log = { added: new Map(), committedSales: new Set(), lastCommitted: 0, errors: [] };
    watch(page, log);
    await gotoRetry(page, url(tech, dur, device, true));
    await page.waitForFunction(() => window.__opened, null, { timeout: 300_000 });

    const total = KILLS + CLOSES;
    for (let c = 0; c < total; c++) {
      const kind = c < KILLS ? 'kill' : 'close';
      await sleep(1500 + Math.random() * 2500); // vende e sincroniza por 1,5 a 4 s
      const before = log;
      if (kind === 'kill') {
        killTree(child.pid);
        const freeMs = await waitProfileFree(dir);
        if (process.env.VERBOSE) console.log('    perfil livre em', freeMs, 'ms');
        ({ child, context, browser } = await startChromium(dir));
      } else {
        await page.close({ runBeforeUnload: false });
      }
      let r;
      const harnessErrors = [];
      for (let a = 0; !r; a++) {
        try {
          r = await verifyAfter(context, tech, dur, device, before);
        } catch (e) {
          // a página de verificação não carregou (Chromium recém-reiniciado travado): reinicia o navegador e tenta de novo
          harnessErrors.push(String(e.message).split('\n')[0]);
          console.log(`  ${kind} ${c + 1}: reabertura não respondeu (${harnessErrors.at(-1)}); reiniciando o navegador`);
          if (a >= 2) throw e;
          killTree(child.pid);
          await waitProfileFree(dir);
          ({ child, context, browser } = await startChromium(dir));
        }
      }
      r.harnessErrors = harnessErrors;
      const ok = Object.values(r.checks).every(Boolean);
      V.cycles.push({ kind, ok, ...r.checks, seqAfter: r.st.seq, ackedAfter: r.st.acked, lastCommittedSeen: r.lastCommittedSeen, pendingSaleLinesSeen: r.linesSeen, cartLinesAfter: r.st.cartLines, reopenMs: r.reopenMs, bootErrors: r.bootErrors, harnessErrors: r.harnessErrors });
      console.log(`  ${kind} ${c + 1}: ${ok ? 'ok' : 'FALHOU'} seq ${r.lastCommittedSeen}->${r.st.seq} carrinho ${r.linesSeen}->${r.st.cartLines} reabrir ${r.reopenMs} ms${r.bootErrors.length ? ` (falhas ao abrir: ${r.bootErrors.length}, ${r.bootErrors[0].err})` : ''}`);
      // retoma a venda na mesma página
      page = r.page;
      log = { added: new Map(), committedSales: new Set(), lastCommitted: r.st.seq, errors: [] };
      watch(page, log);
      await page.evaluate(() => window.start());
    }

    // 2. duas abas no mesmo caixa (Web Locks)
    const second = await context.newPage();
    await gotoRetry(second, url(tech, dur, device, false));
    await second.waitForFunction(() => window.__lock, null, { timeout: 60_000 });
    const secondLock = await second.evaluate(() => window.__lock);
    const secondText = await second.textContent('#status');
    await page.close({ runBeforeUnload: false });
    await sleep(300);
    const t0 = Date.now();
    const took = await second.evaluate(() => window.retryLock());
    V.webLocks = { secondTab: secondLock, message: secondText, afterFirstClosed: took ? 'owner' : 'busy', takeoverMs: Date.now() - t0 };

    // 3. esvazia a fila sem perder respostas e confere no servidor
    const fin = await second.evaluate(() => window.drain(0));
    const ver = await (await fetch(`${BASE}/api/sync/verify`)).json();
    const dev = ver.perDevice.find((d) => d.device_id === device) || {};
    V.final = { localSeq: fin.seq, acked: fin.acked, serverCount: dev.c, serverMax: dev.mx, chainBreaks: ver.chainBreaks };
    V.final.ok = fin.seq === fin.acked && dev.c === fin.seq && dev.mx === fin.seq && dev.mn === 1;
    V.ok = V.cycles.every((c) => c.ok) && V.final.ok && secondLock === 'busy' && took;
    console.log(`  web locks: ${JSON.stringify(V.webLocks)}; final: ${JSON.stringify(V.final)}`);
    await browser.close().catch(() => {});
    killTree(child.pid);
    await sleep(500);
    try { fs.rmSync(dir, { recursive: true, force: true, maxRetries: 5, retryDelay: 500 }); } catch { /* perfil ainda preso; fica na pasta temporária */ }
    out.variants.push(V);
    writeResult(arg('out', 'crash.json'), out); // grava a cada variante
  }
  const ver = await (await fetch(`${BASE}/api/sync/verify`)).json();
  out.server = { duplicatesRejected: ver.stats.duplicate || 0, accepted: ver.stats.accepted || 0, chainBreaks: ver.chainBreaks };
} finally {
  server.kill();
}
const f = writeResult(arg('out', 'crash.json'), out);
console.log('resultado em', f);
