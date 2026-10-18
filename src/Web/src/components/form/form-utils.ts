import { zodResolver } from '@hookform/resolvers/zod'
import { type FieldValues, type Path, useForm, type UseFormProps, type UseFormReturn } from 'react-hook-form'
import type { z } from 'zod'
import { ApiError } from '@/api/http'
/** RHF + Zod (FND-09c CA 8): valida ao sair do campo e, ao enviar, foca o primeiro campo com erro. */
export function useZodForm<TIn extends FieldValues, TOut extends FieldValues>(schema: z.ZodType<TOut, TIn>, options?: Omit<UseFormProps<TIn, unknown, TOut>, 'resolver'>) {
  return useForm<TIn, unknown, TOut>({ resolver: zodResolver(schema), mode: 'onTouched', shouldFocusError: true, ...options })
}

/**
 * ProblemDetails do servidor nos campos (FND-09c CA 9): <c>errors</c> por campo vão para cada campo; o resto vai para o
 * topo do formulário (root.server). Devolve true se tratou o erro.
 */
export function applyServerErrors<T extends FieldValues>(form: UseFormReturn<T, unknown, unknown>, error: unknown): boolean {
  if (!(error instanceof ApiError)) return false
  const fields = Object.entries(error.fields)
  const known = fields.filter(([name]) => name in form.getValues())
  known.forEach(([name, messages], i) => form.setError(name as Path<T>, { type: 'server', message: messages[0] }, { shouldFocus: i === 0 }))
  if (known.length === 0 || known.length < fields.length) form.setError('root.server', { type: 'server', message: error.message })
  return true
}

export const inputClass =
  'w-full rounded-md border border-input bg-card px-3 py-2 text-sm aria-[invalid=true]:border-crit focus-visible:outline-2 focus-visible:outline-ring'
