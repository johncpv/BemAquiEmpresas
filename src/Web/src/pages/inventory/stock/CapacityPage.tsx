import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { listProductionCapacityReferences, useGetProductionCapacity } from '@/api/generated/bemaqui'
import type { CapacityLine, ProductionCapacityView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { DataTable } from '@/components/data-table/DataTable'
import { Combobox, type ComboOption } from '@/components/form/combobox'
import { QuantityInput } from '@/components/form/inputs'
import { Money } from '@/components/ui/money'
import { StatusChip } from '@/components/ui/status-chip'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { InventoryTabs } from '../InventoryTabs'
import { CAPACITY_DICAS, parseQuantity, productOptions, purchasePrefill, referenceLabel, referenceOption, shortageSummary, unitsText } from './capacity'
import { formatQuantity } from './stock'

const header = (label: string, dica: string) => () => <span className="inline-flex items-center">{label}<Dica nome={label} texto={dica} /></span>

const text = (value: unknown) => (typeof value === 'string' && value ? value : undefined)

type LineCell = { row: { original: CapacityLine } }

/**
 * Estoque → Capacidade de produzir (EST-06): escolha o produto (a BOM sugerida é a liberada mais nova dele) ou uma OS, e a quantidade
 * desejada. Mostra quantas unidades o disponível de hoje permite, o item que limita, a falta item a item e, com Compras, a linha
 * informativa "Com os pedidos em aberto". O atalho abre a solicitação de compra já preenchida, sem gravar. Produto, OS e quantidade
 * ficam na URL. A 400 px ficam item, necessidade, disponível e falta; o resto vai para a linha expansível (padrão 8).
 */
export function CapacityPage() {
  const search = useSearch({ strict: false }) as Record<string, unknown>
  const navigate = useNavigate()
  const product = text(search.produto)
  const order = text(search.os)
  const quantity = parseQuantity(search.qtd)
  const chosen = !!product || !!order
  const query = useGetProductionCapacity({ productionOrderId: order, productId: order ? undefined : product, quantity }, { query: { enabled: chosen, retry: false } })
  const view = query.data

  const set = (patch: Record<string, string | number | undefined>) =>
    void navigate({
      to: '.', replace: true,
      search: (prev: Record<string, unknown>) => {
        const next = { ...prev, ...patch } as Record<string, unknown>
        for (const key of Object.keys(next)) if (next[key] === undefined || next[key] === '' || (key === 'qtd' && next[key] === 1)) delete next[key]
        return next
      },
    } as never)

  const searchProducts = async (term: string): Promise<ComboOption[]> => productOptions(await listProductionCapacityReferences({ q: term || undefined }))
  const searchOrders = async (term: string): Promise<ComboOption[]> =>
    (await listProductionCapacityReferences({ q: term || undefined, productId: product })).map((r) => ({ id: r.productionOrderId, label: referenceOption(r), hint: r.suggested ? 'Sugerido' : r.productCode ?? undefined }))

  const selectedProduct = product ? { id: product, label: view?.reference.productId === product ? referenceLabel(view.reference) : 'Produto escolhido' } : null
  const selectedOrder = view ? { id: view.reference.productionOrderId, label: referenceOption(view.reference) } : null

  return (
    <>
      <PageHeading title="Estoque: Capacidade de produzir">
        <p className="mt-2 text-muted-foreground">Quantos iguais a um já feito dá para produzir com o estoque de hoje, e o que falta comprar para chegar à quantidade.</p>
      </PageHeading>
      <InventoryTabs />
      <section aria-label="BOM de referência e quantidade" className="mb-5 grid gap-3 sm:grid-cols-[minmax(0,2fr)_minmax(0,2fr)_10rem]">
        <div className="min-w-0 space-y-1">
          <span className="text-sm font-medium">Produto</span>
          <Combobox aria-label="Produto" queryKey={['capacity-products']} search={searchProducts} value={product ?? null} selected={selectedProduct} placeholder="Código ou nome do produto"
            onChange={(o) => set({ produto: o?.id, os: undefined })} />
        </div>
        <div className="min-w-0 space-y-1">
          <span className="inline-flex items-center text-sm font-medium">BOM de referência<Dica nome="BOM de referência" texto="A BOM liberada de uma OS. Escolhido o produto, vem a mais nova dele; troque por outra OS se quiser comparar." /></span>
          <Combobox aria-label="BOM de referência" queryKey={['capacity-orders', product ?? '']} search={searchOrders} value={view?.reference.productionOrderId ?? order ?? null} selected={selectedOrder}
            placeholder="Número da OS" onChange={(o) => set({ os: o?.id })} />
        </div>
        <div className="min-w-0 space-y-1">
          <label htmlFor="cap-qtd" className="text-sm font-medium">Quantidade desejada</label>
          <QuantityInput id="cap-qtd" value={String(quantity)} onChange={(v) => { const n = Number(v); if (v && n > 0) set({ qtd: n }) }} />
        </div>
      </section>
      {!chosen ? (
        <EmptyState title="Escolha um produto ou uma OS para ver quantas unidades o estoque permite."
          description="A conta usa a BOM liberada da OS: a necessidade por unidade, o disponível de hoje em todos os locais e o que falta para a quantidade desejada."
          action={<Link to={'/app/engenharia/boms' as never} className="text-sm font-semibold text-primary">Ver as BOMs na Engenharia</Link>} />
      ) : query.isPending ? (
        <div className="space-y-2" aria-busy="true">{[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-10" />)}</div>
      ) : query.isError || !view ? (
        <ErrorState title="Não foi possível calcular a capacidade."
          message={query.error instanceof ApiError && query.error.status < 500 ? query.error.message : 'Verifique a conexão e tente de novo. Se continuar, fale com o suporte.'}
          onRetry={() => query.refetch()} />
      ) : (
        <CapacityResult view={view} />
      )}
    </>
  )
}

function CapacityResult({ view }: { view: ProductionCapacityView }) {
  const navigate = useNavigate()
  const canRequest = useCan('purchasing.create')
  const shortages = view.lines.filter((l) => (l.shortage ?? 0) > 0)
  const openRequest = () => void navigate({ to: '/app/compras/solicitacoes/nova' as never, state: { purchasePrefill: purchasePrefill(view) } as never })

  const columns: ColumnDef<CapacityLine>[] = [
    {
      id: 'item', header: 'Item', cell: ({ row: { original: l } }) => (
        <span className="flex flex-wrap items-center gap-x-2 gap-y-1">
          <Link to={'/app/estoque/itens/$id' as never} params={{ id: l.itemId } as never} className="font-semibold text-primary hover:underline"><span className="font-mono">{l.itemCode}</span> {l.itemName}</Link>
          {l.limiting ? <StatusChip tone="warn">Limita</StatusChip> : null}
          {l.warning ? <span className="w-full text-xs font-medium text-warn-ink">{l.warning}</span> : null}
        </span>
      ),
    },
    { id: 'perUnit', header: header('Por unidade', CAPACITY_DICAS.porUnidade), meta: { numeric: true, secondary: true }, cell: ({ row: { original: l } }) => (l.perUnit === null ? '—' : `${formatQuantity(l.perUnit)} ${l.unitCode}`) },
    { id: 'required', header: 'Necessidade', meta: { numeric: true }, cell: ({ row: { original: l } }) => (l.required === null ? '—' : `${formatQuantity(l.required)} ${l.unitCode}`) },
    { id: 'onHand', header: 'Físico', meta: { numeric: true, secondary: true }, cell: ({ row: { original: l } }) => formatQuantity(l.onHand) },
    { id: 'reserved', header: 'Reservado', meta: { numeric: true, secondary: true }, cell: ({ row: { original: l } }) => formatQuantity(l.reserved) },
    { id: 'available', header: header('Disponível', CAPACITY_DICAS.disponivel), meta: { numeric: true }, cell: ({ row: { original: l } }) => <b>{formatQuantity(l.available)}</b> },
    { id: 'possible', header: 'Possível', meta: { numeric: true, secondary: true }, cell: ({ row: { original: l } }) => (l.possible === null ? '—' : formatQuantity(l.possible)) },
    ...(view.openOrdersShown ? [{ id: 'open', header: 'Pedidos em aberto', meta: { numeric: true, secondary: true }, cell: ({ row: { original: l } }: LineCell) => formatQuantity(l.openQuantity ?? 0) }] : []),
    {
      id: 'shortage', header: 'Falta', cell: ({ row: { original: l } }) => l.shortage === null
        ? <StatusChip tone="neutral">Não calculado</StatusChip>
        : l.shortage > 0 ? <StatusChip tone="crit">Falta {formatQuantity(l.shortage)} {l.unitCode}</StatusChip> : <StatusChip tone="ok">Cobre</StatusChip>,
    },
    ...(view.costShown ? [{ id: 'cost', header: 'Valor da falta', meta: { numeric: true, secondary: true }, cell: ({ row: { original: l } }: LineCell) => (l.estimatedShortageCost === null ? '—' : <Money value={l.estimatedShortageCost} />) }] : []),
  ]

  return (
    <div className="space-y-4">
      <p className="flex flex-wrap items-center gap-2 text-sm tabular-nums">
        <span>BOM da <Link to={'/app/producao/os/$id' as never} params={{ id: view.reference.productionOrderId } as never} search={{ aba: 'bom' } as never} className="font-semibold text-primary hover:underline">{view.reference.orderNumber}</Link>, revisão {view.reference.revision}, liberada em {formatDateTime(view.reference.releasedAt)}</span>
        {view.reference.productCode ? <span className="text-muted-foreground">· {referenceLabel(view.reference)}</span> : null}
        {view.reference.suggested ? <span className="inline-flex items-center"><Tag>Sugerido</Tag><Dica nome="Sugerido" texto={CAPACITY_DICAS.sugerido} /></span> : null}
      </p>

      <section aria-label="Total possível" className="rounded-md border border-border bg-card p-4">
        {view.possible === null ? (
          <p className="text-sm">{view.lines.length === 0 ? 'Esta BOM não tem material de estoque: o estoque não limita.' : 'Nenhum item da BOM pôde ser calculado. Veja os avisos nas linhas.'}</p>
        ) : (
          <div className="flex flex-wrap items-end justify-between gap-3">
            <div className="min-w-0">
              <p className="text-sm text-muted-foreground">Dá para produzir hoje</p>
              <p className="font-display text-4xl font-bold font-wide tabular-nums">{unitsText(view.possible)}</p>
              {view.limitingItemName ? <p className="mt-1 flex flex-wrap items-center text-sm">Limita: <b className="ml-1">{view.limitingItemName}</b><Dica nome="Limita" texto={CAPACITY_DICAS.limita} /></p> : null}
              {view.openOrdersShown && view.possibleWithOpenOrders !== null ? (
                <p className="mt-1 flex items-center text-sm text-muted-foreground tabular-nums">Com os pedidos em aberto: {formatQuantity(view.possibleWithOpenOrders)}<Dica nome="Com os pedidos em aberto" texto={CAPACITY_DICAS.pedidosEmAberto} /></p>
              ) : null}
            </div>
            <div className="text-sm sm:text-right">
              <p className={shortages.length ? 'font-semibold text-crit-ink' : 'font-semibold text-ok-ink'}>{shortageSummary(view.shortageCount)}</p>
              {view.costShown && view.estimatedShortageCost !== null && view.shortageCount > 0 ? <p className="tabular-nums">Valor estimado da falta: <Money value={view.estimatedShortageCost} className="font-semibold" /> (custo médio)</p> : null}
              {view.notCalculatedCount > 0 ? <p className="text-warn-ink">{view.notCalculatedCount === 1 ? '1 item não calculado.' : `${view.notCalculatedCount} itens não calculados.`}</p> : null}
            </div>
          </div>
        )}
        {shortages.length > 0 && canRequest ? (
          <div className="mt-3 flex flex-wrap items-center gap-2">
            <button type="button" onClick={openRequest} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Gerar solicitação de compra</button>
            <span className="text-xs text-muted-foreground">Abre a solicitação preenchida com o que falta. Nada é gravado até você enviar.</span>
          </div>
        ) : null}
      </section>

      {view.lines.length > 0 ? (
        <DataTable label="Itens da BOM" columns={columns} searchable={false} getRowId={(l) => l.bomLineId}
          query={{ data: { items: view.lines, page: 1, pageSize: Math.max(view.lines.length, 1), total: view.lines.length }, isPending: false, isError: false, refetch: () => undefined }}
          rowClassName={(l) => ((l.shortage ?? 0) > 0 ? 'border-l-4 border-l-crit' : l.warning ? 'border-l-4 border-l-warn' : undefined)}
          expandable={{
            label: (l) => `capacidade de ${l.itemCode}`,
            render: (l) => (
              <p className="text-sm tabular-nums">
                Por unidade {l.perUnit === null ? '—' : `${formatQuantity(l.perUnit)} ${l.unitCode}`} · Físico {formatQuantity(l.onHand)} · Reservado {formatQuantity(l.reserved)} · Possível {l.possible === null ? '—' : formatQuantity(l.possible)}
                {view.openOrdersShown ? ` · Pedidos em aberto ${formatQuantity(l.openQuantity ?? 0)}` : ''}
                {view.costShown && l.estimatedShortageCost !== null ? <> · Valor da falta <Money value={l.estimatedShortageCost} /></> : null}
              </p>
            ),
          }}
          empty={{ title: 'Esta BOM não tem itens.' }} />
      ) : null}
    </div>
  )
}
