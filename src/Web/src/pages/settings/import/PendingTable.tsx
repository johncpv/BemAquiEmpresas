import type { ColumnDef } from '@tanstack/react-table'
import { useListImportPending } from '@/api/generated/bemaqui'
import type { ImportPendingItem, ImportRunDetail } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { StatusChip } from '@/components/ui/status-chip'
import { entityLabel, KIND, KIND_OPTIONS, targetHref } from './labels'

/** Pendências (erros, conflitos e avisos) com filtro por entidade e tipo na URL e link para o registro (CA 19). */
export function PendingTable({ run }: { run: ImportRunDetail }) {
  const [search] = useTableSearch()
  const { entidade, tipo } = useTableFilters(['entidade', 'tipo'])
  const query = useListImportPending(run.id, { entity: entidade, kind: tipo, page: search.page, pageSize: search.pageSize })

  const columns: ColumnDef<ImportPendingItem>[] = [
    { id: 'kind', header: 'Tipo', cell: ({ row: { original: p } }) => <StatusChip tone={KIND[p.kind]?.tone ?? 'neutral'}>{KIND[p.kind]?.label ?? p.kind}</StatusChip> },
    { id: 'entity', header: 'Entidade', meta: { secondary: true }, cell: ({ row: { original: p } }) => entityLabel(p.entity) },
    { id: 'source', header: 'Origem', cell: ({ row: { original: p } }) => <span className="tabular-nums">{p.sourceTable} #{p.sourceId}</span> },
    {
      id: 'reason', header: 'Motivo', cell: ({ row: { original: p } }) => (
        <span className="block space-y-1">
          {p.message ? <span className="block">{p.message}</span> : null}
          {p.warning ? <span className="block text-muted-foreground">{p.warning}</span> : null}
          {p.differences?.length ? (
            <ul aria-label="Diferenças" className="text-xs text-muted-foreground">
              {p.differences.map((d) => <li key={d.field}><b className="font-semibold">{d.field}</b>: Dolibarr "{d.source ?? '—'}" · BemAqui "{d.target ?? '—'}"</li>)}
            </ul>
          ) : null}
        </span>
      ),
    },
    {
      id: 'target', header: 'Registro', cell: ({ row: { original: p } }) => {
        const href = targetHref(p.targetType, p.targetId)
        return href ? <a href={href} className="font-semibold text-primary hover:underline">Abrir registro</a> : '—'
      },
    },
  ]

  return (
    <DataTable
      label="Pendências da importação"
      columns={columns}
      query={query}
      getRowId={(p) => p.id}
      searchable={false}
      filters={[
        { key: 'entidade', label: 'Entidade', options: run.entities.map((e) => ({ value: e.entity, label: entityLabel(e.entity) })) },
        { key: 'tipo', label: 'Tipo', options: KIND_OPTIONS },
      ]}
      empty={{ title: 'Nenhuma pendência nesta execução.', description: 'Sem erros, conflitos nem avisos: tudo o que foi lido entrou ou foi ignorado com motivo.' }}
    />
  )
}
