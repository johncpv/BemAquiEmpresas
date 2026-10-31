import type { PaymentReviewOption } from '@/api/generated/model'

/** Filtro "Pagamentos a conferir" da lista A receber (BNK-02 CA 9): filtro da lista, não aba própria (§4.4). */
export const REVIEW_FILTER = 'a-conferir'

export const REVIEW_EMPTY = 'Nenhum pagamento a conferir. Os boletos pagos com diferença aparecem aqui.'

/** Aviso do estorno da baixa feita pelo banco (CA 14). */
export const BANK_REVERSE_WARNING = 'Este pagamento foi confirmado pelo Inter. Estorne só se o banco devolveu o valor.'

export const APPROVAL_TEXT = 'Desconto acima do limite: só quem aprova em Caixa e pagamentos pode confirmar.'

export const REVIEW_DICAS = {
  aguardando:
    'O cliente pagou o boleto com um valor diferente (ou um boleto já cancelado). O dinheiro já está na conta, marcado "Recebido do Inter, aguardando conferência"; o título só baixa quando alguém confere.',
  banco: 'Baixa feita pelo aviso de pagamento do Inter ou pela conferência diária, com o valor e a data que o banco informou.',
}

/** "Pagou R$ 100,00 a menos": o sinal da diferença entre o pago e o saldo, em texto (nunca só cor). */
export function differenceKind(difference: number): 'less' | 'more' | 'equal' {
  if (difference < 0) return 'less'
  if (difference > 0) return 'more'
  return 'equal'
}

export const DIFFERENCE_TEXT: Record<'less' | 'more' | 'equal', string> = { less: 'a menos', more: 'a mais', equal: 'sem diferença' }

/** A opção escolhida, ou a sugerida, ou a primeira permitida. */
export function pickOption(options: PaymentReviewOption[], chosen: string | null, suggested: string): PaymentReviewOption | undefined {
  return options.find((o) => o.decision === chosen) ?? options.find((o) => o.decision === suggested) ?? options.find((o) => o.allowed) ?? options[0]
}
