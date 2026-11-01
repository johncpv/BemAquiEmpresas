import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { deletePayrollItemMapping, getListPayrollItemMappingsQueryKey, savePayrollItemMapping, useListPayrollItemMappings, useListPayrollItems } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { notifySuccess } from '@/components/ui/messages'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { useCan } from '@/pages/use-can'

/**
 * De-para de rubricas (CA 1): quando a contadora usa um código diferente do sistema para a mesma rubrica, a correspondência faz a comparação
 * encontrar as duas linhas. Ver com hr.view; cadastrar e tirar com hr.edit.
 */
export function MappingsSection() {
  const client = useQueryClient()
  const canEdit = useCan('hr.edit')
  const query = useListPayrollItemMappings()
  const items = useListPayrollItems({ query: { enabled: canEdit } })
  const [entered, setEntered] = useState('')
  const [calculated, setCalculated] = useState('')
  const [error, setError] = useState<string | null>(null)
  const refresh = () => client.invalidateQueries({ queryKey: getListPayrollItemMappingsQueryKey() })
  const save = async () => {
    setError(null)
    try {
      await savePayrollItemMapping({ enteredCode: entered, calculatedCode: calculated })
      notifySuccess('Correspondência salva')
      setEntered('')
      setCalculated('')
      await refresh()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar. Tente de novo.')
    }
  }

  const remove = async (id: string) => {
    setError(null)
    try {
      await deletePayrollItemMapping(id)
      notifySuccess('Correspondência removida')
      await refresh()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível remover. Tente de novo.')
    }
  }

  return (
    <details className="rounded-md border border-border bg-card p-4">
      <summary className="cursor-pointer text-base font-semibold">De-para de rubricas</summary>
      <p className="mt-2 text-sm text-muted-foreground">Use quando o código da rubrica na folha da contadora é diferente do código no sistema. Depois, compare de novo.</p>
      {query.isPending ? <Skeleton className="mt-3 h-16" /> : query.isError ? (
        <ErrorState title="Não foi possível carregar o de-para." onRetry={() => query.refetch()} className="mt-3" />
      ) : (query.data ?? []).length === 0 ? (
        <p className="mt-3 text-sm">Nenhuma correspondência: os códigos iguais já se encontram sozinhos.</p>
      ) : (
        <ul className="mt-3 space-y-1 text-sm">
          {query.data!.map((m) => (
            <li key={m.id} className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border px-3 py-2">
              <span><b>{m.enteredCode}</b> na contadora → <b>{m.calculatedCode}</b>{m.calculatedName ? ` ${m.calculatedName}` : ''} no sistema</span>
              {canEdit ? <button type="button" onClick={() => void remove(m.id)} className="min-h-11 rounded-md border border-border px-3 py-1 text-sm font-semibold">Remover</button> : null}
            </li>
          ))}
        </ul>
      )}
      {canEdit ? (
        <div className="mt-3 grid gap-3 sm:grid-cols-[1fr_1fr_auto] sm:items-end">
          <Field label="Código na folha da contadora">
            {(f) => <input {...f} value={entered} maxLength={10} onChange={(e) => setEntered(e.target.value)} className={inputClass} />}
          </Field>
          <Field label="Rubrica do sistema">
            {(f) => (
              <select {...f} value={calculated} onChange={(e) => setCalculated(e.target.value)} className={inputClass}>
                <option value="">Escolha</option>
                {(items.data ?? []).map((i) => <option key={i.id} value={i.code}>{i.code} {i.name}</option>)}
              </select>
            )}
          </Field>
          <button type="button" onClick={() => void save()} disabled={!entered || !calculated} className="min-h-11 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">Salvar correspondência</button>
          <div className="sm:col-span-3"><FormError message={error ?? undefined} /></div>
        </div>
      ) : null}
    </details>
  )
}
