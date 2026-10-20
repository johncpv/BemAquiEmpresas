import { useQueryClient } from '@tanstack/react-query'
import { Controller } from 'react-hook-form'
import { getGetCompanyProfileQueryKey, getGetTenantPublicContextQueryKey, lookupCompanyCep, saveCompanyProfile, useGetCompanyProfile } from '@/api/generated/bemaqui'
import type { CompanyProfileDto } from '@/api/generated/model'
import { Field, FormError, FormSection } from '@/components/form/form'
import { applyServerErrors, inputClass, useZodForm } from '@/components/form/form-utils'
import { CepInput, PhoneInput } from '@/components/form/inputs'
import { UnsavedChangesGuard } from '@/components/form/UnsavedChangesGuard'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { notifySuccess } from '@/components/ui/messages'
import { cnpjMask } from '@/lib/masks'
import { STATES } from '@/pages/parties/labels'
import { emptyOnly, useCepLookup } from '@/pages/parties/lookups'
import { PageHeading } from '@/shell/PageHeading'
import { SettingsTabs } from '../SettingsTabs'
import { useCanSettings } from '../use-can-settings'
import { CompanyLogo } from './CompanyLogo'
import { companySchema, toInput, toValues } from './company-form'

/** Configurações → Empresa (CAD-06 CA 1 a 8): dados que saem nos documentos e o logo. Sem settings.edit, só leitura. */
export function CompanyPage() {
  const query = useGetCompanyProfile()
  const canEdit = useCanSettings('edit')
  return (
    <>
      <PageHeading title="Configurações: Empresa" />
      <SettingsTabs />
      {query.isPending ? <Skeleton className="h-96 max-w-3xl" /> : query.isError || !query.data ? <ErrorState onRetry={() => query.refetch()} /> : (
        <CompanyForm key={query.data.id ?? 'novo'} profile={query.data} canEdit={canEdit} />
      )}
    </>
  )
}

type TextField = 'legalName' | 'tradeName' | 'municipalRegistration' | 'stateRegistration' | 'street' | 'number' | 'complement' | 'district' | 'city' | 'cityIbge' | 'email'

function CompanyForm({ profile, canEdit }: { profile: CompanyProfileDto; canEdit: boolean }) {
  const client = useQueryClient()
  const form = useZodForm(companySchema, { defaultValues: toValues(profile) })
  const errors = form.formState.errors
  const text = (name: TextField) => form.register(name)
  const cep = useCepLookup((a) => {
    for (const [key, value] of Object.entries(emptyOnly(form.getValues() as unknown as Record<string, string>, { street: a.street, district: a.district, city: a.city, state: a.state, cityIbge: a.ibgeCode }))) {
      form.setValue(key as never, value as never, { shouldDirty: true })
    }
  }, lookupCompanyCep)

  const submit = form.handleSubmit(async (v) => {
    try {
      const saved = await saveCompanyProfile(toInput(v))
      form.reset(toValues(saved))
      client.setQueryData(getGetCompanyProfileQueryKey(), saved)
      await client.invalidateQueries({ queryKey: getGetTenantPublicContextQueryKey() })
      notifySuccess('Dados da empresa salvos')
    } catch (e) {
      applyServerErrors(form, e)
    }
  })

  const input = (label: string, name: TextField, extra: { required?: boolean; hint?: string } = {}) => (
    <Field label={label} required={extra.required} hint={extra.hint} error={errors[name]?.message}>{(p) => <input {...p} {...text(name)} className={inputClass} />}</Field>
  )

  return (
    <form id="empresa" onSubmit={submit} noValidate className="max-w-3xl space-y-4">
      <UnsavedChangesGuard when={form.formState.isDirty} onSave={async () => { await submit(); return !Object.keys(form.formState.errors).length }} />
      {!profile.exists ? <p role="status" className="rounded-md border border-primary-soft bg-primary-soft px-3 py-2 text-sm">Os dados vieram do cadastro da plataforma. Confira e clique em <b>Salvar dados da empresa</b>.</p> : null}
      <FormError message={errors.root?.server?.message} />
      <fieldset disabled={!canEdit} className="space-y-4">
        <FormSection title="Identificação">
          <Field label="CNPJ" dica="O CNPJ é alterado pelo suporte da plataforma.">
            {(p) => <input {...p} value={profile.document ? cnpjMask.format(profile.document) : ''} readOnly disabled className={inputClass} />}
          </Field>
          {input('Razão social', 'legalName', { required: true })}
          {input('Nome fantasia', 'tradeName', { hint: 'Aparece no topo do sistema e no login.' })}
          {input('Inscrição estadual', 'stateRegistration', { hint: 'Só os dígitos, ou ISENTO.' })}
          {input('Inscrição municipal', 'municipalRegistration')}
        </FormSection>
        <FormSection title="Endereço">
          <Field label="CEP" error={errors.zipCode?.message} hint={cep.message} hintLive>
            {(p) => <Controller control={form.control} name="zipCode" render={({ field }) => <CepInput {...p} value={field.value} onChange={(v) => { field.onChange(v); void cep.onCep(v) }} />} />}
          </Field>
          {input('Logradouro', 'street')}
          {input('Número', 'number')}
          {input('Complemento', 'complement')}
          {input('Bairro', 'district')}
          {input('Cidade', 'city')}
          <Field label="UF" error={errors.state?.message}>
            {(p) => (
              <select {...p} {...form.register('state')} className={inputClass}>
                <option value="">Escolha</option>
                {STATES.map((s) => <option key={s} value={s}>{s}</option>)}
              </select>
            )}
          </Field>
          {input('Código IBGE', 'cityIbge', { hint: 'A consulta de CEP preenche.' })}
        </FormSection>
        <FormSection title="Contato">
          <Field label="Telefone" error={errors.phone?.message}>
            {(p) => <Controller control={form.control} name="phone" render={({ field }) => <PhoneInput {...p} value={field.value} onChange={field.onChange} />} />}
          </Field>
          {input('E-mail', 'email')}
        </FormSection>
      </fieldset>
      <FormSection title="Logo">
        <CompanyLogo profile={profile} canEdit={canEdit} />
      </FormSection>
      {canEdit ? <div className="flex justify-end"><button type="submit" disabled={form.formState.isSubmitting} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar dados da empresa</button></div> : null}
    </form>
  )
}
