// CA 7: baixa de estoque por venda x agregada (item, caixa, janela). Só Node (node:sqlite).
// Mede número de movimentos, gravações na linha da numeração MOV e na linha do item mais vendido, e o tempo.
// A disputa real por lock é do PostgreSQL; aqui contamos as gravações que seriam serializadas nessas linhas.
import fs from 'node:fs';
import path from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { rng } from './lib/catalog.mjs';
import { RESULTS, writeResult, machineInfo } from './lib/harness.mjs';

const arg = (k, d) => (process.argv.find((a) => a.startsWith(`--${k}=`)) || '').split('=')[1] || d;
const CAIXAS = Number(arg('caixas', '20'));
const SALES = Number(arg('sales', '200'));
const ITEMS = Number(arg('items', '2000'));
const WINDOW_MIN = Number(arg('window', '5'));

// Vendas de um dia de loja (8 h), 3 linhas cada, itens com popularidade concentrada (poucos itens vendem muito).
const r = rng(2026);
const events = [];
for (let c = 1; c <= CAIXAS; c++) {
  for (let s = 1; s <= SALES; s++) {
    const at = Math.floor(((s - 1 + r()) / SALES) * 8 * 3600_000);
    const lines = [];
    for (let k = 0; k < 3; k++) lines.push({ item: Math.floor(ITEMS * r() ** 3), qty: 1 + Math.floor(r() * 3) });
    events.push({ caixa: c, seq: s, at, lines });
  }
}
events.sort((a, b) => a.at - b.at);

function run(label, groups) {
  const f = path.join(RESULTS, `stock-${label}.db`);
  fs.rmSync(f, { force: true });
  const db = new DatabaseSync(f);
  db.exec(`PRAGMA journal_mode = WAL; PRAGMA synchronous = FULL;
    CREATE TABLE seq (name TEXT PRIMARY KEY, n INTEGER NOT NULL);
    INSERT INTO seq VALUES ('MOV', 0);
    CREATE TABLE items (id INTEGER PRIMARY KEY, qty INTEGER NOT NULL);
    CREATE TABLE movements (number TEXT PRIMARY KEY, item INTEGER, qty INTEGER, origin TEXT);`);
  const ins = db.prepare('INSERT INTO items (id, qty) VALUES (?, 1000)');
  db.exec('BEGIN'); for (let i = 0; i < ITEMS; i++) ins.run(i); db.exec('COMMIT');
  const nextMov = db.prepare(`UPDATE seq SET n = n + 1 WHERE name = 'MOV' RETURNING n`);
  const insMov = db.prepare('INSERT INTO movements (number, item, qty, origin) VALUES (?, ?, ?, ?)');
  const upd = db.prepare('UPDATE items SET qty = qty - ? WHERE id = ?');
  const perItem = new Map();
  let txs = 0;
  const t0 = performance.now();
  for (const g of groups) {
    // uma transação por unidade de baixa (uma venda, ou um grupo agregado)
    db.exec('BEGIN IMMEDIATE');
    for (const l of g.lines) {
      const n = nextMov.get().n;
      insMov.run(`MOV-${n}`, l.item, -l.qty, g.origin);
      upd.run(l.qty, l.item);
      perItem.set(l.item, (perItem.get(l.item) || 0) + 1);
    }
    db.exec('COMMIT');
    txs++;
  }
  const ms = performance.now() - t0;
  const movs = db.prepare('SELECT COUNT(*) c FROM movements').get().c;
  const totalQty = db.prepare('SELECT SUM(1000 - qty) s FROM items').get().s;
  db.close();
  fs.rmSync(f, { force: true }); fs.rmSync(f + '-wal', { force: true }); fs.rmSync(f + '-shm', { force: true });
  const hot = Math.max(...perItem.values());
  return { label, transactions: txs, movements: movs, movNumberWrites: movs, hottestItemRowWrites: hot, totalQtyBaixada: totalQty, ms: Math.round(ms) };
}

// A: por venda
const perSale = events.map((e) => ({ origin: `venda ${e.caixa}-${e.seq}`, lines: e.lines }));

// B: agregada por item, caixa e janela
function aggregate(byCaixa) {
  const m = new Map();
  for (const e of events) {
    const w = Math.floor(e.at / (WINDOW_MIN * 60_000));
    for (const l of e.lines) {
      const k = `${byCaixa ? e.caixa : '*'}|${w}`;
      if (!m.has(k)) m.set(k, new Map());
      const g = m.get(k);
      g.set(l.item, (g.get(l.item) || 0) + l.qty);
    }
  }
  return [...m.entries()].map(([k, g]) => ({ origin: `janela ${k}`, lines: [...g.entries()].map(([item, qty]) => ({ item, qty })) }));
}

const res = {
  when: new Date().toISOString(), machine: machineInfo(), caixas: CAIXAS, salesPerCaixa: SALES, items: ITEMS, windowMin: WINDOW_MIN,
  lines: events.reduce((s, e) => s + e.lines.length, 0),
  perSale: run('per-sale', perSale),
  aggItemCaixaWindow: run('agg-caixa', aggregate(true)),
  aggItemWindow: run('agg-loja', aggregate(false)),
};
res.sameTotal = res.perSale.totalQtyBaixada === res.aggItemCaixaWindow.totalQtyBaixada && res.perSale.totalQtyBaixada === res.aggItemWindow.totalQtyBaixada;
console.log(JSON.stringify(res, null, 1));
writeResult(arg('out', 'stock.json'), res);
