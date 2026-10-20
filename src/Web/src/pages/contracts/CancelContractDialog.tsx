import { useState } from 'react'
import { cancelContract } from '@/api/generated/bemaqui'
import type { ContractDetail } from '@/api/generated/model'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'

/** Cancelar contrato (COM-04b CA 12): motivo obrigatório; o diálogo mostra o que acontece com cada parcela. */
export function CancelContractDialog({ contract, onDone }: { contract: ContractDetail; onDone: () => Promise<void> }) {
  const [reason, setReason] = useState('')
  const parcels = contract.milestones.flatMap((m) => (m.receivable ? [m.receivable] : []))
  const open = parcels.filter((p) => (p.status === 'Planned' || p.status === 'Open') && p.balance === p.amount)
  const settled = parcels.filter((p) => p.status !== 'Cancelled' && p.balance !== p.amount)
  return (
    <ConfirmDialog destructive size="lg" verb="Cancelar contrato" done="Contrato cancelado" title={`Cancelar o contrato ${contract.number}?`}
      description="O projeto também é cancelado. As parcelas sem baixa são canceladas no financeiro; as que já têm baixa ficam para o financeiro decidir."
      onOpen={() => setReason('')}
      preconditions={[{ ok: reason.trim().length > 0, text: 'Informe o motivo do cancelamento.' }]}
      impact={parcels.length ? (
        <div className="space-y-1 text-sm">
          {open.length ? <p>Serão canceladas: {open.map((p) => p.number).join(', ')} (<Money value={open.reduce((a, p) => a + p.amount, 0)} />).</p> : null}
          {settled.length ? <p>Com baixa, ficam para o financeiro: {settled.map((p) => p.number).join(', ')}.</p> : null}
        </div>
      ) : <p className="text-sm text-muted-foreground">O financeiro cancela as parcelas sem baixa deste contrato.</p>}
      onConfirm={async () => { await cancelContract(contract.id, { reason: reason.trim(), version: contract.version }); await onDone() }}
      trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold text-crit-ink">Cancelar contrato</button>}>
      <label className="mt-3 block space-y-1 text-sm font-medium">Motivo
        <textarea value={reason} maxLength={500} rows={3} onChange={(e) => setReason(e.target.value)} className={inputClass} />
      </label>
    </ConfirmDialog>
  )
}
