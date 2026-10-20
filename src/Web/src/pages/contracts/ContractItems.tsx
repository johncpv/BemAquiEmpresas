import type { ContractDetail, ContractMilestoneView } from '@/api/generated/model'
import { Link } from '@tanstack/react-router'
import { Money } from '@/components/ui/money'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { COST_SECTIONS } from '../quotes/quotes'
import { situation } from '../finance/receivables/receivables'
import { CONTRACT_DICAS } from './contracts'

const EVENTS: Record<string, string> = { Signature: 'assinatura do contrato', FabricationDone: 'fabricação concluída', Delivered: 'entrega', TechnicalAcceptance: 'aceite técnico' }
const qty = (v: number) => v.toLocaleString('pt-BR', { maximumFractionDigits: 4 })

function when(m: ContractMilestoneView) {
  if (m.trigger === 'Date') return m.dueDate ? `em ${formatDate(m.dueDate)}` : '—'
  const prefix = m.daysAfterEvent ? `${m.daysAfterEvent} dias após ` : 'na '
  return prefix + (EVENTS[m.triggerEvent ?? ''] ?? m.triggerEvent ?? '')
}

/** Itens e marcos do snapshot (CA 12): composições só com projects.view_cost (o servidor já tira). */
export function ContractItems({ contract }: { contract: ContractDetail }) {
  const s = contract.snapshot
  return (
    <div className="space-y-4">
      <p className="flex items-center text-sm text-muted-foreground">Snapshot do contrato<Dica nome="Snapshot do contrato" texto={CONTRACT_DICAS.snapshot} /></p>
      <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm" aria-label="Itens do contrato">
        {s.items.map((i) => (
          <li key={i.sequence} className="space-y-1 p-3">
            <p className="flex flex-wrap justify-between gap-2"><span><b>{i.sequence}.</b> {i.description}</span><span className="tabular-nums">{qty(i.quantity)} × <Money value={i.unitPrice} /> = <Money value={i.total} className="font-semibold" /></span></p>
            {i.costs?.length ? (
              <ul className="text-xs text-muted-foreground">
                {i.costs.map((l, k) => <li key={k}>{COST_SECTIONS.find((t) => t.type === l.costType)?.label ?? l.costType}{l.description ? `: ${l.description}` : ''} · {qty(l.quantity)} × <Money value={l.unitCost} /> = <Money value={l.total} /></li>)}
              </ul>
            ) : null}
          </li>
        ))}
      </ul>
      <p className="flex flex-wrap justify-end gap-4 text-sm">
        {s.totalCost !== null && s.totalCost !== undefined ? <span>Custo <Money value={s.totalCost} /></span> : null}
        <span>Total <Money value={s.totalPrice} className="font-semibold" /></span>
      </p>
      <h3 className="text-sm font-semibold">Marcos de pagamento</h3>
      <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm" aria-label="Marcos do contrato">
        {contract.milestones.map((m) => (
          <li key={m.id} className="space-y-1 p-3">
            <p className="flex flex-wrap justify-between gap-2">
              <span><b>{m.sequence}.</b> {m.description} · {qty(m.percent)}%</span>
              <span className="text-muted-foreground">{when(m)}</span>
              <Money value={m.amount} className="font-semibold" />
            </p>
            {m.receivable ? (
              <p className="flex flex-wrap items-center gap-2 text-xs">
                <Link to={'/app/financeiro/receber/$id' as never} params={{ id: m.receivable.id } as never} className="font-mono font-semibold text-primary hover:underline">{m.receivable.number}</Link>
                <StatusChip tone={situation({ status: m.receivable.status as never, isOverdue: false, isDueToday: false }).tone}>{situation({ status: m.receivable.status as never, isOverdue: false, isDueToday: false }).label}</StatusChip>
                <span className="tabular-nums text-muted-foreground">{m.receivable.dueDate ? `vence ${formatDate(m.receivable.dueDate)}` : 'aguarda o marco'} · saldo <Money value={m.receivable.balance} /></span>
              </p>
            ) : m.receivedBeforeOnboarding ? <p className="text-xs text-muted-foreground">Recebido antes da implantação</p> : m.hasReceivable ? <p className="text-xs text-muted-foreground">Parcela gerada</p> : null}
          </li>
        ))}
      </ul>
    </div>
  )
}
