import { AlertTriangle } from 'lucide-react'
import { createContext, type ReactNode, useContext, useId } from 'react'
import { InlineError } from '@/components/ui/states'
import { Dica } from '@/help/Dica'
import { cn } from '@/lib/utils'
import { type FieldSize, type FieldSpan, fieldSpanClass } from './field-size'

export type { FieldSize, FieldSpan } from './field-size'

// Dentro da FormGrid o Field ganha as classes de coluna; fora dela (grade antiga do FormSection, diálogos) fica como era.
const InFormGrid = createContext(false)

interface FieldProps {
  label: string
  error?: string
  /** Aviso que não bloqueia o envio (FND-09d CA 14): âmbar, com ícone. */
  warning?: string
  hint?: string
  /** A dica muda sozinha (ex.: "Consultando o CEP…"): anuncia ao leitor de tela. */
  hintLive?: boolean
  /** "obrigatório" em texto no rótulo e aria-required no controle (FND-09d CA 15). */
  required?: boolean
  /** Explicação curta do termo (Dica com o nome do campo, FND-09d CA 20). */
  dica?: string
  /** Largura pelo conteúdo, só dentro da FormGrid (padrão `lg`). Ver FieldSize. */
  size?: FieldSize
  /** Trilhas explícitas (exceção). Ver FieldSpan. */
  span?: FieldSpan
  children: (props: { id: string; 'aria-invalid': boolean; 'aria-describedby'?: string; 'aria-required'?: boolean }) => ReactNode
}

/** Campo com rótulo, dica, aviso e InlineError ligados por aria-describedby. */
export function Field({ label, error, warning, hint, hintLive, required, dica, size, span, children }: FieldProps) {
  const id = useId()
  const inGrid = useContext(InFormGrid)
  const describedBy = [hint ? `${id}-hint` : '', warning && !error ? `${id}-warning` : '', error ? `${id}-error` : ''].filter(Boolean).join(' ') || undefined
  return (
    <div className={cn('space-y-1', inGrid && fieldSpanClass(size, span))}>
      <div className="flex items-center">
        <label htmlFor={id} className="text-sm font-medium">
          {label}
          {required ? <span className="text-xs font-normal text-muted-foreground"> (obrigatório)</span> : null}
        </label>
        {/* Fora do label: senão a dica entra no nome acessível do campo. */}
        {dica ? <Dica texto={dica} nome={label} /> : null}
      </div>
      {children({ id, 'aria-invalid': !!error, 'aria-describedby': describedBy, ...(required ? { 'aria-required': true } : {}) })}
      {hint ? <p id={`${id}-hint`} aria-live={hintLive ? 'polite' : undefined} className="text-xs text-muted-foreground">{hint}</p> : null}
      {warning && !error ? (
        <p id={`${id}-warning`} role="status" className="flex items-start gap-1 text-xs font-medium text-warn-ink">
          <AlertTriangle aria-hidden="true" className="mt-px size-3.5 shrink-0" />
          {warning}
        </p>
      ) : null}
      <InlineError id={`${id}-error`} message={error} />
    </div>
  )
}

/**
 * Grade do formulário (ADR 0044): 1 coluna no contêiner estreito, 6 a partir de 448 px e 12 a partir de 768 px. A consulta é
 * pelo contêiner, então a mesma grade serve na página, no SidePanel e no diálogo. Nunca ponha uma grade dentro de outra.
 */
export function FormGrid({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div className="@container">
      <div className={cn('grid grid-cols-1 gap-4 @md:grid-cols-6 @3xl:grid-cols-12', className)}>
        <InFormGrid.Provider value>{children}</InFormGrid.Provider>
      </div>
    </div>
  )
}

/** Item da FormGrid que não é um Field (ex.: um aviso ou um grupo de botões), com o mesmo tamanho dos campos. */
export function FormGridItem({ size = 'full', span, className, children }: { size?: FieldSize; span?: FieldSpan; className?: string; children: ReactNode }) {
  return <div className={cn(fieldSpanClass(size, span), className)}>{children}</div>
}

/** Erro sem campo, no topo do formulário. */
export function FormError({ message }: { message?: string }) {
  if (!message) return null
  return (
    <div role="alert" className="rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">
      {message}
    </div>
  )
}

/**
 * Grupo de campos com legenda (molde "cadastro em seções", FND-09d CA 2).
 * `grid`: `legacy` (padrão, até migrar a tela) mantém a grade antiga de 2 colunas; `form` usa a FormGrid de 12 trilhas
 * (cada Field com o seu `size`); `none` deixa o conteúdo sem grade (ex.: uma textarea ou o logo).
 */
export function FormSection({ title, description, grid = 'legacy', children }: { title: string; description?: string; grid?: 'legacy' | 'form' | 'none'; children: ReactNode }) {
  const legacy = grid === 'legacy'
  return (
    <fieldset data-grid={grid} className={cn('rounded-md border border-border bg-card', legacy ? 'p-4' : 'p-4 sm:p-5')}>
      {/* float: a legenda fica dentro do cartão, como título, em vez de cortar a borda. */}
      <legend className="float-left mb-1 w-full text-[15px] font-semibold">{title}</legend>
      {description ? <p className={cn('clear-left text-xs text-muted-foreground', legacy ? 'mb-3' : 'mb-4 max-w-prose')}>{description}</p> : null}
      {legacy ? <div className="clear-left grid gap-3 pt-1 sm:grid-cols-2"><InFormGrid.Provider value={false}>{children}</InFormGrid.Provider></div>
        : <div className="clear-left pt-1">{grid === 'form' ? <FormGrid>{children}</FormGrid> : children}</div>}
    </fieldset>
  )
}

/**
 * Ações do formulário (ADR 0044). Curto (1 seção, cabe na tela): no fim, à direita. Longo (2 ou mais seções ou maior que a tela):
 * `sticky`, barra presa no rodapé logo acima do dock, com o estado à esquerda ("Alterações não salvas" quando `dirty`). O aviso de
 * sair sem salvar continua no UnsavedChangesGuard da tela. Um só botão primário, por último (à direita; no celular, embaixo).
 */
export function FormActions({ sticky, dirty, status, children }: { sticky?: boolean; dirty?: boolean; status?: ReactNode; children: ReactNode }) {
  const shown = status ?? (dirty ? 'Alterações não salvas' : null)
  if (!sticky) {
    return (
      <div data-form-actions="end" className="flex flex-wrap items-center justify-end gap-2">
        {shown ? <p aria-live="polite" className="mr-auto text-xs text-muted-foreground">{shown}</p> : null}
        {children}
      </div>
    )
  }
  return (
    <div
      data-form-actions="sticky"
      className="sticky bottom-[calc(var(--dock-offset)+0.5rem)] z-10 flex flex-col gap-2 rounded-md border border-border bg-card/95 px-4 py-3 shadow-sm backdrop-blur sm:flex-row sm:items-center sm:justify-between"
    >
      {/* Sempre presente: o leitor de tela anuncia quando o aviso aparece. */}
      <p aria-live="polite" className="text-xs text-muted-foreground">{shown}</p>
      <div className="flex flex-col gap-2 *:min-h-11 *:w-full sm:flex-row sm:*:w-auto">{children}</div>
    </div>
  )
}
