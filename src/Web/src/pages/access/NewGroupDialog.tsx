import * as Dialog from '@radix-ui/react-dialog'
import { useState } from 'react'
import { createGroup, duplicateGroup, getGroup, updateGroup } from '@/api/generated/bemaqui'
import type { GroupSummary } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { toastDone } from '@/components/ui/toast'

/** "Novo grupo" (propositivo, FND-06b): oferece começar de um grupo existente em vez da matriz vazia. */
export function NewGroupDialog({ groups, onCreated }: { groups: readonly GroupSummary[]; onCreated: (id: string) => Promise<void> }) {
  const [open, setOpen] = useState(false)
  const [name, setName] = useState('')
  const [from, setFrom] = useState('')
  const [error, setError] = useState<string>()
  const sources = groups.filter((g) => g.isActive && !g.isSystem)

  const create = async () => {
    setError(undefined)
    try {
      let id: string
      if (from) {
        id = (await duplicateGroup(from)).id
        const source = groups.find((g) => g.id === from)!
        if (name.trim()) await updateGroup(id, { name: name.trim(), description: source.description, requiresTwoFactor: source.requiresTwoFactor, modules: await currentModules(id) })
      } else {
        id = (await createGroup({ name: name.trim(), description: '', requiresTwoFactor: false, modules: [] })).id
      }

      setOpen(false)
      setName('')
      setFrom('')
      await onCreated(id)
      toastDone('Grupo criado')
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível criar o grupo.')
    }
  }

  return (
    <Dialog.Root open={open} onOpenChange={setOpen}>
      <Dialog.Trigger className="w-full rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold">Novo grupo</Dialog.Trigger>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-foreground/30" />
        <Dialog.Content className="fixed left-1/2 top-[15vh] z-50 w-[min(420px,calc(100vw-32px))] -translate-x-1/2 space-y-3 rounded-md border border-border bg-popover p-5 text-popover-foreground shadow-lg">
          <Dialog.Title className="text-base font-semibold">Novo grupo</Dialog.Title>
          <Dialog.Description className="text-sm text-muted-foreground">Comece de um grupo parecido e ajuste só o que muda.</Dialog.Description>
          <FormError message={error} />
          <label className="block text-sm font-medium">Nome<input value={name} onChange={(e) => setName(e.target.value)} className={inputClass + ' mt-1'} /></label>
          <label className="block text-sm font-medium">
            Começar de
            <select value={from} onChange={(e) => setFrom(e.target.value)} className={inputClass + ' mt-1'}>
              <option value="">Grupo vazio</option>
              {sources.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}
            </select>
          </label>
          <div className="flex justify-end gap-2 pt-2">
            <Dialog.Close className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</Dialog.Close>
            <button type="button" disabled={!from && !name.trim()} onClick={() => void create()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">Criar grupo</button>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}

/** Matriz da cópia recém-criada, para renomear sem perder as permissões. */
async function currentModules(id: string) {
  const detail = await getGroup(id)
  return detail.modules.filter((m) => m.enabled).map((m) => ({ moduleCode: m.code, enabled: true, actions: m.actions }))
}
