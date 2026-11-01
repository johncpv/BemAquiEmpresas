/**
 * Etiqueta com QR e leitura (PAT-01b). O QR leva só a URL curta `/b/{etiqueta}`; a leitura (câmera ou digitação) vira sempre a rota
 * interna `/b/{etiqueta}`, nunca o endereço lido (sem redirecionamento aberto).
 */

export const LABEL_MIN_WIDTH = 30
export const LABEL_MAX_WIDTH = 150
export const LABEL_MIN_HEIGHT = 15
export const LABEL_MAX_HEIGHT = 150

/** Até 50 etiquetas o PDF sai na hora; "todos do filtro" vai até 500 (CA 3). */
export const LABEL_INLINE_LIMIT = 50
export const LABEL_BATCH_MAX = 500

export const LABEL_DICAS = {
  tamanho: 'Largura × altura da etiqueta adesiva, em milímetros, como no rolo da impressora de etiquetas. Começa em 50 × 25 mm até a empresa informar o modelo da impressora.',
  folhaA4: 'Para imprimir numa impressora comum, em folha A4: as etiquetas saem em grade, com linha de corte.',
}

export const CAMERA_FALLBACK = 'Não foi possível usar a câmera. Digite a etiqueta.'

const TAG = /^[A-Z]{2,5}-\d{1,10}$/

/**
 * Etiqueta a partir do que foi lido ou digitado (CA 5 e 9): a URL do QR (qualquer host, só o caminho `/b/...` conta), a etiqueta inteira
 * (`pat-00012`) ou só o número (`12` vira `PAT-00012`). Fora disso, null.
 */
export function tagFrom(raw: string): string | null {
  const text = raw.trim()
  if (!text) return null
  let candidate = text
  const path = /^https?:\/\/[^/\s]+\/b\/([^/?#\s]+)\/?(?:[?#].*)?$/i.exec(text) ?? /^\/b\/([^/?#\s]+)$/i.exec(text)
  if (path) {
    try {
      candidate = decodeURIComponent(path[1])
    } catch {
      return null
    }
  } else if (/^\d{1,10}$/.test(text)) {
    candidate = 'PAT-' + text.padStart(5, '0')
  }

  const tag = candidate.toUpperCase()
  return TAG.test(tag) ? tag : null
}

/** Faixa do tamanho (CA 1): largura de 30 a 150 mm e altura de 15 a 150 mm, inteiros. */
export function labelSizeError(width: string, height: string): string | undefined {
  const w = Number(width)
  const h = Number(height)
  const ok = /^\d+$/.test(width) && /^\d+$/.test(height) && w >= LABEL_MIN_WIDTH && w <= LABEL_MAX_WIDTH && h >= LABEL_MIN_HEIGHT && h <= LABEL_MAX_HEIGHT
  return ok ? undefined : `Informe a largura de ${LABEL_MIN_WIDTH} a ${LABEL_MAX_WIDTH} mm e a altura de ${LABEL_MIN_HEIGHT} a ${LABEL_MAX_HEIGHT} mm.`
}

/**
 * Para onde a leitura leva (regra da story): no celular e no tablet (toque), a ficha resumida do `/fab`; no computador, a ficha completa do
 * backoffice.
 */
export function cardRouteFor(id: string, touch = isTouchDevice()): string {
  return touch ? `/fab/bens/${id}` : `/app/patrimonio/bens/${id}`
}

function isTouchDevice(): boolean {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return false
  return window.matchMedia('(pointer: coarse)').matches
}
