import * as Dialog from '@radix-ui/react-dialog'
import { useState } from 'react'
import { setGroupMembers, useListUsers } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { FormError } from '@/components/form/form'
import { Skeleton } from '@/components/ui/states'
import { notifySuccess } from '@/components/ui/messages'

/** "Gerenciar membros": quem entra passa pela delegação no servidor; o Proprietário nunca fica sem membro. */
export function GroupMembersDialog({ groupId, groupName, current, onSaved }: { groupId: string; groupName: string; current: string[]; onSaved: () => Promise<void> }) {
  const [open, setOpen] = useState(false)
  const users = useListUsers({ query: { enabled: open } })
  const [selected, setSelected] = useState<Set<string>>(new Set(current))
  const [error, setError] = useState<string>()

  const save = async () => {
    setError(undefined)
    try {
      await setGroupMembers(groupId, { userIds: [...selected] })
      await onSaved()
      setOpen(false)
      notifySuccess('Membros salvos')
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar os membros.')
    }
  }

  return (
    <Dialog.Root open={open} onOpenChange={(o) => { setOpen(o); if (o) setSelected(new Set(current)) }}>
      <Dialog.Trigger className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Gerenciar membros</Dialog.Trigger>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-foreground/30" />
        <Dialog.Content className="fixed left-1/2 top-[12vh] z-50 w-[min(460px,calc(100vw-32px))] -translate-x-1/2 rounded-md border border-border bg-popover p-5 text-popover-foreground shadow-lg">
          <Dialog.Title className="text-base font-semibold">Membros de {groupName}</Dialog.Title>
          <Dialog.Description className="mt-1 text-sm text-muted-foreground">Quem estiver no grupo recebe as permissões dele no próximo acesso.</Dialog.Description>
          <FormError message={error} />
          <div className="mt-3 max-h-72 space-y-1 overflow-y-auto">
            {users.isPending ? <Skeleton className="h-24" /> : (users.data ?? []).filter((u) => u.status !== 'Inactive').map((u) => (
              <label key={u.id} className="flex items-center gap-2 rounded-md px-2 py-1.5 text-sm hover:bg-muted">
                <input type="checkbox" checked={selected.has(u.id)} onChange={(e) => setSelected((s) => { const n = new Set(s); if (e.target.checked) n.add(u.id); else n.delete(u.id); return n })} className="size-4 accent-primary" />
                <span>{u.name || u.email}</span>
                <span className="ml-auto text-xs text-muted-foreground">{u.email}</span>
              </label>
            ))}
          </div>
          <div className="mt-4 flex justify-end gap-2">
            <Dialog.Close className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</Dialog.Close>
            <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar membros</button>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}
