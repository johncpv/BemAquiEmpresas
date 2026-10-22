import { useQueryClient } from '@tanstack/react-query'
import { useNavigate, useParams, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { getGetFabricationOrderQueryKey, getListFabricationOrdersQueryKey, useGetFabricationOrder } from '@/api/generated/bemaqui'
import { FactsBar } from '@/components/ui/facts-bar'
import { NextSteps } from '@/components/ui/next-steps'
import { OrderHeader } from '@/components/ui/order-header'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { UrlTabs } from '@/components/ui/url-tabs'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { OrderDocuments } from '../OrderDocuments'
import { CancelFabricationDialog, ChangeRoutingDialog } from './FabricationActions'
import { FabricationHistory } from './FabricationHistory'
import { FabricationSteps } from './FabricationSteps'
import { FabricationCostsTab } from './labor-costs'
import { LaborEntriesTab } from './LaborEntriesTab'
import { canChangeSteps, delayLabel, FABRICATION_DICAS, FABRICATION_STATUS, hours, quantity } from './fabrication'

/**
 * OS de fabricação (PRD-01 CA 10): OrderHeader e FactsBar comuns com a OS de instalação; abas na ordem do padrão 10 (as das
 * stories seguintes entram nelas): Etapas, Apontamentos, Custos (só com projects.view_cost, PRD-04), Documentos e Histórico de alterações.
 */
export function FabricationOrderPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const { gerada } = useSearch({ strict: false }) as { gerada?: unknown }
  const navigate = useNavigate()
  const client = useQueryClient()
  const canEdit = useCan('production.edit')
  const canSeeCost = useCan('projects.view_cost')
  const query = useGetFabricationOrder(id)
  const [justCreated, setJustCreated] = useState(String(gerada) === '1')
  if (query.isPending) return <Skeleton className="h-96" />
  if (query.isError) return <ErrorState title="Não foi possível carregar a OS." message="Ela pode não existir ou não ser de fabricação. Volte à lista ou tente de novo." onRetry={() => query.refetch()} />
  const o = query.data
  const refresh = async () => {
    await client.invalidateQueries({ queryKey: getGetFabricationOrderQueryKey(id) })
    await client.invalidateQueries({ queryKey: getListFabricationOrdersQueryKey() })
  }
  const current = o.currentStepIndex >= 0 ? o.steps[o.currentStepIndex] : undefined
  const editable = canChangeSteps(o)
  const noRouting = o.steps.length === 0
  return (
    <>
      <OrderHeader title={`${o.number} — ${o.projectName}`}
        status={{ label: FABRICATION_STATUS[o.status]?.label ?? o.status, tone: FABRICATION_STATUS[o.status]?.tone ?? 'neutral' }}
        stages={o.status !== 'Cancelled' && !noRouting ? { names: o.steps.map((s) => s.name), letters: o.steps.map((s) => s.code), current: Math.max(o.currentStepIndex, 0) } : null}
        project={{ id: o.projectId, number: o.projectNumber }}
        meta={(
          <span className="flex flex-wrap items-center gap-x-3 text-sm text-muted-foreground">
            <span>{o.partyName}</span>
            <span>{o.productCode ? `${o.productCode} · ` : ''}{o.productName ?? o.itemDescription}</span>
            <span className="tabular-nums">Qtd. {quantity(o.quantity)}</span>
            {noRouting && o.status !== 'Cancelled' ? <span className="inline-flex items-center"><StatusChip tone="warn">Sem roteiro</StatusChip><Dica nome="Sem roteiro" texto={FABRICATION_DICAS.semRoteiro} /></span> : null}
            {!noRouting && o.status !== 'Cancelled' && o.status !== 'Completed' && o.steps.every((s) => s.status === 'Done') ? <StatusChip tone="ok">Pronta para concluir</StatusChip> : null}
          </span>
        )}
        actions={(
          <span className="flex flex-wrap gap-2">
            {editable && canEdit ? <ChangeRoutingDialog order={o} onDone={refresh} /> : null}
            <CancelFabricationDialog order={o} onDone={refresh} />
          </span>
        )} />
      {justCreated ? (
        <div className="mb-4"><NextSteps onDismiss={() => setJustCreated(false)} steps={[
          ...(editable && canEdit ? [{ label: 'Ajustar as etapas', onSelect: () => { setJustCreated(false); void navigate({ to: '.', search: { aba: 'etapas' } } as never) } }] : []),
          { label: 'Ver projeto', onSelect: () => void navigate({ to: '/app/projetos/$id', params: { id: o.projectId }, search: { aba: 'itens' } } as never) },
          { label: 'Ver OS de fabricação', onSelect: () => void navigate({ to: '/app/producao/os' } as never) },
          { label: 'Ver no Kanban', onSelect: () => void navigate({ to: '/app/producao/kanban', search: o.routingTemplateId ? { roteiro: o.routingTemplateId } : {} } as never) },
        ]} /></div>
      ) : null}
      {o.status === 'Cancelled' ? <p role="note" className="mb-4 rounded-md border border-border bg-muted px-3 py-2 text-sm">OS cancelada{o.cancelReason ? `: ${o.cancelReason}` : ''}.</p> : null}
      {noRouting && o.status === 'Open' ? (
        <div role="note" className="mb-4 flex flex-wrap items-center justify-between gap-2 rounded-md border border-l-4 border-warn-soft border-l-warn bg-warn-soft px-3 py-2 text-sm text-warn-ink">
          <span>Escolha o roteiro desta OS. Sem roteiro, a OS não tem etapas e não entra na fila da fábrica.</span>
          {canEdit ? <ChangeRoutingDialog order={o} onDone={refresh} label="Escolher roteiro" /> : null}
        </div>
      ) : null}
      {o.started && o.status !== 'Cancelled' ? <p role="note" className="mb-4 rounded-md border border-border bg-muted px-3 py-2 text-sm">A OS já começou. As etapas seguem o roteiro gravado.</p> : null}
      <FactsBar facts={[
        { label: 'Prometido', dica: FABRICATION_DICAS.prometido, value: <span className="tabular-nums">{o.promisedDate ? formatDate(o.promisedDate) : '—'}{delayLabel(o.delayDays) ? <span className="block text-xs font-semibold text-crit-ink">{delayLabel(o.delayDays)}</span> : null}</span> },
        { label: 'Horas padrão', dica: FABRICATION_DICAS.horas, value: <span className="tabular-nums">{hours(o.totalStandardHours)} h</span> },
        { label: 'Etapa atual', value: current ? `${current.code} · ${current.name}` : '—' },
        { label: 'Roteiro', value: o.routingTemplateName ?? (noRouting ? 'Sem roteiro' : 'Roteiro excluído') },
      ]} />
      <UrlTabs label="Seções da OS" fallback="etapas" tabs={[
        { id: 'etapas', label: 'Etapas', content: <FabricationSteps key={o.version} order={o} onSaved={refresh} /> },
        { id: 'apontamentos', label: 'Apontamentos', content: <LaborEntriesTab order={o} onChanged={refresh} /> },
        // PRD-04 CA 14: a aba Custos só existe com projects.view_cost (linha Mão de obra; a EST-03 acrescenta Material).
        ...(canSeeCost ? [{ id: 'custos', label: 'Custos', content: <FabricationCostsTab orderId={id} /> }] : []),
        { id: 'documentos', label: 'Documentos', content: <OrderDocuments orderId={id} inputId="desenho-os" accept=".pdf,.png,.jpg,.jpeg,.dwg,.dxf"
          empty={{ title: 'Nenhum desenho ou documento anexado.', description: 'Anexe os desenhos técnicos (DWG, DXF ou PDF até 25 MB) e as fotos de referência da OS.' }} /> },
        { id: 'historico', label: 'Histórico de alterações', content: <FabricationHistory id={id} /> },
      ]} />
    </>
  )
}