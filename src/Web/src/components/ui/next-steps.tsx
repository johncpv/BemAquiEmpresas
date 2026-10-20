import { ArrowRight, X } from 'lucide-react'
import type { ReactNode } from 'react'

export interface Step {
  label: string
  onSelect: () => void
  icon?: ReactNode
}

/**
 * Próximos passos depois de criar (FND-09d CA 16, R-PRP-2): faixa fixa no detalhe, com até 3 ações, até o usuário
 * fechar. A mensagem de sucesso só confirma o verbo; o próximo passo não some sozinho (WCAG 2.2.1).
 */
export function NextSteps({ title = 'Próximos passos', steps, onDismiss }: { title?: string; steps: Step[]; onDismiss: () => void }) {
  if (steps.length === 0) return null
  return (
    <section aria-label={title} className="flex flex-wrap items-center gap-2 rounded-md border border-primary-soft bg-primary-soft px-3 py-2">
      <b className="mr-1 text-sm font-semibold">{title}:</b>
      {steps.slice(0, 3).map((s) => (
        <button key={s.label} type="button" onClick={s.onSelect} className="inline-flex items-center gap-1 rounded-md border border-border bg-card px-2.5 py-1 text-sm font-semibold text-primary">
          {s.icon ?? <ArrowRight aria-hidden="true" className="size-3.5" />}
          {s.label}
        </button>
      ))}
      <button type="button" onClick={onDismiss} aria-label="Fechar próximos passos" className="ml-auto grid size-7 place-items-center rounded-md hover:bg-card">
        <X aria-hidden="true" className="size-4" />
      </button>
    </section>
  )
}
