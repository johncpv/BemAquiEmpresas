import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useListPaymentTerms } from '@/api/generated/bemaqui'
import type { PaymentTermDetail } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { StatusChip } from '@/components/ui/status-chip'
import { Can } from '@/pages/ModuleGate'
import { PageHeading } from '@/shell/PageHeading'
import { FinanceTabs } from './FinanceTabs'
import { describeDays } from './terms'

/** Financeiro → Condições de pagamento (CAD-05b CA 11 a 17): as do seed já vêm prontas; inativas só no filtro. */
export function PaymentTermsPage() {
  const [search] = useTableSearch()
  const { inativos } = useTableFilters([])
  const query = useListPaymentTerms({ includeInactive: inativos === 'true', page: search.page, pageSize: search.pageSize })

  const columns: ColumnDef<PaymentTermDetail>[] = [
    { id: 'name', header: 'Nome', cell: ({ row: { original: t } }) => t.name },
    { id: 'days', header: 'Parcelas', cell: ({ row: { original: t } }) => `${t.installments.length} · ${describeDays(t.installments)}` },
    { id: 'status', header: 'Situação', cell: ({ row: { original: t } }) => <StatusChip tone={t.isActive ? 'ok' : 'neutral'}>{t.isActive ? 'Ativa' : 'Inativa'}</StatusChip> },
  ]

  return (
    <>
      <PageHeading title="Financeiro: Condições de pagamento">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">Como cada título se divide em parcelas. À vista, 30, 30/60 e 30/60/90 dias já vêm prontas.</p>
          <Can perm="finance.create">
            <Link to="/app/financeiro/condicoes-de-pagamento/nova" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Nova condição de pagamento</Link>
          </Can>
        </div>
      </PageHeading>
      <FinanceTabs />
      <DataTable label="Condições de pagamento" columns={columns} query={query} getRowId={(t) => t.id} rowHref={(t) => `/app/financeiro/condicoes-de-pagamento/${t.id}`} inactiveFilter searchable={false}
        empty={{
          title: 'Cadastre a primeira condição de pagamento.',
          description: 'Diga em quantas parcelas o valor se divide e em quantos dias vence cada uma.',
          action: <Can perm="finance.create"><Link to="/app/financeiro/condicoes-de-pagamento/nova" className="text-sm font-semibold text-primary">Nova condição de pagamento</Link></Can>,
        }} />
    </>
  )
}
