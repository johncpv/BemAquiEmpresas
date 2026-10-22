import { useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from '@tanstack/react-router'
import { useState } from 'react'
import { getGetStockMovementQueryKey, reverseStockMovement, useGetStockMovement } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { FactsBar } from '@/components/ui/facts-bar'
import { Money } from '@/components/ui/money'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { PageHeading } from '@/shell/PageHeading'
import { InventoryTabs } from '../InventoryTabs'
import { DICAS, formatQuantity, formatUnitCost, MOVEMENT_TYPE } from './stock'

/**
 * Movimento (CA 20 a 23): o que entrou ou saiu, por qual custo, o saldo e o custo médio depois e a origem. "Estornar movimento"
 * só no lançamento manual, pelas regras da D7; o documento de outra origem é estornado por ele.
 */
export function MovementPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const query = useGetStockMovement(id)
  const client = useQueryClient()
  const [reason, setReason] = useState('')
  const idempotency = useIdempotencyKey()
  if (query.isPending) return <Skeleton className="h-96" />
  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return <ErrorState title={notFound ? 'Movimento não encontrado.' : 'Não foi possível carregar o movimento.'} message={notFound ? 'Confira o número em Movimentos.' : undefined} onRetry={notFound ? undefined : () => query.refetch()} />
  }

  const detail = query.data
  const m = detail.movement
  const refresh = async () => { await client.invalidateQueries({ queryKey: getGetStockMovementQueryKey(id) }) }
  const canReverse = detail.reverseRule === 'Author' || detail.reverseRule === 'Approver' || detail.reverseRule === 'SoleOwner'
  const after = detail.onHandNow - m.quantity

  return (
    <>
      <PageHeading title={`${m.number} — ${MOVEMENT_TYPE[m.type]}`}>
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
            <span><span className="font-mono">{m.itemCode}</span> {m.itemName}</span>
            {m.reversesMovementId ? <Tag>Estorno de {detail.reversesNumber}</Tag> : null}
            {m.reversedByMovementId ? <StatusChip tone="neutral">Estornado em {formatDateTime(m.reversedAt!)} por {detail.reversedByName ?? 'usuário removido'}</StatusChip> : null}
            {detail.reversalSelfApproved ? <Tag>Autoaprovado</Tag> : null}
            <Link to="/app/estoque/movimentos" className="font-semibold text-primary">Voltar aos movimentos</Link>
          </p>
          {canReverse ? (
            <ConfirmDialog destructive size="lg" verb="Estornar movimento" done="Movimento estornado" title={`Estornar o ${m.number}?`}
              description={detail.reverseRule === 'Author' ? 'Você lançou este movimento: o estorno vale até R$ 1.000,00 e 24 h depois do lançamento.' : 'O estorno lança o movimento inverso pelo custo do original.'}
              preconditions={[{ ok: reason.trim().length > 0, text: 'Motivo do estorno escrito.' }]}
              impact={<div className="space-y-1"><p>Saldo no local {formatQuantity(detail.onHandNow)} → <b>{formatQuantity(after)}</b> {m.unitCode}</p><p>Valor estornado: <Money value={Math.abs(m.totalCost)} className="font-semibold" /></p>{after < 0 ? <p className="text-xs text-crit-ink">O saldo do local não cobre o estorno: o sistema vai recusar.</p> : null}</div>}
              onConfirm={async () => { await reverseStockMovement(id, { reason: reason.trim() }, idempotency.options); idempotency.renew(); setReason(''); await refresh() }} onReload={refresh}
              trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold text-crit-ink">Estornar movimento</button>}>
              <div className="space-y-1">
                <label htmlFor="motivo-estorno" className="inline-flex items-center text-sm font-medium">Motivo<Dica nome="Estornar" texto={DICAS.estornar} /></label>
                <textarea id="motivo-estorno" value={reason} maxLength={200} rows={3} onChange={(e) => setReason(e.target.value)} className={inputClass} />
              </div>
            </ConfirmDialog>
          ) : null}
        </div>
      </PageHeading>
      <InventoryTabs />
      {!canReverse && detail.reverseMessage ? (
        <p role="note" className="mb-3 rounded-md border border-border bg-muted px-3 py-2 text-sm">
          {detail.reverseMessage}
          {detail.reverseRule === 'NotManual' && m.sourceLink ? <> <Link to={m.sourceLink as never} className="font-semibold text-primary">Abrir a origem</Link></> : null}
        </p>
      ) : null}
      <FactsBar label="Fatos do movimento" facts={[
        { label: 'Data', value: formatDateTime(m.at) },
        { label: 'Local', value: m.locationName },
        { label: 'Quantidade', value: `${formatQuantity(m.quantity, true)} ${m.unitCode}` },
        { label: 'Custo unitário', value: formatUnitCost(m.unitCost) },
        { label: 'Total', value: <Money value={m.totalCost} /> },
        { label: 'Saldo depois', value: `${formatQuantity(m.balanceAfter)} ${m.unitCode}` },
        { label: 'Custo médio depois', value: formatUnitCost(m.averageCostAfter) },
      ]} />
      <dl className="mt-4 grid gap-3 text-sm sm:grid-cols-2">
        <div><dt className="text-muted-foreground">Lote</dt><dd>{m.lotNumber ?? '—'}</dd></div>
        <div><dt className="text-muted-foreground">Origem</dt><dd>{m.sourceLink ? <Link to={m.sourceLink as never} className="text-primary hover:underline">{m.sourceNumber ?? 'Lançamento'}</Link> : (m.sourceNumber ?? m.sourceType)}</dd></div>
        <div><dt className="text-muted-foreground">Projeto</dt><dd>{detail.projectNumber ?? '—'}</dd></div>
        <div><dt className="text-muted-foreground">Quem lançou</dt><dd>{m.userName ?? 'Sistema'} em {formatDateTime(m.postedAt)}</dd></div>
        <div className="sm:col-span-2"><dt className="text-muted-foreground">Motivo</dt><dd>{detail.reason ?? '—'}</dd></div>
        {detail.reversalReason ? <div className="sm:col-span-2"><dt className="text-muted-foreground">Motivo do estorno</dt><dd>{detail.reversalReason}</dd></div> : null}
      </dl>
    </>
  )
}
