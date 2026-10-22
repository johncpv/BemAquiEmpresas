import { Link } from '@tanstack/react-router'
import type { ShopfloorQueueStep } from '@/api/generated/model'
import { StageIndicator } from '@/components/ui/stage-indicator'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDate } from '@/lib/format'
import { cn } from '@/lib/utils'
import { floorHours } from './shopfloor'

/**
 * Cartão da etapa na fila (CA 1, 3): o cartão inteiro é o alvo (≥ 56 px) e abre a etapa. Mostra a OS, o display de andar, a
 * etapa, o centro, o produto, o projeto e o cliente, o prometido, as horas e quem está nela. Sem custo (CA 25).
 */
export function QueueCard({ step, highlight }: { step: ShopfloorQueueStep; highlight?: boolean }) {
  return (
    <Link to="/fab/etapa/$stepId" params={{ stepId: step.stepId }} data-floor-target="" aria-label={`${step.orderNumber}, etapa ${step.stepName}${step.workCenterName ? `, ${step.workCenterName}` : ''}`}
      className={cn('block min-h-[56px] rounded-md border-2 bg-card p-4 text-left focus-visible:outline-4 focus-visible:outline-ring', highlight ? 'border-primary' : 'border-border')}>
      <span className="flex flex-wrap items-center justify-between gap-2">
        <span className="font-display text-[20px] font-bold tabular-nums">{step.orderNumber}</span>
        <StageIndicator stages={step.stepNames} letters={step.codes} current={step.stepIndex} />
      </span>
      <span className="mt-1 block text-[20px] font-semibold">Etapa {step.stepName}</span>
      <span className="mt-1 block text-[18px]">{step.productName || 'Produto'}</span>
      <span className="block truncate text-base text-muted-foreground">{step.projectNumber} · {step.projectName}{step.partyName ? ` · ${step.partyName}` : ''}</span>
      <span className="mt-2 flex flex-wrap items-center gap-2 text-base">
        {step.isMyCenter ? <StatusChip tone="info">Seu centro</StatusChip> : null}
        <span className="text-muted-foreground">{step.workCenterName ?? 'Sem centro'}</span>
        <span className="tabular-nums text-muted-foreground">· Padrão {floorHours(step.standardHours)} h, apontadas {floorHours(step.appliedHours)} h</span>
        {step.promisedDate ? <span className="tabular-nums text-muted-foreground">· Prometido {formatDate(step.promisedDate)}</span> : null}
      </span>
      {step.pausedReason ? <span className="mt-2 block"><StatusChip tone="warn">Pausada: {step.pausedReason}</StatusChip></span> : null}
      {step.people.length > 0 ? <span className="mt-2 block text-base">Na etapa: {step.people.join(', ')}</span> : null}
    </Link>
  )
}