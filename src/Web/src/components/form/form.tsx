import { AlertTriangle } from 'lucide-react'
import { type ReactNode, useId } from 'react'
import { InlineError } from '@/components/ui/states'
import { Dica } from '@/help/Dica'

interface FieldProps {
  label: string
  error?: string
  /** Aviso que não bloqueia o envio (FND-09d CA 14): âmbar, com ícone. */
  warning?: string
  hint?: string
  /** "obrigatório" em texto no rótulo e aria-required no controle (FND-09d CA 15). */
  required?: boolean
  /** Explicação curta do termo (Dica com o nome do campo, FND-09d CA 20). */
  dica?: string
  children: (props: { id: string; 'aria-invalid': boolean; 'aria-describedby'?: string; 'aria-required'?: boolean }) => ReactNode
}

/** Campo com rótulo, dica, aviso e InlineError ligados por aria-describedby. */
export function Field({ label, error, warning, hint, required, dica, children }: FieldProps) {
  const id = useId()
  const describedBy = [hint ? `${id}-hint` : '', warning && !error ? `${id}-warning` : '', error ? `${id}-error` : ''].filter(Boolean).join(' ') || undefined
  return (
    <div className="space-y-1">
      <div className="flex items-center">
        <label htmlFor={id} className="text-sm font-medium">
          {label}
          {required ? <span className="text-xs font-normal text-muted-foreground"> (obrigatório)</span> : null}
        </label>
        {/* Fora do label: senão a dica entra no nome acessível do campo. */}
        {dica ? <Dica texto={dica} nome={label} /> : null}
      </div>
      {children({ id, 'aria-invalid': !!error, 'aria-describedby': describedBy, ...(required ? { 'aria-required': true } : {}) })}
      {hint ? <p id={`${id}-hint`} className="text-xs text-muted-foreground">{hint}</p> : null}
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

/** Erro sem campo, no topo do formulário. */
export function FormError({ message }: { message?: string }) {
  if (!message) return null
  return (
    <div role="alert" className="rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">
      {message}
    </div>
  )
}

/** Grupo de campos com legenda (molde "cadastro em seções", FND-09d CA 2). */
export function FormSection({ title, description, children }: { title: string; description?: string; children: ReactNode }) {
  return (
    <fieldset className="rounded-md border border-border bg-card p-4">
      {/* float: a legenda fica dentro do cartão, como título, em vez de cortar a borda. */}
      <legend className="float-left mb-1 w-full text-[15px] font-semibold">{title}</legend>
      {description ? <p className="clear-left mb-3 text-xs text-muted-foreground">{description}</p> : null}
      <div className="clear-left grid gap-3 pt-1 sm:grid-cols-2">{children}</div>
    </fieldset>
  )
}
