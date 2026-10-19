import { useQueryClient } from '@tanstack/react-query'
import type { ColumnDef } from '@tanstack/react-table'
import { useState } from 'react'
import { getListLocationsQueryKey, useListLocations } from '@/api/generated/bemaqui'
import type { LocationDetail } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { ImportedTag } from '@/components/ui/imported-tag'
import { StatusChip } from '@/components/ui/status-chip'
import { Can } from '@/pages/ModuleGate'
import { PageHeading } from '@/shell/PageHeading'
import { InventoryTabs } from './InventoryTabs'
import { LocationPanel } from './LocationPanel'

/** Estoque → Locais (CAD-03 CA 2 a 7): código, nome, padrão e situação; edição no painel lateral. */
export function LocationsPage() {
  const [search] = useTableSearch()
  const { inativos } = useTableFilters([])
  const client = useQueryClient()
  const query = useListLocations({ q: search.q, includeInactive: inativos === 'true', page: search.page, pageSize: search.pageSize })
  const [editing, setEditing] = useState<{ location: LocationDetail | null } | null>(null)

  const columns: ColumnDef<LocationDetail>[] = [
    { id: 'code', header: 'Código', cell: ({ row: { original: l } }) => <button type="button" onClick={() => setEditing({ location: l })} className="font-mono font-semibold text-primary hover:underline">{l.code}</button> },
    { id: 'name', header: 'Nome', cell: ({ row: { original: l } }) => <span className="flex flex-wrap items-center gap-1.5">{l.name}<ImportedTag source={l.importSource} /></span> },
    {
      id: 'status', header: 'Situação', cell: ({ row: { original: l } }) => (
        <span className="flex flex-wrap gap-1">
          {l.isDefault ? <StatusChip tone="info">Padrão</StatusChip> : null}
          <StatusChip tone={l.isActive ? 'ok' : 'neutral'}>{l.isActive ? 'Ativo' : 'Inativo'}</StatusChip>
        </span>
      ),
    },
  ]

  return (
    <>
      <PageHeading title="Estoque: Locais">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">Onde o material fica guardado.</p>
          <Can perm="inventory.create">
            <button type="button" onClick={() => setEditing({ location: null })} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Novo local</button>
          </Can>
        </div>
      </PageHeading>
      <InventoryTabs />
      <DataTable label="Locais de estoque" columns={columns} query={query} getRowId={(l) => l.id} inactiveFilter searchPlaceholder="Buscar pelo código ou nome"
        empty={{
          title: 'Cadastre os locais onde o material fica guardado.',
          description: 'O Almoxarifado principal (ALM) já vem pronto e é o padrão dos movimentos.',
          action: <Can perm="inventory.create"><button type="button" onClick={() => setEditing({ location: null })} className="text-sm font-semibold text-primary">Novo local</button></Can>,
        }} />
      {editing ? (
        <LocationPanel location={editing.location} onClose={() => setEditing(null)}
          onSaved={() => { void client.invalidateQueries({ queryKey: getListLocationsQueryKey() }); setEditing(null) }} />
      ) : null}
    </>
  )
}
