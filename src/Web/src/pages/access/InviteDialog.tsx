import * as Dialog from '@radix-ui/react-dialog'
import { useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import { inviteUser, useListGroups } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { applyServerErrors, inputClass, useZodForm } from '@/components/form/form-utils'
import { z } from '@/lib/zod'

const LAST_GROUPS = 'bq-convite-grupos'
const schema = z.object({ name: z.string().min(2, 'Informe o nome.'), email: z.email('Informe um e-mail válido.') })

const readLast = (): string[] => {
  try {
    const v: unknown = JSON.parse(localStorage.getItem(LAST_GROUPS) ?? '[]')
    return Array.isArray(v) ? v.filter((x): x is string => typeof x === 'string') : []
  } catch {
    return []
  }
}

/**
 * "Convidar usuário" (FND-06c CA 5, 6 e 8): os grupos do último convite vêm marcados; e-mail já cadastrado oferece abrir
 * o existente; depois de enviar, o próprio diálogo confirma e oferece convidar outra pessoa (propositivo, ADR 0021).
 */
export function InviteDialog({ onInvited }: { onInvited: () => Promise<void> }) {
  const [open, setOpen] = useState(false)
  const groups = useListGroups({ query: { enabled: open } })
  const [selected, setSelected] = useState<Set<string>>(() => new Set(readLast()))
  const [existing, setExisting] = useState<string | null>(null)
  const [sent, setSent] = useState<string | null>(null)
  const navigate = useNavigate()
  const form = useZodForm(schema, { defaultValues: { name: '', email: '' } })

  const submit = form.handleSubmit(async (values) => {
    setExisting(null)
    try {
      const invited = await inviteUser({ ...values, groupIds: [...selected] })
      try {
        localStorage.setItem(LAST_GROUPS, JSON.stringify([...selected]))
      } catch {
        // sem armazenamento: só não lembra os grupos
      }

      await onInvited()
      form.reset()
      setSent(invited.email)
    } catch (e) {
      if (e instanceof ApiError && e.code === 'user_exists') setExisting(values.email)
      else applyServerErrors(form, e)
    }
  })

  return (
    <Dialog.Root open={open} onOpenChange={(o) => { setOpen(o); if (!o) setSent(null) }}>
      <Dialog.Trigger className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Convidar usuário</Dialog.Trigger>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-foreground/30" />
        <Dialog.Content className="fixed left-1/2 top-[8vh] z-50 max-h-[84vh] w-[min(460px,calc(100vw-32px))] -translate-x-1/2 overflow-y-auto rounded-md border border-border bg-popover p-5 text-popover-foreground shadow-lg">
          <Dialog.Title className="text-base font-semibold">Convidar usuário</Dialog.Title>
          <Dialog.Description className="mt-1 text-sm text-muted-foreground">A pessoa recebe um e-mail com o link para criar o acesso.</Dialog.Description>
          {sent ? (
            <div className="mt-3 space-y-3">
              <p role="status" className="rounded-md border border-ok-soft bg-ok-soft px-3 py-2 text-sm text-ok-ink">Convite enviado para {sent}</p>
              <div className="flex justify-end gap-2">
                <Dialog.Close className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Fechar</Dialog.Close>
                <button type="button" onClick={() => setSent(null)} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Convidar outra pessoa</button>
              </div>
            </div>
          ) : (
          <form onSubmit={submit} noValidate className="mt-3 space-y-3">
            <FormError message={form.formState.errors.root?.server?.message} />
            {existing ? (
              <div role="alert" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">
                Já existe um usuário com esse e-mail.{' '}
                <button type="button" className="font-semibold underline" onClick={() => { setOpen(false); void navigate({ to: '.', search: { q: existing }, replace: true } as never) }}>Abrir o usuário</button>
              </div>
            ) : null}
            <Field label="Nome" error={form.formState.errors.name?.message}>{(p) => <input {...p} {...form.register('name')} className={inputClass} />}</Field>
            <Field label="E-mail" error={form.formState.errors.email?.message}>{(p) => <input {...p} type="email" {...form.register('email')} className={inputClass} />}</Field>
            <fieldset className="space-y-1">
              <legend className="text-sm font-medium">Grupos</legend>
              {(groups.data ?? []).filter((g) => g.isActive).map((g) => (
                <label key={g.id} className="flex items-center gap-2 text-sm">
                  <input type="checkbox" checked={selected.has(g.id)} onChange={(e) => setSelected((s) => { const n = new Set(s); if (e.target.checked) n.add(g.id); else n.delete(g.id); return n })} className="size-4 accent-primary" />
                  {g.name}
                </label>
              ))}
            </fieldset>
            <div className="flex justify-end gap-2 pt-2">
              <Dialog.Close className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</Dialog.Close>
              <button type="submit" disabled={form.formState.isSubmitting} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Enviar convite</button>
            </div>
          </form>
          )}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}
