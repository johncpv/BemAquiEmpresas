// Worker do LocalStore SQLite-WASM. VFS opfs-sahpool (OPFS com SyncAccessHandle; não exige COOP/COEP).
import sqlite3InitModule from '/nm/@sqlite.org/sqlite-wasm/dist/index.mjs';

let sqlite3, db, pool;
const norm = (s) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();

const SCHEMA = `
  CREATE TABLE IF NOT EXISTS items (
    rid INTEGER PRIMARY KEY, id TEXT NOT NULL UNIQUE, name TEXT NOT NULL, name_norm TEXT NOT NULL,
    price INTEGER NOT NULL, unit TEXT, cat INTEGER, seq INTEGER);
  CREATE TABLE IF NOT EXISTS barcodes (code TEXT PRIMARY KEY, item_id TEXT NOT NULL) WITHOUT ROWID;
  CREATE INDEX IF NOT EXISTS barcodes_item ON barcodes(item_id);
  CREATE TABLE IF NOT EXISTS cart (line_id TEXT PRIMARY KEY, data TEXT NOT NULL) WITHOUT ROWID;
  CREATE TABLE IF NOT EXISTS sales (id TEXT PRIMARY KEY, data TEXT NOT NULL) WITHOUT ROWID;
  CREATE TABLE IF NOT EXISTS outbox (seq INTEGER PRIMARY KEY, data TEXT NOT NULL);
  CREATE TABLE IF NOT EXISTS meta (k TEXT PRIMARY KEY, v TEXT) WITHOUT ROWID;
`;

function rows(sql, bind) {
  return db.exec({ sql, bind, rowMode: 'object', returnValue: 'resultRows' });
}

function tx(fn) {
  db.exec('BEGIN IMMEDIATE');
  try { const r = fn(); db.exec('COMMIT'); return r; } catch (e) { db.exec('ROLLBACK'); throw e; }
}

function insertItems(items) {
  const si = db.prepare('INSERT INTO items (id, name, name_norm, price, unit, cat, seq) VALUES (?,?,?,?,?,?,?) ON CONFLICT(id) DO UPDATE SET name=excluded.name, name_norm=excluded.name_norm, price=excluded.price, unit=excluded.unit, cat=excluded.cat, seq=excluded.seq');
  const sb = db.prepare('INSERT OR REPLACE INTO barcodes (code, item_id) VALUES (?, ?)');
  try {
    for (const it of items) {
      si.bind([it.id, it.name, norm(it.name), it.price, it.unit, it.cat, it.seq]).stepReset();
      for (const c of it.barcodes) sb.bind([c, it.id]).stepReset();
    }
  } finally { si.finalize(); sb.finalize(); }
}

