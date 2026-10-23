import { useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import { createBom, discardBom, getGetOrderBomQueryKey, getListBomsQueryKey, reviseBom, saveBom, useGetOrderBom } from '@/api/generated/bemaqui'
import type { BomDetail, BomLineView, BomReleasePreview } from '@/api/generated/model'
import { useIdempotencyKey } from '@/api/idempotency'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { NextSteps } from '@/components/ui/next-steps'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDateTime, formatMoney } from '@/lib/format'
import { cn } from '@/lib/utils'
import { useCan } from '@/pages/use-can'
import { BomEditor } from './BomEditor'
import { CopyBomDialog, ReleaseBomDialog } from './BomDialogs'
import { BomHistory, BomRevisions } from './BomRevisions'
import { BOM_DICAS, BOM_STATUS, draftLines, qty, toInput } from './boms'

const primary = 'rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground'
const secondary = 'rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold'
type View = 'bom' | 'revisoes' | 'historico'

/**
 * Aba BOM da OS de fabricação (ENG-02, padrão 10, só com engineering.view): vazio com "Criar BOM" e "Copiar de outra OS"; o rascunho no
 * editor com "Salvar BOM", "Copiar BOM", "Liberar BOM" e "Descartar rascunho"; a liberada em modo leitura com "Revisar BOM"; "Revisões"
 * e "Histórico de alterações". Cada botão só aparece com a ação do grupo (CA 19).
 */
