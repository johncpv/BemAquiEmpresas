import * as Menu from '@radix-ui/react-dropdown-menu'
import { Link, useNavigate } from '@tanstack/react-router'
import { MoreVertical } from 'lucide-react'
import { useState } from 'react'
import { completeFabricationStep } from '@/api/generated/bemaqui'
import type { BoardCard as Card } from '@/api/generated/model'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { StageIndicator } from '@/components/ui/stage-indicator'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { cn } from '@/lib/utils'
import { quantity } from '../orders/fabrication'
import { canCompleteStep, completeLabel, hoursText, KANBAN_DICAS, MAX_REASON, meter, promisedText, stripe } from './kanban'

const itemClass = 'cursor-pointer rounded-sm px-2 py-1.5 text-sm outline-none data-[highlighted]:bg-muted'

/**
 * Cartão do Kanban (PRD-02 CA 3, 5 e 7): número da OS (link), produto, projeto e cliente, etapa atual no display de andar, medidor
 * horas apontadas × padrão, prazo e chips. Sem arraste e sem interativo aninhado: o link do número e o botão do menu são irmãos.
 * A faixa lateral sempre tem texto (padrão 2).
 */
export function BoardCard({ card, canApprove, onChanged }: { card: Card; canApprove: boolean; onChanged: () => Promise<void> }) {
  const navigate = useNavigate()
  const [completing, setCompleting] = useState(false)
  const band = stripe(card)
  const hours = meter(card)
  const completable = canApprove && canCompleteStep(card)
  const toOrder = { to: '/app/producao/os/$id', params: { id: card.id } } as never
  return (
    <article aria-labelledby={`os-${card.id}`} data-risk={card.risk}
      className={cn('rounded-md border border-border bg-card p-3 text-sm shadow-xs', band && 'border-l-4', band?.tone === 'crit' && 'border-l-crit', band?.tone === 'warn' && 'border-l-warn')}>
      {band ? <p className={cn('mb-1 text-xs font-semibold', band.tone === 'crit' ? 'text-crit-ink' : 'text-warn-ink')}>{band.text}</p> : null}
      <div className="flex items-start justify-between gap-2">
        <Link id={`os-${card.id}`} data-card-link="" draggable={false} to={'/app/producao/os/$id' as never} params={{ id: card.id } as never} className="rounded-sm font-semibold text-primary tabular-nums hover:underline focus-visible:outline-2 focus-visible:outline-ring">
          {card.number}
        </Link>
        <Menu.Root>
          <Menu.Trigger aria-label={`Ações da OS ${card.number}`} className="-mr-1 -mt-1 grid size-8 shrink-0 place-items-center rounded-md text-muted-foreground hover:bg-muted">
            <MoreVertical aria-hidden="true" className="size-4" />
          </Menu.Trigger>
          <Menu.Portal>
            <Menu.Content align="end" sideOffset={4} className="z-40 min-w-56 rounded-md border border-border bg-popover p-1 text-popover-foreground shadow-lg">
              <Menu.Item className={itemClass} onSelect={() => void navigate(toOrder)}>Abrir OS</Menu.Item>
              {completable ? <Menu.Item className={itemClass} onSelect={() => setCompleting(true)}>{completeLabel(card)}</Menu.Item> : null}
            </Menu.Content>
          </Menu.Portal>
        </Menu.Root>
      </div>
      <p className="mt-0.5 font-medium">{card.productName || 'Produto'}<span className="text-muted-foreground tabular-nums"> · Qtd. {quantity(card.quantity)}</span></p>
      <p className="truncate text-xs text-muted-foreground">{card.projectNumber} · {card.partyName}</p>
      {card.stepNames.length > 0 ? <StageIndicator className="mt-2" stages={card.stepNames} letters={card.codes} current={card.currentStepIndex} /> : null}
      {card.currentStepId && !card.readyToComplete ? (
        <div className="mt-2">
          <div className="flex justify-between text-xs tabular-nums text-muted-foreground">
            <span>{card.currentStepName}</span>
            <span>{hoursText(card.appliedHours)} de {hoursText(card.standardHours)}</span>
          </div>
          <div aria-hidden="true" className="mt-1 h-1.5 overflow-hidden rounded-full bg-muted">
            <div className={cn('h-full rounded-full', hours.over ? 'bg-crit' : 'bg-primary')} style={{ width: `${hours.percent}%` }} />
          </div>
          <span className="sr-only">Horas apontadas: {hoursText(card.appliedHours)} de {hoursText(card.standardHours)} padrão.</span>
          {hours.over ? <p className="mt-1 text-xs font-semibold text-crit-ink">{hours.over}</p> : null}
        </div>
      ) : null}
      {!band ? <p className="mt-2 text-xs text-muted-foreground tabular-nums">{promisedText(card)}</p> : null}
      <div className="mt-2 flex flex-wrap items-center gap-1.5">
        {card.inExecution ? <StatusChip tone="info">Em execução</StatusChip> : null}
        {card.pausedReason ? <StatusChip tone="warn">Pausada: {card.pausedReason}</StatusChip> : null}
        {card.readyToComplete ? <StatusChip tone="ok">Pronta para concluir</StatusChip> : null}
        {card.noRouting ? <StatusChip tone="warn">Sem roteiro</StatusChip> : null}
        {card.openNonConformities ? <StatusChip tone="crit">NC aberta</StatusChip> : null}
        {card.selfInspection ? <StatusChip tone="warn">Autoinspeção</StatusChip> : null}
        {card.operatorInitials ? <span title="Operador em execução" className="grid size-6 place-items-center rounded-full bg-primary-soft text-[10px] font-bold text-primary">{card.operatorInitials}</span> : null}
      </div>
      {card.noRouting ? <Link to={'/app/producao/os/$id' as never} params={{ id: card.id } as never} className="mt-1 inline-block text-xs font-semibold text-primary hover:underline">Escolher o roteiro da {card.number}</Link> : null}
      {card.readyToComplete ? <Link to={'/app/producao/os/$id' as never} params={{ id: card.id } as never} className="mt-1 inline-block text-xs font-semibold text-primary hover:underline">Abrir a {card.number} para concluir</Link> : null}
      {completable ? <CompleteStepDialog card={card} open={completing} onOpenChange={setCompleting} onDone={onChanged} /> : null}
    </article>
  )
}

