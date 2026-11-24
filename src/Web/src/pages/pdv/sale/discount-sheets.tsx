import { type ReactNode, useState } from 'react'
import { FloorButton, FloorDica, FloorSheet } from '@/components/shopfloor/kit'
import { type Cents, formatCents } from '@/pdv/money'
import { lineGross, percentToCents, type Sale, type SaleLine, saleSubtotal } from '@/pdv/sale/sale-model'
import type { StoredReceipt } from '@/pdv/print/print-jobs'
import { parseMoney, parsePercent } from './discount-input'
import { ReasonPicker } from './supervisor'

/**
 * Folhas da PDV-04 na tela de venda: desconto (no item ou no total, em % ou em R$), troca de preço e cancelamento da venda concluída. Cada uma
 * pede o motivo; o que passa do que o operador pode vira "Chamar supervisor" na tela.
 */

const field = 'block min-h-[56px] w-full rounded-md border-2 border-input bg-card px-4 text-[22px] tabular-nums text-foreground'

function Actions({ children }: { children: ReactNode }) {
  return <div className="flex gap-[16px]">{children}</div>
}

function Problem({ message }: { message: string | null }) {
  return message ? <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft p-4 text-[18px] font-semibold text-crit-ink">{message}</p> : null
}
/** PDV-04 CA 1 e 2: desconto no item selecionado ou no total, em % ou em R$, com o motivo. Zero tira o desconto. */
export function DiscountSheet({ open, sale, line, limitPercent, onCancel, onSave }: {
  open: boolean
  sale: Sale | null
  line: SaleLine | null
  limitPercent: number
  onCancel: () => void
  onSave: (lineId: string | null, discountCents: Cents, reason: string) => Promise<string | null>
}) {
  const [target, setTarget] = useState<'line' | 'total'>(line ? 'line' : 'total')
  const [mode, setMode] = useState<'percent' | 'amount'>('percent')
  const [value, setValue] = useState('')
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  if (!sale) return null
  const onLine = target === 'line' && line !== null
  const base = onLine ? lineGross(line) : saleSubtotal(sale)
  const percent = mode === 'percent' ? parsePercent(value) : null
  const cents = mode === 'percent' ? (percent === null ? null : percentToCents(base, percent)) : parseMoney(value)
  const valid = cents !== null && cents <= base

  const save = async () => {
    if (!valid || cents === null) { setError(mode === 'percent' ? 'Informe o percentual, de 0 a 100.' : 'Informe o desconto em reais, até o valor.'); return }
    if (cents > 0 && !reason) { setError('Escolha o motivo do desconto.'); return }
    setError(await onSave(onLine ? line.lineId : null, cents, reason))
  }

  return (
    <FloorSheet open={open} onOpenChange={(o) => { if (!o) onCancel() }} title="Desconto"
      description={<>Até {limitPercent.toLocaleString('pt-BR')}% o desconto passa direto; acima, chama o supervisor. <FloorDica nome="limite de desconto" texto="O limite é da loja (Loja → Lojas e caixas). Acima dele, só quem tem a permissão de desconto ou o supervisor, com o PIN." /></>}
      actions={<Actions><FloorButton tone="secondary" className="flex-1" onClick={onCancel}>Voltar</FloorButton><FloorButton className="flex-1" onClick={() => void save()}>Aplicar desconto</FloorButton></Actions>}>
      <div role="group" aria-label="Onde" className="grid grid-cols-2 gap-[8px]">
        <FloorButton tone={onLine ? 'primary' : 'secondary'} aria-pressed={onLine} disabled={!line} onClick={() => setTarget('line')}>{line ? `No item: ${line.name}` : 'No item'}</FloorButton>
        <FloorButton tone={!onLine ? 'primary' : 'secondary'} aria-pressed={!onLine} onClick={() => setTarget('total')}>No total da venda</FloorButton>
      </div>
      <div role="group" aria-label="Como" className="grid grid-cols-2 gap-[8px]">
        <FloorButton tone={mode === 'percent' ? 'primary' : 'secondary'} aria-pressed={mode === 'percent'} onClick={() => { setMode('percent'); setValue('') }}>Em %</FloorButton>
        <FloorButton tone={mode === 'amount' ? 'primary' : 'secondary'} aria-pressed={mode === 'amount'} onClick={() => { setMode('amount'); setValue('') }}>Em R$</FloorButton>
      </div>
      <label className="block space-y-2">
        <span className="font-semibold">{mode === 'percent' ? 'Desconto (%)' : 'Desconto (R$)'}</span>
        <input autoFocus inputMode="decimal" className={field} value={value} onChange={(e) => { setValue(e.target.value); setError(null) }} placeholder={mode === 'percent' ? 'Ex.: 5' : 'Ex.: 2,50'} />
      </label>
      <p aria-live="polite" className="text-[18px] tabular-nums">
        {valid && cents !== null ? <>De <b>{formatCents(base)}</b> para <b>{formatCents(base - cents)}</b> ({formatCents(cents)} de desconto).</> : <>Valor {onLine ? 'do item' : 'da venda'}: <b>{formatCents(base)}</b>.</>}
      </p>
      {onLine ? null : <p className="text-muted-foreground">Mudar os itens depois tira o desconto no total.</p>}
      <ReasonPicker action="Discount" value={reason} onChange={(r) => { setReason(r); setError(null) }} />
      <Problem message={error} />
    </FloorSheet>
  )
}

