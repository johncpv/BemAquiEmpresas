import { useState } from 'react'
import { useGetPayrollReviewSensitive } from '@/api/generated/bemaqui'
import type { PayrollReviewEmployee, PayrollRunSummary } from '@/api/generated/model'
import { Money } from '@/components/ui/money'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { formatInstantDate } from '@/lib/format'
import { formatPct, KIND_LABELS, periodLabel } from './payroll'

/**
 * Aba Conferência (RH-02a CA 11, padrão 3 das Fases 4 a 8): total por rubrica com a lista dos holerites que o compõem (o mesmo total),
 * totais da folha, variação do líquido contra o mês anterior com o chip de alerta, "Sem lançamento", "Novos na folha" e "Conta alterada".
 */
export function ReviewTab({ run }: { run: PayrollRunSummary }) {
  const query = useGetPayrollReviewSensitive(run.id)
  const [open, setOpen] = useState<string | null>(null)
  if (query.isPending) return <Skeleton className="h-64" />
  if (query.isError || !query.data) return <ErrorState title="Não foi possível carregar a conferência." onRetry={() => query.refetch()} />
  const r = query.data
  const alerts = r.variations.filter((v) => v.alert)
  return (
    <div className="max-w-3xl space-y-5">
      <dl className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        {[['Holerites', String(r.payslipCount)], ['Proventos', r.gross], ['Descontos', r.deductions], ['Líquido', r.net]].map(([label, value]) => (
          <div key={label as string} className="rounded-md border border-border bg-card px-3 py-2">
            <dt className="text-xs text-muted-foreground">{label}</dt>
            <dd className="text-lg font-semibold tabular-nums">{typeof value === 'number' ? <Money value={value} /> : value}</dd>
          </div>
        ))}
      </dl>

      <section aria-labelledby="por-rubrica" className="space-y-2">
        <h2 id="por-rubrica" className="text-sm font-semibold">Totais por rubrica</h2>
        {r.byItem.length === 0 ? <p className="text-sm text-muted-foreground">Nenhum lançamento ainda.</p> : (
          <ul className="divide-y divide-border rounded-md border border-border text-sm">
            {r.byItem.map((i) => (
              <li key={i.payrollItemId}>
                <button type="button" aria-expanded={open === i.payrollItemId} onClick={() => setOpen(open === i.payrollItemId ? null : i.payrollItemId)} className="flex min-h-11 w-full flex-wrap items-center justify-between gap-2 px-3 py-2 text-left">
                  <span><b>{i.code}</b> · {i.name} <span className="text-muted-foreground">({KIND_LABELS[i.kind]}, {i.payslips.length} {i.payslips.length === 1 ? 'holerite' : 'holerites'})</span></span>
                  <Money value={i.total} className="font-semibold" />
                </button>
                {open === i.payrollItemId ? (
                  <ul aria-label={`Holerites com ${i.name}`} className="space-y-1 bg-muted px-5 py-2">
                    {i.payslips.map((p) => <li key={p.payslipId} className="flex justify-between gap-2"><span>{p.employeeName}</span><Money value={p.amount} /></li>)}
                    <li className="flex justify-between gap-2 border-t border-border pt-1 font-semibold"><span>Total</span><Money value={i.payslips.reduce((s, p) => s + p.amount, 0)} /></li>
                  </ul>
                ) : null}
              </li>
            ))}
          </ul>
        )}
      </section>

      <section aria-labelledby="variacao" className="space-y-2">
        <h2 id="variacao" className="text-sm font-semibold">Variação do líquido {r.previousPeriod ? `contra ${periodLabel(r.previousPeriod)}` : ''}</h2>
        <p className="text-sm text-muted-foreground">{r.previousPeriod ? `Alerta acima de ${formatPct(r.alertPct).replace('+', '')} (Configurações → Parâmetros → RH). ${alerts.length === 1 ? '1 funcionário com alerta.' : `${alerts.length} funcionários com alerta.`}` : 'Sem folha do mesmo tipo no mês anterior para comparar.'}</p>
        <ul className="divide-y divide-border rounded-md border border-border text-sm">
          {r.variations.map((v) => (
            <li key={v.payslipId} className="flex flex-wrap items-center justify-between gap-2 px-3 py-2">
              <span>{v.employeeName}</span>
              <span className="flex flex-wrap items-center gap-2 tabular-nums">
                {v.previousNet != null ? <span className="text-muted-foreground">antes <Money value={v.previousNet} /></span> : null}
                <Money value={v.net} className="font-semibold" />
                {v.variationPct != null ? <StatusChip tone={v.alert ? 'warn' : 'neutral'}>{v.alert ? 'Alerta ' : ''}{formatPct(v.variationPct)}</StatusChip> : null}
              </span>
            </li>
          ))}
        </ul>
      </section>

      <div className="grid gap-4 sm:grid-cols-3">
        <People title="Sem lançamento" empty="Todos têm lançamento." people={r.empty} />
        <People title="Novos na folha" empty="Ninguém na primeira folha." people={r.newcomers} />
        <People title="Conta alterada" empty="Nenhuma conta em quarentena." people={r.accountChanged} quarantine />
      </div>
    </div>
  )
}

function People({ title, empty, people, quarantine }: { title: string; empty: string; people: PayrollReviewEmployee[]; quarantine?: boolean }) {
  return (
    <section aria-label={title} className="rounded-md border border-border p-3 text-sm">
      <h2 className="mb-1 flex items-center gap-2 font-semibold">{title}{people.length > 0 ? <StatusChip tone={quarantine ? 'warn' : 'info'}>{people.length}</StatusChip> : null}</h2>
      {people.length === 0 ? <p className="text-muted-foreground">{empty}</p> : (
        <ul className="space-y-0.5">{people.map((p) => <li key={p.payslipId}>{p.employeeName}{quarantine && p.quarantineUntil ? <span className="text-muted-foreground"> · até {formatInstantDate(p.quarantineUntil)}</span> : null}</li>)}</ul>
      )}
    </section>
  )
}