const ops = {
  async open({ name, fts = true, journal = 'wal', sync = 'normal', locking = 'exclusive' }) {
    const t0 = performance.now();
    sqlite3 = await sqlite3InitModule();
    const tInit = performance.now() - t0;
    pool = await sqlite3.installOpfsSAHPoolVfs({ name: 'pool-' + name, initialCapacity: 6 });
    const tPool = performance.now() - t0 - tInit;
    db = new pool.OpfsSAHPoolDb('/pdv.sqlite3');
    // um PRAGMA por chamada; WAL no opfs-sahpool só funciona com locking_mode=exclusive (sem memória compartilhada)
    // (o journal_mode só pega depois de o arquivo ter o esquema)
    rows(`PRAGMA locking_mode = ${locking}`);
    db.exec(SCHEMA);
    rows(`PRAGMA journal_mode = ${journal}`);
    rows(`PRAGMA synchronous = ${sync}`);
    if (fts) db.exec(`CREATE VIRTUAL TABLE IF NOT EXISTS items_fts USING fts5(name_norm, content='items', content_rowid='rid', tokenize='trigram')`);
    return { version: sqlite3.version.libVersion, wasmInitMs: tInit, poolInstallMs: tPool, openMs: performance.now() - t0,
      journal: rows('PRAGMA journal_mode')[0].journal_mode, locking: rows('PRAGMA locking_mode')[0].locking_mode, synchronous: rows('PRAGMA synchronous')[0].synchronous };
  },
  close() { db?.close(); pool?.pauseVfs?.(); return true; },

  async loadSnapshotFromUrl({ url }) {
    const t0 = performance.now();
    const res = await fetch(url);
    const text = await res.text();
    const tFetch = performance.now() - t0;
    const data = JSON.parse(text);
    const tParse = performance.now() - t0 - tFetch;
    const t1 = performance.now();
    tx(() => {
      db.exec('DELETE FROM items; DELETE FROM barcodes;');
      insertItems(data.items);
      db.exec(`INSERT OR REPLACE INTO meta (k, v) VALUES ('cursor', '${data.cursor}')`);
    });
    const tInsert = performance.now() - t1;
    const t2 = performance.now();
    let tFts = 0;
    if (hasFts()) { db.exec(`INSERT INTO items_fts(items_fts) VALUES('rebuild')`); tFts = performance.now() - t2; }
    return { count: data.items.length, fetchMs: tFetch, parseMs: tParse, insertMs: tInsert, ftsMs: tFts,
      rawBytes: Number(res.headers.get('X-Raw-Bytes')), gzBytes: Number(res.headers.get('X-Gz-Bytes')) };
  },

  async applyDeltaFromUrl({ url }) {
    const t0 = performance.now();
    const res = await fetch(url);
    const data = await res.json();
    const tFetch = performance.now() - t0;
    const t1 = performance.now();
    const fts = hasFts();
    tx(() => {
      const ups = data.changes.filter((c) => c.op === 'upsert').map((c) => c.item);
      const dels = data.changes.filter((c) => c.op === 'delete').map((c) => c.id);
      const touched = [...ups.map((i) => i.id), ...dels];
      // FTS de conteúdo externo: tira as linhas antigas do índice antes de mudar a tabela
      if (fts) {
        const del = db.prepare(`INSERT INTO items_fts(items_fts, rowid, name_norm) SELECT 'delete', rid, name_norm FROM items WHERE id = ?`);
        try { for (const id of touched) del.bind([id]).stepReset(); } finally { del.finalize(); }
      }
      insertItems(ups);
      const di = db.prepare('DELETE FROM items WHERE id = ?');
      const db_ = db.prepare('DELETE FROM barcodes WHERE item_id = ?');
      try { for (const id of dels) { di.bind([id]).stepReset(); db_.bind([id]).stepReset(); } } finally { di.finalize(); db_.finalize(); }
      if (fts) {
        const add = db.prepare(`INSERT INTO items_fts(rowid, name_norm) SELECT rid, name_norm FROM items WHERE id = ?`);
        try { for (const it of ups) add.bind([it.id]).stepReset(); } finally { add.finalize(); }
      }
      db.exec({ sql: `INSERT OR REPLACE INTO meta (k, v) VALUES ('cursor', ?)`, bind: [String(data.cursor)] });
    });
    return { changes: data.changes.length, fetchMs: tFetch, applyMs: performance.now() - t1, gzBytes: Number(res.headers.get('X-Gz-Bytes')), rawBytes: Number(res.headers.get('X-Raw-Bytes')) };
  },

  countItems() { return rows('SELECT COUNT(*) c FROM items')[0].c; },

  findByBarcode({ code }) {
    return rows('SELECT i.id, i.name, i.price, i.unit FROM barcodes b JOIN items i ON i.id = b.item_id WHERE b.code = ?', [code])[0] || null;
  },

  searchName({ term, mode, limit = 50 }) {
    const toks = norm(term).split(/\s+/).filter(Boolean);
    if (mode === 'prefix') {
      const where = toks.map(() => `(name_norm LIKE ? OR name_norm LIKE ?)`).join(' AND ');
      const bind = toks.flatMap((t) => [t + '%', '% ' + t + '%']);
      return rows(`SELECT id, name, price FROM items WHERE ${where} LIMIT ${limit}`, bind);
    }
    if (mode === 'scan') {
      const where = toks.map(() => `name_norm LIKE ?`).join(' AND ');
      return rows(`SELECT id, name, price FROM items WHERE ${where} LIMIT ${limit}`, toks.map((t) => `%${t}%`));
    }
    if (mode === 'fts') {
      const q = toks.map((t) => `"${t.replace(/"/g, '')}"`).join(' AND ');
      return rows(`SELECT i.id, i.name, i.price FROM items_fts f JOIN items i ON i.rid = f.rowid WHERE items_fts MATCH ? LIMIT ${limit}`, [q]);
    }
    throw new Error('modo desconhecido ' + mode);
  },

  getMeta({ k }) {
    const r = rows('SELECT v FROM meta WHERE k = ?', [k])[0];
    return r ? JSON.parse(r.v) : undefined;
  },
  cartPut({ line }) { db.exec({ sql: 'INSERT OR REPLACE INTO cart (line_id, data) VALUES (?, ?)', bind: [line.lineId, JSON.stringify(line)] }); return true; },
  cartGet() { return rows('SELECT data FROM cart').map((r) => JSON.parse(r.data)); },
  commitSale({ sale, event, meta }) {
    tx(() => {
      db.exec({ sql: 'INSERT INTO sales (id, data) VALUES (?, ?)', bind: [sale.id, JSON.stringify(sale)] });
      db.exec({ sql: 'INSERT INTO outbox (seq, data) VALUES (?, ?)', bind: [event.seq, JSON.stringify(event)] });
      for (const [k, v] of Object.entries(meta)) db.exec({ sql: 'INSERT OR REPLACE INTO meta (k, v) VALUES (?, ?)', bind: [k, JSON.stringify(v)] });
      db.exec('DELETE FROM cart');
    });
    return true;
  },
  pending({ afterSeq, limit }) { return rows('SELECT data FROM outbox WHERE seq > ? ORDER BY seq LIMIT ?', [afterSeq, limit]).map((r) => JSON.parse(r.data)); },
  setAcked({ seq }) { db.exec({ sql: `INSERT OR REPLACE INTO meta (k, v) VALUES ('acked', ?)`, bind: [JSON.stringify(seq)] }); return true; },
  outboxSeqs() { return rows('SELECT seq FROM outbox ORDER BY seq').map((r) => r.seq); },
  salesCount() { return rows('SELECT COUNT(*) c FROM sales')[0].c; },
  dbBytes() { return rows('PRAGMA page_count')[0].page_count * rows('PRAGMA page_size')[0].page_size; },
  pragma({ sql }) { return rows(sql); },
  cpuBench({ n }) { return cpuBench(n); },
};

function hasFts() { return rows(`SELECT 1 x FROM sqlite_master WHERE name = 'items_fts'`).length > 0; }

export function cpuBench(n) {
  const t0 = performance.now();
  let x = 0;
  for (let i = 0; i < n; i++) x = (x + Math.sqrt(i) * 1.0000001) % 1e9;
  return { ms: performance.now() - t0, x };
}

self.onmessage = async (e) => {
  const { id, op, args } = e.data;
  try {
    const result = await ops[op](args || {});
    self.postMessage({ id, ok: true, result });
  } catch (err) {
    self.postMessage({ id, ok: false, error: String(err && err.message || err) });
  }
};
