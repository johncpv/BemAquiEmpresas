import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { approveAmendment, getGetAmendmentQueryKey, rejectAmendment, submitAmendment } from '@/api/generated/bemaqui'
import type { AmendmentDetail } from '@/api/generated/model'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'

/** Enviar, aprovar (com o impacto) e reprovar o aditivo (COM-07 CA 5, 6, 8). */
export function AmendmentActions({ amendment: a }: { amendment: AmendmentDetail }) {
  const client = useQueryClient()
  const canEdit = useCan('projects.edit')
  const [comment, setComment] = useState('')
  const refresh = () => client.invalidateQueries({ queryKey: getGetAmendmentQueryKey(a.contractId, a.id) })
  const impact = (
    <div className="space-y-1 text-sm">
      <p>Contratado de <Money value={a.preview.valueBefore} /> para <Money value={a.preview.valueAfter} />.</p>
      {a.preview.promisedBefore ? <p>Data prometida de {formatDate(a.preview.promisedBefore)} para {formatDate(a.preview.promisedAfter!)}.</p> : null}
      {a.milestones.length ? <p>Cria {a.milestones.length} {a.milestones.length === 1 ? 'parcela' : 'parcelas'} a receber.</p> : null}
      {a.reductions.length ? <p>Reduz {a.reductions.map((r) => r.number).join(', ')}.</p> : null}
    </div>
  )
  return (
    <span className="flex flex-wrap gap-2">
      {a.status === 'Draft' && canEdit ? (
        <ConfirmDialog verb="Enviar para aprovação" done="Aditivo enviado" title="Enviar o aditivo para aprovação?" description="Quem aprova em contratos e projetos é avisado."
          onConfirm={async () => { await submitAmendment(a.contractId, a.id, { version: a.version }); await refresh() }}
          trigger={<button type="button" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Enviar para aprovação</button>} />
      ) : null}
      {a.canDecide ? (
        <>
          <ConfirmDialog size="lg" verb="Aprovar aditivo" done="Aditivo aprovado" title={`Aprovar o aditivo ${a.number}?`} impact={impact}
            onConfirm={async () => { await approveAmendment(a.contractId, a.id, { version: a.version }); await refresh() }}
            trigger={<button type="button" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Aprovar aditivo</button>} />
          <ConfirmDialog verb="Reprovar aditivo" done="Aditivo reprovado" title={`Reprovar o aditivo ${a.number}?`} onOpen={() => setComment('')}
            preconditions={[{ ok: comment.trim().length > 0, text: 'Diga o motivo da reprovação.' }]}
            onConfirm={async () => { await rejectAmendment(a.contractId, a.id, { comment: comment.trim(), version: a.version }); await refresh() }}
            trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Reprovar aditivo</button>}>
            <label className="mt-3 block space-y-1 text-sm font-medium">Motivo<textarea rows={3} maxLength={500} value={comment} onChange={(e) => setComment(e.target.value)} className={inputClass} /></label>
          </ConfirmDialog>
        </>
      ) : null}
    </span>
  )
}
