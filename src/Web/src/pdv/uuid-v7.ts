/**
 * Ids gerados no próprio aparelho de caixa (OFF-01 CA 8): Guid v7 (RFC 9562), com os 48 bits do instante em ms na frente e 74 bits
 * aleatórios do `crypto.getRandomValues`. Ordenam pelo instante (bom para índice no servidor) e não dependem de rede para nascer; o
 * servidor aceita o id do aparelho como chave da venda e do evento (rascunho A, item 6).
 */
export function uuidv7(now: number = Date.now(), random: (bytes: Uint8Array) => Uint8Array = (b) => crypto.getRandomValues(b)): string {
  if (!Number.isSafeInteger(now) || now < 0) throw new Error(`Instante inválido para o Guid v7: ${now}.`)
  const bytes = random(new Uint8Array(16))
  let ms = now
  for (let i = 5; i >= 0; i--) {
    bytes[i] = ms % 256
    ms = Math.floor(ms / 256)
  }

  bytes[6] = (bytes[6] & 0x0f) | 0x70 // versão 7
  bytes[8] = (bytes[8] & 0x3f) | 0x80 // variante RFC 9562
  const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}

const V7 = /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/

export const isUuidV7 = (value: string) => V7.test(value)

/** O instante (ms) gravado num Guid v7. */
export function uuidv7Time(value: string): number {
  if (!isUuidV7(value)) throw new Error(`Não é um Guid v7: ${value}.`)
  return parseInt(value.slice(0, 8) + value.slice(9, 13), 16)
}
