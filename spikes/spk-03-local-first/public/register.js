// Núcleo do caixa simulado: carrinho persistido a cada item, venda concluída como evento imutável
// encadeado por hash, fila de saída e lote de sincronização idempotente (rascunho A, item 6).
import { uuidv7, sha256 } from './common.js';

export class Register {
  constructor(store, { deviceId, epoch = 1 }) {
    this.store = store;
    this.deviceId = deviceId;
    this.epoch = epoch;
  }

  async open() {
    this.seq = (await this.store.getMeta('seq')) || 0;
    this.lastHash = (await this.store.getMeta('lastHash')) || 'GENESIS';
    this.acked = (await this.store.getMeta('acked')) || 0;
    const cart = await this.store.cartGet();
    this.saleId = cart[0]?.saleId || uuidv7();
    return { seq: this.seq, acked: this.acked, cartLines: cart.length, saleId: this.saleId };
  }

  async addItem(item, qty = 1) {
    const line = { lineId: uuidv7(), saleId: this.saleId, itemId: item.id, name: item.name, price: item.price, qty, at: Date.now() };
    await this.store.cartPut(line);
    return line;
  }

  async complete() {
    const lines = await this.store.cartGet();
    const total = lines.reduce((s, l) => s + l.price * l.qty, 0); // centavos inteiros
    const sale = { id: this.saleId, lines, total, payments: [{ method: 'cash', amount: total }], at: Date.now() };
    const seq = this.seq + 1;
    const ev = {
      event_id: uuidv7(),
      device_id: this.deviceId,
      epoch: this.epoch,
      seq,
      schema_version: 1,
      type: 'SaleCompleted',
      device_at: Date.now(),
      prev_hash: this.lastHash,
      payload: { sale_id: sale.id, total, lines: lines.map((l) => ({ item_id: l.itemId, qty: l.qty, price: l.price })) },
    };
    ev.hash = await sha256(JSON.stringify(ev));
    await this.store.commitSale(sale, ev, { seq, lastHash: ev.hash });
    this.seq = seq;
    this.lastHash = ev.hash;
    this.saleId = uuidv7();
    return ev;
  }

  // Um lote. dropProb: a resposta "se perde" depois de o servidor gravar (o cliente reenvia o mesmo lote).
  // failProb: o servidor falha antes de gravar.
  async syncOnce({ batch = 25, dropProb = 0, failProb = 0 } = {}) {
    const evs = await this.store.pending(this.acked, batch);
    if (!evs.length) return { sent: 0 };
    const t0 = performance.now();
    let res;
    try {
      res = await fetch('/api/sync/events', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...(Math.random() < failProb ? { 'x-fail-before-commit': '1' } : {}) },
        body: JSON.stringify({ device_id: this.deviceId, epoch: this.epoch, events: evs }),
      });
    } catch (e) {
      return { sent: evs.length, error: 'network' };
    }
    const rtt = performance.now() - t0;
    if (!res.ok) return { sent: evs.length, error: res.status, rtt };
    const out = await res.json();
    const dup = out.results.filter((r) => r.status === 'duplicate').length;
    if (Math.random() < dropProb) return { sent: evs.length, dropped: true, rtt, dup };
    if (out.ack_seq > this.acked) {
      await this.store.setAcked(out.ack_seq);
      this.acked = out.ack_seq;
    }
    return { sent: evs.length, rtt, dup, ack: out.ack_seq, serverMs: out.server_ms };
  }
}
