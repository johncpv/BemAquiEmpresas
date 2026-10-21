import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useGetAgingByProject } from '@/api/generated/bemaqui'
import type { AgingProject } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { Money } from '@/components/ui/money'

const LABELS = ['0–30 dias', '31–60 dias', '61–90 dias', 'Mais de 90 dias']

/** Inadimplência por projeto (FIN-06b CA 1): vencido por faixa; títulos sem projeto na linha "Sem projeto". */
export function AgingProjectsTab() {
  const query = useGetAgingByProject()
  const rows = query.data?.rows ?? []
  const columns: ColumnDef<AgingProject>[] = [
    { id: 'project', header: 'Projeto', cell: ({ row: { original: r } }) => (r.projectId
      ? <span><Link to={'/app/projetos/$id' as never} params={{ id: r.projectId } as never} search={{ aba: 'financeiro' } as never} className="font-semibold text-primary hover:underline">{r.projectNumber}</Link><span className="block text-xs text-muted-foreground">{r.projectName}</span></span>
      : <span className="font-semibold">Sem projeto</span>) },
    { id: 'party', header: 'Cliente', meta: { secondary: true }, cell: ({ row: { original: r } }) => r.partyName ?? '—' },
    ...LABELS.map((label, i): ColumnDef<AgingProject> => ({ id: `b${i}`, header: label, meta: { numeric: true, secondary: true }, cell: ({ row: { original: r } }) => <Money value={r.buckets[i]} /> })),
    { id: 'total', header: 'Total vencido', meta: { numeric: true }, cell: ({ row: { original: r } }) => <Money value={r.total} className="font-semibold" /> },
    { id: 'late', header: 'Maior atraso', meta: { numeric: true, secondary: true }, cell: ({ row: { original: r } }) => <span className="tabular-nums">{r.maxDaysLate} {r.maxDaysLate === 1 ? 'dia' : 'dias'}</span> },
  ]
  return (
    <DataTable label="Inadimplência por projeto" columns={columns} searchable={false} getRowId={(r) => r.projectId ?? 'sem-projeto'}
      query={{ data: query.data ? { items: rows, page: 1, pageSize: Math.max(rows.length, 1), total: rows.length } : undefined, isPending: query.isPending, isError: query.isError, error: query.error, refetch: query.refetch }}
      empty={{ title: 'Nenhum título vencido. Bom sinal.' }}
      totals={query.data ? { total: <Money value={query.data.total} className="font-semibold" /> } : undefined} />
  )
}
