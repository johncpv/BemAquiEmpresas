import { useNavigate, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { approvePaymentOrder, cancelPaymentOrder, rejectPaymentOrder, reversePaymentOrder } from '@/api/generated/bemaqui'
import type { PaymentOrderDetail } from '@/api/generated/model'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { useCan } from '@/pages/use-can'
import { OrderItemsList } from './OrderItemsList'

interface Props { order: PaymentOrderDetail; refresh: () => Promise<void>; onApproved: () => void }

function Reason({ id, label, value, onChange }: { id: string; label: string; value: string; onChange: (v: string) => void }) {
  return (
    <div className="mt-3 space-y-1">
      <label htmlFor={id} className="text-sm font-medium">{label}</label>
      <textarea id={id} value={value} maxLength={500} rows={3} onChange={(e) => onChange(e.target.value)} className={inputClass} />
    </div>
  )
}

/** Aprovar, reprovar, cancelar e estornar (CA 6 a 9, 20 e 21), conforme a situação e a permissão. */
export function DecisionActions({ order, refresh, onApproved }: Props) {
  const { acao } = useSearch({ strict: false }) as { acao?: string }
  const navigate = useNavigate()
  const canApprove = useCan('finance.approve')
  const canPay = useCan('finance.pay')
  const canEdit = useCan('finance.edit')
  const [comment, setComment] = useState('')
  const [reason, setReason] = useState('')
  const [approveOpen, setApproveOpen] = useState(acao === 'aprovar' && order.canDecide)
  const { id, status } = order.summary
  const version = order.version
  const pending = status === 'Pending'
  const after = async () => { setComment(''); setReason(''); await refresh() }
  const clearAction = () => { if (acao) void navigate({ to: '.', search: (p: Record<string, unknown>) => ({ ...p, acao: undefined }), replace: true } as never) }
  const button = (text: string, primary = false) => <button type="button" className={primary ? 'rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground' : 'rounded-md border border-border px-3 py-2 text-sm font-semibold'}>{text}</button>

  return (
    <>
      {pending && canApprove && order.canDecide ? (
        <>
          <ConfirmDialog size="lg" verb="Aprovar" done="Aprovada" title={`Aprovar a ${order.summary.number}?`} open={approveOpen} onOpenChange={(o) => { setApproveOpen(o); if (!o) clearAction() }}
            description="Confira para quem e para onde vai o dinheiro." impact={<p className="text-sm">Total aprovado: <Money value={order.summary.total} className="font-semibold" /></p>}
            onConfirm={async () => { await approvePaymentOrder(id, { comment: comment.trim() || null, version }); await after(); onApproved() }} onReload={refresh}
            trigger={button('Aprovar', true)}>
            <OrderItemsList items={order.items} compact />
            <Reason id="comentario-aprovar" label="Comentário (opcional)" value={comment} onChange={setComment} />
          </ConfirmDialog>
          <ConfirmDialog size="lg" verb="Reprovar" done="Reprovada" title={`Reprovar a ${order.summary.number}?`} description="Os títulos voltam para A pagar e o solicitante recebe o motivo."
            preconditions={[{ ok: comment.trim().length > 0, text: 'Motivo da reprovação escrito.' }]}
            onConfirm={async () => { await rejectPaymentOrder(id, { comment: comment.trim(), version }); await after() }} onReload={refresh} trigger={button('Reprovar')}>
            <Reason id="comentario-reprovar" label="Motivo da reprovação" value={comment} onChange={setComment} />
          </ConfirmDialog>
        </>
      ) : null}
      {pending && order.isRequester && !order.canDecide ? <p className="text-sm text-muted-foreground">Quem solicitou a ordem não pode aprová-la.</p> : null}
      {(pending && (canEdit && (order.isRequester || canApprove))) || (status === 'Approved' && canApprove) ? (
        <ConfirmDialog verb="Cancelar ordem" done="Ordem cancelada" title={`Cancelar a ${order.summary.number}?`} description="Os títulos voltam para A pagar."
          preconditions={[{ ok: reason.trim().length > 0, text: 'Motivo do cancelamento escrito.' }]}
          onConfirm={async () => { await cancelPaymentOrder(id, { reason: reason.trim(), version }); await after() }} onReload={refresh} trigger={button('Cancelar ordem')}>
          <Reason id="motivo-cancelar" label="Motivo" value={reason} onChange={setReason} />
        </ConfirmDialog>
      ) : null}
      {status === 'Paid' && canPay ? (
        <ConfirmDialog destructive verb="Estornar pagamento" done="Pagamento estornado" title={`Estornar o pagamento da ${order.summary.number}?`}
          description="As baixas são estornadas, a saída volta para a conta e os títulos voltam para A pagar. O Proprietário é avisado."
          preconditions={[{ ok: reason.trim().length > 0, text: 'Motivo do estorno escrito.' }]}
          onConfirm={async () => { await reversePaymentOrder(id, { reason: reason.trim(), version }); await after() }} onReload={refresh} trigger={button('Estornar pagamento')}>
          <Reason id="motivo-estornar" label="Motivo" value={reason} onChange={setReason} />
        </ConfirmDialog>
      ) : null}
    </>
  )
}
