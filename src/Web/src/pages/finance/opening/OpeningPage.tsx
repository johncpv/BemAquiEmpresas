import { useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import { getGetOpeningQueryKey, useGetOpening } from '@/api/generated/bemaqui'
import { useUrlTab } from '@/components/ui/url-tab'
import { UrlTabs } from '@/components/ui/url-tabs'
import { NextSteps } from '@/components/ui/next-steps'
import { StageIndicator } from '@/components/ui/stage-indicator'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { formatDate, formatDateTime } from '@/lib/format'
import { PageHeading } from '@/shell/PageHeading'
import { FinanceTabs } from '../FinanceTabs'
import { BalancesStep } from './BalancesStep'
import { CutoverStep } from './CutoverStep'
import { OpeningLinesStep } from './OpeningLinesStep'
import { currentStep, OPENING_STEPS } from './opening'
import { ReconciliationStep } from './ReconciliationStep'

/**
 * Financeiro → Implantação financeira (FIN-00): passos na URL (Data de corte → Saldos → A receber → A pagar →
 * Conferência), com o progresso no indicador de etapa. Concluída, tudo fica em modo leitura.
 */
export function OpeningPage() {
  const query = useGetOpening()
  const client = useQueryClient()
  const navigate = useNavigate()
  const [completed, setCompleted] = useState(false)
  const step = query.data ? currentStep(query.data) : 0
  const [, setTab] = useUrlTab(OPENING_STEPS[step].id)
  if (query.isPending) return <Skeleton className="h-96" />
  if (query.isError) return <ErrorState title="Não foi possível carregar a implantação." onRetry={() => query.refetch()} />

  const status = query.data
  const refresh = async () => { await client.invalidateQueries({ queryKey: getGetOpeningQueryKey() }) }
  const next = (i: number) => () => setTab(OPENING_STEPS[i + 1].id)
  const needsCutover = !status.cutoverDate
  const tabs = OPENING_STEPS.map((s, i) => ({
    id: s.id,
    label: `${i + 1}. ${s.label}`,
    disabled: i > 0 && needsCutover,
    disabledReason: 'Defina a data de corte antes.',
    content:
      i === 0 ? <CutoverStep status={status} refresh={refresh} onNext={next(0)} />
      : i === 1 ? <BalancesStep status={status} refresh={refresh} onNext={next(1)} />
      : i === 2 ? <OpeningLinesStep kind="receivables" status={status} refresh={refresh} onNext={next(2)} />
      : i === 3 ? <OpeningLinesStep kind="payables" status={status} refresh={refresh} onNext={next(3)} />
      : <ReconciliationStep status={status} refresh={refresh} onCompleted={() => setCompleted(true)} />,
  }))

  return (
    <>
      <PageHeading title="Financeiro: Implantação financeira">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-3">
          <p className="text-muted-foreground">Comece o BemAqui numa data de corte, com o saldo das contas e os títulos em aberto nesse dia.</p>
          <StageIndicator stages={OPENING_STEPS.map((s) => s.label)} current={step} />
        </div>
      </PageHeading>
      <FinanceTabs />
      {status.completedAt ? (
        <p role="status" className="mb-4 rounded-md border border-ok-soft bg-ok-soft px-3 py-2 text-sm text-ok-ink">
          Implantação concluída em {formatDateTime(status.completedAt)}, com data de corte em {formatDate(status.cutoverDate ?? '')}. A tela fica só para consulta.
        </p>
      ) : null}
      {completed ? (
        <div className="mb-4">
          <NextSteps onDismiss={() => setCompleted(false)} steps={[
            { label: 'Ver A receber vencido', onSelect: () => void navigate({ to: '/app/financeiro/receber', search: { situacao: 'vencido' } } as never) },
            { label: 'Ver A pagar', onSelect: () => void navigate({ to: '/app/financeiro/pagar' } as never) },
          ]} />
        </div>
      ) : null}
      <UrlTabs label="Passos da implantação" fallback={OPENING_STEPS[step].id} tabs={tabs} />
    </>
  )
}
