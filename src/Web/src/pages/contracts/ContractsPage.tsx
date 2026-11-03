import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { getExportContractsUrl, useListContracts } from '@/api/generated/bemaqui'
import type { ContractSummary } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { ExportMenu } from '@/components/ui/export-menu'
import { Money } from '@/components/ui/money'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDate } from '@/lib/format'
import { PageHeading } from '@/shell/PageHeading'
import { CONTRACT_STATUS } from './contracts'

const SITUATIONS = [{ value: 'ativo', label: 'Ativos' }, { value: 'concluido', label: 'Concluídos' }, { value: 'cancelado', label: 'Cancelados' }]

/** Comercial → Contratos (COM-04a): nascem da aprovação do orçamento; só leitura. */
export function ContractsPage() {
  const [search] = useTableSearch()
  const { situacao } = useTableFilters(['situacao'])
  const filters = { q: search.q, situation: situacao || undefined }
  const query = useListContracts({ ...filters, page: search.page, pageSize: search.pageSize })
  const columns: ColumnDef<ContractSummary>[] = [
    { id: 'number', header: 'Contrato', cell: ({ row: { original: c } }) => <Link to={'/app/contratos/$id' as never} params={{ id: c.id } as never} className="font-semibold text-primary hover:underline">{c.number}</Link> },
    { id: 'party', header: 'Cliente', cell: ({ row: { original: c } }) => <span>{c.partyName}{c.projectNumber ? <span className="block text-xs text-muted-foreground">{c.projectNumber}</span> : null}</span> },
    { id: 'signed', header: 'Assinatura', meta: { secondary: true }, cell: ({ row: { original: c } }) => <span className="tabular-nums">{formatDate(c.signedAt)}</span> },
    { id: 'status', header: 'Situação', cell: ({ row: { original: c } }) => <StatusChip tone={CONTRACT_STATUS[c.status]?.tone ?? 'neutral'}>{CONTRACT_STATUS[c.status]?.label ?? c.status}</StatusChip> },
    { id: 'value', header: 'Valor', meta: { numeric: true }, cell: ({ row: { original: c } }) => <Money value={c.totalValue} /> },
  ]
  const goQuotes = <Link to={'/app/orcamentos' as never} search={{ situacao: 'enviado' } as never} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Ver orçamentos enviados</Link>
  return (
    <>
      <PageHeading title="Comercial: Contratos" description="Cada contrato nasce da aprovação de um orçamento, com a cópia congelada, o projeto e as parcelas a receber." />
      <DataTable label="Contratos" columns={columns} query={query} getRowId={(c) => c.id} searchPlaceholder="Buscar pelo número ou cliente"
        filters={[{ key: 'situacao', label: 'Situação', options: SITUATIONS }]}
        toolbar={<ExportMenu formats={['xlsx']} fileBase="contratos" empty={query.data?.total === 0} url={(format) => getExportContractsUrl({ ...filters, format })} />}
        empty={{ title: 'Nenhum contrato ainda. Aprove um orçamento enviado para gerar o primeiro.', action: goQuotes }} />
    </>
  )
}
