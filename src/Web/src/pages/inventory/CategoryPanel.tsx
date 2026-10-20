import { useState } from 'react'
import { createItemCategory, deleteItemCategory, inactivateItemCategory, reactivateItemCategory, updateItemCategory } from '@/api/generated/bemaqui'
import type { CategoryDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { SidePanel } from '@/components/ui/side-panel'
import { notifySuccess } from '@/components/ui/messages'
import { Can } from '@/pages/ModuleGate'
import { useCan } from '@/pages/use-can'

/** A própria categoria e as descendentes: não podem virar pai dela (a árvore não aceita ciclo, CA 3). */
function descendants(all: CategoryDetail[], id: string) {
  const out = new Set<string>([id])
  let grew = true
  while (grew) {
    grew = false
    for (const c of all) {
      if (c.parentId && out.has(c.parentId) && !out.has(c.id)) {
        out.add(c.id)
        grew = true
      }
    }
  }
  return out
}

/** Painel da categoria (CA 3): nome e categoria pai; com itens ou filhas, o excluir oferece "Inativar". */
export function CategoryPanel({ category, all, onClose, onSaved }: { category: CategoryDetail | null; all: CategoryDetail[]; onClose: () => void; onSaved: () => void }) {
  const [draft, setDraft] = useState({ name: category?.name ?? '', parentId: category?.parentId ?? '' })
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const canWrite = useCan(category ? 'inventory.edit' : 'inventory.create')
  const blocked = category ? descendants(all, category.id) : new Set<string>()
  const dirty = draft.name !== (category?.name ?? '') || draft.parentId !== (category?.parentId ?? '')

  const save = async () => {
    const input = { name: draft.name.trim(), parentId: draft.parentId || null }
    try {
      if (category) await updateItemCategory(category.id, input)
      else await createItemCategory(input)
      notifySuccess('Categoria salva')
      onSaved()
      return true
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar a categoria.', fields: {} })
      return false
    }
  }

  const actions = category ? (
    <span className="mr-auto flex flex-wrap gap-2">
      <Can perm="inventory.edit">
        {category.isActive ? (
          <ConfirmDialog verb="Inativar" done="Categoria inativada" title={`Inativar a categoria ${category.name}?`} description="Ela some dos seletores e continua nos itens que já a usam."
            onConfirm={async () => { await inactivateItemCategory(category.id); onSaved() }} trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Inativar categoria</button>} />
        ) : <button type="button" onClick={async () => { await reactivateItemCategory(category.id); notifySuccess('Categoria reativada'); onSaved() }} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Reativar</button>}
      </Can>
      <Can perm="inventory.delete">
        <ConfirmDialog verb="Excluir" done="Categoria excluída" destructive title={`Excluir a categoria ${category.name}?`} description="Só dá para excluir categoria sem itens e sem subcategorias."
          onConfirm={async () => { await deleteItemCategory(category.id); onSaved() }}
          recovery={category.isActive ? { verb: 'Inativar', done: 'Categoria inativada', when: (code) => code === 'category_in_use', onSelect: async () => { await inactivateItemCategory(category.id); onSaved() } } : undefined}
          trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold text-crit-ink">Excluir categoria</button>} />
      </Can>
    </span>
  ) : null

  return (
    <SidePanel open onOpenChange={(o) => !o && onClose()} title={category ? `Categoria ${category.name}` : 'Nova categoria'} dirty={dirty && canWrite} onSave={save}
      footer={(
        <>
          {actions}
          <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
          {canWrite ? <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar categoria</button> : null}
        </>
      )}
    >
      <fieldset disabled={!canWrite} className="space-y-3">
        <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
        <Field label="Nome" required error={error?.fields.name?.[0]}>
          {(p) => <input {...p} value={draft.name} maxLength={80} onChange={(e) => setDraft({ ...draft, name: e.target.value })} className={inputClass} />}
        </Field>
        <Field label="Categoria pai" error={error?.fields.parentId?.[0]} hint="Vazio = categoria principal.">
          {(p) => (
            <select {...p} value={draft.parentId} onChange={(e) => setDraft({ ...draft, parentId: e.target.value })} className={inputClass}>
              <option value="">Nenhuma (principal)</option>
              {all.filter((c) => !blocked.has(c.id)).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </select>
          )}
        </Field>
      </fieldset>
    </SidePanel>
  )
}
