import { Controller } from 'react-hook-form'
import { addPartyContact, updatePartyContact } from '@/api/generated/bemaqui'
import type { PartyContactDto, PartyDetail } from '@/api/generated/model'
import { Field, FormError } from '@/components/form/form'
import { applyServerErrors, inputClass, useZodForm } from '@/components/form/form-utils'
import { PhoneInput } from '@/components/form/inputs'
import { SidePanel } from '@/components/ui/side-panel'
import { toastDone } from '@/components/ui/toast'
import { Dica } from '@/help/Dica'
import { z } from '@/lib/zod'

const schema = z.object({
  name: z.string().trim().min(1, 'Informe o nome do contato.').max(150, 'Use até 150 caracteres.'),
  role: z.string().max(100, 'Use até 100 caracteres.'),
  email: z.union([z.literal(''), z.email('E-mail inválido.')]),
  phone: z.string().refine((v) => v === '' || v.length === 10 || v.length === 11, 'Telefone com DDD: 10 ou 11 dígitos.'),
  receivesBilling: z.boolean(),
})

/** Painel do contato (molde "painel lateral"): guarda de alteração não salva ao fechar. */
export function ContactPanel({ party, contact, onClose, onSaved }: { party: PartyDetail; contact: PartyContactDto | null; onClose: () => void; onSaved: (p: PartyDetail) => void }) {
  const form = useZodForm(schema, {
    defaultValues: { name: contact?.name ?? '', role: contact?.role ?? '', email: contact?.email ?? '', phone: contact?.phone ?? '', receivesBilling: contact?.receivesBilling ?? party.contacts.length === 0 },
  })
  const errors = form.formState.errors
  const save = async () => {
    let ok = false
    await form.handleSubmit(async (v) => {
      const body = { name: v.name.trim(), role: v.role.trim() || null, email: v.email || null, phone: v.phone || null, receivesBilling: v.receivesBilling }
      try {
        onSaved(contact ? await updatePartyContact(party.id, contact.id, body) : await addPartyContact(party.id, body))
        toastDone('Contato salvo')
        ok = true
        onClose()
      } catch (e) {
        applyServerErrors(form, e)
      }
    })()
    return ok
  }

  return (
    <SidePanel open onOpenChange={(o) => !o && onClose()} title={contact ? 'Editar contato' : 'Novo contato'} dirty={form.formState.isDirty} onSave={save}
      footer={(
        <>
          <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
          <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar contato</button>
        </>
      )}>
      <div className="space-y-3">
        <FormError message={errors.root?.server?.message} />
        <Field label="Nome" required error={errors.name?.message}>{(p) => <input {...p} {...form.register('name')} className={inputClass} />}</Field>
        <Field label="Cargo" error={errors.role?.message}>{(p) => <input {...p} {...form.register('role')} className={inputClass} />}</Field>
        <Field label="E-mail" error={errors.email?.message}>{(p) => <input {...p} type="email" {...form.register('email')} className={inputClass} />}</Field>
        <Field label="Telefone" error={errors.phone?.message}>
          {(p) => <Controller control={form.control} name="phone" render={({ field }) => <PhoneInput {...p} value={field.value} onChange={field.onChange} />} />}
        </Field>
        <div className="flex items-center">
          <label className="flex items-center gap-2 text-sm"><input type="checkbox" {...form.register('receivesBilling')} className="size-4 accent-primary" /> Recebe cobrança</label>
          <Dica nome="Recebe cobrança" texto="Os boletos e avisos de vencimento deste terceiro vão para este contato." />
        </div>
      </div>
    </SidePanel>
  )
}
