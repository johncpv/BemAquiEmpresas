import * as Dialog from '@radix-ui/react-dialog'
import { useState } from 'react'
import { setUserGroups, useListGroups } from '@/api/generated/bemaqui'
import type { UserRow } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { FormError } from '@/components/form/form'
import { toastDone } from '@/components/ui/toast'

/** Grupos do usuário: o que entra passa pela delegação no servidor (ADR 0008); erro aparece aqui com o motivo. */
export function UserGroupsDialog({ user, open, onOpenChange, onSaved }: { user: UserRow; open: boolean; onOpenChange: (o: boolean) => void; onSaved: () => Promise<void> }) {
  const groups = useListGroups({ query: { enabled: open } })
  const [selected, setSelected] = useState<Set<string>>(() => new Set(user.groups.map((g) => g.id)))
  const [error, setError] = useState<string>()

  const save = async () => {
    setError(undefined)
    try {
      await setUserGroups(user.id, { groupIds: [...selected] })
      await onSaved()
      onOpenChange(false)
      toastDone('Grupos salvos')
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar os grupos.')
    }
  }

  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-foreground/30" />
        <Dialog.Content className="fixed left-1/2 top-[12vh] z-50 w-[min(420px,calc(100vw-32px))] -translate-x-1/2 space-y-3 rounded-md border border-border bg-popover p-5 text-popover-foreground shadow-lg">
          <Dialog.Title className="text-base font-semibold">Grupos de {user.name || user.email}</Dialog.Title>
          <Dialog.Description className="text-sm text-muted-foreground">Vale a soma das permissões dos grupos, no próximo acesso.</Dialog.Description>
          <FormError message={error} />
          {(groups.data ?? []).filter((g) => g.isActive).map((g) => (
            <label key={g.id} className="flex items-center gap-2 text-sm">
              <input type="checkbox" checked={selected.has(g.id)} onChange={(e) => setSelected((s) => { const n = new Set(s); if (e.target.checked) n.add(g.id); else n.delete(g.id); return n })} className="size-4 accent-primary" />
              {g.name}
            </label>
          ))}
          <div className="flex justify-end gap-2 pt-2">
            <Dialog.Close className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</Dialog.Close>
            <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar grupos</button>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}
