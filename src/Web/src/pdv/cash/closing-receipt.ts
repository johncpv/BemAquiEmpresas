import type { CashCountResult } from '@/pdv/cash/cash-session'
import type { Cents } from '@/pdv/money'
import { NOT_FISCAL_TEXT, type ReceiptDocument, type ReceiptLine, receiptMoney } from '@/pdv/print/receipt-model'

const when = (iso: string) => new Date(iso).toLocaleString('pt-BR', { timeZone: 'America/Sao_Paulo', day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' })

/**
 * Bobina do fechamento (CX-02 CA 9 e 12), montada no aparelho com o dado local: o **comprovante do recolhimento** em duas vias (caixa e quem leva
 * ao Cofre, com assinatura) e a **via do operador** do relatório: o contado por forma, vendas, sangrias, suprimentos, o recolhimento e o fundo que
 * fica. **Nunca** esperado, diferença nem total de vendas por forma: o aparelho não tem esses números (conferência cega).
 */
export function buildClosingReceipt(input: { result: CashCountResult; place: string; methodName: (methodId: string | null) => string }): ReceiptDocument {
  const { result, place } = input
  const lines: ReceiptLine[] = []
  if (result.depositCents > 0) {
    for (const copy of ['1ª via — caixa', '2ª via — quem leva ao cofre']) {
      lines.push(
        { kind: 'text', text: 'RECOLHIMENTO AO COFRE', align: 'center', bold: true },
        { kind: 'text', text: copy, align: 'center' },
        { kind: 'text', text: place, align: 'center' },
        { kind: 'rule' },
        { kind: 'pair', left: 'Data', right: when(result.countedAt) },
        { kind: 'pair', left: 'Valor', right: receiptMoney(result.depositCents), bold: true },
        { kind: 'pair', left: 'Operador', right: result.operatorName },
        { kind: 'blank' }, { kind: 'blank' },
        { kind: 'text', text: '______________________________', align: 'center' },
        { kind: 'text', text: 'Assinatura de quem leva', align: 'center' },
        { kind: 'text', text: NOT_FISCAL_TEXT, align: 'center' },
        { kind: 'blank' }, { kind: 'cut' },
      )
    }
  }

  const moves = result.movements.filter((m) => m.kind === 'CashOut' || m.kind === 'CashIn')
  const sum = (kind: 'CashOut' | 'CashIn'): Cents => moves.filter((m) => m.kind === kind).reduce((total, m) => total + m.amountCents, 0)
  lines.push(
    { kind: 'text', text: 'FECHAMENTO DE CAIXA', align: 'center', bold: true },
    { kind: 'text', text: 'Via do operador', align: 'center' },
    { kind: 'text', text: place, align: 'center' },
    { kind: 'rule' },
    { kind: 'pair', left: 'Operador', right: result.operatorName },
    { kind: 'pair', left: 'Abertura', right: when(result.openedAt) },
    { kind: 'pair', left: 'Contagem', right: when(result.countedAt) },
    { kind: 'pair', left: 'Vendas', right: String(result.salesCount) },
    { kind: 'pair', left: `Sangrias (${moves.filter((m) => m.kind === 'CashOut').length})`, right: receiptMoney(sum('CashOut')) },
    { kind: 'pair', left: `Suprimentos (${moves.filter((m) => m.kind === 'CashIn').length})`, right: receiptMoney(sum('CashIn')) },
    { kind: 'rule' },
    { kind: 'text', text: 'Contado', bold: true },
    ...result.lines.map((l): ReceiptLine => ({ kind: 'pair', left: input.methodName(l.methodId), right: receiptMoney(l.countedCents) })),
    { kind: 'rule' },
    { kind: 'pair', left: 'Recolhimento ao cofre', right: receiptMoney(result.depositCents) },
    { kind: 'pair', left: 'Fundo que fica', right: receiptMoney(result.keptCents) },
    { kind: 'blank' },
    { kind: 'text', text: 'Fechamento enviado para conferência.', align: 'center' },
    { kind: 'text', text: NOT_FISCAL_TEXT, align: 'center' },
    { kind: 'blank' }, { kind: 'cut' },
  )
  return { title: 'Fechamento de caixa', lines }
}
