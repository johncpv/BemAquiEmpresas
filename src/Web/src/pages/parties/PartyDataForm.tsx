import { Controller } from 'react-hook-form'
import type { PartyDetail, PartyRoleKind, TaxRegime } from '@/api/generated/model'
import { ChipGroup } from '@/components/form/choice'
import { Field, FormError } from '@/components/form/form'
import { applyServerErrors, inputClass, useZodForm } from '@/components/form/form-utils'
import { DocumentInput, PhoneInput } from '@/components/form/inputs'
import { UnsavedChangesGuard } from '@/components/form/UnsavedChangesGuard'
import { toastDone } from '@/components/ui/toast'
import { isValidCnpj, isValidCpf, onlyAlnum } from '@/lib/masks'
import { z } from '@/lib/zod'
import { ROLE_OPTIONS, TAX_REGIME_LABELS } from './labels'
import { updateFrom } from './party-actions'

const schema = z.object({
  document: z.string(),
  legalName: z.string().trim().min(2, 'Informe o nome ou a razão social.').max(200, 'Use até 200 caracteres.'),
  tradeName: z.string().max(200, 'Use até 200 caracteres.'),
  roles: z.array(z.enum(['Customer', 'Supplier', 'Installer', 'Carrier'])).min(1, 'O terceiro precisa de ao menos um papel.'),
  stateRegistration: z.string().refine((v) => v === '' || /^(ISENTO|\d{2,14})$/i.test(v.trim()), 'Informe só os dígitos da inscrição estadual, ou ISENTO.'),
  municipalRegistration: z.string().max(20, 'Use até 20 caracteres.'),
  taxRegime: z.enum(['NotInformed', 'SimplesNacional', 'Mei', 'LucroPresumido', 'LucroReal', 'Exempt']),
  email: z.union([z.literal(''), z.email('E-mail inválido.')]),
  phone: z.string().refine((v) => v === '' || v.length === 10 || v.length === 11, 'Telefone com DDD: 10 ou 11 dígitos.'),
  notes: z.string().max(2000, 'Use até 2.000 caracteres.'),
})

/** Aba Dados (CA 10 e 15): papéis, dados fiscais e contato do cadastro. Sem parties.edit, fica só leitura. */
export function PartyDataForm({ party, canEdit, onSaved }: { party: PartyDetail; canEdit: boolean; onSaved: (p: PartyDetail) => void }) {
  const form = useZodForm(schema.superRefine((v, ctx) => {
    if (v.document && !(party.kind === 'PF' ? isValidCpf(v.document) : isValidCnpj(v.document))) ctx.addIssue({ code: 'custom', path: ['document'], message: party.kind === 'PF' ? 'CPF inválido.' : 'CNPJ inválido.' })
  }), {
    defaultValues: {
      document: onlyAlnum(party.document ?? ''), legalName: party.legalName, tradeName: party.tradeName ?? '', roles: party.roles,
      stateRegistration: party.stateRegistration ?? '', municipalRegistration: party.municipalRegistration ?? '', taxRegime: party.taxRegime,
      email: party.email ?? '', phone: party.phone ?? '', notes: party.notes ?? '',
    },
  })
  const errors = form.formState.errors
  const text = (v: string) => (v.trim() ? v.trim() : null)

  const submit = form.handleSubmit(async (v) => {
    try {
      const saved = await updateFrom(party, {
        document: text(v.document), legalName: v.legalName.trim(), tradeName: party.kind === 'PF' ? null : text(v.tradeName), roles: v.roles as PartyRoleKind[],
        stateRegistration: text(v.stateRegistration), municipalRegistration: text(v.municipalRegistration), taxRegime: v.taxRegime as TaxRegime,
        email: text(v.email), phone: text(v.phone), notes: text(v.notes),
      })
      form.reset(form.getValues())
      onSaved(saved)
      toastDone('Terceiro salvo')
    } catch (e) {
      applyServerErrors(form, e)
    }
  })

  return (
    <form id="dados-terceiro" onSubmit={submit} noValidate className="max-w-3xl">
      <UnsavedChangesGuard when={form.formState.isDirty} onSave={async () => { await submit(); return !Object.keys(form.formState.errors).length }} />
      <fieldset disabled={!canEdit} className="grid gap-3 sm:grid-cols-2">
        <div className="sm:col-span-2"><FormError message={errors.root?.server?.message} /></div>
        <Field label={party.kind === 'PF' ? 'CPF' : 'CNPJ'} error={errors.document?.message}>
          {(p) => <Controller control={form.control} name="document" render={({ field }) => <DocumentInput {...p} value={field.value} onChange={field.onChange} />} />}
        </Field>
        <Field label={party.kind === 'PF' ? 'Nome' : 'Razão social'} required error={errors.legalName?.message}>{(p) => <input {...p} {...form.register('legalName')} className={inputClass} />}</Field>
        {party.kind === 'PJ' ? <Field label="Nome fantasia" error={errors.tradeName?.message}>{(p) => <input {...p} {...form.register('tradeName')} className={inputClass} />}</Field> : null}
        <div className="sm:col-span-2">
          <Controller control={form.control} name="roles" render={({ field }) => (
            <ChipGroup legend="Papéis" required value={field.value} onChange={field.onChange} error={errors.roles?.message} options={ROLE_OPTIONS} disabled={!canEdit} />
          )} />
        </div>
        <Field label="Inscrição estadual" error={errors.stateRegistration?.message} hint="Só os dígitos, ou ISENTO.">{(p) => <input {...p} {...form.register('stateRegistration')} className={inputClass} />}</Field>
        <Field label="Inscrição municipal" error={errors.municipalRegistration?.message}>{(p) => <input {...p} {...form.register('municipalRegistration')} className={inputClass} />}</Field>
        <Field label="Regime tributário" dica="Define como os impostos são calculados nas notas e nos custos. Na dúvida, confira com a contadora ou na consulta do CNPJ.">
          {(p) => (
            <select {...p} {...form.register('taxRegime')} className={inputClass}>
              {Object.entries(TAX_REGIME_LABELS).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
            </select>
          )}
        </Field>
        <Field label="E-mail" error={errors.email?.message}>{(p) => <input {...p} type="email" {...form.register('email')} className={inputClass} />}</Field>
        <Field label="Telefone" error={errors.phone?.message}>
          {(p) => <Controller control={form.control} name="phone" render={({ field }) => <PhoneInput {...p} value={field.value} onChange={field.onChange} />} />}
        </Field>
        <div className="sm:col-span-2">
          <Field label="Observações" error={errors.notes?.message}>{(p) => <textarea {...p} rows={3} {...form.register('notes')} className={inputClass} />}</Field>
        </div>
      </fieldset>
    </form>
  )
}
