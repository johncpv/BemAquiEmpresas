import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useState } from 'react'
import { getListStockAdjustmentsQueryKey, useListStockAdjustments } from '@/api/generated/bemaqui'
import type { StockAdjustmentDetail, StockAdjustmentMode, StockAdjustmentStatus, StockAdjustmentSummary } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { Money } from '@/components/ui/money'
import { NextSteps, type Step } from '@/components/ui/next-steps'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { Can } from '@/pages/ModuleGate'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { InventoryTabs } from '../InventoryTabs'
import { AdjustmentDialog } from './AdjustmentDialog'
import { ADJUSTMENT_DICAS, ADJUSTMENT_MODE, ADJUSTMENT_STATUS, itemsLabel } from './adjustments'

const STATUS_OPTIONS = (Object.keys(ADJUSTMENT_STATUS) as StockAdjustmentStatus[]).map((value) => ({ value, label: ADJUSTMENT_STATUS[value].label }))
const MODE_OPTIONS = (Object.keys(ADJUSTMENT_MODE) as StockAdjustmentMode[]).map((value) => ({ value, label: ADJUSTMENT_MODE[value] }))
const SHOW_LABELS: Record<string, string> = { 'aguardando-voce': 'Aguardando você', todos: 'Todos os ajustes' }

/** Situação do ajuste com o estorno à vista (o estornado continua aprovado, com a marca). */
export function AdjustmentStatusChip({ status, reversed }: { status: StockAdjustmentStatus; reversed: boolean }) {
  return reversed ? <StatusChip tone="neutral">Estornado</StatusChip> : <StatusChip tone={ADJUSTMENT_STATUS[status].tone}>{ADJUSTMENT_STATUS[status].label}</StatusChip>
}

/**
 * Estoque → Ajustes (CA 15, padrão 5): para quem aprova, o filtro padrão é "Aguardando você" (pendentes de outras pessoas); quem lança
 * vê todos. Número, local, modo, autor, data, situação, itens e valor da diferença; a 400 px fica número, local, valor e situação e o
 * resto vai para a linha expansível.
 */
export function AdjustmentsPage() {
  const [search] = useTableSearch()
  const client = useQueryClient()
  const navigate = useNavigate()
  const canApprove = useCan('inventory.approve')
  const { filtro, situacao, modo } = useTableFilters(['filtro', 'situacao', 'modo'])
  const show = filtro ?? (canApprove ? 'aguardando-voce' : 'todos')
  const awaiting = show === 'aguardando-voce'
  const query = useListStockAdjustments({
    awaitingMe: awaiting || undefined, status: awaiting ? undefined : (situacao || undefined) as StockAdjustmentStatus | undefined, mode: (modo || undefined) as StockAdjustmentMode | undefined,
    page: search.page, pageSize: search.pageSize,
  })
  const [open, setOpen] = useState(false)
  const [steps, setSteps] = useState<Step[]>([])
  const sent = async (detail: StockAdjustmentDetail) => {
    setOpen(false)
    await client.invalidateQueries({ queryKey: getListStockAdjustmentsQueryKey() })
    setSteps([
      { label: `Ver ${detail.number}`, onSelect: () => void navigate({ to: '/app/estoque/ajustes/$id' as never, params: { id: detail.id } as never }) },
      { label: 'Lançar outro ajuste', onSelect: () => setOpen(true) },
    ])
  }

  const launch = (primary: boolean) => (
    <button type="button" onClick={() => setOpen(true)} className={primary ? 'rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground' : 'text-sm font-semibold text-primary'}>Lançar ajuste</button>
  )
  const columns: ColumnDef<StockAdjustmentSummary>[] = [
    { id: 'number', header: 'Número', cell: ({ row: { original: a } }) => <Link to={'/app/estoque/ajustes/$id' as never} params={{ id: a.id } as never} className="font-semibold text-primary hover:underline">{a.number}</Link> },
    { id: 'location', header: 'Local', cell: ({ row: { original: a } }) => a.locationName },
    { id: 'mode', header: 'Modo', meta: { secondary: true }, cell: ({ row: { original: a } }) => <span>{ADJUSTMENT_MODE[a.mode]}{a.origin === 'InventoryCount' ? <span className="text-muted-foreground"> · inventário</span> : null}</span> },
    { id: 'by', header: 'Quem lançou', meta: { secondary: true }, cell: ({ row: { original: a } }) => `${a.requestedByName ?? '—'} · ${formatDateTime(a.requestedAt)}` },
    { id: 'items', header: 'Itens', meta: { numeric: true, secondary: true }, cell: ({ row: { original: a } }) => a.itemCount },
    { id: 'value', header: 'Diferença', meta: { numeric: true }, cell: ({ row: { original: a } }) => <Money value={a.differenceValue} /> },
    { id: 'status', header: 'Situação', cell: ({ row: { original: a } }) => <span className="inline-flex flex-wrap items-center gap-1"><AdjustmentStatusChip status={a.status} reversed={a.reversed} />{a.selfApproved ? <Tag>Autoaprovado</Tag> : null}</span> },
  ]
  return (
    <>
      <PageHeading title="Estoque: Ajustes">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="inline-flex items-center text-muted-foreground">Sobras e faltas sem documento de origem, sempre com a aprovação de outra pessoa.<Dica nome="Aprovação do ajuste" texto={ADJUSTMENT_DICAS.aprovacao} /></p>
          <Can perm="inventory.adjust">{launch(true)}</Can>
        </div>
      </PageHeading>
      <InventoryTabs />
      {steps.length ? <div className="mb-3"><NextSteps steps={steps} onDismiss={() => setSteps([])} /></div> : null}
      <p className="mb-2 text-sm text-muted-foreground">Mostrando: {SHOW_LABELS[show] ?? SHOW_LABELS.todos}</p>
      <DataTable label="Ajustes de estoque" columns={columns} query={query} searchable={false} getRowId={(a) => a.id}
        filters={[
          { key: 'filtro', label: 'Mostrar', options: [{ value: 'aguardando-voce', label: 'Aguardando você' }, { value: 'todos', label: 'Todos os ajustes' }] },
          { key: 'situacao', label: 'Situação', options: STATUS_OPTIONS },
          { key: 'modo', label: 'Modo', options: MODE_OPTIONS },
        ]}
        rowClassName={(a) => (a.awaitingMe ? 'border-l-4 border-l-warn' : undefined)}
        expandable={{ label: (a) => `detalhes de ${a.number}`, render: (a) => <p className="text-sm">{ADJUSTMENT_MODE[a.mode]} · {itemsLabel(a.itemCount)} · {a.requestedByName ?? '—'} · {formatDateTime(a.requestedAt)}</p> }}
        empty={awaiting
          ? { title: 'Nada aguardando sua aprovação.', action: <Link to={'/app/estoque/ajustes' as never} search={{ filtro: 'todos' } as never} className="text-sm font-semibold text-primary">Ver todos os ajustes</Link> }
          : { title: 'Nenhum ajuste lançado.', action: <Can perm="inventory.adjust">{launch(false)}</Can> }} />
      {open ? <AdjustmentDialog onClose={() => setOpen(false)} onDone={(d) => void sent(d)} /> : null}
    </>
  )
}
