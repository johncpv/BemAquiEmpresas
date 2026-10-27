import { Link, useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import { useGetPayrollPaymentsSensitive } from '@/api/generated/bemaqui'
import type { PayrollPaymentRow, PayrollRunSummary } from '@/api/generated/model'
import { Money } from '@/components/ui/money'
import { NextSteps } from '@/components/ui/next-steps'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate, formatInstantDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PAYROLL_DICAS } from './payroll'

const TONES: Record<string, 'ok' | 'info' | 'warn' | 'neutral'> = { Open: 'info', AwaitingApproval: 'warn', Approved: 'warn', PartiallyPaid: 'warn', Paid: 'ok' }

function Row({ r, canOpen }: { r: PayrollPaymentRow; canOpen: boolean }) {
  return (
    <li className="flex flex-wrap items-start justify-between gap-2 px-3 py-2 text-sm">
      <div className="min-w-0">
        <p className="font-semibold">{canOpen ? <Link to={'/app/financeiro/pagar/$id' as never} params={{ id: r.payableId } as never} className="font-mono text-primary hover:underline">{r.number}</Link> : <span className="font-mono">{r.number}</span>} — {r.payeeName}</p>
        <p className="text-xs text-muted-foreground tabular-nums">{r.dueDate ? `Vence ${formatDate(r.dueDate)}` : 'Sem vencimento'}{r.inQuarantine ? ' · conta em quarentena' : ''}</p>
      </div>
      <div className="flex items-center gap-2">
        <Money value={r.amount} />
        <StatusChip tone={r.status ? TONES[r.status] ?? 'neutral' : 'neutral'}>{r.statusLabel}</StatusChip>
      </div>
    </li>
  )
}

/**
 * Aba Pagamentos (RH-02c CA 5): os títulos que o fechamento gerou, com a situação (para quem vê o Financeiro), e o próximo passo: a OP
 * dos salários com os títulos pré-selecionados (menos os de conta em quarentena, listados à parte com "Revisar conta") e a OP das guias.
 */
export function PaymentsTab({ run }: { run: PayrollRunSummary }) {
  const navigate = useNavigate()
  const [stepsDismissed, setStepsDismissed] = useState(false)
  const canOrder = useCan('finance.create')
  const canOpen = useCan('finance.view')
  const query = useGetPayrollPaymentsSensitive(run.id, { query: { enabled: run.status !== 'Draft' } })
  if (run.status === 'Draft') return <EmptyState title="Feche a folha para gerar os títulos a pagar." description="Cada funcionário com líquido ganha um título de salário, e cada guia, um título no nome do órgão." />
  if (query.isPending) return <Skeleton className="h-48" />
  if (query.isError || !query.data) return <ErrorState title="Não foi possível carregar os pagamentos." onRetry={() => query.refetch()} />
  const view = query.data
  const order = (ids: string[]) => void navigate({ to: '/app/financeiro/ordens-de-pagamento/nova', search: { titulos: ids.join(',') } } as never)
  const salaries = view.items.filter((i) => i.isSalary)
  const charges = view.items.filter((i) => !i.isSalary)
  const steps = [
    ...(view.salaryPreselected.length > 0 ? [{ label: 'Criar ordem de pagamento dos salários', onSelect: () => order(view.salaryPreselected) }] : []),
    ...(view.chargePreselected.length > 0 ? [{ label: 'Criar ordem de pagamento das guias', onSelect: () => order(view.chargePreselected) }] : []),
  ]
  return (
    <div className="max-w-4xl space-y-4">
      {canOrder && !stepsDismissed && steps.length > 0 ? <NextSteps steps={steps} onDismiss={() => setStepsDismissed(true)} /> : null}
      {view.quarantined.length > 0 ? (
        <section aria-label="Fora do pagamento automático" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">
          <p className="inline-flex items-center font-semibold">Fora do pagamento automático<Dica nome="Conta em quarentena" texto={PAYROLL_DICAS.quarentena} /></p>
          <ul className="mt-1 space-y-1">
            {view.quarantined.map((q) => (
              <li key={q.payableId} className="flex flex-wrap items-center justify-between gap-2">
                <span>{q.payeeName}: conta em quarentena{q.quarantineUntil ? ` até ${formatInstantDate(q.quarantineUntil)}` : ''}</span>
                {q.employeeId ? <Link to={'/app/rh/funcionarios/$id' as never} params={{ id: q.employeeId } as never} search={{ aba: 'pagamento' } as never} className="inline-flex min-h-11 items-center font-semibold text-primary underline">Revisar conta</Link> : null}
              </li>
            ))}
          </ul>
        </section>
      ) : null}
      <section aria-label="Salários">
        <h3 className="mb-1 flex flex-wrap items-center justify-between gap-2 text-sm font-semibold"><span>Salários ({salaries.length})</span><span className="tabular-nums">Total <Money value={view.salaryTotal} /></span></h3>
        {salaries.length === 0 ? <p className="text-sm text-muted-foreground">Nenhum salário a pagar nesta folha.</p> : <ul className="divide-y divide-border rounded-md border border-border bg-card">{salaries.map((r) => <Row key={r.payableId} r={r} canOpen={canOpen} />)}</ul>}
      </section>
      <section aria-label="Guias">
        <h3 className="mb-1 flex flex-wrap items-center justify-between gap-2 text-sm font-semibold"><span className="inline-flex items-center">Guias ({charges.length})<Dica nome="Conta da guia" texto={PAYROLL_DICAS.guia} /></span><span className="tabular-nums">Total <Money value={view.chargeTotal} /></span></h3>
        {charges.length === 0 ? <p className="text-sm text-muted-foreground">Nenhuma guia nesta folha.</p> : <ul className="divide-y divide-border rounded-md border border-border bg-card">{charges.map((r) => <Row key={r.payableId} r={r} canOpen={canOpen} />)}</ul>}
      </section>
      {!view.canSeeStatus ? <p className="text-xs text-muted-foreground">A situação de cada pagamento aparece para quem acompanha o Financeiro.</p> : null}
    </div>
  )
}