export function BomTab({ orderId }: { orderId: string }) {
  const client = useQueryClient()
  const navigate = useNavigate()
  const query = useGetOrderBom(orderId)
  const canCreate = useCan('engineering.create')
  const canEdit = useCan('engineering.edit')
  const canApprove = useCan('engineering.approve')
  const canDelete = useCan('engineering.delete')
  const canSeeCost = useCan('projects.view_cost')
  const { options: createKey, renew: renewCreate } = useIdempotencyKey()
  const { options: reviseKey, renew: renewRevise } = useIdempotencyKey()
  const [view, setView] = useState<View>('bom')
  const [copying, setCopying] = useState(false)
  const [releasing, setReleasing] = useState(false)
  const [discarding, setDiscarding] = useState(false)
  const [revising, setRevising] = useState(false)
  const [dirty, setDirty] = useState(false)
  const [released, setReleased] = useState<BomReleasePreview | null>(null)

  if (query.isPending) return <div className="space-y-2"><Skeleton className="h-10" /><Skeleton className="h-40" /></div>
  if (query.isError) return <ErrorState title="Não foi possível carregar a BOM." message="Confira a conexão e tente de novo. Se a OS foi excluída, volte à lista." onRetry={() => query.refetch()} />
  const { order, draft, released: current, revisions } = query.data
  const refresh = async () => {
    await client.invalidateQueries({ queryKey: getGetOrderBomQueryKey(orderId) })
    await client.invalidateQueries({ queryKey: getListBomsQueryKey() })
  }
  const willStop = !current && ((order.currentStepSequence ?? 0) > 1)
  const editing = draft
  const create = async () => {
    await createBom({ productionOrderId: orderId, copyFromBomId: null }, createKey)
    renewCreate()
    await refresh()
  }

  const tabs: { id: View; label: string }[] = [{ id: 'bom', label: 'BOM' }, { id: 'revisoes', label: 'Revisões' }, { id: 'historico', label: 'Histórico de alterações' }]
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-2 text-sm">
        {order.productId ? null : <span className="inline-flex items-center"><StatusChip tone="warn">Sem item de estoque</StatusChip><Dica nome="Sem item de estoque" texto={BOM_DICAS.semItem} /></span>}
        {current ? <StatusChip tone="ok">Revisão {current.revision} liberada</StatusChip> : null}
        {draft ? <StatusChip tone="info">Revisão {draft.revision} em rascunho</StatusChip> : null}
        <span className="inline-flex items-center text-muted-foreground">A primeira etapa começa sem BOM; as seguintes só com a BOM liberada.<Dica nome="Etapas sem BOM" texto={BOM_DICAS.primeiraEtapa} /></span>
      </div>
      {willStop && !order.closed ? (
        <p role="note" className="rounded-md border border-l-4 border-warn-soft border-l-warn bg-warn-soft px-3 py-2 text-sm text-warn-ink">
          A etapa atual ({order.currentStepCode} · {order.currentStepName}) só começa com a BOM liberada. Libere a BOM para a fábrica não parar.
        </p>
      ) : null}
      {order.closed ? <p role="note" className="rounded-md border border-border bg-muted px-3 py-2 text-sm">A OS {order.number} está {order.status === 'Cancelled' ? 'cancelada' : 'concluída'}: a BOM não muda.</p> : null}
      {released ? (
        <NextSteps onDismiss={() => setReleased(null)} steps={[
          ...(released.shortageCount > 0 ? [{ label: 'Ver solicitações de compra geradas', onSelect: () => void navigate({ to: '/app/compras/solicitacoes', search: { os: orderId, filtro: 'todas' } } as never) }] : []),
          ...(released.shortageCount > 0 ? [{ label: 'Ver faltas da OS', onSelect: () => { setReleased(null); void navigate({ to: '.', search: { aba: 'material' } } as never) } }] : []),
          { label: 'Ver etapas da OS', onSelect: () => void navigate({ to: '.', search: { aba: 'etapas' } } as never) },
          { label: 'Ver BOMs da Engenharia', onSelect: () => void navigate({ to: '/app/engenharia/boms' } as never) },
        ].slice(0, 3)} />
      ) : null}

      <div role="group" aria-label="Seções da BOM" className="flex flex-wrap gap-1">
        {tabs.map((t) => (
          <button key={t.id} type="button" aria-pressed={view === t.id} onClick={() => setView(t.id)}
            className={cn('rounded-md border px-3 py-1.5 text-sm font-semibold', view === t.id ? 'border-primary bg-primary-soft text-foreground' : 'border-border bg-card text-muted-foreground')}>{t.label}</button>
        ))}
      </div>

      {view === 'revisoes' ? <BomRevisions revisions={revisions} /> : view === 'historico' ? <BomHistory orderId={orderId} /> : !draft && !current ? (
        <EmptyState title="Esta OS ainda não tem BOM." description="Monte a lista de materiais do zero ou copie de uma OS parecida. Nada vai para o estoque até liberar."
          action={order.closed || !canCreate ? undefined : (
            <span className="flex flex-wrap justify-center gap-2">
              <ConfirmDialog verb="Criar BOM" done="BOM criada" title="Criar a BOM desta OS?" description="Nasce a revisão 0 em rascunho, vazia. Nada vai para o estoque até liberar."
                trigger={<button type="button" className={primary}>Criar BOM</button>} onConfirm={create} />
              <button type="button" className={secondary} onClick={() => setCopying(true)}>Copiar de outra OS</button>
            </span>
          )} />
      ) : (
        <>
          {editing ? (
            <>
              {current ? <p role="note" className="rounded-md border border-border bg-muted px-3 py-2 text-sm">Revisão {editing.revision} em rascunho. A revisão {current.revision} continua valendo até esta ser liberada.</p> : null}
              {editing.copiedFromOrderNumber ? <p className="text-sm text-muted-foreground">Copiada da {editing.copiedFromOrderNumber}, revisão {editing.copiedFromRevision}.</p> : null}
              <BomEditor key={`${editing.id}-${editing.version}`} orderId={orderId} orderQuantity={order.quantity} initial={draftLines(editing)} canEdit={canEdit && !order.closed}
                stockConnected={editing.stockConnected} onDirtyChange={setDirty}
                estimatedCost={canSeeCost && editing.estimatedCost !== null && editing.estimatedCost !== undefined ? <>Custo previsto {formatMoney(editing.estimatedCost)}</> : undefined}
                onSubmit={async (lines) => {
                  await saveBom(editing.id, { lines: lines.map(toInput), version: editing.version })
                  notifySuccess('BOM salva')
                  await refresh()
                }}
                footerExtra={order.closed ? null : (
                  <>
                    {canEdit ? <button type="button" className={secondary} onClick={() => setCopying(true)}>Copiar BOM</button> : null}
                    {canDelete ? <button type="button" className={secondary} onClick={() => setDiscarding(true)}>Descartar rascunho</button> : null}
                    {canApprove ? <button type="button" className={primary} onClick={() => setReleasing(true)}>{current ? 'Liberar revisão' : 'Liberar BOM'}</button> : null}
                  </>
                )} />
              <ReleaseBomDialog bom={editing} dirty={dirty} open={releasing} onOpenChange={setReleasing} onReload={refresh}
                onDone={async (preview) => { setReleased(preview ?? null); setDirty(false); await refresh() }} />
              <ConfirmDialog destructive open={discarding} onOpenChange={setDiscarding} verb="Descartar rascunho" done="Rascunho descartado" title={`Descartar a revisão ${editing.revision} em rascunho?`}
                description={current ? `O rascunho é apagado. A revisão ${current.revision} liberada continua valendo.` : 'O rascunho é apagado e a OS volta a ficar sem BOM. O histórico guarda quem descartou.'}
                onReload={refresh}
                onConfirm={async () => { await discardBom(editing.id, { version: editing.version }); setDirty(false); await refresh() }} />
            </>
          ) : current ? (
            <>
              <div role="note" className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border bg-muted px-3 py-2 text-sm">
                <span>BOM liberada em {current.releasedAt ? formatDateTime(current.releasedAt) : '—'}{current.releasedByName ? ` por ${current.releasedByName}` : ''}. Para mudar, revise a BOM.</span>
                {canEdit && !order.closed ? <button type="button" className={primary} onClick={() => setRevising(true)}>Revisar BOM</button> : null}
              </div>
              <ConfirmDialog open={revising} onOpenChange={setRevising} verb="Revisar BOM" done="Revisão criada" title={`Criar a revisão ${current.revision + 1} da BOM?`}
                description={`Nasce um rascunho com as linhas da revisão ${current.revision}. A revisão ${current.revision} continua valendo até a nova ser liberada.`}
                onReload={refresh}
                onConfirm={async () => { await reviseBom(current.id, { version: current.version }, reviseKey); renewRevise(); await refresh() }} />
            </>
          ) : null}
          {current && !editing ? <BomLines bom={current} canSeeCost={canSeeCost} /> : null}
        </>
      )}
      <CopyBomDialog orderId={orderId} draft={draft ?? null} open={copying} onOpenChange={setCopying} onDone={async () => { setDirty(false); await refresh() }} />
    </div>
  )
}

