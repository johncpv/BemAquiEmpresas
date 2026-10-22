import { useState } from 'react'
import { cancelFabricationOrder, changeFabricationRouting, useListFabricationRoutingOptions } from '@/api/generated/bemaqui'
import type { FabricationOrderDetail } from '@/api/generated/model'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Skeleton } from '@/components/ui/states'
import { Tag } from '@/components/ui/tag'
import { useCan } from '@/pages/use-can'
import { canCancel, hours, routingImpact } from './fabrication'

const secondary = 'rounded-md border border-border px-3 py-2 text-sm font-semibold'

/**
 * "Trocar roteiro" (CA 9, padrões 4 e 6): os roteiros ativos com etapas e horas, o sugerido primeiro com a tag "Sugerido" e o
 * impacto antes → depois; as etapas atuais dão lugar às do roteiro escolhido.
 */
export function ChangeRoutingDialog({ order: o, onDone, label = 'Trocar roteiro' }: { order: FabricationOrderDetail; onDone: () => Promise<void>; label?: string }) {
  const [open, setOpen] = useState(false)
  const options = useListFabricationRoutingOptions(o.id, { query: { enabled: open } })
  const [chosen, setChosen] = useState<string | null>(null)
  const list = options.data ?? []
  const picked = list.find((r) => r.id === chosen)
  return (
    <ConfirmDialog size="lg" verb="Trocar roteiro" done="Roteiro trocado" title={`Trocar o roteiro da ${o.number}?`} open={open} onOpenChange={setOpen}
      description="As etapas desta OS são substituídas pelas do roteiro escolhido. O roteiro não muda."
      onOpen={() => setChosen(null)}
      preconditions={[{ ok: !!picked, text: 'Escolha um roteiro.' }]}
      impact={picked ? <p className="text-sm tabular-nums">{routingImpact({ steps: o.steps.length, hours: o.totalStandardHours }, { steps: picked.stepCount, hours: picked.totalStandardHours })}</p> : null}
      onConfirm={async () => { await changeFabricationRouting(o.id, { routingTemplateId: chosen!, version: o.version }); await onDone() }}
      trigger={<button type="button" className={secondary}>{label}</button>}>
      {options.isPending ? <Skeleton className="mt-3 h-24" /> : list.length === 0 ? (
        <p className="mt-3 text-sm text-muted-foreground">Nenhum roteiro ativo. Cadastre o roteiro em Engenharia → Roteiros.</p>
      ) : (
        <fieldset className="mt-3 space-y-2">
          <legend className="text-sm font-medium">Roteiro</legend>
          {list.map((r) => (
            <label key={r.id} className="flex cursor-pointer items-start gap-3 rounded-md border border-border p-3 text-sm has-[:checked]:border-primary">
              <input type="radio" name="roteiro" className="mt-1 size-4 accent-primary" checked={chosen === r.id} onChange={() => setChosen(r.id)} />
              <span className="min-w-0">
                <span className="flex flex-wrap items-center gap-2 font-semibold">{r.name}{r.suggested ? <Tag>Sugerido</Tag> : null}</span>
                <span className="block text-muted-foreground tabular-nums">{r.productType ? `${r.productType} · ` : ''}{r.stepCount} etapas · {hours(r.totalStandardHours)} h padrão{r.hasAllWorkCenters ? '' : ' · etapas sem centro'}</span>
              </span>
            </label>
          ))}
        </fieldset>
      )}
    </ConfirmDialog>
  )
}

/** "Cancelar OS" (CA 16 e 17): motivo obrigatório; a iniciada só com production.approve, com o impacto das horas apontadas. */
export function CancelFabricationDialog({ order: o, onDone }: { order: FabricationOrderDetail; onDone: () => Promise<void> }) {
  const canEdit = useCan('production.edit')
  const canApprove = useCan('production.approve')
  const [reason, setReason] = useState('')
  if (!canCancel(o, canEdit, canApprove)) return null
  return (
    <ConfirmDialog destructive verb="Cancelar OS" done="OS cancelada" title={`Cancelar a ${o.number}?`}
      description={o.started ? 'A OS já começou: o cancelamento fica registrado com o motivo.' : 'A OS ainda não começou. O item volta para os itens de projeto sem OS.'}
      impact={o.started ? <p className="text-sm">Horas apontadas: nenhuma até agora. O material já baixado aparece aqui quando o estoque estiver ligado.</p> : null}
      onOpen={() => setReason('')} preconditions={[{ ok: reason.trim().length > 0, text: 'Informe o motivo.' }]}
      onConfirm={async () => { await cancelFabricationOrder(o.id, { reason: reason.trim(), version: o.version }); await onDone() }}
      trigger={<button type="button" className={secondary}>Cancelar OS</button>}>
      <label className="mt-3 block space-y-1 text-sm font-medium">Motivo<textarea rows={3} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} className={inputClass} /></label>
    </ConfirmDialog>
  )
}