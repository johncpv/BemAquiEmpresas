import { Link, useNavigate } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useState } from 'react'
import { openInventoryCount, useListInventoryCounts, useListItemCategories, useListLocations } from '@/api/generated/bemaqui'
import type { InventoryCountStatus, InventoryCountSummary, ItemType } from '@/api/generated/model'
import { useIdempotencyKey } from '@/api/idempotency'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { Can } from '@/pages/ModuleGate'
import { PageHeading } from '@/shell/PageHeading'
import { InventoryTabs } from '../InventoryTabs'
import { COUNT_STATUS, INVENTORY_DICAS } from './inventory-counts'

const STATUS_OPTIONS = (Object.keys(COUNT_STATUS) as InventoryCountStatus[]).map((value) => ({ value, label: COUNT_STATUS[value].label }))
const TYPES: { value: ItemType; label: string }[] = [
  { value: 'RawMaterial', label: 'Matéria-prima' }, { value: 'Component', label: 'Componente' }, { value: 'Finished', label: 'Produto acabado' }, { value: 'Consumable', label: 'Consumível' },
]

/** "Novo inventário" (CA 1): local (o padrão sugerido) e o escopo; o padrão é "itens com saldo no local". */
export function OpenCountDialog({ onClose, locationId: initial }: { onClose: () => void; locationId?: string }) {
  const navigate = useNavigate()
  const locations = useListLocations({ pageSize: 100 })
  const categories = useListItemCategories()
  const active = (locations.data?.items ?? []).filter((l) => l.isActive)
  const [location, setLocation] = useState(initial ?? '')
  const [withMovement, setWithMovement] = useState(false)
  const [category, setCategory] = useState('')
  const [type, setType] = useState<ItemType | ''>('')
  const idempotency = useIdempotencyKey()
  const suggested = active.find((l) => l.isDefault)?.id ?? ''
  const locationId = location || suggested
  const open = async () => {
    const detail = await openInventoryCount({ locationId, scope: { includeZeroWithMovement: withMovement, categoryId: category || null, itemType: type || null } }, idempotency.options)
    idempotency.renew()
    onClose()
    await navigate({ to: '/app/estoque/inventarios/$id' as never, params: { id: detail.id } as never })
  }
  return (
    <ConfirmDialog open onOpenChange={(o) => { if (!o) onClose() }} verb="Abrir inventário" done="Inventário aberto" title="Novo inventário"
      description="O sistema tira a foto do saldo agora. Enquanto o local é contado, ajustes e estornos manuais nele ficam bloqueados; entradas e saídas continuam."
      preconditions={[{ ok: !!locationId, text: 'Local escolhido.' }]} onConfirm={open}>
      <Field label="Local" required>
        {(p) => (
          <span className="flex items-center gap-2">
            <select {...p} value={locationId} onChange={(e) => setLocation(e.target.value)} className={inputClass}>
              <option value="">Escolha</option>
              {active.map((l) => <option key={l.id} value={l.id}>{l.code} — {l.name}</option>)}
            </select>
            {!location && locationId ? <Tag>Sugerido</Tag> : null}
          </span>
        )}
      </Field>
      <fieldset className="space-y-1 text-sm">
        <legend className="font-medium">Itens</legend>
        <label className="flex min-h-[44px] items-center gap-2"><input type="radio" name="escopo" checked={!withMovement} onChange={() => setWithMovement(false)} />Itens com saldo no local</label>
        <label className="flex min-h-[44px] items-center gap-2"><input type="radio" name="escopo" checked={withMovement} onChange={() => setWithMovement(true)} />Também os zerados que já tiveram movimento</label>
      </fieldset>
      <div className="grid gap-3 sm:grid-cols-2">
        <Field label="Categoria (opcional)">
          {(p) => (
            <select {...p} value={category} onChange={(e) => setCategory(e.target.value)} className={inputClass}>
              <option value="">Todas</option>
              {(categories.data ?? []).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </select>
          )}
        </Field>
        <Field label="Tipo (opcional)">
          {(p) => (
            <select {...p} value={type} onChange={(e) => setType(e.target.value as ItemType | '')} className={inputClass}>
              <option value="">Todos</option>
              {TYPES.map((t) => <option key={t.value} value={t.value}>{t.label}</option>)}
            </select>
          )}
        </Field>
      </div>
    </ConfirmDialog>
  )
}

/**
 * Estoque → Inventários (EST-05): número, local, situação, foto, progresso e o ajuste gerado. "Novo inventário" com
 * <c>inventory.adjust</c>. A 400 px fica número, local e situação; o resto vai para a linha expansível.
 */
export function InventoryCountsPage() {
  const [search] = useTableSearch()
  const { situacao } = useTableFilters(['situacao'])
  const query = useListInventoryCounts({ status: (situacao || undefined) as InventoryCountStatus | undefined, page: search.page, pageSize: search.pageSize })
  const [open, setOpen] = useState(false)
  const start = (primary: boolean) => (
    <button type="button" onClick={() => setOpen(true)} className={primary ? 'min-h-[44px] rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground' : 'text-sm font-semibold text-primary'}>Novo inventário</button>
  )
  const columns: ColumnDef<InventoryCountSummary>[] = [
    { id: 'number', header: 'Número', cell: ({ row: { original: c } }) => <Link to={'/app/estoque/inventarios/$id' as never} params={{ id: c.id } as never} className="font-semibold text-primary hover:underline">{c.number}</Link> },
    { id: 'location', header: 'Local', cell: ({ row: { original: c } }) => c.locationName },
    { id: 'snapshot', header: 'Foto do saldo', meta: { secondary: true }, cell: ({ row: { original: c } }) => `${formatDateTime(c.snapshotAt)} · ${c.openedByName ?? '—'}` },
    { id: 'progress', header: 'Contados', meta: { numeric: true, secondary: true }, cell: ({ row: { original: c } }) => `${c.countedCount} de ${c.lineCount}` },
    { id: 'adjustment', header: 'Ajuste', meta: { secondary: true }, cell: ({ row: { original: c } }) => (c.adjustmentId ? <Link to={'/app/estoque/ajustes/$id' as never} params={{ id: c.adjustmentId } as never} className="font-semibold text-primary">{c.adjustmentNumber}</Link> : '—') },
    { id: 'status', header: 'Situação', cell: ({ row: { original: c } }) => <StatusChip tone={COUNT_STATUS[c.status].tone}>{COUNT_STATUS[c.status].label}</StatusChip> },
  ]
  return (
    <>
      <PageHeading title="Estoque: Inventários">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="inline-flex items-center text-muted-foreground">Conte um local sem ver o saldo do sistema; a diferença vira um ajuste para aprovação.<Dica nome="Contagem cega" texto={INVENTORY_DICAS.cega} /></p>
          <Can perm="inventory.adjust">{start(true)}</Can>
        </div>
      </PageHeading>
      <InventoryTabs />
      <DataTable label="Inventários" columns={columns} query={query} searchable={false} getRowId={(c) => c.id}
        filters={[{ key: 'situacao', label: 'Situação', options: STATUS_OPTIONS }]}
        rowClassName={(c) => (c.status === 'Counting' ? 'border-l-4 border-l-warn' : undefined)}
        expandable={{ label: (c) => `detalhes de ${c.number}`, render: (c) => <p className="text-sm">{c.countedCount} de {c.lineCount} contados · foto {formatDateTime(c.snapshotAt)}{c.adjustmentNumber ? ` · ajuste ${c.adjustmentNumber}` : ''}</p> }}
        empty={{ title: 'Nenhum inventário. Conte um local para acertar o saldo.', action: <Can perm="inventory.adjust">{start(false)}</Can> }} />
      {open ? <OpenCountDialog onClose={() => setOpen(false)} /> : null}
    </>
  )
}
