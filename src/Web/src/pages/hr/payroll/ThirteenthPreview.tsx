import type { ThirteenthPreview } from '@/api/generated/model'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { avosLabel, PAYROLL_DICAS } from './payroll'

const dateLabel = (iso: string) => iso.split('-').reverse().join('/')

/**
 * Prévia dos avos do 13º antes de criar a folha (FLH-04): um cartão por funcionário com os avos e os meses que não contam, a data
 * limite e, na 2ª parcela sem a 1ª fechada, a confirmação "calcular sem adiantamento". Nenhum valor.
 */
export function ThirteenthPreviewPanel({ query, confirm, onConfirm }: {
  query: { isPending: boolean; isError: boolean; data?: ThirteenthPreview; refetch: () => unknown }
  confirm: boolean
  onConfirm: (value: boolean) => void
}) {
  if (query.isPending) return <Skeleton className="h-24" />
  if (query.isError || !query.data) return <ErrorState title="Não foi possível carregar a prévia dos avos." onRetry={() => query.refetch()} />
  const p = query.data
  return (
    <section aria-label="Prévia dos avos" className="space-y-2">
      <p className="inline-flex items-center text-sm font-medium">Avos de {p.year} por funcionário<Dica nome="Avos" texto={PAYROLL_DICAS.avos} /></p>
      <p className="text-xs text-muted-foreground">
        {p.dueDate ? `Data limite: ${dateLabel(p.dueDate)}. ` : ''}O mês conta com {p.monthMinDays} dias ou mais trabalhados.
      </p>
      {p.exists ? <p role="alert" className="text-sm font-medium text-crit-ink">Já existe a folha calculada desta parcela em {p.year}.</p> : null}
      {p.type === 'Thirteenth2' && !p.firstClosed ? (
        <div className="space-y-1 rounded-md border border-warn bg-warn-soft p-2 text-sm">
          <p>A 1ª parcela não foi paga: a 2ª será calculada sem adiantamento.</p>
          <div className="flex items-center gap-2">
            <input id="sem-adiantamento" type="checkbox" checked={confirm} onChange={(e) => onConfirm(e.target.checked)} className="size-4 accent-primary" />
            <label htmlFor="sem-adiantamento">Calcular a 2ª parcela sem adiantamento</label>
          </div>
        </div>
      ) : null}
      {p.employees.length === 0 ? (
        <EmptyState title="Nenhum funcionário com avos no ano." description="Confira as admissões e os afastamentos no cadastro dos funcionários." />
      ) : (
        <ul className="grid max-h-64 gap-2 overflow-y-auto sm:grid-cols-2">
          {p.employees.map((e) => {
            const out = e.months.filter((m) => m.employedDays > 0 && !m.counts)
            return (
              <li key={e.employeeId} className="rounded-md border border-border p-2 text-sm">
                <span className="flex items-center justify-between gap-2">
                  <span className="font-semibold">{e.name}{e.registration ? <span className="font-normal text-muted-foreground"> · {e.registration}</span> : null}</span>
                  <StatusChip tone={e.avos === 12 ? 'ok' : 'info'}><span className="tabular-nums">{avosLabel(e.avos)}</span></StatusChip>
                </span>
                {out.length > 0 ? (
                  <span className="block text-xs text-muted-foreground">Não contam: {out.map((m) => `${m.label} (${m.workedDays} dias)`).join(', ')}</span>
                ) : null}
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}
