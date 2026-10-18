import { type KeyboardEvent, type ReactNode, useRef } from 'react'
import { useHelpTab } from '@/help/help-context'
import { useUrlTab } from './url-tab'

export interface UrlTab {
  id: string
  label: string
  /** Campos inválidos nesta aba (FND-09d CA 10). */
  errors?: number
  disabled?: boolean
  /** Por que está desabilitada (ex.: "Salve o cadastro para adicionar endereços."). */
  disabledReason?: string
  content: ReactNode
}

/**
 * Abas do detalhe (molde "detalhe com abas", FND-09d CA 2 e 10): ligadas à URL e à ajuda da aba (useHelpTab).
 * Todas as abas ficam montadas (escondidas), para o formulário não perder campos nem o foco no primeiro erro.
 */
/** `help`: a aba vira a "aba" da ajuda (Ajuda/<chave>.md com `aba: <id>`, ADR 0013). */
export function UrlTabs({ tabs, fallback, label, help = true }: { tabs: UrlTab[]; fallback?: string; label: string; help?: boolean }) {
  const first = fallback ?? tabs[0]?.id ?? ''
  const [current, setCurrent] = useUrlTab(first)
  const active = tabs.find((t) => t.id === current && !t.disabled) ? current : first
  useHelpTab(help ? active : null)
  const refs = useRef<(HTMLButtonElement | null)[]>([])
  const enabled = tabs.filter((t) => !t.disabled)

  const move = (event: KeyboardEvent) => {
    const index = enabled.findIndex((t) => t.id === active)
    const delta = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0
    if (!delta) return
    event.preventDefault()
    const next = enabled[(index + delta + enabled.length) % enabled.length]
    setCurrent(next.id)
    refs.current[tabs.indexOf(next)]?.focus()
  }

  return (
    <div>
      <div role="tablist" aria-label={label} onKeyDown={move} className="flex gap-1 overflow-x-auto border-b border-border">
        {tabs.map((t, i) => (
          <button
            key={t.id}
            ref={(el) => { refs.current[i] = el }}
            type="button"
            role="tab"
            id={`aba-${t.id}`}
            aria-controls={`painel-${t.id}`}
            aria-selected={t.id === active}
            tabIndex={t.id === active ? 0 : -1}
            disabled={t.disabled}
            title={t.disabled ? t.disabledReason : undefined}
            onClick={() => setCurrent(t.id)}
            className="-mb-px inline-flex shrink-0 items-center gap-1.5 whitespace-nowrap border-b-2 border-transparent px-3 py-2 text-sm font-semibold text-muted-foreground aria-selected:border-primary aria-selected:text-foreground disabled:opacity-50"
          >
            {t.label}
            {t.errors ? (
              <span className="rounded-full bg-crit px-1.5 text-[10px] font-bold text-critical-foreground tabular-nums">
                {t.errors}
                <span className="sr-only"> {t.errors === 1 ? 'erro' : 'erros'}</span>
              </span>
            ) : null}
          </button>
        ))}
      </div>
      {tabs.map((t) => (
        <div key={t.id} role="tabpanel" id={`painel-${t.id}`} aria-labelledby={`aba-${t.id}`} hidden={t.id !== active} className="pt-4">
          {t.disabled ? null : t.content}
        </div>
      ))}
    </div>
  )
}
