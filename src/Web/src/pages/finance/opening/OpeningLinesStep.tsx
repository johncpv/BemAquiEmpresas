import { useState } from 'react'
import { createOpeningPayables, createOpeningReceivables, useListAccounts, useListCostCenters } from '@/api/generated/bemaqui'
import type { OpeningStatus } from '@/api/generated/model'
import { useIdempotencyKey } from '@/api/idempotency'
import { FormError } from '@/components/form/form'
import { RowsEditor } from '@/components/form/rows-editor'
import { notifySuccess } from '@/components/ui/messages'
import { Money } from '@/components/ui/money'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { Dica } from '@/help/Dica'
import { formatMoney } from '@/lib/format'
import { toNumber } from '../receivables/receivables'
import { emptyLine, lineErrors, localLineErrors, OPENING_DICAS, type OpeningLineDraft, useCanFinance } from './opening'
import { type OpeningKind, openingColumns } from './opening-columns'

const COPY = {
  receivables: { verb: 'Lançar títulos a receber', done: 'títulos a receber lançados', next: 'Ir para A pagar', empty: 'Nenhum título a receber em aberto na data de corte? Siga para o próximo passo.' },
  payables: { verb: 'Lançar títulos a pagar', done: 'títulos a pagar lançados', next: 'Ir para a conferência', empty: 'Nenhum título a pagar em aberto na data de corte? Siga para a conferência.' },
} as const

interface StepProps { kind: OpeningKind; status: OpeningStatus; refresh: () => Promise<void>; onNext: () => void }
interface Option { id: string; label: string; code?: string }

/** Passos 3 e 4 (CA 5 a 7): uma linha por título em aberto, pelo saldo; o lote entra inteiro ou nada entra. */
export function OpeningLinesStep(props: StepProps) {
  const accounts = useListAccounts()
  const centers = useListCostCenters()
  if (accounts.isPending || centers.isPending) return <Skeleton className="h-64" />
  if (accounts.isError || centers.isError) return <ErrorState onRetry={() => { void accounts.refetch(); void centers.refetch() }} />
  const revenue = props.kind === 'receivables'
  const options = accounts.data.filter((a) => (revenue ? a.type === 'Revenue' : a.type !== 'Revenue') && a.acceptsEntries && a.isActive).map((a) => ({ id: a.id, label: `${a.code} ${a.name}`, code: a.code }))
  return <LinesEditor key={props.kind} {...props} accounts={options} centers={centers.data.filter((c) => c.isActive).map((c) => ({ id: c.id, label: c.name }))} />
}

function LinesEditor({ kind, status, refresh, onNext, accounts, centers }: StepProps & { accounts: Option[]; centers: Option[] }) {
  const copy = COPY[kind]
  const canCreate = useCanFinance('create') && !status.completedAt
  const idempotency = useIdempotencyKey()
  const fallback = kind === 'receivables' ? (accounts.find((a) => a.code === '1.9')?.id ?? '') : ''
  const fresh = (): OpeningLineDraft => ({ ...emptyLine(), accountId: fallback })
  const [rows, setRows] = useState<OpeningLineDraft[]>(() => [fresh()])
  const [errors, setErrors] = useState<Record<number, string>>({})
  const [general, setGeneral] = useState<string | undefined>()
  const [saving, setSaving] = useState(false)
  const launched = kind === 'receivables' ? status.receivables : status.payables
  const total = rows.reduce((sum, r) => sum + toNumber(r.balance), 0)
  const change = (next: OpeningLineDraft[]) => { setErrors({}); setGeneral(undefined); setRows(next) }

  const submit = async () => {
    const local = localLineErrors(rows, toNumber)
    setErrors(local)
    if (Object.keys(local).length) return
    setSaving(true)
    try {
      const common = rows.map((r) => ({ partyId: r.party?.id ?? '', description: r.description.trim(), reference: r.reference.trim() || null, dueDate: r.dueDate, balance: toNumber(r.balance), accountId: r.accountId || null, costCenterId: r.costCenterId || null }))
      const result = kind === 'receivables'
        ? await createOpeningReceivables({ lines: common.map((c, i) => ({ ...c, issueDate: rows[i].issueDate || null })) }, idempotency.options)
        : await createOpeningPayables({ lines: common.map((c) => ({ ...c, paymentDetails: null })) }, idempotency.options)
      idempotency.renew()
      notifySuccess(`${result.count} ${copy.done} (${formatMoney(result.total)})`)
      setRows([fresh()])
      await refresh()
    } catch (e) {
      const parsed = lineErrors(e)
      setErrors(parsed.byLine)
      setGeneral(parsed.general)
    } finally {
      setSaving(false)
    }
  }

  const count = launched.reduce((n, l) => n + l.count, 0)
  return (
    <section aria-label={copy.verb} className="space-y-3">
      <p className="flex flex-wrap items-center text-sm text-muted-foreground">
        Lance cada título pelo que faltava no fim do dia de corte. Os títulos ganham a marca Implantação<Dica nome="Implantação" texto={OPENING_DICAS.implantacao} />
      </p>
      {count > 0 ? <p className="text-sm">Já lançados: <b className="tabular-nums">{count}</b> {count === 1 ? 'título' : 'títulos'}, <Money value={launched.reduce((n, l) => n + l.total, 0)} />.</p> : <p className="text-sm text-muted-foreground">{copy.empty}</p>}
      <FormError message={general} />
      {canCreate ? (
        <RowsEditor noun="título" rows={rows} onChange={change} newRow={fresh} columns={openingColumns(kind, accounts, centers, false)} rowError={(i) => errors[i]} duplicate min={1} max={500} summary={<>Total do lote: <Money value={total} /></>} />
      ) : null}
      <div className="flex flex-wrap justify-end gap-2">
        <button type="button" onClick={onNext} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">{copy.next}</button>
        {canCreate ? <button type="button" disabled={saving} onClick={() => void submit()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-50">{copy.verb}</button> : null}
      </div>
    </section>
  )
}
