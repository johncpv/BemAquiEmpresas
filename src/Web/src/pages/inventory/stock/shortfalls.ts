/** Divergências de estoque (PDV-05): regras de tela, sem chamada à API. */
export interface ShortfallLike {
  quantity: number
  available: number
  sales: readonly { quantity: number }[]
}

export const SHORTFALL_DICAS = {
  falta: 'Quanto as vendas do caixa tiraram da prateleira sem o sistema ter saldo. A venda vale; o estoque é que precisa ser acertado.',
  disponivel: 'Físico menos o reservado no local da loja, agora. "Aplicar baixa" só lança a falta que este valor cobre.',
}

/** A menor falta de uma venda: aplicar só faz sentido se o disponível cobre pelo menos ela. */
export function canApply(row: ShortfallLike): boolean {
  if (row.sales.length === 0) return false
  const smallest = Math.min(...row.sales.map((s) => s.quantity))
  return row.available >= smallest
}

/** Frase do diálogo de "Aplicar baixa": o que vai sair agora e o que fica. */
export function applyLabel(row: ShortfallLike): string {
  if (row.available >= row.quantity) return 'O disponível cobre toda a falta.'
  if (!canApply(row)) return 'O disponível não cobre nenhuma venda: ajuste o estoque antes.'
  return 'O disponível cobre só parte: o resto continua na lista.'
}
