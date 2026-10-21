import { useState } from 'react'
import { acceptInstallation, cancelInstallation, startInstallation, useGetContract } from '@/api/generated/bemaqui'
import type { InstallationAcceptResult, InstallationDetail } from '@/api/generated/model'
import { useIdempotencyKey } from '@/api/idempotency'
import { inputClass } from '@/components/form/form-utils'
import { DateInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { today } from '../../finance/receivables/receivables'
import { acceptanceMissing, INSTALLATION_DICAS } from './installations'

/** Iniciar, aceite técnico e cancelar (COM-06 CA 6, 9, 11, 13), com o impacto no financeiro. */
export function InstallationActions({ order, onChange, onAccepted }: { order: InstallationDetail; onChange: () => Promise<void>; onAccepted: (r: InstallationAcceptResult) => void }) {
  const canEdit = useCan('production.edit')
  const canApprove = useCan('production.approve')
  const canFinance = useCan('finance.view')
  const contract = useGetContract(order.contractId, { query: { enabled: canFinance && order.status === 'InProgress' } })
  const [date, setDate] = useState(today())
  const [name, setName] = useState('')
  const [reason, setReason] = useState('')
  const idempotency = useIdempotencyKey()
  const missing = acceptanceMissing(order.steps, order.checklist)
  const released = (contract.data?.milestones ?? []).filter((m) => (m.triggerEvent === 'TechnicalAcceptance' || m.triggerEvent === 'Delivered') && m.receivable?.status === 'Planned')
  const primary = 'rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground'
  const secondary = 'rounded-md border border-border px-3 py-2 text-sm font-semibold'

  return (
    <span className="flex flex-wrap gap-2">
      {order.status === 'Open' && canEdit ? (
        <ConfirmDialog verb="Iniciar instalação" done="Instalação iniciada" title="Iniciar a instalação?" description="O projeto passa a Instalando."
          onOpen={() => setDate(today())} preconditions={[{ ok: !!date && date <= today(), text: 'A data não pode ser no futuro.' }]}
          onConfirm={async () => { await startInstallation(order.id, { date, version: order.version }); await onChange() }}
          trigger={<button type="button" className={primary}>Iniciar instalação</button>}>
          <label className="mt-3 block space-y-1 text-sm font-medium">Data de início<DateInput value={date} onChange={setDate} /></label>
        </ConfirmDialog>
      ) : null}
      {order.status === 'InProgress' && canApprove ? (
        <ConfirmDialog size="lg" verb="Registrar aceite técnico" done="Aceite técnico registrado" title={`Registrar o aceite técnico da ${order.number}?`}
          onOpen={() => { setDate(today()); setName('') }}
          preconditions={[...missing.map((m) => ({ ok: false, text: m })), { ok: !!date && date <= today(), text: 'A data não pode ser no futuro.' }, { ok: name.trim().length > 0, text: 'Informe quem aceitou pelo cliente.' }]}
          impact={(
            <div className="space-y-1 text-sm">
              <p>Confirma o título a pagar de <Money value={order.contractValue} /> ao instalador (segue para ordem de pagamento).</p>
              {canFinance ? released.map((m) => <p key={m.id}>Libera a parcela {m.description} de <Money value={m.amount} /> do cliente, vencendo {m.daysAfterEvent ? `${m.daysAfterEvent} dias depois` : 'na data do aceite'}.</p>) : <p>Libera a parcela do cliente ligada ao aceite.</p>}
            </div>
          )}
          onConfirm={async () => { const r = await acceptInstallation(order.id, { date, acceptedByName: name.trim(), version: order.version }, idempotency.options); idempotency.renew(); await onChange(); onAccepted(r) }}
          trigger={<button type="button" className={primary}>Registrar aceite técnico</button>}>
          <div className="mt-3 grid gap-3 sm:grid-cols-2">
            <label className="space-y-1 text-sm font-medium"><span className="inline-flex items-center">Data do aceite<Dica nome="Aceite técnico" texto={INSTALLATION_DICAS.aceite} /></span><DateInput value={date} onChange={setDate} /></label>
            <label className="space-y-1 text-sm font-medium">Quem aceitou pelo cliente<input value={name} maxLength={120} onChange={(e) => setName(e.target.value)} className={inputClass} /></label>
          </div>
        </ConfirmDialog>
      ) : null}
      {(order.status === 'Open' && canEdit) || (order.status === 'InProgress' && canApprove) ? (
        <ConfirmDialog destructive verb="Cancelar OS" done="OS cancelada" title={`Cancelar a ${order.number}?`}
          description={order.status === 'Open' ? 'O título previsto do instalador é cancelado junto.' : 'A instalação já começou: o título do instalador fica previsto para o financeiro decidir.'}
          onOpen={() => setReason('')} preconditions={[{ ok: reason.trim().length > 0, text: 'Informe o motivo.' }]}
          onConfirm={async () => { await cancelInstallation(order.id, { reason: reason.trim(), version: order.version }); await onChange() }}
          trigger={<button type="button" className={secondary}>Cancelar OS</button>}>
          <label className="mt-3 block space-y-1 text-sm font-medium">Motivo<textarea rows={3} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} className={inputClass} /></label>
        </ConfirmDialog>
      ) : null}
      {order.payable ? <span className="self-center text-xs text-muted-foreground">Título {order.payable.number}{order.payable.dueDate ? `, vence ${formatDate(order.payable.dueDate)}` : ''}{order.payable.isEstimate ? ' (estimado)' : ''}</span> : null}
    </span>
  )
}
