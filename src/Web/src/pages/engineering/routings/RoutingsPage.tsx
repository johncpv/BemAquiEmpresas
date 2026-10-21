import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useListRoutingProductTypes, useListRoutingTemplates } from '@/api/generated/bemaqui'
import type { RoutingTemplateSummary } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { StageIndicator } from '@/components/ui/stage-indicator'
import { StatusChip } from '@/components/ui/status-chip'
import { formatBrDecimal } from '@/lib/decimal'
import { Can } from '@/pages/ModuleGate'
import { PageHeading } from '@/shell/PageHeading'
import { ROUTING_SITUATIONS, situationParam } from './routings'

const newButton = 'rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground'

/** Fábrica → Engenharia → Roteiros (ENG-01): etapas com as letras, horas padrão, produtos, situação e uso; filtros na URL. */
export function RoutingsPage() {
  const [search] = useTableSearch()
  const { situacao, tipo } = useTableFilters(['situacao', 'tipo'])
  const query = useListRoutingTemplates({ q: search.q, situation: situationParam(situacao), productType: tipo || undefined, page: search.page, pageSize: search.pageSize })
  const types = useListRoutingProductTypes()
  const columns: ColumnDef<RoutingTemplateSummary>[] = [
    { id: 'name', header: 'Nome', cell: ({ row: { original: r } }) => <Link to={'/app/engenharia/roteiros/$id' as never} params={{ id: r.id } as never} className="font-semibold text-primary hover:underline">{r.name}</Link> },
    { id: 'type', header: 'Tipo de produto', meta: { secondary: true }, cell: ({ row: { original: r } }) => r.productType ?? '—' },
    {
      id: 'steps', header: 'Etapas', cell: ({ row: { original: r } }) => (r.stepNames.length === 0 ? '—' : (
        <span className="flex flex-wrap items-center gap-2">
          <StageIndicator stages={r.stepNames} letters={r.codes} current={0} />
          <span className="text-xs text-muted-foreground" aria-label={`Letras: ${r.codes.join(', ')}`}>{r.codes.join(' ')}</span>
        </span>
      )),
    },
    { id: 'hours', header: 'Horas padrão', meta: { numeric: true, secondary: true }, cell: ({ row: { original: r } }) => <span className="tabular-nums">{formatBrDecimal(r.totalStandardHours.toFixed(4), 2)} h</span> },
    { id: 'items', header: 'Produtos', meta: { numeric: true, secondary: true }, cell: ({ row: { original: r } }) => <span className="tabular-nums">{r.itemCount}</span> },
    { id: 'status', header: 'Situação', cell: ({ row: { original: r } }) => <StatusChip tone={r.isActive ? 'ok' : 'neutral'}>{r.isActive ? 'Ativo' : 'Inativo'}</StatusChip> },
    { id: 'usage', header: 'Uso', meta: { secondary: true }, cell: ({ row: { original: r } }) => (r.usage ? `Usado em ${r.usage}` : '—') },
  ]
  const create = <Can perm="engineering.create"><Link to={'/app/engenharia/roteiros/novo' as never} className={newButton}>Novo roteiro</Link></Can>
  return (
    <>
      <PageHeading title="Roteiros de fabricação">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">O caminho de cada produto na fábrica: etapas, centros e horas padrão.</p>
          {create}
        </div>
      </PageHeading>
      <DataTable label="Roteiros de fabricação" columns={columns} query={query} getRowId={(r) => r.id} searchPlaceholder="Buscar pelo nome ou tipo"
        filters={[
          { key: 'situacao', label: 'Situação', options: ROUTING_SITUATIONS },
          { key: 'tipo', label: 'Tipo de produto', options: (types.data ?? []).map((t) => ({ value: t, label: t })) },
        ]}
        empty={{ title: 'Nenhum roteiro cadastrado. Comece pelo roteiro de exemplo ou crie um novo.', action: create }} />
    </>
  )
}