/**
 * Linhas da BOM liberada, só leitura (padrão 8): item, quantidade por unidade, necessidade da OS e disponível; físico e reservado na
 * linha expansível (a 400 px fica o essencial).
 */
function BomLines({ bom, canSeeCost }: { bom: BomDetail; canSeeCost: boolean }) {
  if (bom.lines.length === 0) return <EmptyState title="BOM sem material." description="Esta OS usa só mão de obra. Para incluir material, revise a BOM." />
  return (
    <div className="space-y-2">
      <ul aria-label={`Linhas da BOM, revisão ${bom.revision}`} className="space-y-2">
        {bom.lines.map((l) => <BomLineCard key={l.id} line={l} canSeeCost={canSeeCost} />)}
      </ul>
      <p aria-live="polite" className="text-sm tabular-nums">
        {bom.lines.length} {bom.lines.length === 1 ? 'item' : 'itens'} · <StatusChip tone={BOM_STATUS[bom.status]?.tone ?? 'neutral'}>{BOM_STATUS[bom.status]?.label ?? bom.status}</StatusChip>
        {canSeeCost && bom.estimatedCost !== null && bom.estimatedCost !== undefined ? <> · Custo previsto {formatMoney(bom.estimatedCost)}</> : null}
      </p>
    </div>
  )
}

function BomLineCard({ line: l, canSeeCost }: { line: BomLineView; canSeeCost: boolean }) {
  return (
    <li className="rounded-md border border-border bg-card px-3 py-2 text-sm">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <span>
          <b className="font-semibold">{l.itemCode}</b> {l.itemName}
          {!l.itemActive ? <span className="ml-1"><StatusChip tone="warn">Item inativo</StatusChip></span> : null}
          <span className="block text-muted-foreground tabular-nums">{qty(l.quantity, l.unitCode)} por unidade · perda {qty(l.scrapPct)}%{l.notes ? ` · ${l.notes}` : ''}</span>
        </span>
        <span className="text-right tabular-nums">
          <b className="font-semibold">{qty(l.requiredQuantity, l.stockUnitCode)}</b>
          <span className="block text-xs text-muted-foreground">Necessidade da OS</span>
        </span>
      </div>
      {l.available !== null && l.available !== undefined ? (
        <details className="mt-1">
          <summary className="cursor-pointer text-xs font-semibold text-primary">Disponível {qty(l.available, l.stockUnitCode)}</summary>
          <p className="mt-1 text-xs tabular-nums text-muted-foreground">Físico {qty(l.onHand, l.stockUnitCode)} · Reservado {qty(l.reserved, l.stockUnitCode)}</p>
        </details>
      ) : null}
      {canSeeCost && l.estimatedCost !== null && l.estimatedCost !== undefined ? <p className="text-xs tabular-nums text-muted-foreground">Custo previsto {formatMoney(l.estimatedCost)}</p> : null}
    </li>
  )
}
