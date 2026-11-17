// Servidor mínimo do spike: arquivos estáticos, catálogo (foto e delta) e endpoint de lote de sincronização.
// Simula o servidor do PDV; NÃO é o backend .NET. O banco é SQLite (node:sqlite), não PostgreSQL.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import zlib from 'node:zlib';
import { fileURLToPath } from 'node:url';
import { DatabaseSync } from 'node:sqlite';
import { snapshot, delta, searchTerms } from './lib/catalog.mjs';

const ROOT = path.dirname(fileURLToPath(import.meta.url));
const PORT = Number(process.env.PORT || 5317);
// Banco fora do repositório (pasta temporária), para nada no worktree mexer nele durante a carga.
const DATA = process.env.SPK03_DATA || path.join(os.tmpdir(), `spk03-server-${PORT}`);
fs.mkdirSync(DATA, { recursive: true });
const DB_FILE = path.join(DATA, 'server.db');

let db;
function openDb(reset) {
  if (db) db.close();
  if (reset) for (const f of [DB_FILE, DB_FILE + '-wal', DB_FILE + '-shm']) fs.rmSync(f, { force: true });
  db = new DatabaseSync(DB_FILE);
  db.exec(`
    PRAGMA journal_mode = WAL;
    PRAGMA synchronous = FULL;
    CREATE TABLE IF NOT EXISTS device_events (
      event_id TEXT PRIMARY KEY,
      device_id TEXT NOT NULL,
      epoch INTEGER NOT NULL,
      seq INTEGER NOT NULL,
      prev_hash TEXT NOT NULL,
      hash TEXT NOT NULL,
      payload TEXT NOT NULL,
      received_at INTEGER NOT NULL,
      UNIQUE (device_id, epoch, seq)
    );
    CREATE TABLE IF NOT EXISTS sales (
      sale_id TEXT PRIMARY KEY,
      number TEXT NOT NULL UNIQUE,
      device_id TEXT NOT NULL,
      total INTEGER NOT NULL,
      lines INTEGER NOT NULL
    );
    CREATE TABLE IF NOT EXISTS stats (k TEXT PRIMARY KEY, v INTEGER NOT NULL);
  `);
}
openDb(process.argv.includes('--reset'));

const insEvent = () => db.prepare(`INSERT INTO device_events (event_id, device_id, epoch, seq, prev_hash, hash, payload, received_at)
  VALUES (?, ?, ?, ?, ?, ?, ?, ?) ON CONFLICT DO NOTHING`);
const insSale = () => db.prepare(`INSERT INTO sales (sale_id, number, device_id, total, lines) VALUES (?, ?, ?, ?, ?) ON CONFLICT DO NOTHING`);
const bump = () => db.prepare(`INSERT INTO stats (k, v) VALUES (?, ?) ON CONFLICT(k) DO UPDATE SET v = v + excluded.v`);

// Catálogo em memória, comprimido uma vez.
const cache = {};
function catalogBody(kind, n) {
  const key = `${kind}:${n}`;
  if (!cache[key]) {
    const obj = kind === 'snapshot' ? snapshot(n) : kind === 'delta' ? delta(n) : searchTerms(n);
    const raw = Buffer.from(JSON.stringify(obj));
    cache[key] = { raw, gz: zlib.gzipSync(raw, { level: 6 }) };
  }
  return cache[key];
}

const batchTimes = [];

function ingest(body) {
  const t0 = performance.now();
  const results = [];
  const ie = insEvent();
  const is = insSale();
  const b = bump();
  db.exec('BEGIN IMMEDIATE');
  try {
    for (const ev of body.events) {
      const r = ie.run(ev.event_id, ev.device_id, ev.epoch, ev.seq, ev.prev_hash, ev.hash, JSON.stringify(ev.payload), Date.now());
      if (r.changes === 1) {
        // Projeção na venda (no produto real: sales.sales via ISaleIngestion).
        const p = ev.payload;
        is.run(p.sale_id, `${ev.device_id}.${ev.epoch}-${ev.seq}`, ev.device_id, p.total, p.lines.length);
        results.push({ seq: ev.seq, status: 'accepted' });
        b.run('accepted', 1);
      } else {
        results.push({ seq: ev.seq, status: 'duplicate' });
        b.run('duplicate', 1);
      }
    }
    db.exec('COMMIT');
  } catch (e) {
    db.exec('ROLLBACK');
    throw e;
  }
  // Até qual seq o servidor tem tudo contíguo para este aparelho e época.
  const ack = db.prepare(`SELECT COALESCE(MAX(seq), 0) AS m, COUNT(*) AS c FROM device_events WHERE device_id = ? AND epoch = ?`)
    .get(body.device_id, body.epoch);
  const ackSeq = ack.m === ack.c ? ack.m : contiguous(body.device_id, body.epoch);
  const ms = performance.now() - t0;
  batchTimes.push(ms);
  return { ack_seq: ackSeq, results, server_ms: ms };
}

function contiguous(device, epoch) {
  const rows = db.prepare('SELECT seq FROM device_events WHERE device_id = ? AND epoch = ? ORDER BY seq').all(device, epoch);
  let n = 0;
  for (const r of rows) { if (r.seq === n + 1) n++; else break; }
  return n;
}

