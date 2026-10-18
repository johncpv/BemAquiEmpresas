import * as Menu from '@radix-ui/react-dropdown-menu'
import { MoreHorizontal } from 'lucide-react'
import { useState } from 'react'
import { cancelInvite, deactivateUser, reactivateUser, resendInvite } from '@/api/generated/bemaqui'
import type { UserRow } from '@/api/generated/model'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { toastDone, toastError } from '@/components/ui/toast'
import { UserGroupsDialog } from './UserGroupsDialog'

/** Ações da linha (FND-06c CA 7, 9 e 11): grupos, reenviar/cancelar convite, desativar com confirmação e reativar. */
export function UserRowActions({ user, refresh }: { user: UserRow; refresh: () => Promise<void> }) {
  const [groupsOpen, setGroupsOpen] = useState(false)
  const run = async (action: () => Promise<unknown>, done: string) => {
    try {
      await action()
      await refresh()
      toastDone(done)
    } catch (e) {
      toastError(e instanceof Error ? e.message : 'Não foi possível concluir.')
    }
  }

  const item = 'cursor-pointer rounded-sm px-2 py-1.5 text-sm outline-none data-[highlighted]:bg-muted'
  const who = user.name || user.email
  return (
    <span className="flex items-center justify-end gap-1">
      {user.status === 'Active' ? (
        <ConfirmDialog
          verb="Desativar"
          done="Desativado"
          title={`Desativar ${who}?`}
          description="Perde o acesso no próximo clique. Os registros feitos por ele continuam com o nome dele no histórico."
          destructive
          onConfirm={async () => { await deactivateUser(user.id); await refresh() }}
          trigger={<button type="button" className="text-xs font-semibold text-crit-ink">Desativar</button>}
        />
      ) : null}
      <Menu.Root>
        <Menu.Trigger aria-label={`Ações de ${who}`} className="rounded-sm p-1 hover:bg-muted"><MoreHorizontal aria-hidden="true" className="size-4" /></Menu.Trigger>
        <Menu.Portal>
          <Menu.Content align="end" className="z-40 min-w-48 rounded-md border border-border bg-popover p-1 text-popover-foreground shadow-lg">
            <Menu.Item className={item} onSelect={() => setGroupsOpen(true)}>Editar grupos</Menu.Item>
            {user.status === 'Invited' ? <Menu.Item className={item} onSelect={() => void run(() => resendInvite(user.id), 'Convite reenviado')}>Reenviar convite</Menu.Item> : null}
            {user.inviteOpen ? <Menu.Item className={item} onSelect={() => void run(() => cancelInvite(user.id), 'Convite cancelado')}>Cancelar convite</Menu.Item> : null}
            {user.status === 'Inactive' ? <Menu.Item className={item} onSelect={() => void run(() => reactivateUser(user.id), 'Reativado')}>Reativar</Menu.Item> : null}
          </Menu.Content>
        </Menu.Portal>
      </Menu.Root>
      <UserGroupsDialog user={user} open={groupsOpen} onOpenChange={setGroupsOpen} onSaved={refresh} />
    </span>
  )
}
