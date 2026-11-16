import { SquarePen } from 'lucide-react'
import { useState } from 'react'
import { editProjectBudget } from '@/api/generated/bemaqui'
import type { ProjectCostView } from '@/api/generated/model'
import { inputClass } from '@/components/form/form-utils'
import { MoneyInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { type BudgetDraft, budgetChanges, budgetDraft, budgetedMarginPct, budgetTotal } from './budget'
import { COST_TYPES, NO_COST_BUDGET, pct1 } from './margins/margins'

interface EditBudgetDialogProps {
  projectId: string
  projectNumber: string
  /** Versão do projeto (ADR 0019): outra gravação no meio dá 409 e o diálogo oferece "Recarregar". */
  version: number
  costs: Pick<ProjectCostView, 'lines' | 'revenue'>
  onDone: () => Promise<unknown>
  onReload: () => Promise<unknown>
}

/**
 * "Editar custo orçado" (COM-09): o orçado por natureza e o motivo, com o total e a margem orçada antes e depois. Só as naturezas
 * alteradas são gravadas; cada edição fica no histórico da aba Custos com o valor anterior, o novo, quem e quando.
 */
export function EditBudgetDialog({ projectId, projectNumber, version, costs, onDone, onReload }: EditBudgetDialogProps) {
  const [draft, setDraft] = useState<BudgetDraft>(() => budgetDraft(costs.lines))
  const [reason, setReason] = useState('')
  const changes = budgetChanges(costs.lines, draft)
  const before = budgetTotal(budgetDraft(costs.lines))
  const after = budgetTotal(draft)
  const margin = (total: number) => { const pct = budgetedMarginPct(costs.revenue, total); return pct === null ? NO_COST_BUDGET : pct1(pct) }
  return (
    <ConfirmDialog size="lg" verb="Salvar custo orçado" done="Custo orçado salvo" title={`Editar o custo orçado do projeto ${projectNumber}?`}
      description="Informe ou corrija o custo orçado de cada natureza. A margem orçada e o desvio mudam na hora no projeto, no Orçado × realizado e no painel."
      onOpen={() => { setDraft(budgetDraft(costs.lines)); setReason('') }}
      onReload={onReload}
      preconditions={[
        { ok: COST_TYPES.every(({ type }) => draft[type] !== null && Number(draft[type]) >= 0), text: 'Preencha o valor de todas as naturezas (use 0,00 quando não houver custo).' },
        { ok: changes.length > 0, text: 'Altere o valor de ao menos uma natureza.' },
        { ok: reason.trim().length > 0, text: 'Informe o motivo da edição.' },
      ]}
      impact={(
        <dl className="grid grid-cols-2 gap-x-4 gap-y-1 text-sm">
          <dt className="text-muted-foreground">Custo orçado total</dt>
          <dd className="text-right tabular-nums"><Money value={before} /> → <Money value={after} className="font-semibold" /></dd>
          <dt className="text-muted-foreground">Margem orçada</dt>
          <dd className="text-right tabular-nums">{margin(before)} → <b>{margin(after)}</b></dd>
        </dl>
      )}
      onConfirm={async () => {
        await editProjectBudget(projectId, { lines: changes, reason: reason.trim(), version })
        await onDone()
      }}
      trigger={(
        <button type="button" className="inline-flex min-h-[44px] items-center gap-1.5 rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold text-foreground sm:min-h-0">
          <SquarePen aria-hidden="true" className="size-4" />
          Editar custo orçado
        </button>
      )}>
      <fieldset className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <legend className="sr-only">Custo orçado por natureza</legend>
        {COST_TYPES.map(({ type, label }) => (
          <label key={type} className="block space-y-1 text-sm font-medium">{label}
            <MoneyInput value={draft[type]} aria-invalid={draft[type] === null || Number(draft[type]) < 0} onChange={(value) => setDraft((d) => ({ ...d, [type]: value }))} />
          </label>
        ))}
      </fieldset>
      <label className="block space-y-1 text-sm font-medium">Motivo
        <textarea value={reason} maxLength={500} rows={3} onChange={(e) => setReason(e.target.value)} className={inputClass} />
        <span className="block text-xs font-normal text-muted-foreground">Fica no histórico, com o valor anterior, o novo, quem editou e quando.</span>
      </label>
    </ConfirmDialog>
  )
}
