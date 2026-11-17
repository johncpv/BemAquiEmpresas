// LocalStore "idb-chunks": o catálogo fica em memória (Map por código e por id) e o IndexedDB guarda só
// blocos de ~1.000 itens. Venda, carrinho e fila são os mesmos do IdbStore (um registro por linha/evento).
import Dexie from 'dexie';
import { IdbStore } from './store-idb.js';
import { norm } from './common.js';

const NCHUNKS = 50;
const chunkOf = (id) => {
  let h = 0;
  for (let i = 0; i < id.length; i++) h = (h * 31 + id.charCodeAt(i)) | 0;
  return Math.abs(h) % NCHUNKS;
};

export class IdbChunkStore {
  constructor(name, { durability = 'relaxed' } = {}) {
    this.name = name;
    this.kind = 'idb-chunks';
    this.db = new Dexie(name, { chromeTransactionDurability: durability });
    this.db.version(1).stores({ chunks: 'k', cart: 'lineId', sales: 'id', outbox: 'seq', meta: 'k' });
  }

  async open() {
    await this.db.open();
    const t0 = performance.now();
    const chunks = await this.db.chunks.toArray();
    this.index(chunks.flatMap((c) => c.items));
    this.loadMs = performance.now() - t0;
  }
  async close() { this.db.close(); }

  index(items) {
    this.byId = new Map();
    this.byCode = new Map();
    for (const it of items) this.put(it);
  }
  put(it) {
    const old = this.byId.get(it.id);
    if (old) for (const c of old.barcodes) this.byCode.delete(c);
    it.nameNorm = norm(it.name);
    this.byId.set(it.id, it);
    for (const c of it.barcodes) this.byCode.set(c, it);
    this.names = null;
  }
  del(id) {
    const old = this.byId.get(id);
    if (!old) return;
    for (const c of old.barcodes) this.byCode.delete(c);
    this.byId.delete(id);
    this.names = null;
  }

  async writeChunks(which) {
    const groups = new Map();
    for (const k of which) groups.set(k, []);
    for (const it of this.byId.values()) {
      const k = chunkOf(it.id);
      if (groups.has(k)) groups.get(k).push({ id: it.id, name: it.name, barcodes: it.barcodes, price: it.price, unit: it.unit, cat: it.cat, seq: it.seq });
    }
    await this.db.chunks.bulkPut([...groups.entries()].map(([k, items]) => ({ k, items })));
  }

  async loadSnapshot(items) {
    this.index(items.map((i) => ({ ...i })));
    await this.writeChunks([...Array(NCHUNKS).keys()]);
  }

  async applyDelta(changes) {
    const touched = new Set();
    for (const c of changes) {
      if (c.op === 'upsert') { this.put({ ...c.item }); touched.add(chunkOf(c.item.id)); } else { this.del(c.id); touched.add(chunkOf(c.id)); }
    }
    await this.writeChunks([...touched]);
  }

  async countItems() { return this.byId.size; }
  async findByBarcode(code) { return this.byCode.get(code) || null; }
  async buildMemIndex() {
    this.names = [];
    this.items = [];
    for (const it of this.byId.values()) { this.names.push(it.nameNorm); this.items.push(it); }
  }
  async searchName(term, mode, limit = 50) {
    if (!this.names) await this.buildMemIndex();
    const toks = norm(term).split(/\s+/).filter(Boolean);
    const out = [];
    for (let i = 0; i < this.names.length && out.length < limit; i++) {
      const n = this.names[i];
      if (toks.every((t) => n.includes(t))) out.push(this.items[i]);
    }
    return out;
  }
}

// venda, carrinho e fila: mesmas tabelas e mesmo código do IdbStore
for (const m of ['getMeta', 'cartPut', 'cartGet', 'commitSale', 'pending', 'setAcked', 'outboxSeqs', 'salesCount']) {
  IdbChunkStore.prototype[m] = IdbStore.prototype[m];
}
