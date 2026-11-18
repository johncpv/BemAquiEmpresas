import { zodResolver } from '@hookform/resolvers/zod'
import { type FieldValues, type Path, useForm, type UseFormProps, type UseFormReturn } from 'react-hook-form'
import type { z } from 'zod'
import { ApiError } from '@/api/http'
/** RHF + Zod (FND-09c CA 8): valida ao sair do campo e, ao enviar, foca o primeiro campo com erro. */
export function useZodForm<TIn extends FieldValues, TOut extends FieldValues>(schema: z.ZodType<TOut, TIn>, options?: Omit<UseFormProps<TIn, unknown, TOut>, 'resolver'>) {
  return useForm<TIn, unknown, TOut>({ resolver: zodResolver(schema), mode: 'onTouched', shouldFocusError: true, ...options })
}

/** Caminho do ProblemDetails ("lines[2].unitCost") para o do React Hook Form ("lines.2.unitCost"). */
export const toFormPath = (name: string) => name.replace(/\[(\d+)\]/g, (_, n: string) => '.' + n)

/**
 * ProblemDetails do servidor nos campos (FND-09c CA 9, FND-09e CA 8): <c>errors</c> por campo vão para cada campo,
 * inclusive em linhas ("milestones[0].percent"); o resto vai para o topo do formulário (root.server). Devolve true se
 * tratou o erro.
 */
export function applyServerErrors<T extends FieldValues>(form: UseFormReturn<T, unknown, unknown>, error: unknown): boolean {
  if (!(error instanceof ApiError)) return false
  const fields = Object.entries(error.fields).map(([name, messages]) => [toFormPath(name), messages] as const)
  const values = form.getValues() as Record<string, unknown>
  const known = fields.filter(([path]) => path.split('.')[0] in values && form.getValues(path as Path<T>) !== undefined)
  known.forEach(([name, messages], i) => form.setError(name as Path<T>, { type: 'server', message: messages[0] }, { shouldFocus: i === 0 }))
  if (known.length === 0 || known.length < fields.length) form.setError('root.server', { type: 'server', message: error.message })
  return true
}

const fieldClass = 'rounded-md border border-input bg-card px-3 py-2 text-sm aria-[invalid=true]:border-crit focus-visible:outline-2 focus-visible:outline-ring'

export const inputClass = `w-full ${fieldClass}`

/**
 * Select curto ao lado de outro campo na mesma célula (a unidade ao lado da quantidade): cresce até a opção mais longa, em
 * vez de uma largura fixa que corta o texto ("BARRA", "PCT"), e não encolhe abaixo de 5rem.
 */
export const inlineSelectClass = `w-auto min-w-20 shrink-0 ${fieldClass}`