/** PDV-04 CA 3: troca do preço do item; a linha guarda o preço da tabela e o praticado, com o motivo. */
export function PriceSheet({ open, line, onCancel, onSave }: {
  open: boolean
  line: SaleLine | null
  onCancel: () => void
  onSave: (unitCents: Cents, reason: string) => Promise<string | null>
}) {
  const [value, setValue] = useState('')
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  if (!line) return null
  const cents = parseMoney(value)

  const save = async () => {
    if (cents === null) { setError('Informe o preço novo em reais.'); return }
    if (!reason) { setError('Escolha o motivo da troca de preço.'); return }
    setError(await onSave(cents, reason))
  }

  return (
    <FloorSheet open={open} onOpenChange={(o) => { if (!o) onCancel() }} title="Trocar preço" description={line.name}
      actions={<Actions><FloorButton tone="secondary" className="flex-1" onClick={onCancel}>Voltar</FloorButton><FloorButton className="flex-1" onClick={() => void save()}>Trocar preço</FloorButton></Actions>}>
      <p className="text-[18px] tabular-nums">Preço da tabela: <b>{formatCents(line.listUnitCents ?? line.unitCents)}</b>{line.listUnitCents != null ? <> · praticado agora: <b>{formatCents(line.unitCents)}</b></> : null}</p>
      <label className="block space-y-2">
        <span className="font-semibold">Preço novo por {line.unit.toLowerCase()} (R$)</span>
        <input autoFocus inputMode="decimal" className={field} value={value} onChange={(e) => { setValue(e.target.value); setError(null) }} placeholder="Ex.: 9,90" />
      </label>
      <ReasonPicker action="ChangePrice" value={reason} onChange={(r) => { setReason(r); setError(null) }} />
      <p className="text-muted-foreground">A troca pede a permissão de alterar preço ou o supervisor, e tira o desconto do item.</p>
      <Problem message={error} />
    </FloorSheet>
  )
}

/** O que o cancelamento pede depois do motivo: o estorno confirmado na maquininha e, no cartão, o NSU do estorno. */
export interface CancelSaleInput {
  reason: string
  refundConfirmed: boolean
  refundNsu: string | null
}

const CARD_TYPES = new Set(['Debit', 'Credit', 'CreditInstallments', 'Voucher'])

/**
 * PDV-04 CA 6 a 8: cancelar a venda concluída desta sessão. Pagamento que não é dinheiro: "Estorne na maquininha e informe o NSU do estorno.", e só
 * segue com a confirmação. Venda de outra sessão: "Esta venda é de outro caixa. Use a devolução." (PDV-06).
 */
export function CancelSaleSheet({ open, receipt, otherSession, onCancel, onConfirm }: {
  open: boolean
  receipt: StoredReceipt | null
  otherSession: boolean
  onCancel: () => void
  onConfirm: (input: CancelSaleInput) => Promise<string | null>
}) {
  const [reason, setReason] = useState('')
  const [confirmed, setConfirmed] = useState(false)
  const [nsu, setNsu] = useState('')
  const [error, setError] = useState<string | null>(null)
  if (!receipt) return null
  const nonCash = receipt.payments.filter((p) => p.methodType !== 'Cash')
  const card = receipt.payments.some((p) => p.methodType !== undefined && CARD_TYPES.has(p.methodType))

  const confirm = async () => {
    if (!reason) { setError('Escolha o motivo do cancelamento.'); return }
    if (nonCash.length > 0 && !confirmed) { setError('Confirme que estornou na maquininha.'); return }
    if (card && !nsu.trim()) { setError('Informe o NSU do estorno.'); return }
    setError(await onConfirm({ reason, refundConfirmed: confirmed, refundNsu: nsu.trim() || null }))
  }

  return (
    <FloorSheet role="alertdialog" open={open} onOpenChange={(o) => { if (!o) onCancel() }} title={`Cancelar a venda ${receipt.number}?`}
      actions={otherSession
        ? <FloorButton className="w-full" onClick={onCancel}>Entendi</FloorButton>
        : <Actions><FloorButton tone="secondary" className="flex-1" onClick={onCancel}>Voltar</FloorButton><FloorButton tone="danger" className="flex-1" onClick={() => void confirm()}>Chamar supervisor</FloorButton></Actions>}>
      <div className="space-y-1 rounded-md border border-border bg-card p-4 tabular-nums">
        <p className="font-semibold">Venda {receipt.number} · {formatCents(receipt.totalCents)}</p>
        {receipt.payments.map((p, i) => <p key={i} className="text-muted-foreground">{p.label}: {formatCents(p.amountCents)}</p>)}
      </div>
      {otherSession ? <p className="text-[18px] font-semibold">Esta venda é de outro caixa. Use a devolução.</p> : (
        <>
          <ReasonPicker action="CancelSale" value={reason} onChange={(r) => { setReason(r); setError(null) }} />
          {nonCash.length > 0 ? (
            <div className="space-y-2 rounded-md border border-warn-soft bg-warn-soft p-4 text-warn-ink">
              <p className="text-[18px] font-semibold">Estorne na maquininha e informe o NSU do estorno.</p>
              {card ? (
                <label className="block space-y-1">
                  <span className="font-semibold">NSU do estorno</span>
                  <input inputMode="numeric" maxLength={12} className={field} value={nsu} onChange={(e) => { setNsu(e.target.value); setError(null) }} />
                </label>
              ) : null}
              <label className="flex min-h-[56px] items-center gap-3 text-[18px]">
                <input type="checkbox" className="size-6" checked={confirmed} onChange={(e) => { setConfirmed(e.target.checked); setError(null) }} /> Estornei na maquininha
              </label>
            </div>
          ) : <p className="text-muted-foreground">O dinheiro da venda sai do caixa: devolva ao cliente.</p>}
          <p className="text-muted-foreground">Cancelar pede o PIN de um supervisor (outra pessoa) e avisa o Proprietário.</p>
        </>
      )}
      <Problem message={error} />
    </FloorSheet>
  )
}