function verify() {
  const perDevice = db.prepare(`SELECT device_id, epoch, COUNT(*) c, MIN(seq) mn, MAX(seq) mx FROM device_events GROUP BY device_id, epoch ORDER BY device_id`).all();
  // Cadeia de hash: cada evento aponta para o hash do anterior do mesmo aparelho e época.
  let chainBreaks = 0;
  const rows = db.prepare('SELECT device_id, epoch, seq, prev_hash, hash FROM device_events ORDER BY device_id, epoch, seq').all();
  let prev = null;
  for (const r of rows) {
    if (prev && prev.device_id === r.device_id && prev.epoch === r.epoch) {
      if (r.prev_hash !== prev.hash || r.seq !== prev.seq + 1) chainBreaks++;
    } else if (r.seq !== 1 || r.prev_hash !== 'GENESIS') chainBreaks++;
    prev = r;
  }
  const totals = db.prepare('SELECT (SELECT COUNT(*) FROM device_events) ev, (SELECT COUNT(*) FROM sales) sales, (SELECT COUNT(DISTINCT number) FROM sales) numbers').get();
  const stats = Object.fromEntries(db.prepare('SELECT k, v FROM stats').all().map((r) => [r.k, r.v]));
  db.exec('PRAGMA wal_checkpoint(TRUNCATE)');
  const pageCount = db.prepare('PRAGMA page_count').get().page_count;
  const pageSize = db.prepare('PRAGMA page_size').get().page_size;
  const sorted = [...batchTimes].sort((a, b) => a - b);
  const q = (p) => (sorted.length ? sorted[Math.min(sorted.length - 1, Math.floor(p * sorted.length))] : 0);
  return {
    perDevice, chainBreaks, totals, stats,
    dbBytes: pageCount * pageSize,
    batches: sorted.length,
    serverBatchMs: { p50: q(0.5), p95: q(0.95), max: sorted.at(-1) || 0 },
  };
}

const TYPES = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.mjs': 'text/javascript', '.wasm': 'application/wasm', '.json': 'application/json' };

function send(res, status, body, headers = {}) {
  res.writeHead(status, {
    'Cross-Origin-Opener-Policy': 'same-origin',
    'Cross-Origin-Embedder-Policy': 'require-corp',
    'Cache-Control': 'no-store',
    ...headers,
  });
  res.end(body);
}

function readBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    req.on('data', (c) => chunks.push(c));
    req.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
    req.on('error', reject);
  });
}

const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, `http://${req.headers.host}`);
  const p = url.pathname;
  try {
    if (p === '/api/catalog/snapshot' || p === '/api/catalog/changes' || p === '/api/catalog/terms') {
      const n = Number(url.searchParams.get('n') || 50000);
      const kind = p.endsWith('snapshot') ? 'snapshot' : p.endsWith('changes') ? 'delta' : 'terms';
      const body = catalogBody(kind, n);
      const gz = /gzip/.test(req.headers['accept-encoding'] || '');
      return send(res, 200, gz ? body.gz : body.raw, {
        'Content-Type': 'application/json',
        ...(gz ? { 'Content-Encoding': 'gzip' } : {}),
        'X-Raw-Bytes': String(body.raw.length),
        'X-Gz-Bytes': String(body.gz.length),
        'Access-Control-Expose-Headers': 'X-Raw-Bytes, X-Gz-Bytes',
      });
    }
    if (p === '/api/sync/events' && req.method === 'POST') {
      const body = JSON.parse(await readBody(req));
      // Falha injetada ANTES de gravar (o cliente tem de reenviar).
      if (req.headers['x-fail-before-commit'] === '1') return send(res, 503, '{"error":"injected"}', { 'Content-Type': 'application/json' });
      const out = ingest(body);
      return send(res, 200, JSON.stringify(out), { 'Content-Type': 'application/json' });
    }
    if (p === '/api/sync/verify') return send(res, 200, JSON.stringify(verify()), { 'Content-Type': 'application/json' });
    if (p === '/api/reset' && req.method === 'POST') { openDb(true); batchTimes.length = 0; return send(res, 200, '{}'); }
    if (p === '/api/log' && req.method === 'POST') { process.stdout.write(`[page] ${await readBody(req)}\n`); return send(res, 204, ''); }

    // Estáticos: /nm/* -> node_modules, resto -> public
    let file;
    if (p.startsWith('/nm/')) file = path.join(ROOT, 'node_modules', decodeURIComponent(p.slice(4)));
    else file = path.join(ROOT, 'public', p === '/' ? 'index.html' : decodeURIComponent(p.slice(1)));
    if (!file.startsWith(ROOT) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) return send(res, 404, 'not found');
    return send(res, 200, fs.readFileSync(file), { 'Content-Type': TYPES[path.extname(file)] || 'application/octet-stream' });
  } catch (e) {
    console.error(new Date().toISOString(), p, e);
    return send(res, 500, JSON.stringify({ error: String(e) }), { 'Content-Type': 'application/json' });
  }
});

server.listen(PORT, '127.0.0.1', () => console.log(`spk-03 server http://127.0.0.1:${PORT}`));
