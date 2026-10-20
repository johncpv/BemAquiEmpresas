import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { getGetEmployeeUserQueryKey, getListLinkableUsersQueryKey, linkEmployeeUser, unlinkEmployeeUser, useGetEmployeeUser, useListLinkableUsers } from '@/api/generated/bemaqui'
import type { EmployeeDetail } from '@/api/generated/model'
import { Field, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DialogMessage } from '@/components/ui/dialog-message'
import { Skeleton } from '@/components/ui/states'
import { useCan } from '@/pages/use-can'

interface Props { employee: EmployeeDetail; linking?: boolean; onLinkingChange?: (open: boolean) => void }

/** Usuário do sistema ligado ao funcionário (CA 19 e 20): vincular e desvincular exigem hr.edit e identity.edit. */
export function EmployeeUser({ employee, linking, onLinkingChange }: Props) {
  const client = useQueryClient()
  const canEditHr = useCan('hr.edit')
  const canEditUsers = useCan('identity.edit')
  const canLink = canEditHr && canEditUsers
  const linked = useGetEmployeeUser(employee.id)
  const [own, setOwn] = useState(false)
  const open = linking ?? own
  const setOpen = (o: boolean) => { setOwn(o); onLinkingChange?.(o) }
  const refresh = async () => {
    await client.invalidateQueries({ queryKey: getGetEmployeeUserQueryKey(employee.id) })
    await client.invalidateQueries({ queryKey: getListLinkableUsersQueryKey() })
  }

  if (linked.isPending) return <Skeleton className="h-20" />
  const user = linked.data?.user ?? null
  return (
    <FormSection title="Usuário do sistema" description="O vínculo liga o login ao funcionário (ponto, apontamento). Não muda grupo nem permissão.">
      {user ? (
        <div className="flex flex-wrap items-center justify-between gap-2 text-sm">
          <span><b>{user.name}</b> <span className="text-muted-foreground">{user.email}</span></span>
          {canLink ? (
            <ConfirmDialog verb="Desvincular" done="Usuário desvinculado" title={`Desvincular o usuário ${user.name}?`} description="O login continua ativo, só deixa de ser deste funcionário."
              onConfirm={async () => { await unlinkEmployeeUser(employee.id); await refresh() }}
              trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Desvincular usuário</button>} />
          ) : null}
        </div>
      ) : (
        <div className="flex flex-wrap items-center justify-between gap-2 text-sm">
          <span className="text-muted-foreground">Nenhum usuário vinculado.</span>
          {canLink ? (
            <>
              <button type="button" onClick={() => setOpen(true)} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Vincular usuário</button>
              <LinkDialog employee={employee} open={open} onOpenChange={setOpen} onDone={refresh} />
            </>
          ) : null}
        </div>
      )}
    </FormSection>
  )
}

function LinkDialog({ employee, open, onOpenChange, onDone }: { employee: EmployeeDetail; open: boolean; onOpenChange: (o: boolean) => void; onDone: () => Promise<void> }) {
  const users = useListLinkableUsers({ query: { enabled: open } })
  const [userId, setUserId] = useState('')
  return (
    <ConfirmDialog verb="Vincular" done="Usuário vinculado" title={`Vincular um usuário a ${employee.name}`} description="Só aparecem usuários ativos sem funcionário." open={open}
      onOpenChange={(o) => { if (o) setUserId(''); onOpenChange(o) }}
      onConfirm={async () => {
        if (!userId) throw new DialogMessage('Escolha o usuário.')
        await linkEmployeeUser(employee.id, { userId })
        await onDone()
      }}>
      <Field label="Usuário" required hint={users.data?.length === 0 ? 'Todos os usuários ativos já têm funcionário. Convide um usuário em Sistema → Usuários.' : undefined}>
        {(p) => (
          <select {...p} value={userId} onChange={(e) => setUserId(e.target.value)} className={inputClass}>
            <option value="">{users.isPending ? 'Carregando…' : 'Escolha'}</option>
            {(users.data ?? []).map((u) => <option key={u.userId} value={u.userId}>{u.name} ({u.email})</option>)}
          </select>
        )}
      </Field>
    </ConfirmDialog>
  )
}
