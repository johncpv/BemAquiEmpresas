import { useNavigate, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { Controller } from 'react-hook-form'
import { checkPartyDocument, createParty, restoreParty } from '@/api/generated/bemaqui'
import type { PartyRoleKind } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { ChipGroup, ToggleGroup } from '@/components/form/choice'
import { Field, FormError, FormSection } from '@/components/form/form'
import { applyServerErrors, inputClass, useZodForm } from '@/components/form/form-utils'
import { CepInput, DocumentInput, PhoneInput } from '@/components/form/inputs'
import { UnsavedChangesGuard } from '@/components/form/UnsavedChangesGuard'
import { toastDone } from '@/components/ui/toast'
import { documentKind } from '@/lib/masks'
import { PageHeading } from '@/shell/PageHeading'
import { DocumentNotice, type ExistingParty } from './DocumentNotice'
import { ROLE_OPTIONS, STATES } from './labels'
import { addRoles } from './party-actions'
import { emptyParty, newPartySchema, toCreateRequest } from './party-form'

/**
 * Novo terceiro (CAD-01a CA 2 a 9, molde "cadastro em seções"): o documento vem primeiro e a duplicidade é conferida
 * antes de qualquer outra ação; o papel vem pré-marcado pela tela de origem (?papel=).
 */
export function NewPartyPage() {
  const navigate = useNavigate()
  const search = useSearch({ strict: false }) as Record<string, unknown>
  const origin = ROLE_OPTIONS.find((o) => o.value === search.papel)?.value
  const form = useZodForm(newPartySchema, { defaultValues: emptyParty(origin) })
  const [kindTouched, setKindTouched] = useState(false)
  const [existing, setExisting] = useState<ExistingParty | null>(null)
  const [busy, setBusy] = useState(false)
  const [saved, setSaved] = useState(false)
  const errors = form.formState.errors
  const kind = form.watch('kind')
  const document = form.watch('document')
  const kindLabel = kind === 'PF' ? 'CPF' : 'CNPJ'
  // Depois de salvar, a guarda de alteração não salva não vale (o estado ainda não re-renderizou).
  const open = (id: string, novo = false) => navigate({ to: '/app/cadastros/terceiros/$id', params: { id }, search: novo ? { novo: '1' } : {}, ignoreBlocker: true } as never)

  const onDocument = (value: string) => {
    form.setValue('document', value, { shouldDirty: true })
    setExisting(null)
    const inferred = documentKind(value)
    if (inferred && !kindTouched) form.setValue('kind', inferred)
  }

  // Ao sair do campo: duplicidade primeiro (CA 4), antes da consulta à Receita da CAD-01b.
  const checkDocument = async () => {
    if (!(await form.trigger('document')) || !document) return
    const check = await checkPartyDocument({ document })
    setExisting(check.exists && check.id ? { id: check.id, name: check.name ?? '', deleted: check.deleted, roles: check.roles } : null)
  }

  const submit = form.handleSubmit(async (values) => {
    try {
      const created = await createParty(toCreateRequest(values))
      setSaved(true)
      toastDone('Terceiro salvo')
      await open(created.id, true)
    } catch (e) {
      if (e instanceof ApiError && (e.code === 'party_exists' || e.code === 'party_deleted')) {
        setExisting({ id: String(e.data.existingId), name: String(e.data.existingName ?? ''), deleted: e.code === 'party_deleted', roles: [] })
      } else applyServerErrors(form, e)
    }
  })

  const act = async (run: () => Promise<{ id: string }>, done: string) => {
    setBusy(true)
    try {
      const party = await run()
      setSaved(true)
      toastDone(done)
      await open(party.id)
    } catch (e) {
      applyServerErrors(form, e)
    } finally {
      setBusy(false)
    }
  }

  const actions = (
    <div className="flex flex-wrap gap-2">
      <button type="button" onClick={() => window.history.back()} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
      <button type="submit" form="novo-terceiro" disabled={form.formState.isSubmitting || busy} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">Salvar terceiro</button>
    </div>
  )

  const text = (name: 'legalName' | 'tradeName' | 'email' | 'street' | 'number' | 'complement' | 'district' | 'city' | 'contactName' | 'contactEmail') => form.register(name)

  return (
    <>
      <UnsavedChangesGuard when={form.formState.isDirty && !saved} onSave={async () => { await submit(); return false }} />
      <PageHeading title="Novo terceiro">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">Cliente, fornecedor, instalador ou transportadora. Um mesmo documento pode ter vários papéis.</p>
          {actions}
        </div>
      </PageHeading>

      <form id="novo-terceiro" onSubmit={submit} noValidate className="max-w-3xl space-y-4">
        <FormError message={errors.root?.server?.message} />

        <FormSection title="Identificação" description="Comece pelo documento: ele define PF ou PJ e confere se o terceiro já existe.">
          <Field label="CPF ou CNPJ" error={errors.document?.message} hint="Aceita o CNPJ com letras. Em branco, o terceiro fica com o aviso &quot;Sem documento&quot;." dica="Sem documento, o terceiro não recebe nota fiscal nem boleto. Dá para completar depois.">
            {(p) => <DocumentInput {...p} value={document} onChange={onDocument} onBlur={() => void checkDocument()} />}
          </Field>
          <div className="space-y-1">
            <span className="block text-sm font-medium">Tipo</span>
            <Controller control={form.control} name="kind" render={({ field }) => (
              <ToggleGroup label="Tipo de pessoa" value={field.value} onChange={(v) => { setKindTouched(true); field.onChange(v) }} options={[{ value: 'PJ', label: 'Pessoa jurídica' }, { value: 'PF', label: 'Pessoa física' }]} />
            )} />
          </div>
          {existing ? (
            <DocumentNotice existing={existing} wanted={form.watch('roles') as PartyRoleKind[]} kindLabel={kindLabel} busy={busy}
              onOpen={() => void open(existing.id)}
              onAddRoles={(roles) => void act(() => addRoles(existing.id, roles), roles.length > 1 ? 'Papéis adicionados' : 'Papel adicionado')}
              onRestore={() => void act(() => restoreParty(existing.id), 'Cadastro restaurado')} />
          ) : null}
          <Field label={kind === 'PF' ? 'Nome' : 'Razão social'} error={errors.legalName?.message} required>
            {(p) => <input {...p} {...text('legalName')} autoComplete="off" className={inputClass} />}
          </Field>
          {kind === 'PJ' ? <Field label="Nome fantasia" error={errors.tradeName?.message}>{(p) => <input {...p} {...text('tradeName')} className={inputClass} />}</Field> : <div />}
          <div className="sm:col-span-2">
            <Controller control={form.control} name="roles" render={({ field }) => (
              <ChipGroup legend="Papéis" required value={field.value} onChange={field.onChange} error={errors.roles?.message} options={ROLE_OPTIONS} />
            )} />
          </div>
        </FormSection>

        <FormSection title="Endereço de cobrança" description="Opcional. Vira o endereço padrão; endereços de entrega e de obra entram na aba Endereços depois de salvar.">
          <Field label="CEP" error={errors.zipCode?.message}>
            {(p) => <Controller control={form.control} name="zipCode" render={({ field }) => <CepInput {...p} value={field.value} onChange={field.onChange} />} />}
          </Field>
          <Field label="Logradouro" error={errors.street?.message}>{(p) => <input {...p} {...text('street')} className={inputClass} />}</Field>
          <Field label="Número" error={errors.number?.message}>{(p) => <input {...p} {...text('number')} className={inputClass} />}</Field>
          <Field label="Complemento">{(p) => <input {...p} {...text('complement')} className={inputClass} />}</Field>
          <Field label="Bairro" error={errors.district?.message}>{(p) => <input {...p} {...text('district')} className={inputClass} />}</Field>
          <Field label="Cidade" error={errors.city?.message}>{(p) => <input {...p} {...text('city')} className={inputClass} />}</Field>
          <Field label="UF" error={errors.state?.message}>
            {(p) => (
              <select {...p} {...form.register('state')} className={inputClass}>
                <option value="">Escolha</option>
                {STATES.map((s) => <option key={s} value={s}>{s}</option>)}
              </select>
            )}
          </Field>
        </FormSection>

        <FormSection title="Contato principal" description="Opcional. Recebe a cobrança; outros contatos entram na aba Contatos.">
          <Field label="Nome do contato" error={errors.contactName?.message}>{(p) => <input {...p} {...text('contactName')} className={inputClass} />}</Field>
          <Field label="E-mail" error={errors.contactEmail?.message}>{(p) => <input {...p} type="email" {...text('contactEmail')} className={inputClass} />}</Field>
          <Field label="Telefone" error={errors.contactPhone?.message}>
            {(p) => <Controller control={form.control} name="contactPhone" render={({ field }) => <PhoneInput {...p} value={field.value} onChange={field.onChange} />} />}
          </Field>
        </FormSection>

        <div className="flex justify-end">{actions}</div>
      </form>
    </>
  )
}
