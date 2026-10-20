import { Link } from '@tanstack/react-router'
import { useState } from 'react'
import { useGetDiscountsAndCancellations } from '@/api/generated/bemaqui'
import { Field } from '@/components/form/form'
import { DateInput } from '@/components/form/inputs'
import { Money } from '@/components/ui/money'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDate, formatDateTime } from '@/lib/format'
import { PageHeading } from '@/shell/PageHeading'
import { today } from './receivables'

const firstOfMonth = () => today().slice(0, 8) + '01'

/** Descontos e cancelamentos do período (FIN-01 CA 22): quem, quando, quanto e se passou do limite. */
export function DiscountsReportPage() {
  const [from, setFrom] = useState(firstOfMonth())
  const [to, setTo] = useState(today())
  const query = useGetDiscountsAndCancellations({ from: from || undefined, to: to || undefined })
  return (
    <>
      <PageHeading title="Descontos e cancelamentos">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">O que deixou de entrar no caixa no período, e quem decidiu.</p>
          <Link to="/app/financeiro/receber" className="text-sm font-semibold text-primary">Voltar a A receber</Link>
        </div>
      </PageHeading>
      <div className="mb-4 grid max-w-md grid-cols-2 gap-3">
        <Field label="De">{(p) => <DateInput {...p} value={from} onChange={setFrom} />}</Field>
        <Field label="Até">{(p) => <DateInput {...p} value={to} onChange={setTo} />}</Field>
      </div>
      {query.isPending ? <Skeleton className="h-64" /> : query.isError ? <ErrorState onRetry={() => query.refetch()} /> : (
        <div className="space-y-6">
          <section aria-labelledby="descontos" className="space-y-2">
            <h2 id="descontos" className="text-base font-semibold">Descontos <span className="font-normal text-muted-foreground">· total <Money value={query.data.totalDiscount} /></span></h2>
            {query.data.discounts.length === 0 ? <EmptyState title="Nenhum desconto no período." description="Bom sinal: tudo o que venceu entrou inteiro." action={<Link to="/app/financeiro/receber" className="text-sm font-semibold text-primary">Ver a receber</Link>} /> : (
              <div className="overflow-x-auto rounded-md border border-border bg-card">
                <table className="w-full text-sm" aria-label="Descontos do período">
                  <thead className="border-b border-border text-xs text-muted-foreground"><tr><th className="px-3 py-2 text-left">Título</th><th className="px-3 py-2 text-left">Cliente</th><th className="px-3 py-2 text-left">Data</th><th className="px-3 py-2 text-right">Desconto</th><th className="hidden px-3 py-2 text-right sm:table-cell">% do saldo</th><th className="px-3 py-2 text-left">Limite</th></tr></thead>
                  <tbody>
                    {query.data.discounts.map((d) => (
                      <tr key={d.receivableId + d.at} className="border-b border-border last:border-0">
                        <td className="px-3 py-2"><Link to="/app/financeiro/receber/$id" params={{ id: d.receivableId }} className="font-mono font-semibold text-primary">{d.number}</Link></td>
                        <td className="px-3 py-2">{d.partyName}</td>
                        <td className="px-3 py-2 tabular-nums">{formatDate(d.date)}</td>
                        <td className="px-3 py-2 text-right"><Money value={d.discount} /></td>
                        <td className="hidden px-3 py-2 text-right tabular-nums sm:table-cell">{d.pct.toLocaleString('pt-BR', { minimumFractionDigits: 2 })}%</td>
                        <td className="px-3 py-2">{d.aboveLimit ? <StatusChip tone="warn">Acima do limite</StatusChip> : <StatusChip tone="ok">Dentro</StatusChip>}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>
          <section aria-labelledby="cancelamentos" className="space-y-2">
            <h2 id="cancelamentos" className="text-base font-semibold">Cancelamentos <span className="font-normal text-muted-foreground">· total <Money value={query.data.totalCancelled} /></span></h2>
            {query.data.cancellations.length === 0 ? <EmptyState title="Nenhum título cancelado no período." /> : (
              <div className="overflow-x-auto rounded-md border border-border bg-card">
                <table className="w-full text-sm" aria-label="Cancelamentos do período">
                  <thead className="border-b border-border text-xs text-muted-foreground"><tr><th className="px-3 py-2 text-left">Título</th><th className="px-3 py-2 text-left">Cliente</th><th className="px-3 py-2 text-right">Saldo cancelado</th><th className="px-3 py-2 text-left">Motivo</th><th className="hidden px-3 py-2 text-left sm:table-cell">Quando</th></tr></thead>
                  <tbody>
                    {query.data.cancellations.map((c) => (
                      <tr key={c.receivableId} className="border-b border-border last:border-0">
                        <td className="px-3 py-2"><Link to={c.isPayable ? '/app/financeiro/pagar/$id' : '/app/financeiro/receber/$id'} params={{ id: c.receivableId }} className="font-mono font-semibold text-primary">{c.number}</Link></td>
                        <td className="px-3 py-2">{c.partyName}</td>
                        <td className="px-3 py-2 text-right"><Money value={c.balance} /></td>
                        <td className="px-3 py-2">{c.reason}</td>
                        <td className="hidden px-3 py-2 tabular-nums sm:table-cell">{formatDateTime(c.at)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        </div>
      )}
    </>
  )
}
