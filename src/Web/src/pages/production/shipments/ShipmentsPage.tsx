import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { useListReadyToShip, useListShipments } from '@/api/generated/bemaqui'
import type { ReadyToShipProject } from '@/api/generated/model'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { formatDate, formatDateTime } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { ProductionTabs } from '../ProductionTabs'
import { quantity, SHIPMENT_DICAS, SHIPMENT_SITUATIONS, SHIPMENT_STATUS, SHIPMENT_TEXTS } from './shipments'

const action = 'inline-flex min-h-9 items-center rounded-md border border-border px-3 text-sm font-semibold text-primary'
const primary = 'inline-flex min-h-9 items-center rounded-md bg-primary px-3 text-sm font-semibold text-primary-foreground'
const pill = 'rounded-full border px-3 py-1 text-sm font-semibold aria-pressed:border-primary aria-pressed:bg-primary-soft'

/**
 * Expedição (/app/producao/expedicao, PRD-06b CA 13): "Prontos para expedir" (projetos com produto acabado no estoque, com físico,
 * reservado e disponível) e os romaneios com número, projeto, cliente, itens, data e situação. Quem tem inventory.approve abre em
 * "Aguardando você" (estoque geral a confirmar). A 400 px, só o disponível; o resto na linha expansível.
 */
