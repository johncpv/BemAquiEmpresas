import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import { getGetOrderMaterialQueryKey, getListOrderRequisitionsQueryKey, useListOrderRequisitions } from '@/api/generated/bemaqui'
import type { RequisitionDetail } from '@/api/generated/model'
import { Money } from '@/components/ui/money'
import { NextSteps, type Step } from '@/components/ui/next-steps'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { REQUISITION_DICAS, REQUISITION_KIND, REQUISITION_STATUS } from './requisitions'
import { RequisitionDialog } from './RequisitionDialog'
import { ReturnDialog } from './ReturnDialog'

const action = (primary = false) => primary
  ? 'min-h-[44px] rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60'
  : 'min-h-[44px] rounded-md border border-border px-3 py-2 text-sm font-semibold'

/**
 * Requisições da OS na aba Material (EST-03 CA 2, 12, 13 e 17): "Requisitar material" e "Devolver ao estoque" com
 * <c>inventory.create</c>, "Manter no projeto como perda" na OS cancelada, a lista com quem pediu, quando e o valor, e o próximo
 * passo "Ver custo do projeto" (com projects.view_cost).
 */
export function OrderRequisitions({ orderId, projectId, projectNumber }: { orderId: string; projectId?: string; projectNumber?: string | null }) {
  const client = useQueryClient()
  const navigate = useNavigate()
  const canCreate = useCan('inventory.create')
  const canSeeCost = useCan('projects.view_cost')
  const query = useListOrderRequisitions(orderId)
  const [dialog, setDialog] = useState<'issue' | 'return' | 'loss' | null>(null)
  const [steps, setSteps] = useState<Step[]>([])
  if (query.isPending) return <Skeleton className="h-24" />
  if (query.isError) return <ErrorState title="Não foi possível carregar as requisições da OS." message="Tente de novo." onRetry={() => query.refetch()} />
  const view = { ...query.data, requisitions: query.data.requisitions ?? [], returnable: query.data.returnable ?? [] }
  const done = async (detail: RequisitionDetail) => {
    setDialog(null)
    await client.invalidateQueries({ queryKey: getListOrderRequisitionsQueryKey(orderId) })
    await client.invalidateQueries({ queryKey: getGetOrderMaterialQueryKey(orderId) })
    setSteps([
      { label: 'Ver requisição', onSelect: () => void navigate({ to: '/app/estoque/requisicoes/$id', params: { id: detail.id } } as never) },
      ...(canSeeCost && projectId ? [{ label: 'Ver custo do projeto', onSelect: () => void navigate({ to: '/app/projetos/$id', params: { id: projectId }, search: { aba: 'custos' } } as never) }] : []),
    ])
  }

  return (
    <section aria-labelledby="requisicoes-os" className="space-y-2">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="requisicoes-os" className="text-sm font-semibold uppercase tracking-wide text-muted-foreground">Requisições e devoluções</h2>
        {canCreate ? (
          <span className="flex flex-wrap gap-2">
            {!view.orderClosed ? <button type="button" className={action(true)} onClick={() => setDialog('issue')}>Requisitar material</button> : null}
            {view.returnable.length > 0 ? (
              <span className="inline-flex items-center"><button type="button" className={action()} onClick={() => setDialog('return')}>Devolver ao estoque</button><Dica nome="Devolver ao estoque" texto={REQUISITION_DICAS.devolver} /></span>
            ) : null}
            {view.orderCancelled && view.returnable.length > 0 ? <button type="button" className={action()} onClick={() => setDialog('loss')}>Manter no projeto como perda</button> : null}
          </span>
        ) : null}
      </div>
      {view.orderCancelled && view.returnable.length > 0 ? (
        <p role="note" className="rounded-md border border-l-4 border-warn-soft border-l-warn bg-warn-soft px-3 py-2 text-sm text-warn-ink">A OS foi cancelada com material baixado: devolva ao estoque o que voltou ou mantenha no projeto como perda, com o motivo.</p>
      ) : null}
      {steps.length ? <NextSteps steps={steps} onDismiss={() => setSteps([])} /> : null}
      {view.requisitions.length === 0 ? (
        <p className="text-sm text-muted-foreground">Nenhuma requisição ainda. {canCreate && !view.orderClosed ? 'Clique em Requisitar material para baixar o que está reservado.' : ''}</p>
      ) : (
        <ul className="divide-y divide-border rounded-md border border-border text-sm" aria-label="Requisições da OS">
          {view.requisitions.map((r) => (
            <li key={r.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
              <span className="flex flex-wrap items-center gap-2">
                <Link to={'/app/estoque/requisicoes/$id' as never} params={{ id: r.id } as never} className="font-semibold text-primary hover:underline">{r.number}</Link>
                <span>{REQUISITION_KIND[r.kind]}</span>
                <StatusChip tone={REQUISITION_STATUS[r.status].tone}>{REQUISITION_STATUS[r.status].label}</StatusChip>
                {r.stepName ? <span className="text-muted-foreground">etapa {r.stepName}</span> : null}
              </span>
              <span className="text-muted-foreground">{r.firstItem ?? '—'}{r.lineCount > 1 ? ` e mais ${r.lineCount - 1}` : ''}</span>
              <span className="tabular-nums">{r.value > 0 ? <Money value={r.value} /> : null} <span className="text-muted-foreground">{r.requestedByName ?? '—'} · {formatDateTime(r.requestedAt)}</span></span>
            </li>
          ))}
        </ul>
      )}
      {dialog === 'issue' ? <RequisitionDialog orderId={orderId} onClose={() => setDialog(null)} onDone={(d) => void done(d)} /> : null}
      {dialog === 'return' || dialog === 'loss' ? (
        <ReturnDialog orderId={orderId} projectNumber={projectNumber} lines={view.returnable} mode={dialog} onClose={() => setDialog(null)} onDone={(d) => void done(d)} />
      ) : null}
    </section>
  )
}
