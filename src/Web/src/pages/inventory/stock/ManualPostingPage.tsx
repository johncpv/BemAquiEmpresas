import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from '@tanstack/react-router'
import { useState } from 'react'
import { approveManualPosting, cancelManualPosting, getGetManualPostingQueryKey, rejectManualPosting, useGetManualPosting } from '@/api/generated/bemaqui'
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
import { formatDate, formatDateTime } from '@/lib/format'
import { PageHeading } from '@/shell/PageHeading'
import { InventoryTabs } from '../InventoryTabs'
import { DICAS, formatQuantity, formatUnitCost, KIND, POSTING_STATUS } from './stock'

const button = (text: string, primary = false) => <button type="button" className={primary ? 'rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground' : 'rounded-md border border-border px-3 py-2 text-sm font-semibold'}>{text}</button>

/**
 * Lançamento manual (CA 9 a 11): o que foi enviado, sem edição; "Aprovar lançamento" com o impacto calculado agora, "Reprovar
 * lançamento" com motivo e "Cancelar lançamento" pelo autor. Quem lançou não aprova (salvo o Proprietário único, "Autoaprovado").
 */
export function ManualPostingPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const query = useGetManualPosting(id)
  const client = useQueryClient()
  const navigate = useNavigate()
  const [comment, setComment] = useState('')
  const [steps, setSteps] = useState<Step[]>([])
  const idempotency = useIdempotencyKey()
  if (query.isPending) return <Skeleton className="h-96" />
  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return <ErrorState title={notFound ? 'Lançamento não encontrado.' : 'Não foi possível carregar o lançamento.'} onRetry={notFound ? undefined : () => query.refetch()} />
  }

  const p = query.data
  const refresh = async () => { await client.invalidateQueries({ queryKey: getGetManualPostingQueryKey(id) }) }
  const pending = p.status === 'Pending'
  const impact = p.impact
  const decided = () => setSteps([{ label: 'Próximo aguardando você', onSelect: () => void navigate({ to: '/app/estoque/lancamentos' as never, search: { filtro: 'aguardando-voce' } as never }) }])

  return (
    <>
      <PageHeading title={`${p.number} — ${KIND[p.kind]}`}>
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
            <StatusChip tone={POSTING_STATUS[p.status].tone}>{POSTING_STATUS[p.status].label}</StatusChip>
            {p.approvalReason ? <span className="inline-flex items-center"><Tag>{p.approvalReason}</Tag><Dica nome="Aguardando aprovação" texto={DICAS.aprovacao} /></span> : null}
            {p.selfApproved ? <Tag>Autoaprovado</Tag> : null}
            <Link to={'/app/estoque/lancamentos' as never} className="font-semibold text-primary">Voltar aos lançamentos</Link>
          </p>
          <div className="flex flex-wrap gap-2">
            {pending && p.canDecide ? (
              <>
                <ConfirmDialog size="lg" verb="Aprovar lançamento" done="Lançamento aprovado" title={`Aprovar o ${p.number}?`} description="Você decide sobre o que foi enviado: para mudar algo, reprove com o motivo."
                  preconditions={impact?.insufficient ? [{ ok: false, text: `Saldo suficiente no local (há ${formatQuantity(impact.onHandBefore)}).` }] : []}
                  impact={impact ? (
                    <div className="space-y-1">
                      <p>Saldo no local {formatQuantity(impact.onHandBefore)} → <b>{formatQuantity(impact.onHandAfter)}</b></p>
                      <p>Custo médio {formatUnitCost(impact.averageBefore)} → <b>{formatUnitCost(impact.averageAfter)}</b></p>
                      <p>Valor: <Money value={impact.value} className="font-semibold" /></p>
                    </div>
                  ) : undefined}
                  onConfirm={async () => { await approveManualPosting(id, { comment: comment.trim() || null, version: p.version }, idempotency.options); idempotency.renew(); setComment(''); await refresh(); decided() }}
                  onReload={refresh} trigger={button('Aprovar lançamento', true)}>
                  <label htmlFor="comentario-aprovar" className="text-sm font-medium">Comentário (opcional)</label>
                  <textarea id="comentario-aprovar" value={comment} maxLength={500} rows={2} onChange={(e) => setComment(e.target.value)} className={inputClass} />
                </ConfirmDialog>
                <ConfirmDialog verb="Reprovar lançamento" done="Lançamento reprovado" title={`Reprovar o ${p.number}?`} description="Nada muda no saldo e quem lançou recebe o motivo."
                  preconditions={[{ ok: comment.trim().length > 0, text: 'Motivo da reprovação escrito.' }]}
                  onConfirm={async () => { await rejectManualPosting(id, { comment: comment.trim(), version: p.version }); setComment(''); await refresh(); decided() }} onReload={refresh} trigger={button('Reprovar lançamento')}>
                  <label htmlFor="comentario-reprovar" className="text-sm font-medium">Motivo da reprovação</label>
                  <textarea id="comentario-reprovar" value={comment} maxLength={500} rows={3} onChange={(e) => setComment(e.target.value)} className={inputClass} />
                </ConfirmDialog>
              </>
            ) : null}
            {p.canCancel ? (
              <ConfirmDialog verb="Cancelar lançamento" done="Lançamento cancelado" title={`Cancelar o ${p.number}?`} description="O lançamento sai da fila de aprovação e nada muda no saldo."
                onConfirm={async () => { await cancelManualPosting(id, { version: p.version }); await refresh() }} onReload={refresh} trigger={button('Cancelar lançamento')} />
            ) : null}
          </div>
        </div>
      </PageHeading>
      <InventoryTabs />
      {pending && p.decideBlockedReason ? <p role="note" className="mb-3 rounded-md border border-border bg-muted px-3 py-2 text-sm">{p.decideBlockedReason}</p> : null}
      {steps.length ? <div className="mb-3"><NextSteps steps={steps} onDismiss={() => setSteps([])} /></div> : null}
      <FactsBar label="Fatos do lançamento" facts={[
        { label: 'Valor', value: <Money value={p.estimatedValue} /> },
        { label: 'Quem lançou', value: `${p.requestedByName ?? '—'} · ${formatDateTime(p.requestedAt)}` },
        { label: 'Decisão', value: p.decidedAt ? `${p.decidedByName ?? '—'} · ${formatDateTime(p.decidedAt)}` : '—' },
        { label: p.cutoffDate ? 'Data de corte' : 'Projeto', value: p.cutoffDate ? formatDate(p.cutoffDate) : (p.projectNumber ?? '—') },
      ]} />
      {p.reason ? <p className="mb-2 text-sm"><span className="text-muted-foreground">Motivo:</span> {p.reason}</p> : null}
      {p.decisionComment ? <p className="mb-2 text-sm"><span className="text-muted-foreground">Comentário da decisão:</span> {p.decisionComment}</p> : null}
      <UrlTabs label="Seções do lançamento" fallback="linhas" tabs={[
        {
          id: 'linhas', label: 'Linhas', content: (
            <ul className="mt-3 divide-y divide-border rounded-md border border-border text-sm" aria-label="Linhas do lançamento">
              {p.lines.map((l) => (
                <li key={l.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
                  <span><span className="font-mono">{l.itemCode}</span> {l.itemName}{l.lotNumber ? ` · lote ${l.lotNumber}` : ''}</span>
                  <span className="tabular-nums">{formatQuantity(l.quantity)} {l.unitCode}{l.enteredUnitCode && l.enteredUnitCode !== l.unitCode ? ` (${formatQuantity(l.enteredQuantity)} ${l.enteredUnitCode})` : ''}</span>
                  <span>{l.locationName}{l.toLocationName ? ` → ${l.toLocationName}` : ''}</span>
                  {l.unitCost !== null ? <span className="tabular-nums">{formatUnitCost(l.unitCost)}</span> : null}
                  {l.movementId ? <Link to={'/app/estoque/movimentos/$id' as never} params={{ id: l.movementId } as never} className="font-semibold text-primary">Ver movimento</Link> : null}
                  {l.reversedAt ? <StatusChip tone="neutral">Estornado em {formatDateTime(l.reversedAt)}{l.reversedByName ? ` por ${l.reversedByName}` : ''}</StatusChip> : null}
                </li>
              ))}
            </ul>
          ),
        },
        { id: 'historico', label: 'Histórico de alterações', content: <RecordHistory entity="ManualPosting" id={p.id} /> },
      ]} />
    </>
  )
}
