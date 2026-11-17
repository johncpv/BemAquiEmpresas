import type { AcquirerFeeView, CardBrand, PaymentMethodType } from '@/api/generated/model'

/** Rótulos dos tipos (PAG-01 CA 1), na ordem do seletor. */
export const TYPE_LABELS: Record<PaymentMethodType, string> = {
  Cash: 'Dinheiro',
  Pix: 'Pix',
  Debit: 'Débito',
  Credit: 'Crédito à vista',
  CreditInstallments: 'Crédito parcelado',
  Voucher: 'Vale',
  StoreCredit: 'Vale-troca',
  OnAccount: 'Fiado',
}

export const BRAND_LABELS: Record<CardBrand, string> = {
  Any: 'Qualquer bandeira',
  Visa: 'Visa',
  Mastercard: 'Mastercard',
  Elo: 'Elo',
  Amex: 'American Express',
  Hipercard: 'Hipercard',
  Diners: 'Diners',
  Cabal: 'Cabal',
  Other: 'Outra',
}

/** Meio de pagamento da nota fiscal (tPag), para o seletor do formulário. */
export const NFE_CODES: { value: string; label: string }[] = [
  { value: '01', label: '01 · Dinheiro' },
  { value: '02', label: '02 · Cheque' },
  { value: '03', label: '03 · Cartão de crédito' },
  { value: '04', label: '04 · Cartão de débito' },
  { value: '05', label: '05 · Crédito loja' },
  { value: '10', label: '10 · Vale-alimentação' },
  { value: '11', label: '11 · Vale-refeição' },
  { value: '12', label: '12 · Vale-presente' },
  { value: '13', label: '13 · Vale-combustível' },
  { value: '15', label: '15 · Boleto' },
  { value: '16', label: '16 · Depósito bancário' },
  { value: '17', label: '17 · Pix dinâmico' },
  { value: '18', label: '18 · Transferência, carteira digital' },
  { value: '19', label: '19 · Fidelidade, cashback' },
  { value: '20', label: '20 · Pix estático' },
  { value: '90', label: '90 · Sem pagamento' },
  { value: '99', label: '99 · Outros' },
]

/** O mesmo tPag sugerido pelo servidor para o tipo. */
export function defaultNfeCode(type: PaymentMethodType): string {
  switch (type) {
    case 'Cash': return '01'
    case 'Credit': case 'CreditInstallments': return '03'
    case 'Debit': return '04'
    case 'StoreCredit': case 'OnAccount': return '05'
    case 'Voucher': return '10'
    case 'Pix': return '17'
  }
}

export const requiresAcquirer = (type: PaymentMethodType) => type === 'Debit' || type === 'Credit' || type === 'CreditInstallments'
export const acceptsAcquirer = (type: PaymentMethodType) => requiresAcquirer(type) || type === 'Voucher'

/** Faixa de parcelas aceita na taxa: só o crédito parcelado tem de 2 a 24. */
export const installmentRange = (type: PaymentMethodType): [number, number] => (type === 'CreditInstallments' ? [2, 24] : [1, 1])

const pct = new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 4 })

/** "2,99% · D+30". */
export const describeFee = (f: Pick<AcquirerFeeView, 'feePercent' | 'settlementDays'>) => `${pct.format(f.feePercent)}% · D+${f.settlementDays}`

/** "1x", "a partir de 2x". */
export const describeInstallments = (n: number, type: PaymentMethodType) => (type === 'CreditInstallments' ? `a partir de ${n}x` : 'à vista')

/**
 * Data mínima da nova vigência (CA 3), a mesma regra do servidor: a primeira taxa de uma bandeira e parcela pode começar em qualquer data; já
 * havendo uma vigente, a nova começa a partir de hoje e depois do início da atual. Datas em "aaaa-mm-dd".
 */
export function minValidFrom(fees: AcquirerFeeView[], brand: CardBrand, installments: number, today: string): string | null {
  const open = fees.filter((f) => f.brand === brand && f.installments === installments && !f.validTo).map((f) => f.validFrom).sort().at(-1)
  if (!open) return null
  const next = new Date(`${open}T00:00:00Z`)
  next.setUTCDate(next.getUTCDate() + 1)
  const after = next.toISOString().slice(0, 10)
  return after > today ? after : today
}
