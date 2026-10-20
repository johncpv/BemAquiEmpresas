import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { getGetBankBalancesQueryKey, getGetBankStatementQueryKey, useGetBankStatement } from '@/api/generated/bemaqui'
import { DateInput } from '@/components/form/inputs'
import { Money } from '@/components/ui/money'
import { NextSteps } from '@/components/ui/next-steps'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { today } from '../receivables/receivables'
import { FinanceTabs } from '../FinanceTabs'
import { EntryDialog } from './EntryDialog'
import { StatementTable } from './StatementTable'
import { TransferDialog } from './TransferDialog'

const monthStart = (iso: string) => iso.slice(0, 8) + '01'
const monthEnd = (iso: string) => { const [y, m] = iso.split('-').map(Number); return new Date(Date.UTC(y, m, 0)).toISOString().slice(0, 10) }

/** Extrato da conta (FIN-04 CA 1 a 3, 12 e 17): período na URL, saldo de partida, acumulado e movimentos sem OP. */
export function StatementPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const search = useSearch({ strict: false }) as { de?: string; ate?: string; semOp?: string; page?: number }
  const navigate = useNavigate()
  const client = useQueryClient()
  const canPay = useCan('finance.pay')
  const from = search.de ?? monthStart(today())
  const to = search.ate ?? monthEnd(from)
  const page = Number(search.page ?? 1)
  const query = useGetBankStatement(id, { from, to, withoutOrder: search.semOp === '1' || undefined, page, pageSize: 50 })
  const [next, setNext] = useState<{ label: string; onSelect: () => void }[]>([])
  const set = (patch: Record<string, unknown>) => void navigate({ to: '.', search: (p: Record<string, unknown>) => ({ ...p, ...patch }), replace: true } as never)
  const refresh = async () => {
    await client.invalidateQueries({ queryKey: getGetBankStatementQueryKey(id) })
    await client.invalidateQueries({ queryKey: getGetBankBalancesQueryKey() })
  }

  const previousMonth = () => { const d = new Date(Date.parse(from + 'T00:00:00Z') - 86_400_000).toISOString().slice(0, 10); set({ de: monthStart(d), ate: monthEnd(d), page: undefined }) }
  let body
  if (query.isPending) body = <Skeleton className="h-64" />
  else if (query.isError) body = <ErrorState title="Não foi possível carregar o extrato." onRetry={() => query.refetch()} />
  else {
    const s = query.data
    const pages = Math.max(1, Math.ceil(s.lines.total / s.lines.pageSize))
    body = (
      <div className="space-y-3">
        {s.openingMissing ? <p className="flex flex-wrap items-center gap-2 text-sm"><Tag>Saldo inicial não informado</Tag>{s.hasMovement ? null : <Link to={'/app/financeiro/implantacao' as never} search={{ aba: 'saldos' } as never} className="font-semibold text-primary hover:underline">Informar saldo inicial</Link>}</p> : null}
        {s.lines.total === 0
          ? <EmptyState title="Nenhuma movimentação no período." action={<button type="button" onClick={previousMonth} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Ver mês anterior</button>} />
          : <StatementTable lines={s.lines.items} startLabel={s.startLabel} startBalance={s.startBalance} canPay={canPay} onChanged={refresh} />}
        <div className="flex flex-wrap items-center justify-between gap-2 text-sm">
          <p>Saldo final do período: <Money value={s.endBalance} highlightNegative className="font-semibold" /></p>
          {pages > 1 ? (
            <span className="flex items-center gap-2">
              <button type="button" disabled={page <= 1} onClick={() => set({ page: page - 1 })} className="rounded-md border border-border px-2 py-1 disabled:opacity-40">Anterior</button>
              <span className="tabular-nums">Página {page} de {pages}</span>
              <button type="button" disabled={page >= pages} onClick={() => set({ page: page + 1 })} className="rounded-md border border-border px-2 py-1 disabled:opacity-40">Próxima</button>
            </span>
          ) : null}
        </div>
      </div>
    )
  }

  const transferred = async (toId: string) => {
    await refresh()
    setNext([{ label: 'Ver extrato do destino', onSelect: () => void navigate({ to: '/app/financeiro/contas/$id/extrato', params: { id: toId } } as never) }])
  }

  return (
    <>
      <PageHeading title={`Extrato: ${query.data?.accountName ?? 'conta'}`}>
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">Tudo o que entrou e saiu desta conta, com o saldo dia a dia.</p>
          {canPay ? <span className="flex flex-wrap gap-2"><TransferDialog fromId={id} onDone={transferred} /><EntryDialog accountId={id} onDone={refresh} /></span> : null}
        </div>
      </PageHeading>
      <FinanceTabs />
      {next.length ? <div className="mb-3"><NextSteps onDismiss={() => setNext([])} steps={next} /></div> : null}
      <div className="mb-3 flex flex-wrap items-end gap-3">
        <label className="space-y-1 text-sm font-medium">De<DateInput value={from} onChange={(v) => { if (v) set({ de: v, page: undefined }) }} /></label>
        <label className="space-y-1 text-sm font-medium">Até<DateInput value={to} onChange={(v) => { if (v) set({ ate: v, page: undefined }) }} /></label>
        <label className="flex items-center gap-2 pb-2 text-sm">
          <input type="checkbox" checked={search.semOp === '1'} onChange={(e) => set({ semOp: e.target.checked ? '1' : undefined, page: undefined })} className="size-4 accent-primary" />
          Só movimentos sem OP<Dica nome="Sem OP" texto="Tarifas, IOF, juros e rendimentos lançados direto na conta, sem ordem de pagamento. O Proprietário é avisado de cada um." />
        </label>
      </div>
      {body}
    </>
  )
}
