import { Link, useNavigate } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useEffect, useState } from 'react'
import { useGetOrderMaterial } from '@/api/generated/bemaqui'
import type { OrderMaterialLine } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { ReservationHistory } from '@/pages/inventory/stock/ReservationHistory'
import { locationsText, RESERVATION_DICAS, shortageLabel, SLOW_AFTER_MS } from '@/pages/inventory/stock/reservations'
import { formatQuantity } from '@/pages/inventory/stock/stock'
import { useCan } from '@/pages/use-can'

const header = (label: string, dica: string) => () => <span className="inline-flex items-center">{label}<Dica nome={label} texto={dica} /></span>

/**
 * Aba Material da OS (EST-02 CA 14): por item da BOM liberada, necessidade, reservado, baixado (EST-03), falta e local. A 400 px
 * ficam item, reservado e falta; necessidade, baixado e locais vão para a linha expansível (padrão 8). Cada falta leva à solicitação
 * de compra (CMP-01). Depois da liberação, "Reservando material…" até as reservas chegarem à revisão da BOM (padrão 7).
 */
export function MaterialTab({ orderId }: { orderId: string }) {
  const navigate = useNavigate()
  const canSeeStock = useCan('inventory.view')
  const query = useGetOrderMaterial(orderId, { query: { refetchInterval: (q) => (q.state.data?.reconciling ? 3000 : false) } })
  const reconciling = query.data?.reconciling === true
  const [slow, setSlow] = useState(false)
  useEffect(() => {
    if (!reconciling) return
    const timer = setTimeout(() => setSlow(true), SLOW_AFTER_MS)
    return () => { clearTimeout(timer); setSlow(false) }
  }, [reconciling])

  if (query.isPending) return <div className="space-y-2">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-10" />)}</div>
  if (query.isError) return <ErrorState title="Não foi possível carregar o material da OS." message="Verifique a conexão e tente de novo. Se a OS foi removida, volte à lista." onRetry={() => query.refetch()} />
  const view = query.data
  if (!view.bomReleased && view.lines.length === 0) {
    return (
      <EmptyState title="Libere a BOM para reservar o material." description="Quando a BOM da OS é liberada, o estoque reserva cada item e mostra aqui o que falta comprar."
        action={<button type="button" onClick={() => void navigate({ to: '.', search: { aba: 'bom' } } as never)} className="text-sm font-semibold text-primary">Abrir BOM</button>} />
    )
  }

  if (view.lines.length === 0) {
    return <EmptyState title="Esta OS não usa material de estoque." description="A BOM liberada só tem mão de obra ou serviços. Nada a reservar." />
  }

  const columns: ColumnDef<OrderMaterialLine>[] = [
    {
      id: 'item', header: 'Item', cell: ({ row: { original: l } }) => (
        <span className="flex flex-wrap items-center gap-x-2 gap-y-1">
          <span><span className="font-mono">{l.itemCode}</span> {l.itemName}</span>
          {l.isBackfill ? <span className="inline-flex items-center"><Tag>Calculado na implantação</Tag><Dica nome="Calculado na implantação" texto={RESERVATION_DICAS.backfill} /></span> : null}
          {l.overIssued > 0 ? <span className="inline-flex items-center"><StatusChip tone="warn">Baixado acima da BOM: {formatQuantity(l.overIssued)}</StatusChip><Dica nome="Baixado acima da BOM" texto={RESERVATION_DICAS.baixadoAcima} /></span> : null}
        </span>
      ),
    },
    { id: 'required', header: 'Necessidade', meta: { numeric: true, secondary: true }, cell: ({ row: { original: l } }) => `${formatQuantity(l.required)} ${l.unitCode}` },
    { id: 'reserved', header: header('Reservado', RESERVATION_DICAS.reservado), meta: { numeric: true }, cell: ({ row: { original: l } }) => <b>{formatQuantity(l.reserved)}</b> },
    { id: 'issued', header: 'Baixado', meta: { numeric: true, secondary: true }, cell: ({ row: { original: l } }) => formatQuantity(l.issued) },
    {
      id: 'shortage', header: header('Falta', RESERVATION_DICAS.falta), cell: ({ row: { original: l } }) => {
        const label = shortageLabel(l.shortage, l.purchaseRequestLink)
        if (!label) return <StatusChip tone="ok">Reservado</StatusChip>
        return (
          <span className="flex flex-wrap items-center gap-2">
            <StatusChip tone="crit">{label}</StatusChip>
            {l.purchaseRequestLink
              ? <a href={l.purchaseRequestLink} className="text-sm font-semibold text-primary">Ver solicitação</a>
              : <Link to={'/app/compras' as never} className="text-sm font-semibold text-primary">Gerar solicitação de compra</Link>}
          </span>
        )
      },
    },
    { id: 'locations', header: 'Local', meta: { secondary: true }, cell: ({ row: { original: l } }) => <span className="tabular-nums">{locationsText(l)}</span> },
  ]

  return (
    <div className="space-y-4">
      {reconciling ? (
        <div role="status" aria-live="polite" className="rounded-md border border-info-soft bg-info-soft px-3 py-2 text-sm text-info-ink">
          <p className="font-semibold">Reservando material…</p>
          <p>{slow ? 'Está demorando mais que o normal. A reserva continua sozinha: você pode sair desta tela e voltar depois.' : 'O estoque está separando o material da BOM liberada. Esta tela se atualiza sozinha.'}</p>
        </div>
      ) : null}
      {view.orderClosed ? <p role="note" className="rounded-md border border-border bg-muted px-3 py-2 text-sm">A OS foi concluída ou cancelada: as reservas foram liberadas.</p> : null}
      <div className="flex flex-wrap items-center justify-between gap-2 text-sm">
        <p className="tabular-nums">
          {view.bomRevision ? <>BOM revisão {view.bomRevision}{view.bomReleasedAt ? ` liberada em ${formatDateTime(view.bomReleasedAt)}` : ''}. </> : null}
          {view.totalShortage > 0 ? <b className="text-crit-ink">{view.lines.filter((l) => l.shortage > 0).length} item(ns) com falta.</b> : <span className="text-ok-ink">Todo o material está reservado.</span>}
        </p>
        {canSeeStock && view.totalShortage > 0 ? <Link to={'/app/estoque/faltas' as never} className="font-semibold text-primary">Ver todas as faltas</Link> : null}
      </div>
      <DataTable label="Material da OS" columns={columns} searchable={false} getRowId={(l) => l.itemId}
        query={{ data: { items: view.lines, page: 1, pageSize: Math.max(view.lines.length, 1), total: view.lines.length }, isPending: false, isError: false, refetch: () => query.refetch() }}
        rowClassName={(l) => (l.shortage > 0 ? 'border-l-4 border-l-crit' : undefined)}
        expandable={{
          label: (l) => `material ${l.itemCode}`,
          render: (l) => <p className="text-sm tabular-nums">Necessidade {formatQuantity(l.required)} {l.unitCode} · Baixado {formatQuantity(l.issued)} · Local {locationsText(l)}</p>,
        }}
        empty={{ title: 'Nenhum material nesta OS.' }} />
      <section aria-labelledby="historico-reserva" className="space-y-2">
        <h2 id="historico-reserva" className="text-sm font-semibold uppercase tracking-wide text-muted-foreground">Histórico da reserva</h2>
        <ReservationHistory orderId={orderId} />
      </section>
    </div>
  )
}