/** "Concluir etapa" (CA 8, padrão 4): motivo obrigatório (até 200) e confirmação com o mesmo verbo. */
export function CompleteStepDialog({ card, open, onOpenChange, onDone }: { card: Card; open: boolean; onOpenChange: (open: boolean) => void; onDone: () => Promise<void> }) {
  const [reason, setReason] = useState('')
  return (
    <ConfirmDialog verb="Concluir etapa" done="Etapa concluída" open={open} onOpenChange={onOpenChange} onOpen={() => setReason('')}
      title={`${completeLabel(card)} da ${card.number}?`}
      description={<span className="inline-flex items-center">A etapa fica concluída pelo supervisor, sem apontamento, e a próxima fica pronta.<Dica nome="Concluir etapa" texto={KANBAN_DICAS.concluirEtapa} /></span>}
      preconditions={[{ ok: reason.trim().length > 0, text: 'Informe o motivo.' }]}
      onReload={onDone}
      onConfirm={async () => {
        await completeFabricationStep(card.id, card.currentStepId!, { reason: reason.trim(), version: card.version })
        await onDone()
      }}>
      <label className="mt-3 block space-y-1 text-sm font-medium">
        Motivo
        <textarea rows={3} maxLength={MAX_REASON} value={reason} onChange={(e) => setReason(e.target.value)} className={inputClass} />
        <span className="block text-right text-xs font-normal text-muted-foreground tabular-nums">{reason.length}/{MAX_REASON}</span>
      </label>
    </ConfirmDialog>
  )
}
