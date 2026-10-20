import type { QuoteItemView } from '@/api/generated/model'

export type Change = 'Incluído' | 'Removido' | 'Alterado' | 'Igual'

export interface ItemDiff { sequence: number; before?: QuoteItemView; after?: QuoteItemView; change: Change }

/** Itens lado a lado pela posição (a revisão copia a sequência): incluídos, removidos e alterados em quantidade ou preço. */
export function diffItems(before: QuoteItemView[], after: QuoteItemView[]): ItemDiff[] {
  const sequences = [...new Set([...before, ...after].map((i) => i.sequence))].sort((a, b) => a - b)
  return sequences.map((sequence) => {
    const b = before.find((i) => i.sequence === sequence)
    const a = after.find((i) => i.sequence === sequence)
    const change: Change = !b ? 'Incluído' : !a ? 'Removido' : b.quantity !== a.quantity || b.unitPrice !== a.unitPrice || b.description !== a.description ? 'Alterado' : 'Igual'
    return { sequence, before: b, after: a, change }
  })
}

/** Diferença em valor e em % ("+R$ 500,00 (+5,56%)"). */
export function delta(before: number, after: number) {
  const diff = Math.round((after - before) * 100) / 100
  const pct = before === 0 ? null : Math.round((diff / before) * 10000) / 100
  return { diff, pct }
}