export function ShipmentsPage() {
  const navigate = useNavigate()
  const canStock = useCan('inventory.approve')
  const canCreate = useCan('production.create')
  const search = useSearch({ strict: false }) as { filtro?: string; situacao?: string; pagina?: number }
  const filter = search.filtro ?? (canStock ? 'aguardando' : 'todas')
  const page = Number(search.pagina) > 0 ? Number(search.pagina) : 1
  const ready = useListReadyToShip()
  const list = useListShipments({ filter: filter === 'aguardando' ? 'aguardando' : undefined, situation: search.situacao || undefined, page, pageSize: 25 })
  const go = (next: Partial<{ filtro: string; situacao: string; pagina: number }>) =>
    void navigate({ to: '/app/producao/expedicao', search: (prev: Record<string, unknown>) => ({ ...prev, pagina: undefined, ...next }) } as never)

  let readyBody
  if (ready.isPending) readyBody = <Skeleton className="h-32" />
  else if (ready.isError) readyBody = <ErrorState title="Não foi possível carregar o que está pronto para expedir." onRetry={() => ready.refetch()} />
  else if (ready.data.length === 0) {
    readyBody = (
      <EmptyState title={SHIPMENT_TEXTS.vazio} description="Quando a OS é concluída com o produto no estoque, o projeto aparece aqui com as séries e as quantidades."
        action={<Link to={'/app/producao/os' as never} search={{ situacao: 'pronta' } as never} className={action}>Ver OS prontas para concluir</Link>} />
    )
  } else {
    readyBody = <ul className="space-y-2" aria-label="Prontos para expedir">{ready.data.map((p) => <ReadyProject key={p.projectId} project={p} canCreate={canCreate} />)}</ul>
  }

  let listBody
  if (list.isPending) listBody = <Skeleton className="h-40" />
  else if (list.isError) listBody = <ErrorState title="Não foi possível carregar os romaneios." onRetry={() => list.refetch()} />
  else if (list.data.items.length === 0) {
    listBody = (
      <EmptyState title={filter === 'aguardando' ? 'Nenhum romaneio aguardando você.' : 'Nenhum romaneio ainda.'}
        description={filter === 'aguardando' ? 'Os romaneios com produto do estoque geral aparecem aqui para você confirmar.' : 'Monte o romaneio pelo projeto pronto para expedir, acima.'}
        action={filter === 'aguardando' ? <button type="button" className={action} onClick={() => go({ filtro: 'todas' })}>Ver todos</button> : undefined} />
    )
  } else {
    const pages = Math.max(1, Math.ceil(list.data.total / list.data.pageSize))
    listBody = (
      <>
        <div className="overflow-x-auto rounded-md border border-border bg-card">
          <table className="w-full text-sm">
            <caption className="sr-only">Romaneios</caption>
            <thead className="border-b border-border text-left text-xs uppercase text-muted-foreground">
              <tr><th className="p-2">Número</th><th className="p-2">Projeto</th><th className="p-2 max-sm:hidden">Cliente</th><th className="p-2 text-right max-sm:hidden">Itens</th><th className="p-2 max-sm:hidden">Data</th><th className="p-2">Situação</th></tr>
            </thead>
            <tbody className="divide-y divide-border">
              {list.data.items.map((r) => (
                <tr key={r.id} className="align-top">
                  <td className="p-2">
                    <Link to={'/app/producao/expedicao/$id' as never} params={{ id: r.id } as never} className="font-semibold text-primary tabular-nums hover:underline">{r.number}</Link>
                    {r.hasGeneralStock ? <span className="ml-2"><Tag>Estoque geral</Tag></span> : null}
                  </td>
                  <td className="p-2 tabular-nums">{r.projectNumber}</td>
                  <td className="p-2 max-sm:hidden">{r.partyName}</td>
                  <td className="p-2 text-right tabular-nums max-sm:hidden">{r.lines} · {quantity(r.units)} un.</td>
                  <td className="p-2 tabular-nums max-sm:hidden">{r.shippedAt ? formatDateTime(r.shippedAt) : r.plannedDate ? `Prevista ${formatDate(r.plannedDate)}` : '—'}</td>
                  <td className="p-2"><StatusChip tone={SHIPMENT_STATUS[r.status].tone}>{SHIPMENT_STATUS[r.status].label}</StatusChip></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {pages > 1 ? (
          <div className="mt-3 flex items-center gap-2 text-sm">
            <button type="button" disabled={page <= 1} onClick={() => go({ pagina: page - 1 })} className="rounded-md border border-border px-3 py-1.5 font-semibold disabled:opacity-50">Anterior</button>
            <span className="tabular-nums">{page} de {pages}</span>
            <button type="button" disabled={page >= pages} onClick={() => go({ pagina: page + 1 })} className="rounded-md border border-border px-3 py-1.5 font-semibold disabled:opacity-50">Próxima</button>
          </div>
        ) : null}
      </>
    )
  }

  return (
    <>
      <PageHeading title="Produção: Expedição" />
      <ProductionTabs />
      <section aria-labelledby="prontos" className="mb-6">
        <h2 id="prontos" className="mb-2 text-base font-semibold">Prontos para expedir</h2>
        {readyBody}
      </section>
      <section aria-labelledby="romaneios">
        <h2 id="romaneios" className="mb-2 flex items-center text-base font-semibold">Romaneios<Dica nome="Romaneio" texto={SHIPMENT_DICAS.romaneio} /></h2>
        <div className="mb-3 flex flex-wrap items-center gap-2">
          {canStock ? (
            <div role="group" aria-label="De quem" className="flex flex-wrap gap-2">
              <button type="button" className={pill} aria-pressed={filter === 'aguardando'} onClick={() => go({ filtro: 'aguardando' })}>Aguardando você{list.data ? ` (${list.data.awaitingMine})` : ''}</button>
              <button type="button" className={pill} aria-pressed={filter !== 'aguardando'} onClick={() => go({ filtro: 'todas' })}>Todos</button>
            </div>
          ) : null}
          <label className="flex items-center gap-2 text-sm">
            <span className="font-medium">Situação</span>
            <select value={search.situacao ?? ''} onChange={(e) => go({ situacao: e.target.value || undefined })} className="min-h-9 rounded-md border border-input bg-card px-2">
              <option value="">Todas</option>
              {SHIPMENT_SITUATIONS.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
            </select>
          </label>
        </div>
        {listBody}
      </section>
    </>
  )
}

/** Projeto pronto para expedir: o disponível na linha; físico, reservado e as séries na parte expansível (400 px). */
function ReadyProject({ project: p, canCreate }: { project: ReadyToShipProject; canCreate: boolean }) {
  return (
    <li className="rounded-md border border-border bg-card p-3 text-sm">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span>
          <Link to={'/app/projetos/$id' as never} params={{ id: p.projectId } as never} search={{ aba: 'itens' } as never} className="font-semibold text-primary tabular-nums hover:underline">{p.projectNumber}</Link>
          <span className="text-muted-foreground"> · {p.partyName}</span>
        </span>
        <span className="flex items-center gap-2">
          <span className="inline-flex items-center tabular-nums">Disponível {quantity(p.shippable)}<Dica nome="Disponível" texto={SHIPMENT_DICAS.disponivel} /></span>
          {canCreate ? <Link to={'/app/producao/expedicao/novo' as never} search={{ projeto: p.projectId } as never} className={primary}>Novo romaneio</Link> : null}
        </span>
      </div>
      <details className="mt-2">
        <summary className="cursor-pointer text-xs font-semibold text-muted-foreground">Itens, físico e reservado</summary>
        <ul className="mt-2 space-y-1">
          {p.lines.map((l) => (
            <li key={`${l.itemId}-${l.locationId}-${l.lotId ?? ''}`} className="rounded-md border border-border p-2">
              <b className="font-semibold">{l.itemCode}</b> {l.itemName} <span className="text-muted-foreground">· {l.locationName}{l.lotNumber ? ` · lote ${l.lotNumber}` : ''}</span>
              <span className="block tabular-nums text-xs text-muted-foreground">
                Físico {quantity(l.onHand)} · reservado {quantity(l.reserved)} · disponível {quantity(l.shippable)}{l.inOpenShipments > 0 ? ` · ${quantity(l.inOpenShipments)} em romaneio aberto` : ''}
              </span>
              {l.serials.length > 0 ? <span className="block text-xs tabular-nums">Séries: {l.serials.map((s) => s.serialNumber).join(', ')}</span> : null}
            </li>
          ))}
        </ul>
      </details>
    </li>
  )
}
