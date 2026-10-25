/**
 * Solicitação de compra já preenchida vinda de outra tela (EST-06 CA 4): os itens e as quantidades vão no estado da navegação, não
 * na URL (uma BOM pode ter dezenas de itens), e nada é gravado até o usuário enviar (R-PRP-7). Destino "Estoque geral", editável.
 */
export interface PurchasePrefillLine {
  itemId: string
  code: string
  name: string
  unitId: string
  unitCode: string
  /** Na unidade de estoque. */
  quantity: number
}

export interface PurchasePrefill {
  /** De onde veio, para a frase acima do formulário (ex.: "Faltas para produzir 5 × ELEV-6, BOM da OS-2026-00041"). */
  source: string
  notes: string
  lines: PurchasePrefillLine[]
  /** Quantas faltas ficaram de fora pelo limite de linhas da solicitação. */
  leftOut: number
}

/** A solicitação preenchida do estado da navegação, se veio de outra tela; senão undefined. */
export function readPrefill(): PurchasePrefill | undefined {
  try {
    const state = window.history.state as { purchasePrefill?: PurchasePrefill } | null
    return state?.purchasePrefill?.lines ? state.purchasePrefill : undefined
  } catch {
    return undefined
  }
}

/** Limite de linhas da solicitação (o mesmo do backend, CMP-01). */
export const MAX_REQUEST_LINES = 50
