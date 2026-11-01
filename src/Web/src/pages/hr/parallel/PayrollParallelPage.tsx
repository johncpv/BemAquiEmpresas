import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { Lock } from 'lucide-react'
import { useState } from 'react'
import { comparePayrollParallel, getGetPayrollParallelOverviewQueryKey, getListPayrollParallelHistoryQueryKey, useGetPayrollParallelOverview, useListPayrollParallelHistory } from '@/api/generated/bemaqui'
import type { PayrollParallelOverview, PayrollParallelPending } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { FormError } from '@/components/form/form'
import { notifySuccess } from '@/components/ui/messages'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { useNavigation } from '@/navigation/navigation-context'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { HrTabs } from '../HrTabs'
import { periodLabel, typeLabel } from '../payroll/payroll'
import { MappingsSection } from './MappingsSection'
import { ParallelDetail } from './ParallelDetail'
import { RevertDialog, SwitchDialog } from './ParallelDialogs'
import { monthOf, PARALLEL_DICAS, STATUS, streakLabel, TYPE_LABELS } from './parallel'

const primary = 'inline-flex min-h-11 items-center gap-2 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60'
const secondary = 'inline-flex min-h-11 items-center gap-2 rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold disabled:opacity-60'

/**
 * RH → Conferência da folha (FLH-06): as competências com o chip (Em conferência, Aceita, Reprovada), "{n} de 3 competências seguidas
 * aceitas", a situação por tipo, as folhas prontas para comparar e, com hr.view_sensitive, a tabela lado a lado da competência escolhida
 * (?id=). Aceitar, virar e voltar só para o Proprietário. Sem hr.view_sensitive, o bloco "Dados protegidos".
 */
export function PayrollParallelPage() {
  const search = useSearch({ strict: false }) as { mes?: string; id?: string }
  const navigate = useNavigate()
  const client = useQueryClient()
  const canSensitive = useCan('hr.view_sensitive')
  const { isOwner } = useNavigation()
  const query = useGetPayrollParallelOverview()
  const view = query.data
  const refresh = async () => {
    await client.invalidateQueries({ queryKey: getGetPayrollParallelOverviewQueryKey() })
    await client.invalidateQueries({ queryKey: getListPayrollParallelHistoryQueryKey() })
  }

  const selected = view ? search.id ?? view.runs.find((r) => search.mes && monthOf(r.period) === search.mes && r.runType === 'Monthly')?.id : undefined
  const open = (id: string) => void navigate({ to: '/app/rh/conferencia-folha', search: { id } as never })

  return (
    <>
      <PageHeading title="RH: Conferência da folha">
        <p className="mt-2 text-muted-foreground">A folha calculada pelo sistema lado a lado com a da contadora, competência por competência, até a virada.</p>
      </PageHeading>
      <HrTabs />
      {query.isPending ? <Skeleton className="h-64" /> : query.isError ? (
        <ErrorState title="Não foi possível carregar a conferência da folha." message={query.error instanceof ApiError ? query.error.message : undefined} onRetry={() => query.refetch()} />
      ) : !view ? null : (
        <div className="space-y-6">
          <Situation view={view} isOwner={isOwner} onChanged={refresh} />
          <Pending items={view.pending} canSensitive={canSensitive} onCompared={async (id) => { await refresh(); open(id) }} />
          {view.runs.length === 0 && view.pending.length === 0 ? (
            <EmptyState title="Calcule a folha da competência para comparar com a da contadora." description="Crie a folha Mensal no modo Calculada, calcule e importe a folha da contadora da mesma competência."
              action={<Link to="/app/rh/folha" className={primary}>Calcular folha</Link>} />
          ) : (
            <Months view={view} selected={selected} onOpen={open} />
          )}
          {selected ? (
            canSensitive ? <ParallelDetail id={selected} isOwner={isOwner} onChanged={refresh} /> : (
              <div className="flex max-w-3xl items-center gap-2 rounded-md border border-border bg-muted px-4 py-6 text-sm text-muted-foreground">
                <Lock aria-hidden="true" className="size-4" />
                Dados protegidos
                <Dica nome="Dados protegidos" texto={PARALLEL_DICAS.protegidos} />
              </div>
            )
          ) : null}
          <MappingsSection />
          <History />
        </div>
      )}
    </>
  )
}

/** Contagem das seguidas, situação por tipo (CA 6) e os botões do Proprietário (CA 5 e 7). */
function Situation({ view, isOwner, onChanged }: { view: PayrollParallelOverview; isOwner: boolean; onChanged: () => Promise<void> }) {
  const monthly = view.modes.find((m) => m.type === 'Monthly')
  const anyCalculated = view.modes.some((m) => m.mode === 'Calculated')
  return (
    <section aria-label="Situação da folha" className="space-y-3 rounded-md border border-border bg-card p-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <p className="text-sm text-muted-foreground">Folha oficial: {monthly?.mode === 'Calculated' ? 'calculada' : 'lançada (da contadora)'}</p>
          <p className="text-lg font-semibold tabular-nums">{streakLabel(view.consecutive.length, view.required)}</p>
        </div>
        {isOwner ? (
          <div className="flex flex-wrap items-center gap-2">
            {view.canSwitch && view.switchFrom ? (
              <span className="inline-flex items-center">
                <SwitchDialog view={view} onDone={onChanged} />
                <Dica nome="Virar para a folha calculada" texto={PARALLEL_DICAS.virada} />
              </span>
            ) : null}
            {anyCalculated ? <RevertDialog modes={view.modes} onDone={onChanged} /> : null}
          </div>
        ) : null}
      </div>
      <ul aria-label="Situação por tipo" className="grid gap-2 sm:grid-cols-2">
        {view.modes.map((m) => (
          <li key={m.type} className="flex flex-wrap items-center gap-2 text-sm">
            <span className="font-semibold">{TYPE_LABELS[m.type] ?? m.label}</span>
            <StatusChip tone={m.mode === 'Calculated' ? 'ok' : 'neutral'}>{m.mode === 'Calculated' ? 'Calculada' : 'Lançada'}</StatusChip>
            <span className="text-muted-foreground">{m.situation}</span>
          </li>
        ))}
      </ul>
    </section>
  )
}

