import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from '@tanstack/react-router'
import { useState } from 'react'
import {
  approveMaterialRequisition, declineMaterialRequest, getGetMaterialRequisitionQueryKey, rejectMaterialRequisition, reverseMaterialRequisition, useGetMaterialRequisition,
} from '@/api/generated/bemaqui'
import type { RequisitionDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { RecordHistory } from '@/components/history/RecordHistory'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { FactsBar } from '@/components/ui/facts-bar'
import { Money } from '@/components/ui/money'
import { NextSteps, type Step } from '@/components/ui/next-steps'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { UrlTabs } from '@/components/ui/url-tabs'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { InventoryTabs } from '../InventoryTabs'
import { REQUISITION_DICAS, REQUISITION_KIND, REQUISITION_STATUS } from './requisitions'
import { RequisitionDialog } from './RequisitionDialog'
import { formatQuantity, formatUnitCost } from './stock'

const button = (text: string, primary = false, onClick?: () => void) => (
  <button type="button" onClick={onClick} className={primary ? 'min-h-[44px] rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground' : 'min-h-[44px] rounded-md border border-border px-3 py-2 text-sm font-semibold'}>{text}</button>
)

/**
 * Requisição (EST-03 CA 4, 7, 14 e 17): o que foi pedido e baixado, sem edição; "Confirmar pedido" e "Recusar pedido" no pedido do
 * operador (inventory.create), "Aprovar requisição" e "Reprovar requisição" acima de R$ 1.000,00 (inventory.approve, outra pessoa),
 * "Estornar requisição" pela regra do autor ou de quem aprova. O histórico mostra quem pediu, quem confirmou, quando, lote e valor.
 */
export function RequisitionPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const query = useGetMaterialRequisition(id)
  const client = useQueryClient()
  const navigate = useNavigate()
  const canSeeCost = useCan('projects.view_cost')
  const [text, setText] = useState('')
  const [confirming, setConfirming] = useState(false)
  const [steps, setSteps] = useState<Step[]>([])
  const idempotency = useIdempotencyKey()
  if (query.isPending) return <Skeleton className="h-96" />
  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return <ErrorState title={notFound ? 'Requisição não encontrada.' : 'Não foi possível carregar a requisição.'} onRetry={notFound ? undefined : () => query.refetch()} />
  }

  const r = query.data
  const refresh = async () => { await client.invalidateQueries({ queryKey: getGetMaterialRequisitionQueryKey(id) }) }
  const after = (detail?: RequisitionDetail) => {
    setText('')
    setSteps([
      { label: 'Próximo aguardando você', onSelect: () => void navigate({ to: '/app/estoque/requisicoes' as never, search: { filtro: 'aguardando-voce' } as never }) },
      { label: 'Ver OS', onSelect: () => void navigate({ to: '/app/producao/os/$id', params: { id: r.productionOrderId }, search: { aba: 'material' } } as never) },
      ...(canSeeCost && detail?.status === 'Completed' ? [{ label: 'Ver custo do projeto', onSelect: () => void navigate({ to: '/app/projetos/$id', params: { id: r.projectId }, search: { aba: 'custos' } } as never) }] : []),
    ])
  }
  const status = REQUISITION_STATUS[r.status]
  return (
    <>
      <PageHeading title={`${r.number} — ${REQUISITION_KIND[r.kind]}${r.orderNumber ? ` da ${r.orderNumber}` : ''}`}>
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
            <StatusChip tone={status.tone}>{status.label}</StatusChip>
            {r.selfApproved || r.reversalSelfApproved ? <Tag>Autoaprovado</Tag> : null}
            {r.overBomValue > 0 ? <span className="inline-flex items-center"><Tag>Acima da BOM: <Money value={r.overBomValue} /></Tag><Dica nome="Consumo acima da BOM" texto={REQUISITION_DICAS.consumoAcima} /></span> : null}
            <Link to={'/app/estoque/requisicoes' as never} className="font-semibold text-primary">Voltar às requisições</Link>
          </p>
          <div className="flex flex-wrap gap-2">
            {r.canConfirm ? (
              <>
                {button('Confirmar pedido', true, () => setConfirming(true))}
                <ConfirmDialog verb="Recusar pedido" done="Pedido recusado" title={`Recusar o pedido ${r.number}?`} description="Nada sai do estoque e o operador vê o motivo no app."
                  preconditions={[{ ok: text.trim().length > 0, text: 'Motivo da recusa escrito.' }]}
                  onConfirm={async () => { const d = await declineMaterialRequest(id, { reason: text.trim(), version: r.version }); await refresh(); after(d) }} onReload={refresh} trigger={button('Recusar pedido')}>
                  <label htmlFor="motivo-recusa" className="text-sm font-medium">Motivo da recusa</label>
                  <textarea id="motivo-recusa" value={text} maxLength={200} rows={3} onChange={(e) => setText(e.target.value)} className={inputClass} />
                </ConfirmDialog>
              </>
            ) : null}
            {r.canDecide ? (
              <>
                <ConfirmDialog size="lg" verb="Aprovar requisição" done="Requisição aprovada" title={`Aprovar a ${r.number}?`}
                  description="Você decide sobre o que foi enviado: a baixa sai com o saldo de agora. Para mudar algo, reprove com o motivo."
                  impact={<p>Acima da BOM: <Money value={r.overBomValue} className="font-semibold" />{canSeeCost && r.projectNumber ? ` · entra no custo do projeto ${r.projectNumber}` : ''}</p>}
                  onConfirm={async () => { const d = await approveMaterialRequisition(id, { comment: text.trim() || null, version: r.version }, idempotency.options); idempotency.renew(); await refresh(); after(d) }}
                  onReload={refresh} trigger={button('Aprovar requisição', true)}>
                  <label htmlFor="comentario-aprovar" className="text-sm font-medium">Comentário (opcional)</label>
                  <textarea id="comentario-aprovar" value={text} maxLength={500} rows={2} onChange={(e) => setText(e.target.value)} className={inputClass} />
                </ConfirmDialog>
                <ConfirmDialog verb="Reprovar requisição" done="Requisição reprovada" title={`Reprovar a ${r.number}?`} description="Nada sai do estoque e quem enviou recebe o motivo."
                  preconditions={[{ ok: text.trim().length > 0, text: 'Motivo da reprovação escrito.' }]}
                  onConfirm={async () => { const d = await rejectMaterialRequisition(id, { comment: text.trim(), version: r.version }); await refresh(); after(d) }} onReload={refresh} trigger={button('Reprovar requisição')}>
                  <label htmlFor="comentario-reprovar" className="text-sm font-medium">Motivo da reprovação</label>
                  <textarea id="comentario-reprovar" value={text} maxLength={500} rows={3} onChange={(e) => setText(e.target.value)} className={inputClass} />
                </ConfirmDialog>
              </>
            ) : null}
            {r.canReverse ? (
              <ConfirmDialog destructive size="lg" verb="Estornar requisição" done="Requisição estornada" title={`Estornar a ${r.number}?`}
                description="Nascem os movimentos inversos pelo custo da baixa; a reserva volta pela BOM e o custo sai do projeto."
                impact={<p>Valor estornado: <Money value={r.value} className="font-semibold" /></p>}
                preconditions={[{ ok: text.trim().length > 0, text: 'Motivo do estorno escrito.' }]}
                onConfirm={async () => { const d = await reverseMaterialRequisition(id, { reason: text.trim(), version: r.version }, idempotency.options); idempotency.renew(); await refresh(); after(d) }}
                onReload={refresh} trigger={button('Estornar requisição')}>
                <label htmlFor="motivo-estorno" className="text-sm font-medium">Motivo do estorno</label>
                <textarea id="motivo-estorno" value={text} maxLength={200} rows={2} onChange={(e) => setText(e.target.value)} className={inputClass} />
              </ConfirmDialog>
            ) : null}
          </div>
        </div>
      </PageHeading>
      <InventoryTabs />
      {r.status === 'PendingApproval' && r.decideBlockedReason ? <p role="note" className="mb-3 rounded-md border border-border bg-muted px-3 py-2 text-sm">{r.decideBlockedReason}</p> : null}
      {r.status === 'Completed' && !r.canReverse && r.reverseBlockedReason ? <p role="note" className="mb-3 rounded-md border border-border bg-muted px-3 py-2 text-sm">{r.reverseBlockedReason}</p> : null}
      {steps.length ? <div className="mb-3"><NextSteps steps={steps} onDismiss={() => setSteps([])} /></div> : null}
      <FactsBar label="Fatos da requisição" facts={[
        { label: 'OS', value: <Link to={'/app/producao/os/$id' as never} params={{ id: r.productionOrderId } as never} search={{ aba: 'material' } as never} className="font-semibold text-primary">{r.orderNumber ?? 'Abrir OS'}</Link> },
        { label: 'Quem pediu', value: `${r.requestedByName ?? '—'} · ${formatDateTime(r.requestedAt)}${r.stepName ? ` · etapa ${r.stepName}` : ''}` },
        { label: r.kind === 'Issue' ? 'Quem baixou' : 'Quem registrou', value: r.completedAt ? `${r.completedByName ?? '—'} · ${formatDateTime(r.completedAt)}` : '—' },
        { label: 'Valor', value: <Money value={r.value} /> },
      ]} />
      {r.decidedAt ? <p className="mb-2 text-sm"><span className="text-muted-foreground">Decisão:</span> {r.decidedByName ?? '—'} · {formatDateTime(r.decidedAt)}{r.decisionComment ? ` · ${r.decisionComment}` : ''}</p> : null}
      {r.rejectReason ? <p className="mb-2 text-sm"><span className="text-muted-foreground">{r.kind === 'Loss' ? 'Motivo da perda' : 'Motivo da recusa'}:</span> {r.rejectReason}</p> : null}
      {r.reversedAt ? <p className="mb-2 text-sm"><span className="text-muted-foreground">Estornada:</span> {r.reversedByName ?? '—'} · {formatDateTime(r.reversedAt)} · {r.reversalReason}</p> : null}
      <UrlTabs label="Seções da requisição" fallback="linhas" tabs={[
        {
          id: 'linhas', label: 'Linhas', content: (
            <ul className="mt-3 space-y-2 text-sm" aria-label="Linhas da requisição">
              {r.lines.map((l) => (
                <li key={l.id} className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border bg-card p-3">
                  <span><span className="font-mono">{l.itemCode}</span> {l.itemName}{l.lotNumber ? ` · lote ${l.lotNumber}` : ''} · {l.locationCode}</span>
                  <span className="tabular-nums">{formatQuantity(l.quantity)} {l.unitCode}{l.overQuantity > 0 ? ` (${formatQuantity(l.overQuantity)} acima da BOM)` : ''}</span>
                  {l.isExtra ? <StatusChip tone="warn">Material extra</StatusChip> : null}
                  {l.overReason ? <span className="w-full text-muted-foreground">Consumo acima da BOM: {l.overReason}</span> : null}
                  {l.unitCost !== null && l.amount !== null ? <span className="tabular-nums">{formatUnitCost(l.unitCost)} · <Money value={l.amount} /></span> : null}
                  {l.movementId ? <Link to={'/app/estoque/movimentos/$id' as never} params={{ id: l.movementId } as never} className="font-semibold text-primary">{l.movementNumber ?? 'Ver movimento'}</Link> : null}
                  {l.returnable > 0 && l.returnable < l.quantity ? <span className="text-muted-foreground">pode voltar {formatQuantity(l.returnable)}</span> : null}
                </li>
              ))}
            </ul>
          ),
        },
        { id: 'historico', label: 'Histórico de alterações', content: <RecordHistory entity="MaterialRequisition" id={r.id} /> },
      ]} />
      {confirming ? (
        <RequisitionDialog orderId={r.productionOrderId} request={{ id: r.id, number: r.number, version: r.version, lines: r.lines.map((l) => ({ itemId: l.itemId, quantity: l.quantity })) }}
          onClose={() => setConfirming(false)} onDone={(d) => { setConfirming(false); void refresh(); after(d) }} />
      ) : null}
    </>
  )
}
