import { Link } from '@tanstack/react-router'
import { useGetOrderSerials } from '@/api/generated/bemaqui'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { FABRICATION_DICAS, FINISHED_GOODS, quantity, SERIAL_STATUS } from './fabrication'

/**
 * Aba Séries da OS (PRD-06a2 CA 11, padrão 10): a entrada do produto acabado no estoque (o movimento, com o link para quem vê o Estoque),
 * cada série com a situação e o local, e os lotes de matéria-prima consumidos, que cada série herda. Sem custo (CA 14): o custo apurado
 * fica na aba Custos. A 400 px, as séries ficam em lista com rolagem.
 */
export function SerialsTab({ orderId, completed, completedWithoutStock }: { orderId: string; completed: boolean; completedWithoutStock: boolean }) {
  const canSeeStock = useCan('inventory.view')
  const query = useGetOrderSerials(orderId)
  if (query.isPending) return <div className="space-y-2">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-10" />)}</div>
  if (query.isError) return <ErrorState title="Não foi possível carregar as séries da OS." message="Verifique a conexão e tente de novo." onRetry={() => query.refetch()} />
  const v = query.data
  if (completedWithoutStock) {
    return <EmptyState title={FINISHED_GOODS.antesDoEstoque} description={FABRICATION_DICAS.antesDoEstoque} />
  }

  if (!v.entryMovementId) {
    return (
      <EmptyState title={completed ? 'A OS não tem entrada no estoque.' : 'O produto entra no estoque na conclusão da OS.'}
        description={v.tracksSerial ? 'As séries de cada unidade nascem quando a OS é concluída.' : `${FINISHED_GOODS.semSerie} A entrada é por quantidade.`} />
    )
  }

  const unit = v.unitCode ? ` ${v.unitCode}` : ''
  return (
    <div className="mt-3 space-y-4">
      <p className="text-sm tabular-nums">
        {v.entryReversed ? 'Entrada estornada: ' : 'Entrada no estoque: '}
        {canSeeStock ? <Link to="/app/estoque/movimentos/$id" params={{ id: v.entryMovementId }} className="font-semibold text-primary underline">{v.entryMovementNumber}</Link> : <b>{v.entryMovementNumber}</b>}
        {' · '}{quantity(v.entryQuantity ?? 0)}{unit} de {v.productCode} — {v.productName}{v.entryLocationName ? ` em ${v.entryLocationName}` : ''}
        {v.entryPostedAt ? ` · ${formatDateTime(v.entryPostedAt)}` : ''}
      </p>
      <section aria-label="Séries" className="space-y-2">
        <h3 className="flex items-center text-sm font-semibold">Número de série<Dica nome="Número de série" texto={FABRICATION_DICAS.serie} /></h3>
        {!v.tracksSerial ? <p className="text-sm text-muted-foreground">{FINISHED_GOODS.semSerie}</p> : v.serials.length === 0 ? (
          <p className="text-sm text-muted-foreground">Nenhuma série gravada.</p>
        ) : (
          <ul className="max-h-80 divide-y divide-border overflow-y-auto rounded-md border border-border">
            {v.serials.map((s) => (
              <li key={s.id} className="flex flex-wrap items-center justify-between gap-2 px-3 py-2 text-sm">
                <span className="font-semibold tabular-nums">{s.serialNumber}</span>
                <span className="flex items-center gap-2">
                  <StatusChip tone={SERIAL_STATUS[s.status]?.tone ?? 'neutral'}>{SERIAL_STATUS[s.status]?.label ?? s.status}</StatusChip>
                  <span className="text-muted-foreground">{s.locationName ?? '—'}</span>
                </span>
              </li>
            ))}
          </ul>
        )}
      </section>
      <section aria-label="Lotes consumidos" className="space-y-2">
        <h3 className="text-sm font-semibold">Lotes de matéria-prima consumidos</h3>
        {v.consumedLots.length === 0 ? <p className="text-sm text-muted-foreground">Nenhum material foi baixado para esta OS.</p> : (
          <ul className="space-y-1 text-sm tabular-nums">
            {v.consumedLots.map((l) => (
              <li key={`${l.itemId}-${l.lotId ?? 'sem-lote'}`}>{l.itemCode} — {l.itemName}: {quantity(l.quantity)} {l.unitCode}{l.lotNumber ? ` · lote ${l.lotNumber}` : ' · sem lote'}</li>
            ))}
          </ul>
        )}
        <p className="text-xs text-muted-foreground">Cada série herda os lotes da OS inteira.</p>
      </section>
    </div>
  )
}
