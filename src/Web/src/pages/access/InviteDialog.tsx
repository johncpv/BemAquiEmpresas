import * as Dialog from '@radix-ui/react-dialog'
import { useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { useEffect, useState } from 'react'
import { getGetEmployeeUserQueryKey, getListInvitableEmployeesQueryKey, inviteUser, useListGroups, useListInvitableEmployees } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { applyServerErrors, inputClass, useZodForm } from '@/components/form/form-utils'
import { z } from '@/lib/zod'
import { clearInviteSuggestion, peekInviteSuggestion } from '@/pages/hr/payroll/payslip-notices'
import { useCan } from '@/pages/use-can'

const LAST_GROUPS = 'bq-convite-grupos'
const schema = z.object({ name: z.string().min(2, 'Informe o nome.'), email: z.email('Informe um e-mail válido.'), employeeId: z.string() })

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
 * o existente; depois de enviar, o próprio diálogo confirma e oferece convidar outra pessoa (propositivo, ADR 0021). O campo opcional
 * "Funcionário" (USR-01 CA 1 a 4, só com hr.edit) lista os ativos sem usuário e preenche nome e e-mail; o vínculo nasce com o convite.
 */
export function InviteDialog({ onInvited }: { onInvited: () => Promise<void> }) {
  // "Convidar usuário" do aviso de holerite (RH-02b CA 7): abre já com o nome e o e-mail pessoal sugeridos.
  const [suggestion] = useState(peekInviteSuggestion)
  const [open, setOpen] = useState(() => suggestion !== null)
  useEffect(() => clearInviteSuggestion(), [])
  const groups = useListGroups({ query: { enabled: open } })
  const canLinkEmployee = useCan('hr.edit')
  const staff = useListInvitableEmployees({ query: { enabled: open && canLinkEmployee } })
  const client = useQueryClient()
  const [selected, setSelected] = useState<Set<string>>(() => new Set(readLast()))
  const [existing, setExisting] = useState<string | null>(null)
  const [sent, setSent] = useState<string | null>(null)
  const navigate = useNavigate()
  const form = useZodForm(schema, { defaultValues: { name: suggestion?.name ?? '', email: suggestion?.email ?? '', employeeId: canLinkEmployee ? suggestion?.employeeId ?? '' : '' } })
  const employeeId = form.watch('employeeId')

  // Funcionário escolhido antes de a lista chegar (ficha ou aviso de holerite): o e-mail pessoal entra se o campo estiver vazio.
  useEffect(() => {
    const chosen = staff.data?.find((e) => e.id === employeeId)
    if (chosen?.email && !form.getValues('email')) form.setValue('email', chosen.email)
  }, [staff.data, employeeId, form])

  const choose = (id: string) => {
    form.setValue('employeeId', id)
    const chosen = staff.data?.find((e) => e.id === id)
    if (!chosen) return
    form.setValue('name', chosen.name, { shouldValidate: true })
    if (chosen.email) form.setValue('email', chosen.email, { shouldValidate: true })
  }

  const submit = form.handleSubmit(async ({ employeeId: chosen, ...values }) => {
    setExisting(null)
    try {
      const invited = await inviteUser({ ...values, groupIds: [...selected], employeeId: chosen || null })
      if (chosen) {
        void client.invalidateQueries({ queryKey: getListInvitableEmployeesQueryKey() })
        void client.invalidateQueries({ queryKey: getGetEmployeeUserQueryKey(chosen) })
      }
      try {
        localStorage.setItem(LAST_GROUPS, JSON.stringify([...selected]))
      } catch {
        // sem armazenamento: só não lembra os grupos
      }

      await onInvited()
      form.reset({ name: '', email: '', employeeId: '' })
      setSent(invited.email)
    } catch (e) {
      if (e instanceof ApiError && e.code === 'user_exists') setExisting(values.email)
      else applyServerErrors(form, e)
    }
  })

  return (
    <Dialog.Root open={open} onOpenChange={(o) => { setOpen(o); if (!o) { setSent(null); clearInviteSuggestion() } }}>
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
            {canLinkEmployee ? (
              <Field label="Funcionário" error={form.formState.errors.employeeId?.message}
                hint={staff.data?.length === 0 ? 'Todos os funcionários ativos já têm usuário.' : 'Opcional. Deixe em branco para quem não é funcionário (ex.: a contadora).'}
                dica="Liga o login ao funcionário (ponto, apontamento, holerite). Ao registrar o desligamento, o login é desativado.">
                {(p) => (
                  <select {...p} value={employeeId} onChange={(e) => choose(e.target.value)} className={inputClass}>
                    <option value="">{staff.isPending ? 'Carregando…' : 'Nenhum (usuário externo)'}</option>
                    {(staff.data ?? []).map((e) => <option key={e.id} value={e.id}>{e.name}{e.registration ? ` · ${e.registration}` : ''}</option>)}
                  </select>
                )}
              </Field>
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
