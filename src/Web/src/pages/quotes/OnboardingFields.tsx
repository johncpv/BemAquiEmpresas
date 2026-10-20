import { Link } from '@tanstack/react-router'
import type { ApprovalPreview } from '@/api/generated/model'
import { DateInput } from '@/components/form/inputs'
import { Money } from '@/components/ui/money'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { type OnboardingDraft, openingIsOld, receivedBy } from './onboarding'

/**
 * Contrato de implantação (COM-04c CA 1, 3, 7): marcos por data com data no passado e "Recebido antes da implantação",
 * pré-marcado quando a data do marco é até a data de corte. Sem data de corte, a opção fica desabilitada com o motivo.
 */
export function OnboardingFields({ preview, signedAt, value, onChange }: { preview: ApprovalPreview; signedAt: string; value: OnboardingDraft; onChange: (v: OnboardingDraft) => void }) {
  const cutover = preview.onboarding?.cutoverDate ?? null
  const completedAt = preview.onboarding?.completedAt ?? null
  const old = openingIsOld(completedAt)
  const toggle = (enabled: boolean) => onChange(enabled ? receivedBy(preview, signedAt, cutover, {}) : { enabled: false, milestones: {} })
  return (
    <div className="mt-3 space-y-2 text-sm">
      <label className="flex items-center gap-2 font-medium">
        <input type="checkbox" checked={value.enabled} disabled={!cutover} onChange={(e) => toggle(e.target.checked)} />
        Contrato de implantação (já estava em andamento)
      </label>
      {!cutover ? (
        <p className="text-muted-foreground">Defina a data de corte na Implantação financeira antes. <Link to={'/app/financeiro/implantacao' as never} className="font-semibold text-primary hover:underline">Implantação financeira</Link></p>
      ) : null}
      {value.enabled && old && completedAt ? <p className="rounded border border-warn-soft bg-warn-soft px-2 py-1 text-warn-ink">A implantação foi concluída em {formatDate(completedAt.slice(0, 10))}. Use só para contrato que já existia antes dela.</p> : null}
      {value.enabled ? (
        <ul className="divide-y divide-border rounded-md border border-border" aria-label="Marcos da implantação">
          {preview.milestones.map((m) => {
            const draft = value.milestones[m.id] ?? { dueDate: m.dueDate ?? null, receivedBefore: false }
            const set = (patch: Partial<typeof draft>) => onChange({ ...value, milestones: { ...value.milestones, [m.id]: { ...draft, ...patch } } })
            return (
              <li key={m.id} className="grid gap-2 p-2 sm:grid-cols-[1fr_auto_auto] sm:items-center">
                <span>{m.description} · <Money value={m.amount} /> <span className="text-muted-foreground">{m.when}</span></span>
                {m.trigger === 'Date' ? <DateInput aria-label={`Data de ${m.description}`} value={draft.dueDate} onChange={(dueDate) => set({ dueDate })} /> : <span />}
                <label className="flex items-center gap-1.5">
                  <input type="checkbox" checked={draft.receivedBefore} onChange={(e) => set({ receivedBefore: e.target.checked })} />
                  Recebido antes da implantação<Dica nome="Recebido antes da implantação" texto="O valor já está no saldo das contas na data de corte; o sistema não gera título para ele." />
                </label>
              </li>
            )
          })}
        </ul>
      ) : null}
      {value.enabled && cutover ? <p className="text-xs text-muted-foreground">Data de corte: {formatDate(cutover)}. Recebimento depois dela entra como baixa no título.</p> : null}
    </div>
  )
}
