import { type KeyboardEvent, type ReactNode, useId, useRef } from 'react'
import { InlineError } from '@/components/ui/states'
import { cn } from '@/lib/utils'

export interface ChoiceOption<T extends string> {
  value: T
  label: ReactNode
}

/**
 * Escolha única (FND-09d CA 9): PF/PJ, Markup/Margem. Funciona como grupo de rádio: uma parada de Tab, setas trocam
 * a opção e a selecionam.
 */
export function ToggleGroup<T extends string>({ label, options, value, onChange, disabled }: { label: string; options: ChoiceOption<T>[]; value: T | undefined; onChange: (value: T) => void; disabled?: boolean }) {
  const refs = useRef<(HTMLButtonElement | null)[]>([])
  const current = Math.max(0, options.findIndex((o) => o.value === value))
  const move = (event: KeyboardEvent, index: number) => {
    const delta = event.key === 'ArrowRight' || event.key === 'ArrowDown' ? 1 : event.key === 'ArrowLeft' || event.key === 'ArrowUp' ? -1 : 0
    if (!delta) return
    event.preventDefault()
    const next = (index + delta + options.length) % options.length
    onChange(options[next].value)
    refs.current[next]?.focus()
  }
  return (
    <div role="radiogroup" aria-label={label} className="inline-flex rounded-md border border-border bg-card p-0.5">
      {options.map((o, i) => (
        <button
          key={o.value}
          ref={(el) => { refs.current[i] = el }}
          type="button"
          role="radio"
          aria-checked={o.value === value}
          tabIndex={i === current ? 0 : -1}
          disabled={disabled}
          onClick={() => onChange(o.value)}
          onKeyDown={(e) => move(e, i)}
          className="rounded-sm px-3 py-1.5 text-sm font-semibold text-muted-foreground aria-checked:bg-primary aria-checked:text-primary-foreground disabled:opacity-60"
        >
          {o.label}
        </button>
      ))}
    </div>
  )
}

/**
 * Seleção múltipla em chips (FND-09d CA 8), ex.: papéis do terceiro. É um fieldset com legenda; cada chip é um checkbox
 * de verdade, com o estado anunciado; o erro é do grupo ("Escolha ao menos um papel.").
 */
export function ChipGroup<T extends string>({ legend, options, value, onChange, error, required, disabled }: { legend: string; options: ChoiceOption<T>[]; value: T[]; onChange: (value: T[]) => void; error?: string; required?: boolean; disabled?: boolean }) {
  const id = useId()
  return (
    <fieldset aria-describedby={error ? `${id}-error` : undefined} aria-invalid={!!error} className="space-y-1.5">
      <legend className="text-sm font-medium">
        {legend}
        {required ? <span className="text-xs font-normal text-muted-foreground"> (obrigatório)</span> : null}
      </legend>
      <div className="flex flex-wrap gap-2">
        {options.map((o) => {
          const checked = value.includes(o.value)
          return (
            <label key={o.value} className={cn('relative inline-flex cursor-pointer items-center rounded-full border px-3 py-1.5 text-sm font-medium has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-ring', checked ? 'border-primary bg-primary-soft text-foreground' : 'border-border bg-card text-muted-foreground', disabled && 'cursor-not-allowed opacity-60')}>
              <input
                type="checkbox"
                className="sr-only"
                checked={checked}
                disabled={disabled}
                onChange={(e) => onChange(e.target.checked ? [...value, o.value] : value.filter((v) => v !== o.value))}
              />
              <span aria-hidden="true" className="mr-1.5">{checked ? '✓' : '+'}</span>
              {o.label}
            </label>
          )
        })}
      </div>
      <InlineError id={`${id}-error`} message={error} />
    </fieldset>
  )
}
