import { useState } from 'react'
import { cancelReceivable, changeReceivableDueDate, reverseReceivableSettlement, useGetFinanceLimits, writeOffReceivable } from '@/api/generated/bemaqui'
import type { ReceivableDetail, SettlementView } from '@/api/generated/model'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DialogMessage } from '@/components/ui/dialog-message'
import { Money } from '@/components/ui/money'
import { useCan } from '@/pages/use-can'
import { BILLING_SLIP_OPEN } from './billing-slips'
import { today } from './receivables'

interface Base { receivable: ReceivableDetail; open: boolean; onOpenChange: (o: boolean) => void; onDone: (r: ReceivableDetail) => void; onReload: () => Promise<unknown> }

function Reason({ value, onChange, label = 'Motivo' }: { value: string; onChange: (v: string) => void; label?: string }) {
  return <Field label={label} required hint="Até 200 caracteres. Fica no histórico.">{(p) => <input {...p} value={value} maxLength={200} onChange={(e) => onChange(e.target.value)} className={inputClass} />}</Field>
}

const required = (v: string, message = 'Informe o motivo.') => {
  if (!v.trim()) throw new DialogMessage(message)
  return v.trim()
}

/** Quitar o saldo com desconto (CA 11): sem transação; 100% de desconto do saldo, então só quem aprova confirma (CA 13). */
export function WriteOffDialog({ receivable, open, onOpenChange, onDone, onReload }: Base) {
  const canApprove = useCan('finance.approve')
  const limits = useGetFinanceLimits({ query: { enabled: open } })
  const over = !!limits.data && receivable.balance > 0 && (100 > limits.data.discountApprovalPct || receivable.balance > limits.data.discountApprovalAmount)
  return (
    <ConfirmDialog size="lg" open={open} onOpenChange={onOpenChange} verb="Quitar o saldo com desconto" done="Saldo quitado" title={`Quitar o saldo de ${receivable.number}?`}
      description="O saldo vira desconto e o título fica pago. Nada entra na conta." onReload={onReload}
      preconditions={[{ ok: !over || canApprove, text: 'O desconto passa do limite: só quem aprova em Caixa e pagamentos pode confirmar.' }]}
      impact={<dl className="grid grid-cols-2 gap-1"><dt>Desconto</dt><dd className="text-right"><Money value={receivable.balance} /></dd><dt>Entra na conta</dt><dd className="text-right"><Money value={0} /></dd></dl>}
      onConfirm={async () => onDone(await writeOffReceivable(receivable.id, { date: today(), backdateReason: null, version: receivable.version }))}
      recovery={{ verb: 'Cancelar o boleto e quitar', done: 'Saldo quitado', when: (code) => code === BILLING_SLIP_OPEN,
        onSelect: async () => onDone(await writeOffReceivable(receivable.id, { date: today(), backdateReason: null, version: receivable.version, cancelBillingSlip: true })) }} />
  )
}

/** Alterar o vencimento com motivo (CA 19, D14): o Proprietário é avisado. */
export function DueDateDialog({ receivable, open, onOpenChange, onDone, onReload }: Base) {
  const [date, setDate] = useState(receivable.dueDate ?? today())
  const [reason, setReason] = useState('')
  return (
    <ConfirmDialog open={open} onOpenChange={(o) => { if (o) { setDate(receivable.dueDate ?? today()); setReason('') } onOpenChange(o) }} verb="Alterar vencimento" done="Vencimento alterado"
      title={`Alterar o vencimento de ${receivable.number}`} onReload={onReload}
      onConfirm={async () => onDone(await changeReceivableDueDate(receivable.id, { dueDate: required(date, 'Informe o vencimento.'), reason: required(reason), version: receivable.version }))}>
      <Field label="Novo vencimento" required>{(p) => <DateInput {...p} value={date} onChange={setDate} />}</Field>
      <Reason value={reason} onChange={setReason} />
    </ConfirmDialog>
  )
}

/** Cancelar título (CA 17 e 18): com baixa ativa, o servidor recusa e o diálogo oferece estornar na aba Baixas. */
export function CancelDialog({ receivable, open, onOpenChange, onDone, onReload, onGoToSettlements }: Base & { onGoToSettlements: () => void }) {
  const canApprove = useCan('finance.approve')
  const limits = useGetFinanceLimits({ query: { enabled: open } })
  const [reason, setReason] = useState('')
  const over = !!limits.data && receivable.balance > limits.data.cancelApprovalAmount
  return (
    <ConfirmDialog size="lg" destructive open={open} onOpenChange={(o) => { if (o) setReason(''); onOpenChange(o) }} verb="Cancelar título" done="Título cancelado"
      title={`Cancelar o título ${receivable.number}?`} description="Ele sai dos totais em aberto e continua consultável. O Proprietário é avisado." onReload={onReload}
      preconditions={[{ ok: !over || canApprove, text: 'Saldo acima do limite de cancelamento: só quem aprova em Caixa e pagamentos pode cancelar.' }]}
      recovery={{ verb: 'Ver as baixas para estornar', when: (code) => code === 'receivable_has_settlements', onSelect: async () => onGoToSettlements() }}
      impact={<dl className="grid grid-cols-2 gap-1"><dt>Saldo cancelado</dt><dd className="text-right"><Money value={receivable.balance} /></dd></dl>}
      onConfirm={async () => onDone(await cancelReceivable(receivable.id, { reason: required(reason), version: receivable.version }))}>
      <Reason value={reason} onChange={setReason} />
    </ConfirmDialog>
  )
}

/** Estornar baixa (CA 16): transação inversa na data de hoje; o Proprietário é avisado. */
export function ReverseDialog({ receivable, settlement, open, onOpenChange, onDone, onReload }: Base & { settlement: SettlementView }) {
  const [reason, setReason] = useState('')
  return (
    <ConfirmDialog size="lg" destructive open={open} onOpenChange={(o) => { if (o) setReason(''); onOpenChange(o) }} verb="Estornar baixa" done="Baixa estornada"
      title="Estornar esta baixa?" description="A baixa fica marcada como estornada e, se movimentou a conta, uma saída igual é lançada hoje." onReload={onReload}
      impact={<dl className="grid grid-cols-2 gap-1"><dt>Principal que volta ao saldo</dt><dd className="text-right"><Money value={settlement.principal} /></dd><dt>Sai da conta</dt><dd className="text-right"><Money value={settlement.total} /></dd></dl>}
      onConfirm={async () => onDone(await reverseReceivableSettlement(receivable.id, settlement.id, { reason: required(reason), version: receivable.version }))}>
      <Reason value={reason} onChange={setReason} />
    </ConfirmDialog>
  )
}
