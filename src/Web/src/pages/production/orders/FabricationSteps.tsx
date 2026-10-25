import { saveFabricationOrderSteps, useListWorkCenters } from '@/api/generated/bemaqui'
import type { FabricationOrderDetail, OrderForecastInfo, StepForecastInfo } from '@/api/generated/model'
import { notifySuccess } from '@/components/ui/messages'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { useCan } from '@/pages/use-can'
import { RoutingEditor } from '../../engineering/routings/RoutingEditor'
import { type RoutingDraft, toDraftSteps, toStepsInput } from '../../engineering/routings/routings'
import { canChangeSteps, FABRICATION_DICAS, hours, STEP_STATUS } from './fabrication'
import { forecastText, hoursText, PLANNING_DICAS, stepForecast } from '../planning/planning'

/** PLN-01a CA 16: "Previsto {início} → {fim}" e as horas restantes da etapa, lidos do plano; sem data, "Sem previsão: {motivo}". */
function StepForecastLine({ forecast }: { forecast?: StepForecastInfo }) {
  if (!forecast) return null
  const blocked = !forecast.plannedStart || !!forecast.noForecastReason
  return (
    <p className={`mt-1 flex flex-wrap items-center gap-x-3 text-xs ${blocked ? 'font-semibold text-warn-ink' : 'text-muted-foreground'}`}>
      <span className="inline-flex items-center tabular-nums">{forecastText(forecast)}<Dica nome={blocked ? 'Sem previsão' : 'Previsto'} texto={blocked ? PLANNING_DICAS.semPrevisao : PLANNING_DICAS.previsto} /></span>
      {!blocked ? <span className="inline-flex items-center tabular-nums">Horas restantes {hoursText(forecast.hours)}<Dica nome="Horas restantes" texto={PLANNING_DICAS.horasRestantes} /></span> : null}
    </p>
  )
}

/**
 * Aba Etapas (CA 7 e 8): com production.edit e a OS aberta sem etapa iniciada, o editor do roteiro só com as etapas (mesmas
 * regras da ENG-01, "Salvar etapas"); senão, as etapas em cartões, só leitura.
 */
export function FabricationSteps({ order: o, onSaved, forecast }: { order: FabricationOrderDetail; onSaved: () => Promise<void>; forecast?: OrderForecastInfo }) {
  const canEdit = useCan('production.edit')
  const editable = canEdit && canChangeSteps(o)
  const centers = useListWorkCenters({ pageSize: 100 }, { query: { enabled: editable } })
  if (o.steps.length === 0 && !editable) {
    return <EmptyState title="OS sem etapas." description="Quem edita na produção escolhe o roteiro desta OS." />
  }

  if (editable) {
    if (centers.isPending) return <Skeleton className="h-64" />
    if (centers.isError) return <ErrorState title="Não foi possível carregar os centros de trabalho." onRetry={() => centers.refetch()} />
    const options = centers.data.items.filter((c) => c.isActive).map((c) => ({ id: c.id, name: c.name, line: c.line }))
    const initial: RoutingDraft = { name: o.number, productType: '', steps: toDraftSteps(o.steps, options) }
    const submit = async (draft: RoutingDraft) => {
      await saveFabricationOrderSteps(o.id, { steps: toStepsInput(draft.steps), version: o.version })
      notifySuccess('Etapas salvas')
      await onSaved()
    }
    return (
      <div className="space-y-3">
        <p className="text-sm text-muted-foreground">As mudanças valem só para esta OS. O roteiro não muda.</p>
        {forecast && forecast.steps.length > 0 ? (
          <ul aria-label="Previsão das etapas" className="space-y-0.5 rounded-md border border-border bg-card p-3">
            {o.steps.map((s) => stepForecast(forecast, s.id) ? (
              <li key={s.id} className="text-sm"><b className="font-semibold">{s.code} · {s.name}</b><StepForecastLine forecast={stepForecast(forecast, s.id)} /></li>
            ) : null)}
          </ul>
        ) : null}
        <RoutingEditor initial={initial} centers={options} suggestions={[]} productTypes={[]} canEdit stepsOnly={{ saveLabel: 'Salvar etapas' }}
          onSubmit={submit} onReload={onSaved} />
      </div>
    )
  }

  return (
    <ol aria-label="Etapas da OS" className="grid gap-2 sm:grid-cols-2">
      {o.steps.map((s, i) => (
        <li key={s.id} className={`rounded-md border border-border bg-card p-3 text-sm ${i === o.currentStepIndex && o.status !== 'Cancelled' ? 'border-l-4 border-l-signal' : ''}`}>
          <div className="flex items-center justify-between gap-2">
            <span className="flex items-center gap-2">
              <span className="grid h-[26px] min-w-[30px] place-items-center rounded-[5px] bg-rail px-1.5 font-display text-[13px] font-extrabold text-signal">{s.code}</span>
              <b className="font-semibold">{s.name}</b>
            </span>
            <StatusChip tone={s.status === 'Done' ? 'ok' : s.status === 'Paused' ? 'warn' : s.status === 'Ready' || s.status === 'InProgress' ? 'info' : 'neutral'}>{s.awaitingApproval ? 'Aguardando aprovação' : STEP_STATUS[s.status] ?? s.status}</StatusChip>
          </div>
          <p className="mt-1 flex flex-wrap items-center gap-x-3 text-muted-foreground">
            <span>{s.workCenterName ?? 'Sem centro'}{s.line ? ` · ${s.line}` : ''}</span>
            <span className="inline-flex items-center tabular-nums">{hours(s.standardHours)} h<Dica nome="Horas padrão" texto={FABRICATION_DICAS.horas} /></span>
            {s.appliedHours ? <span className="tabular-nums">Apontadas {hours(s.appliedHours)} h</span> : null}
            {s.selfApproved ? <Tag>Autoaprovado</Tag> : null}
            {s.isQualityStep ? <span className="inline-flex items-center"><Tag>Qualidade</Tag><Dica nome="Etapa de qualidade" texto={FABRICATION_DICAS.qualidade} /></span> : null}
            {s.requiresApproval ? <Tag>Exige aprovação</Tag> : null}
          </p>
          <StepForecastLine forecast={stepForecast(forecast, s.id)} />
          {s.checklist.length > 0 ? <ul aria-label={`Checklist da etapa ${s.name}`} className="mt-2 list-disc pl-5">{s.checklist.map((t, n) => <li key={n}>{t}</li>)}</ul> : null}
        </li>
      ))}
    </ol>
  )
}