/** Folhas calculadas ainda sem comparação (Propositivo): o que falta e o atalho, ou "Comparar". */
function Pending({ items, canSensitive, onCompared }: { items: PayrollParallelPending[]; canSensitive: boolean; onCompared: (id: string) => Promise<void> }) {
  const [busy, setBusy] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  if (items.length === 0) return null
  const compare = async (item: PayrollParallelPending) => {
    setBusy(item.period + item.type)
    setError(null)
    try {
      const detail = await comparePayrollParallel({ period: monthOf(item.period), type: item.type })
      notifySuccess('Comparada')
      await onCompared(detail.run.id)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível comparar. Tente de novo.')
    } finally {
      setBusy(null)
    }
  }

  return (
    <section aria-labelledby="para-comparar" className="space-y-2">
      <h2 id="para-comparar" className="text-base font-semibold">Para comparar</h2>
      <FormError message={error ?? undefined} />
      <ul className="space-y-2">
        {items.map((p) => (
          <li key={p.period + p.type} className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border bg-card px-3 py-2 text-sm">
            <span><b>{typeLabel(p.type)} de {periodLabel(p.period)}</b> · {p.message}</span>
            {p.canCompare ? (
              canSensitive ? <button type="button" className={primary} disabled={busy !== null} onClick={() => void compare(p)}>{busy === p.period + p.type ? 'Comparando...' : 'Comparar'}</button> : null
            ) : p.link ? <a href={p.link} className={secondary}>{p.actionLabel}</a> : null}
          </li>
        ))}
      </ul>
    </section>
  )
}

/** Competências comparadas: chip, diferenças, quem comparou e quem aceitou; a aberta fica marcada. */
function Months({ view, selected, onOpen }: { view: PayrollParallelOverview; selected: string | undefined; onOpen: (id: string) => void }) {
  if (view.runs.length === 0) return null
  return (
    <section aria-labelledby="competencias" className="space-y-2">
      <h2 id="competencias" className="text-base font-semibold">Competências</h2>
      <ul className="space-y-2">
        {view.runs.map((r) => {
          const status = STATUS[r.status] ?? { label: r.statusLabel, tone: 'neutral' as const }
          return (
            <li key={r.id} aria-current={r.id === selected ? 'true' : undefined}
              className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border bg-card px-3 py-2 text-sm aria-[current=true]:border-primary">
              <span className="flex flex-wrap items-center gap-2">
                <b>{TYPE_LABELS[r.runType] ?? r.runType} de {periodLabel(r.period)}</b>
                <StatusChip tone={status.tone}>{status.label}</StatusChip>
                {r.countsForSwitch ? <StatusChip tone="ok">Conta para a virada</StatusChip> : null}
                <span className="tabular-nums text-muted-foreground">
                  {r.differences === 0 ? 'Sem diferença' : r.differences === 1 ? '1 diferença' : `${r.differences} diferenças`} · {r.employees} {r.employees === 1 ? 'funcionário' : 'funcionários'} · comparada em {formatDateTime(r.comparedAt)}
                </span>
                {r.acceptedAt ? <span className="text-muted-foreground">Aceita em {formatDateTime(r.acceptedAt)}{r.accountantName ? ` · contadora ${r.accountantName}, CRC ${r.accountantCrc}` : ''}</span> : null}
              </span>
              <button type="button" className={secondary} onClick={() => onOpen(r.id)}>Abrir</button>
            </li>
          )
        })}
      </ul>
    </section>
  )
}

/** Histórico (CA 10): quem, quando e o quê; a justificativa só para quem vê dados sensíveis. */
function History() {
  const query = useListPayrollParallelHistory()
  return (
    <section aria-labelledby="historico-conferencia" className="space-y-2">
      <h2 id="historico-conferencia" className="text-base font-semibold">Histórico</h2>
      {query.isPending ? <Skeleton className="h-24" /> : query.isError ? (
        <ErrorState title="Não foi possível carregar o histórico." onRetry={() => query.refetch()} />
      ) : (query.data ?? []).length === 0 ? (
        <EmptyState title="Nada registrado ainda." description="Comparações, classificações, aceites, a virada e a volta aparecem aqui, com quem e quando." />
      ) : (
        <ul className="space-y-1 text-sm">
          {query.data!.map((e, i) => (
            <li key={`${e.at}-${i}`} className="rounded-md border border-border bg-card px-3 py-2">
              <span className="tabular-nums text-muted-foreground">{formatDateTime(e.at)}</span> · <b>{e.action}</b> · {e.description}
              {e.userName ? <span className="text-muted-foreground"> · {e.userName}</span> : null}
              {e.detail ? <p className="text-muted-foreground">{e.detail}</p> : null}
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
