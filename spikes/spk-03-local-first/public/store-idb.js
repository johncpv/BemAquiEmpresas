// LocalStore sobre IndexedDB, via Dexie. Roda na thread principal.
import Dexie from 'dexie';
import { itemRow, norm } from './common.js';

export class IdbStore {
  constructor(name, { durability = 'relaxed', lean = false } = {}) {
    // lean: sem o índice multiEntry de palavras (a busca por nome fica no índice em memória)
    this.lean = lean;
    this.name = name;
    this.kind = 'idb';
    this.db = new Dexie(name, { chromeTransactionDurability: durability });
    this.db.version(1).stores({
      items: lean ? 'id, *barcodes' : 'id, *barcodes, *words',
      cart: 'lineId',
      sales: 'id',
      outbox: 'seq',
      meta: 'k',
    });
    this.mem = null;
  }

  row(it) { const r = itemRow(it); if (this.lean) delete r.words; return r; }

  async open() { await this.db.open(); }
  async close() { this.db.close(); }

  // ---------- catálogo ----------
  async loadSnapshot(items) {
    await this.db.items.clear();
    const CH = 5000;
    for (let i = 0; i < items.length; i += CH) {
      await this.db.items.bulkPut(items.slice(i, i + CH).map((it) => this.row(it)));
    }
  }

  async applyDelta(changes) {
    await this.db.transaction('rw', this.db.items, async () => {
      const ups = changes.filter((c) => c.op === 'upsert').map((c) => this.row(c.item));
      const dels = changes.filter((c) => c.op === 'delete').map((c) => c.id);
      await this.db.items.bulkPut(ups);
      await this.db.items.bulkDelete(dels);
    });
    this.mem = null;
  }

  async countItems() { return this.db.items.count(); }

  async findByBarcode(code) {
    return (await this.db.items.where('barcodes').equals(code).first()) || null;
  }

  // Índice em memória (nome normalizado + id), montado a partir do IndexedDB.
  async buildMemIndex() {
    const names = [];
    const ids = [];
    // getAll em blocos (bem mais rápido que cursor item a item)
    let last = '';
    for (;;) {
      const chunk = await this.db.items.where('id').above(last).limit(10000).toArray();
      if (!chunk.length) break;
      for (const it of chunk) { names.push(it.nameNorm); ids.push(it.id); }
      last = chunk.at(-1).id;
    }
    this.mem = { names, ids };
  }

  async searchName(term, mode, limit = 50) {
    const toks = norm(term).split(/\s+/).filter(Boolean);
    if (mode === 'prefix') {
      // índice multiEntry por palavra: começo de palavra
      const [t0, ...rest] = toks;
      return this.db.items.where('words').startsWith(t0).distinct()
        .filter((it) => rest.every((r) => it.words.some((w) => w.startsWith(r))))
        .limit(limit).toArray();
    }
    if (mode === 'scan') {
      // trecho em qualquer lugar: varre a tabela com cursor
      return this.db.items.filter((it) => toks.every((t) => it.nameNorm.includes(t))).limit(limit).toArray();
    }
    if (mode === 'mem') {
      if (!this.mem) await this.buildMemIndex();
      const hit = [];
      const { names, ids } = this.mem;
      for (let i = 0; i < names.length && hit.length < limit; i++) {
        const n = names[i];
        if (toks.every((t) => n.includes(t))) hit.push(ids[i]);
      }
      return (await this.db.items.bulkGet(hit)).filter(Boolean);
    }
    throw new Error('modo desconhecido ' + mode);
  }

  // ---------- venda ----------
  async getMeta(k) { return (await this.db.meta.get(k))?.v; }
  async cartPut(line) { await this.db.cart.put(line); }
  async cartGet() { return this.db.cart.toArray(); }

  // Gravação atômica da venda concluída: venda + evento na fila + contador + carrinho limpo.
  async commitSale(sale, event, meta) {
    await this.db.transaction('rw', [this.db.sales, this.db.outbox, this.db.meta, this.db.cart], async () => {
      await this.db.sales.put(sale);
      await this.db.outbox.put(event);
      await this.db.meta.bulkPut(Object.entries(meta).map(([k, v]) => ({ k, v })));
      await this.db.cart.clear();
    });
  }

  async pending(afterSeq, limit) {
    return this.db.outbox.where('seq').above(afterSeq).limit(limit).toArray();
  }

  async setAcked(seq) { await this.db.meta.put({ k: 'acked', v: seq }); }
  async outboxSeqs() { return this.db.outbox.toCollection().primaryKeys(); }
  async salesCount() { return this.db.sales.count(); }
}
