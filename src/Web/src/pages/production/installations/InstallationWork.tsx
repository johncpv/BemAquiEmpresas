import { completeInstallationStep, markInstallationChecklist } from '@/api/generated/bemaqui'
import type { ChecklistResult, InstallationDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { notifyError, notifySuccess } from '@/components/ui/messages'
import { formatDateTime } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { cn } from '@/lib/utils'

/** Etapas (COM-06 CA 7): em sequência; a última, "Entrega técnica", conclui pelo aceite. */
export function InstallationSteps({ order, onChange }: { order: InstallationDetail; onChange: () => Promise<void> }) {
  const canEdit = useCan('production.edit') && order.status === 'InProgress'
  const next = order.steps.findIndex((s) => !s.completedAt)
  const complete = async (stepId: string) => {
    try {
      await completeInstallationStep(order.id, stepId, { version: order.version })
      notifySuccess('Etapa concluída')
      await onChange()
    } catch (e) {
      notifyError(e instanceof ApiError ? e.message : 'Não foi possível concluir a etapa.')
    }
  }

  return (
    <ol className="divide-y divide-border rounded-md border border-border bg-card text-sm" aria-label="Etapas da OS">
      {order.steps.map((s, i) => (
        <li key={s.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
          <span><b>{s.sequence}.</b> {s.name}{s.completedAt ? <span className="block text-xs text-muted-foreground">Concluída em {formatDateTime(s.completedAt)}{s.completedByName ? ` por ${s.completedByName}` : ''}</span> : null}</span>
          {!s.completedAt && canEdit && i === next && i < order.steps.length - 1
            ? <button type="button" onClick={() => void complete(s.id)} className="min-h-[56px] rounded-md bg-primary px-4 font-semibold text-primary-foreground">Concluir etapa</button>
            : !s.completedAt && i === order.steps.length - 1 ? <span className="text-xs text-muted-foreground">Conclui pelo aceite técnico</span> : null}
        </li>
      ))}
    </ol>
  )
}

const OPTIONS: { value: ChecklistResult; label: string }[] = [{ value: 'Done', label: 'Feito' }, { value: 'NotApplicable', label: 'Não se aplica' }]

/** Checklist de entrega técnica (COM-06 CA 8): marcação por toque, alvo de 56 px no celular. */
export function InstallationChecklist({ order, onChange }: { order: InstallationDetail; onChange: () => Promise<void> }) {
  const canEdit = useCan('production.edit') && (order.status === 'Open' || order.status === 'InProgress')
  const mark = async (itemId: string, result: ChecklistResult | null) => {
    try {
      await markInstallationChecklist(order.id, itemId, { result, version: order.version })
      await onChange()
    } catch (e) {
      notifyError(e instanceof ApiError ? e.message : 'Não foi possível marcar o item.')
    }
  }

  return (
    <ul className="space-y-2 text-sm" aria-label="Checklist de entrega técnica">
      {order.checklist.map((c) => (
        <li key={c.id} className="rounded-md border border-border bg-card p-3">
          <p className="font-medium">{c.text}</p>
          {c.markedAt ? <p className="text-xs text-muted-foreground">{c.result === 'Done' ? 'Feito' : 'Não se aplica'} · {formatDateTime(c.markedAt)}{c.markedByName ? ` por ${c.markedByName}` : ''}</p> : null}
          {canEdit ? (
            <div className="mt-2 flex flex-wrap gap-2" role="group" aria-label={`Marcar ${c.text}`}>
              {OPTIONS.map((o) => (
                <button key={o.value} type="button" aria-pressed={c.result === o.value} onClick={() => void mark(c.id, c.result === o.value ? null : o.value)}
                  className={cn('min-h-[56px] min-w-28 rounded-md border px-4 font-semibold', c.result === o.value ? 'border-primary bg-primary text-primary-foreground' : 'border-border bg-card')}>
                  {o.label}
                </button>
              ))}
            </div>
          ) : null}
        </li>
      ))}
    </ul>
  )
}
