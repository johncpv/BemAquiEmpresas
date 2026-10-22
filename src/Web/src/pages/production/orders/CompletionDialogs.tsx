import { useState } from 'react'
import { completeFabricationOrder, reverseFabricationOrderCompletion, useGetFabricationOrderCompletion } from '@/api/generated/bemaqui'
import type { FabricationOrderDetail, OrderCompletionPreview } from '@/api/generated/model'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Skeleton } from '@/components/ui/states'
import { Dica } from '@/help/Dica'
import { formatDate, formatMoney } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { canShowComplete, completionImpact, FABRICATION_DICAS, reversalImpact } from './fabrication'

const primary = 'rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground'
const secondary = 'rounded-md border border-border px-3 py-2 text-sm font-semibold'

function Impact({ lines, loading, failed }: { lines: string[]; loading: boolean; failed: boolean }) {
  if (loading) return <Skeleton className="h-16" />
  if (failed) return <p className="text-sm text-crit-ink">Não foi possível ler o impacto. Feche e abra de novo.</p>
  return <ul className="list-disc space-y-1 pl-5 text-sm">{lines.map((l) => <li key={l}>{l}</li>)}</ul>
}

/**
 * "Concluir OS" (PRD-06a1 CA 2 a 5): com production.approve, na OS "Pronta para concluir"; fora dela, o botão fica desabilitado com o
 * motivo do servidor. O diálogo (tela cheia a 400 px) mostra a OS concluída, o projeto Fabricado e as parcelas liberadas quando é a
 * última OS, e o aviso do estoque. Sem campo de quantidade: a OS conclui inteira (CA 5).
 */
export function CompleteOrderDialog({ order: o, onDone }: { order: FabricationOrderDetail; onDone: (preview: OrderCompletionPreview | undefined) => Promise<void> }) {
  const canApprove = useCan('production.approve')
  const [open, setOpen] = useState(false)
  const preview = useGetFabricationOrderCompletion(o.id, { query: { enabled: open } })
  if (!canShowComplete(o, canApprove)) return null
  if (o.completionBlocker) {
    return (
      <span className="inline-flex flex-wrap items-center gap-2">
        <button type="button" disabled aria-describedby={`concluir-${o.id}`} className={`${primary} opacity-50`}>Concluir OS</button>
        <span id={`concluir-${o.id}`} className="text-xs text-muted-foreground">{o.completionBlocker}</span>
      </span>
    )
  }
  const p = preview.data
  const lines = p ? completionImpact(p, (v) => formatMoney(v), formatDate) : []
  return (
    <ConfirmDialog size="lg" verb="Concluir OS" done="OS concluída" title={`Concluir a ${o.number}?`} open={open} onOpenChange={setOpen}
      description="A OS conclui com a quantidade inteira. A conclusão pode ser estornada enquanto a parcela não for recebida."
      preconditions={[{ ok: !!p?.canComplete, text: p?.completeBlocker ?? 'Lendo o impacto da conclusão.' }]}
      impact={(
        <div className="space-y-2">
          <Impact lines={lines} loading={preview.isPending} failed={preview.isError} />
          {p ? <p className="flex items-center text-sm text-muted-foreground">{p.stockNotice}<Dica nome="Estoque" texto={FABRICATION_DICAS.estoque} /></p> : null}
        </div>
      )}
      onConfirm={async () => { await completeFabricationOrder(o.id, { version: p?.version ?? o.version }); await onDone(p) }}
      trigger={<button type="button" className={primary}>Concluir OS</button>} />
  )
}

/**
 * "Estornar conclusão" (PRD-06a1 CA 10 e 11): com production.approve, na OS concluída; motivo obrigatório (até 500), diálogo destructive
 * com o impacto. Parcela já recebida ou projeto além da fabricação: o servidor recusa e o motivo aparece no diálogo.
 */
export function ReverseCompletionDialog({ order: o, onDone }: { order: FabricationOrderDetail; onDone: () => Promise<void> }) {
  const canApprove = useCan('production.approve')
  const [open, setOpen] = useState(false)
  const [reason, setReason] = useState('')
  const preview = useGetFabricationOrderCompletion(o.id, { query: { enabled: open } })
  if (!canApprove || o.status !== 'Completed') return null
  const p = preview.data
  return (
    <ConfirmDialog size="lg" destructive verb="Estornar conclusão" done="Conclusão estornada" title={`Estornar a conclusão da ${o.number}?`} open={open} onOpenChange={setOpen}
      description="A OS volta para a última etapa. O título a receber não é estornado: a decisão é do Financeiro."
      onOpen={() => setReason('')}
      preconditions={[
        { ok: !p || p.canReverse, text: p?.reverseBlocker ?? '' },
        { ok: reason.trim().length > 0, text: 'Informe o motivo.' },
      ]}
      impact={<Impact lines={p ? reversalImpact(p) : []} loading={preview.isPending} failed={preview.isError} />}
      onConfirm={async () => { await reverseFabricationOrderCompletion(o.id, { reason: reason.trim(), version: o.version }); await onDone() }}
      trigger={<button type="button" className={secondary}>Estornar conclusão</button>}>
      <label className="mt-3 block space-y-1 text-sm font-medium">Motivo<textarea rows={3} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} className={inputClass} /></label>
    </ConfirmDialog>
  )
}