import { Link, useSearch } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useGetAging } from '@/api/generated/bemaqui'
import type { AgingCustomer } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { Money } from '@/components/ui/money'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { Dica } from '@/help/Dica'
import { PageHeading } from '@/shell/PageHeading'
import { FinanceTabs } from '../FinanceTabs'

const DICAS = {
  aging: 'Os títulos vencidos separados pelo tempo de atraso. Quanto mais à direita, mais difícil de receber.',
  atraso: 'Quantos dias se passaram desde o vencimento do título mais antigo do cliente.',
}
const TONES = ['border-warn-soft bg-warn-soft text-warn-ink', 'border-warn-soft bg-warn-soft text-warn-ink', 'border-crit-soft bg-crit-soft text-crit-ink', 'border-crit-soft bg-crit-soft text-crit-ink']
const LABELS = ['0–30 dias', '31–60 dias', '61–90 dias', 'Mais de 90 dias']

const titlesOf = (partyId: string) => `/app/financeiro/receber?situacao=vencido&cliente=${partyId}`

/** Financeiro → Inadimplência (FIN-06a CA 1 e 2): o que está vencido, por faixa de atraso e por cliente. */
export function AgingPage() {
  const search = useSearch({ strict: false }) as { sort?: string }
  const query = useGetAging({ sort: search.sort })
  const columns: ColumnDef<AgingCustomer>[] = [
    { id: 'name', header: 'Cliente', meta: { sortField: 'name' }, cell: ({ row: { original: r } }) => <Link to={titlesOf(r.partyId) as never} className="font-semibold text-primary hover:underline">{r.partyName}</Link> },
    ...LABELS.map((label, i): ColumnDef<AgingCustomer> => ({ id: `b${i}`, header: label, meta: { numeric: true, secondary: true }, cell: ({ row: { original: r } }) => <Money value={r.buckets[i]} /> })),
    { id: 'total', header: 'Total vencido', meta: { numeric: true, sortField: 'total' }, cell: ({ row: { original: r } }) => <Money value={r.total} className="font-semibold" /> },
    { id: 'maxDaysLate', header: 'Maior atraso', meta: { numeric: true, sortField: 'maxDaysLate' }, cell: ({ row: { original: r } }) => <span className="tabular-nums">{r.maxDaysLate} {r.maxDaysLate === 1 ? 'dia' : 'dias'}</span> },
  ]

  let summary = null
  if (query.isPending) summary = <Skeleton className="h-24" />
  else if (query.isError) summary = <ErrorState title="Não foi possível carregar a inadimplência." onRetry={() => query.refetch()} />
  else if (query.data.count > 0) {
    const a = query.data
    summary = (
      <section aria-label="Faixas de atraso" className="space-y-3">
        <p className="text-sm">
          <b><Money value={a.total} /></b> vencidos em <b className="tabular-nums">{a.count}</b> {a.count === 1 ? 'título' : 'títulos'} de <b className="tabular-nums">{a.customers}</b> {a.customers === 1 ? 'cliente' : 'clientes'}.
        </p>
        <div className="grid gap-3 sm:grid-cols-4">
          {a.buckets.map((b, i) => (
            <p key={b.label} className={`rounded-md border p-3 text-sm ${TONES[i]}`}>
              <span className="block text-xs font-semibold">{LABELS[i]}</span>
              <Money value={b.total} className="text-lg font-semibold" />
              <span className="block text-xs">{b.count} {b.count === 1 ? 'título' : 'títulos'}</span>
            </p>
          ))}
        </div>
      </section>
    )
  }

  const rows = query.data?.rows ?? []
  return (
    <>
      <PageHeading title="Financeiro: Inadimplência">
        <p className="mt-2 flex flex-wrap items-center text-muted-foreground">Quem está devendo e há quanto tempo, para cobrar primeiro quem pesa mais.<Dica nome="Aging" texto={DICAS.aging} /></p>
      </PageHeading>
      <FinanceTabs />
      <div className="space-y-4">
        {summary}
        <div>
          <p className="mb-2 flex items-center text-xs text-muted-foreground">Por cliente, do maior saldo vencido para o menor. Dias de atraso<Dica nome="Dias de atraso" texto={DICAS.atraso} /></p>
          <DataTable
            label="Inadimplência por cliente"
            columns={columns}
            searchable={false}
            getRowId={(r) => r.partyId}
            query={{ data: query.data ? { items: rows, page: 1, pageSize: Math.max(rows.length, 1), total: rows.length } : undefined, isPending: query.isPending, isError: query.isError, error: query.error, refetch: query.refetch }}
            empty={{ title: 'Nenhum título vencido. Bom sinal.', action: <Link to={'/app/financeiro/receber' as never} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Ver a receber</Link> }}
            totals={query.data ? { total: <Money value={query.data.total} className="font-semibold" /> } : undefined}
            expandable={{ label: (r) => `faixas de ${r.partyName}`, render: (r) => (
              <dl className="grid grid-cols-2 gap-2 text-sm sm:hidden">{LABELS.map((l, i) => <div key={l}><dt className="text-xs text-muted-foreground">{l}</dt><dd><Money value={r.buckets[i]} /></dd></div>)}</dl>
            ) }}
          />
        </div>
      </div>
    </>
  )
}
