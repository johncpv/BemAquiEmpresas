import { type ReactNode, useId } from 'react'
import { InlineError } from '@/components/ui/states'

interface FieldProps {
  label: string
  error?: string
  hint?: string
  children: (props: { id: string; 'aria-invalid': boolean; 'aria-describedby'?: string }) => ReactNode
}

/** Campo com rótulo, dica e InlineError ligados por aria-describedby. */
export function Field({ label, error, hint, children }: FieldProps) {
  const id = useId()
  const describedBy = [hint ? `${id}-hint` : '', error ? `${id}-error` : ''].filter(Boolean).join(' ') || undefined
  return (
    <div className="space-y-1">
      <label htmlFor={id} className="block text-sm font-medium">{label}</label>
      {children({ id, 'aria-invalid': !!error, 'aria-describedby': describedBy })}
      {hint ? <p id={`${id}-hint`} className="text-xs text-muted-foreground">{hint}</p> : null}
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
