import { useState } from 'react'
import { updateItem } from '@/api/generated/bemaqui'
import type { ItemDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { FormError } from '@/components/form/form'
import { UnsavedChangesGuard } from '@/components/form/UnsavedChangesGuard'
import { toastDone } from '@/components/ui/toast'
import { draftErrors, fromItem, toInput } from './item-draft'
import { ItemFields } from './ItemFields'

/** Aba Dados do item (CA 6 a 14): mesma tela da criação; a troca de unidade ou de lote com movimento volta com o motivo. */
export function ItemDataForm({ item, canEdit, onSaved }: { item: ItemDetail; canEdit: boolean; onSaved: (i: ItemDetail) => void }) {
  const [initial, setInitial] = useState(() => fromItem(item))
  const [draft, setDraft] = useState(initial)
  const [server, setServer] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const local = draftErrors(draft, item.isActive)
  const errors: Partial<Record<string, string>> = { ...local, ...Object.fromEntries(Object.entries(server?.fields ?? {}).map(([k, v]) => [k, v[0]])) }
  const dirty = JSON.stringify(draft) !== JSON.stringify(initial)

  const save = async () => {
    if (Object.keys(local).length) return false
    try {
      const saved = await updateItem(item.id, toInput(draft))
      toastDone('Item salvo')
      setInitial(fromItem(saved))
      setDraft(fromItem(saved))
      onSaved(saved)
      return true
    } catch (e) {
      setServer(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar o item.', fields: {} })
      return false
    }
  }

  return (
    <form id="dados-item" noValidate onSubmit={(e) => { e.preventDefault(); void save() }} className="max-w-3xl space-y-4">
      <UnsavedChangesGuard when={dirty && canEdit} onSave={save} />
      <FormError message={server ? server.message : undefined} />
      <ItemFields draft={draft} onChange={(d) => { setServer(null); setDraft(d) }} errors={dirty ? errors : {}} readOnly={!canEdit} active={item.isActive} />
      {canEdit ? <div className="flex justify-end"><button type="submit" disabled={!dirty} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">Salvar item</button></div> : null}
    </form>
  )
}
