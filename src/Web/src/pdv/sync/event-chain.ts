/**
 * Cadeia de hash dos eventos do caixa (OFF-02 CA 2 e 11). O mesmo cálculo roda no servidor (`PosEventChain.Hash`): SHA-256 (hex minúsculo) do
 * texto `v1\n{eventId}\n{deviceId}\n{registerId}\n{deviceEpoch}\n{seq}\n{schemaVersion}\n{kind}\n{occurredAt}\n{prevHash}\n{SHA-256 hex do payload}`,
 * com o `occurredAt` e o `payload` exatamente como vão no envio (texto) e `prevHash` vazio no primeiro evento da época. O CPF nunca entra no
 * evento: apagá-lo (retenção) não quebra a cadeia (CA 17).
 */
export const EVENT_SCHEMA_VERSION = 1

export interface ChainedEvent {
  eventId: string
  deviceId: string
  registerId: string
  deviceEpoch: number
  seq: number
  schemaVersion: number
  kind: string
  occurredAt: string
  prevHash: string | null
  payload: string
}

export async function sha256Hex(text: string): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text))
  return Array.from(new Uint8Array(digest), (b) => b.toString(16).padStart(2, '0')).join('')
}

export async function eventHash(e: ChainedEvent): Promise<string> {
  const text = [
    'v1', e.eventId, e.deviceId, e.registerId, String(e.deviceEpoch), String(e.seq), String(e.schemaVersion), e.kind, e.occurredAt, e.prevHash ?? '',
    await sha256Hex(e.payload),
  ].join('\n')
  return sha256Hex(text)
}

/**
 * O conteúdo que sobe, sem dado pessoal (CA 2): o nome do cliente (`customerName`) fica só no aparelho; o documento do consumidor nunca esteve
 * no evento (só o id do registro cifrado). O resto vai como foi gravado.
 */
export function uploadPayload(kind: string, payload: unknown): unknown {
  if (kind !== 'pos.sale' || !payload || typeof payload !== 'object') return payload
  const sale = payload as { events?: { type?: string; data?: { customer?: Record<string, unknown> | null } }[] }
  if (!Array.isArray(sale.events)) return payload
  return {
    ...sale,
    events: sale.events.map((e) => {
      if (e.type !== 'CustomerSet' || !e.data?.customer) return e
      const { customerName: _name, ...customer } = e.data.customer
      return { ...e, data: { ...e.data, customer } }
    }),
  }
}
