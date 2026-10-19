import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { getListWarrantyDefaultsQueryKey, updateWarrantyDefault, useListWarrantyDefaults } from '@/api/generated/bemaqui'
import type { WarrantyDefaultDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { RecordHistory } from '@/components/history/RecordHistory'
import { SidePanel } from '@/components/ui/side-panel'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { toastDone } from '@/components/ui/toast'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { DICAS, TYPE_LABELS } from './item-labels'
import { InventoryTabs } from './InventoryTabs'

const months = (m: number | null) => (m === null ? 'Sem garantia' : m === 1 ? '1 mês' : `${m} meses`)

/** Estoque → Garantia padrão (CAD-02a CA 4 e 5): meses por tipo, para os itens novos; os já cadastrados não mudam. */
export function WarrantyPage() {
  const query = useListWarrantyDefaults()
  const client = useQueryClient()
  const [editing, setEditing] = useState<WarrantyDefaultDetail | null>(null)
  return (
    <>
      <PageHeading title="Estoque: Garantia padrão">
        <p className="mt-2 text-muted-foreground">Os meses de garantia que cada tipo de item novo já traz. Dá para trocar no item.</p>
      </PageHeading>
      <InventoryTabs />
      {query.isPending ? <Skeleton className="h-64" /> : query.isError ? <ErrorState onRetry={() => query.refetch()} /> : (
        <div className="overflow-x-auto rounded-md border border-border">
          <table className="w-full text-sm">
            <caption className="sr-only">Garantia padrão por tipo de item</caption>
            <thead className="bg-muted text-left text-xs text-muted-foreground"><tr><th scope="col" className="px-3 py-2">Tipo</th><th scope="col" className="px-3 py-2">Garantia</th></tr></thead>
            <tbody>
              {query.data.map((w) => (
                <tr key={w.itemType} className="border-t border-border">
                  <td className="px-3 py-2"><button type="button" onClick={() => setEditing(w)} className="font-semibold text-primary hover:underline">{TYPE_LABELS[w.itemType]}</button></td>
                  <td className="px-3 py-2 tabular-nums">{months(w.months)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {editing ? <WarrantyPanel row={editing} onClose={() => setEditing(null)} onSaved={() => { void client.invalidateQueries({ queryKey: getListWarrantyDefaultsQueryKey() }); setEditing(null) }} /> : null}
    </>
  )
}

function WarrantyPanel({ row, onClose, onSaved }: { row: WarrantyDefaultDetail; onClose: () => void; onSaved: () => void }) {
  const [value, setValue] = useState(row.months === null ? '' : String(row.months))
  const [error, setError] = useState<string | null>(null)
  const canEdit = useCan('inventory.edit')
  const invalid = value !== '' && Number(value) > 120 ? 'Informe de 0 a 120 meses, ou deixe vazio para sem garantia.' : undefined
  const save = async () => {
    if (invalid) return false
    try {
      await updateWarrantyDefault(row.itemType, { months: value === '' ? null : Number(value) })
      toastDone('Garantia padrão salva')
      onSaved()
      return true
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar a garantia padrão.')
      return false
    }
  }
  return (
    <SidePanel open onOpenChange={(o) => !o && onClose()} title={`Garantia padrão: ${TYPE_LABELS[row.itemType]}`} description="Vale para os itens novos. Os itens já cadastrados não mudam."
      dirty={canEdit && value !== (row.months === null ? '' : String(row.months))} onSave={save}
      footer={(
        <>
          <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
          {canEdit ? <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar garantia padrão</button> : null}
        </>
      )}
    >
      <FormError message={error ?? undefined} />
      <Field label="Garantia (meses)" error={invalid} dica={DICAS.garantia}>
        {(p) => <input {...p} disabled={!canEdit} inputMode="numeric" maxLength={3} value={value} placeholder="Sem garantia" onChange={(e) => setValue(e.target.value.replace(/\D/g, ''))} className={inputClass + ' tabular-nums'} />}
      </Field>
      <details className="mt-5 rounded-md border border-border bg-card px-3 py-2 text-sm">
        <summary className="cursor-pointer font-semibold">Histórico de alterações</summary>
        <div className="mt-3"><RecordHistory entity="WarrantyDefault" id={row.id} /></div>
      </details>
    </SidePanel>
  )
}
