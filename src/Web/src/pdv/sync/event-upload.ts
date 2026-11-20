import { CHAIN_META, type ChainHead, type LocalStore, type OutboxEntry, type OutboxSeal } from '@/pdv/local-store/local-store'
import { EVENT_SCHEMA_VERSION, eventHash, uploadPayload } from './event-chain'

/**
 * Subida da fila de saída do caixa (OFF-02 CA 1 a 6). Em duas etapas, para o reenvio ser sempre o mesmo evento:
 * 1. **Selo**: cada evento novo da fila recebe, uma vez, o `seq` contínuo da época do caixa, o hash do anterior e o próprio (`event-chain.ts`),
 *    com o conteúdo já em texto e sem dado pessoal. O selo fica gravado no evento; reenviar manda o mesmo texto e o mesmo hash.
 * 2. **Envio**: lotes na ordem do `seq`, assinados com a chave do aparelho (sem operador com sessão). O CPF/CNPJ do consumidor vai num bloco à
 *    parte do lote, decifrado só para o envio (CA 17). O que o servidor responde (aceito, com divergência ou em quarentena: todos guardados lá)
 *    sai da fila; o resto fica e sobe na próxima rodada, a partir do primeiro não confirmado (CA 6).
 */
export interface DeviceRef {
  deviceId: string
  registerId: string
  deviceEpoch: number
}

export interface UploadEvent {
  eventId: string
  deviceId: string
  registerId: string
  deviceEpoch: number
  seq: number
  schemaVersion: number
  kind: string
  occurredAt: string
  prevHash: string | null
  hash: string
  payload: string
}

export interface UploadDocument {
  saleId: string
  kind: 'cpf' | 'cnpj'
  value: string
}

export interface UploadBody {
  events: UploadEvent[]
  documents: UploadDocument[]
  oldestPendingAt: string | null
}

export type UploadOutcome = 'Accepted' | 'AcceptedWithDivergence' | 'Quarantined'

export interface UploadResponse {
  results: { eventId: string; deviceEpoch: number; seq: number; outcome: UploadOutcome; replayed: boolean }[]
  acks: { deviceEpoch: number; seq: number }[]
}

export interface UploadSummary {
  sent: number
  withDivergence: number
  quarantined: number
  remaining: number
}

export const BATCH_SIZE = 100
const MAX_BATCHES = 20

/** Sela, na ordem da fila, os eventos ainda sem selo. Devolve quantos selou. */
export async function sealPending(store: Pick<LocalStore, 'outbox' | 'getMeta' | 'sealOutbox'>, device: DeviceRef): Promise<number> {
  for (let attempt = 0; attempt < 3; attempt++) {
    const rows = (await store.outbox()).filter((r) => !r.seal)
    if (rows.length === 0) return 0
    const stored = (await store.getMeta<ChainHead>(CHAIN_META)) ?? null
    // Época nova (aparelho registrado de novo no caixa): a sequência recomeça em 1.
    let head: ChainHead = stored && stored.deviceEpoch === device.deviceEpoch ? stored : { deviceEpoch: device.deviceEpoch, seq: 0, hash: null }
    const seals: { seq: number; seal: OutboxSeal }[] = []
    for (const row of rows) {
      const payload = JSON.stringify(uploadPayload(row.kind, row.payload) ?? null)
      const seal: Omit<OutboxSeal, 'hash'> = {
        deviceId: device.deviceId, registerId: device.registerId, deviceEpoch: device.deviceEpoch, seq: head.seq + 1, schemaVersion: EVENT_SCHEMA_VERSION,
        occurredAt: row.createdAt, prevHash: head.hash, payload,
      }
      const hash = await eventHash({ ...seal, eventId: row.id, kind: row.kind })
      seals.push({ seq: row.seq, seal: { ...seal, hash } })
      head = { deviceEpoch: device.deviceEpoch, seq: seal.seq, hash }
    }

    if (await store.sealOutbox(seals, stored, head)) return seals.length
  }

  return 0
}

export function toUploadEvent(row: OutboxEntry): UploadEvent {
  const s = row.seal!
  return {
    eventId: row.id, deviceId: s.deviceId, registerId: s.registerId, deviceEpoch: s.deviceEpoch, seq: s.seq, schemaVersion: s.schemaVersion, kind: row.kind,
    occurredAt: s.occurredAt, prevHash: s.prevHash, hash: s.hash, payload: s.payload,
  }
}

/** O documento do consumidor da venda (o último `CustomerSet` com documento), para o bloco à parte. */
export function documentRefOf(row: OutboxEntry): { saleId: string; documentId: string } | null {
  if (row.kind !== 'pos.sale' || !row.payload || typeof row.payload !== 'object') return null
  const sale = row.payload as { saleId?: string; status?: string; events?: { type?: string; data?: { customer?: { documentId?: string | null } | null } }[] }
  if (sale.status !== 'completed' || !sale.saleId || !Array.isArray(sale.events)) return null
  const last = sale.events.filter((e) => e.type === 'CustomerSet').at(-1)
  const documentId = last?.data?.customer?.documentId
  return documentId ? { saleId: sale.saleId, documentId } : null
}

export interface UploadOptions {
  store: Pick<LocalStore, 'outbox' | 'getMeta' | 'sealOutbox' | 'removeOutbox'>
  device: DeviceRef
  send: (body: UploadBody) => Promise<UploadResponse>
  /** Decifra o documento do consumidor (só para o envio; nunca para log). Null: o documento já foi apagado. */
  document?: (documentId: string) => Promise<{ kind: UploadDocument['kind']; value: string } | null>
  batchSize?: number
}

/** Sela e envia a fila inteira, em lotes, até esvaziar ou dar erro (o erro sobe para quem chamou: rede, relógio, aparelho). */
export async function uploadOutbox(options: UploadOptions): Promise<UploadSummary> {
  const summary: UploadSummary = { sent: 0, withDivergence: 0, quarantined: 0, remaining: 0 }
  await sealPending(options.store, options.device)
  for (let batch = 0; batch < MAX_BATCHES; batch++) {
    const queue = await options.store.outbox(options.batchSize ?? BATCH_SIZE)
    const rows = queue.filter((r) => r.seal)
    if (rows.length === 0) break
    const documents: UploadDocument[] = []
    for (const row of rows) {
      const ref = documentRefOf(row)
      const doc = ref && options.document ? await options.document(ref.documentId).catch(() => null) : null
      if (ref && doc) documents.push({ saleId: ref.saleId, kind: doc.kind, value: doc.value })
    }

    const response = await options.send({ events: rows.map(toUploadEvent), documents, oldestPendingAt: queue[0]?.createdAt ?? null })
    const done = new Map(response.results.map((r) => [r.eventId, r.outcome]))
    const confirmed = rows.filter((r) => done.has(r.id))
    await options.store.removeOutbox(confirmed.map((r) => r.seq))
    summary.sent += confirmed.length
    summary.withDivergence += confirmed.filter((r) => done.get(r.id) === 'AcceptedWithDivergence').length
    summary.quarantined += confirmed.filter((r) => done.get(r.id) === 'Quarantined').length
    if (confirmed.length < rows.length) break
  }

  summary.remaining = (await options.store.outbox()).length
  return summary
}
