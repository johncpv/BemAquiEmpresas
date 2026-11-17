// LocalStore sobre SQLite-WASM: a thread principal só conversa com o worker (latência medida inclui o postMessage).
export class SqliteStore {
  constructor(name, { fts = true, journal = 'wal', sync = 'normal', locking = 'exclusive' } = {}) {
    this.name = name;
    this.kind = 'sqlite';
    this.fts = fts;
    this.opts = { journal, sync, locking };
    this.w = new Worker('./sqlite-worker.js', { type: 'module' });
    this.n = 0;
    this.wait = new Map();
    this.w.onmessage = (e) => {
      const { id, ok, result, error } = e.data;
      const p = this.wait.get(id);
      this.wait.delete(id);
      ok ? p.resolve(result) : p.reject(new Error(error));
    };
  }
  call(op, args) {
    const id = ++this.n;
    return new Promise((resolve, reject) => { this.wait.set(id, { resolve, reject }); this.w.postMessage({ id, op, args }); });
  }
  async open() { this.info = await this.call('open', { name: this.name, fts: this.fts, ...this.opts }); return this.info; }
  async close() { await this.call('close'); this.w.terminate(); }
  loadSnapshotFromUrl(url) { return this.call('loadSnapshotFromUrl', { url }); }
  applyDeltaFromUrl(url) { return this.call('applyDeltaFromUrl', { url }); }
  countItems() { return this.call('countItems'); }
  findByBarcode(code) { return this.call('findByBarcode', { code }); }
  searchName(term, mode, limit = 50) { return this.call('searchName', { term, mode, limit }); }
  getMeta(k) { return this.call('getMeta', { k }); }
  cartPut(line) { return this.call('cartPut', { line }); }
  cartGet() { return this.call('cartGet'); }
  commitSale(sale, event, meta) { return this.call('commitSale', { sale, event, meta }); }
  pending(afterSeq, limit) { return this.call('pending', { afterSeq, limit }); }
  setAcked(seq) { return this.call('setAcked', { seq }); }
  outboxSeqs() { return this.call('outboxSeqs'); }
  salesCount() { return this.call('salesCount'); }
  dbBytes() { return this.call('dbBytes'); }
  pragma(sql) { return this.call('pragma', { sql }); }
  cpuBench(n) { return this.call('cpuBench', { n }); }
}
