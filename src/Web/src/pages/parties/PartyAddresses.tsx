import { useState } from 'react'
import { Controller } from 'react-hook-form'
import { addPartyAddress, removePartyAddress, updatePartyAddress } from '@/api/generated/bemaqui'
import type { AddressKind, PartyAddressDto, PartyDetail } from '@/api/generated/model'
import { Field, FormError } from '@/components/form/form'
import { applyServerErrors, inputClass, useZodForm } from '@/components/form/form-utils'
import { CepInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { EmptyState } from '@/components/ui/states'
import { SidePanel } from '@/components/ui/side-panel'
import { Tag } from '@/components/ui/tag'
import { notifySuccess } from '@/components/ui/messages'
import { z } from '@/lib/zod'
import { ADDRESS_LABELS, STATES } from './labels'
import { emptyOnly, useCepLookup } from './lookups'

const schema = z.object({
  kind: z.enum(['Billing', 'Delivery', 'Site']),
  zipCode: z.string().length(8, 'CEP com 8 dígitos.'),
  street: z.string().trim().min(1, 'Informe o logradouro.'),
  number: z.string().trim().min(1, 'Informe o número (ou S/N).'),
  complement: z.string(),
  district: z.string().trim().min(1, 'Informe o bairro.'),
  city: z.string().trim().min(1, 'Informe a cidade.'),
  ibgeCode: z.string().refine((v) => v === '' || /^\d{7}$/.test(v), 'Código IBGE com 7 dígitos.'),
  state: z.enum(STATES, 'Escolha a UF.'),
  isDefault: z.boolean(),
})
type Values = z.input<typeof schema>

const blank = (kind: AddressKind): Values => ({ kind, zipCode: '', street: '', number: '', complement: '', district: '', city: '', ibgeCode: '', state: '' as never, isDefault: false })

/** Aba Endereços (CA 12 e 13): cobrança, entrega e obra; o primeiro de cada tipo é o padrão. */
export function PartyAddresses({ party, canEdit, onSaved, adding, onAddingChange }: { party: PartyDetail; canEdit: boolean; onSaved: (p: PartyDetail) => void; adding: AddressKind | null; onAddingChange: (k: AddressKind | null) => void }) {
  const [editing, setEditing] = useState<PartyAddressDto | null>(null)
  const open = editing !== null || adding !== null

  return (
    <div className="max-w-3xl space-y-3">
      {party.addresses.length === 0 ? (
        <EmptyState title="Nenhum endereço ainda." description="Cadastre o endereço de cobrança e os de entrega e de obra." action={canEdit ? <button type="button" onClick={() => onAddingChange('Billing')} className="text-sm font-semibold text-primary">Adicionar endereço</button> : undefined} />
      ) : (
        <ul className="space-y-2">
          {party.addresses.map((a) => (
            <li key={a.id} className="flex flex-wrap items-start justify-between gap-2 rounded-md border border-border bg-card p-3 text-sm">
              <div>
                <span className="flex flex-wrap gap-1"><Tag>{ADDRESS_LABELS[a.kind]}</Tag>{a.isDefault ? <Tag>Padrão</Tag> : null}</span>
                <p className="mt-1">{a.street}, {a.number}{a.complement ? ` — ${a.complement}` : ''}</p>
                <p className="text-muted-foreground">{a.district}, {a.city}/{a.state} · <span className="tabular-nums">{a.zipCode.replace(/^(\d{5})(\d{3})$/, '$1-$2')}</span></p>
              </div>
              {canEdit ? (
                <div className="flex flex-wrap gap-1">
                  {!a.isDefault ? <button type="button" onClick={async () => { onSaved(await updatePartyAddress(party.id, a.id, { ...a, isDefault: true })); notifySuccess('Endereço padrão alterado') }} className="rounded-md border border-border px-2 py-1 text-xs font-semibold">Tornar padrão</button> : null}
                  <button type="button" onClick={() => setEditing(a)} className="rounded-md border border-border px-2 py-1 text-xs font-semibold">Editar</button>
                  <ConfirmDialog verb="Remover" done="Endereço removido" destructive title="Remover este endereço?" description={a.isDefault ? 'Ele é o padrão: outro endereço do mesmo tipo passa a ser o padrão.' : undefined}
                    onConfirm={async () => onSaved(await removePartyAddress(party.id, a.id))}
                    trigger={<button type="button" className="rounded-md border border-border px-2 py-1 text-xs font-semibold">Remover</button>} />
                </div>
              ) : null}
            </li>
          ))}
        </ul>
      )}
      {canEdit && party.addresses.length > 0 ? <button type="button" onClick={() => onAddingChange('Delivery')} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Adicionar endereço</button> : null}
      {open ? <AddressPanel key={editing?.id ?? adding ?? ''} party={party} address={editing} kind={editing?.kind ?? adding ?? 'Billing'} onClose={() => { setEditing(null); onAddingChange(null) }} onSaved={onSaved} /> : null}
    </div>
  )
}

function AddressPanel({ party, address, kind, onClose, onSaved }: { party: PartyDetail; address: PartyAddressDto | null; kind: AddressKind; onClose: () => void; onSaved: (p: PartyDetail) => void }) {
  const form = useZodForm(schema, {
    defaultValues: address
      ? { kind: address.kind, zipCode: address.zipCode, street: address.street, number: address.number, complement: address.complement ?? '', district: address.district, city: address.city, ibgeCode: address.ibgeCode ?? '', state: address.state as Values['state'], isDefault: address.isDefault }
      : blank(kind),
  })
  const errors = form.formState.errors
  // CEP completo preenche só os campos vazios (CAD-01b CA 10).
  const cep = useCepLookup((a) => {
    const current = form.getValues() as unknown as Record<string, string>
    for (const [key, value] of Object.entries(emptyOnly(current, { street: a.street, district: a.district, city: a.city, state: a.state, ibgeCode: a.ibgeCode }))) {
      form.setValue(key as never, value as never, { shouldDirty: true })
    }
  })
  const save = async () => {
    let ok = false
    await form.handleSubmit(async (v) => {
      const body = { ...v, complement: v.complement.trim() || null, ibgeCode: v.ibgeCode || null }
      try {
        onSaved(address ? await updatePartyAddress(party.id, address.id, body) : await addPartyAddress(party.id, body))
        notifySuccess('Endereço salvo')
        ok = true
        onClose()
      } catch (e) {
        applyServerErrors(form, e)
      }
    })()
    return ok
  }

  return (
    <SidePanel open onOpenChange={(o) => !o && onClose()} title={address ? 'Editar endereço' : 'Novo endereço'} dirty={form.formState.isDirty} onSave={save}
      footer={(
        <>
          <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
          <button type="button" onClick={() => void save()} disabled={form.formState.isSubmitting} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar endereço</button>
        </>
      )}>
      <div className="space-y-3">
        <FormError message={errors.root?.server?.message} />
        <Field label="Tipo" required>
          {(p) => (
            <select {...p} {...form.register('kind')} className={inputClass}>
              {Object.entries(ADDRESS_LABELS).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
            </select>
          )}
        </Field>
        <Field label="CEP" required error={errors.zipCode?.message} hint={cep.message} hintLive>
          {(p) => <Controller control={form.control} name="zipCode" render={({ field }) => <CepInput {...p} value={field.value} onChange={(v) => { field.onChange(v); void cep.onCep(v) }} />} />}
        </Field>
        <Field label="Logradouro" required error={errors.street?.message}>{(p) => <input {...p} {...form.register('street')} className={inputClass} />}</Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Número" required error={errors.number?.message}>{(p) => <input {...p} {...form.register('number')} className={inputClass} />}</Field>
          <Field label="Complemento">{(p) => <input {...p} {...form.register('complement')} className={inputClass} />}</Field>
        </div>
        <Field label="Bairro" required error={errors.district?.message}>{(p) => <input {...p} {...form.register('district')} className={inputClass} />}</Field>
        <div className="grid grid-cols-[1fr_6rem] gap-3">
          <Field label="Cidade" required error={errors.city?.message}>{(p) => <input {...p} {...form.register('city')} className={inputClass} />}</Field>
          <Field label="UF" required error={errors.state?.message}>
            {(p) => (
              <select {...p} {...form.register('state')} className={inputClass}>
                <option value="">—</option>
                {STATES.map((s) => <option key={s} value={s}>{s}</option>)}
              </select>
            )}
          </Field>
        </div>
        <Field label="Código IBGE" error={errors.ibgeCode?.message} hint="Opcional. 7 dígitos; a consulta de CEP preenche.">{(p) => <input {...p} inputMode="numeric" maxLength={7} {...form.register('ibgeCode')} className={inputClass} />}</Field>
        <label className="flex items-center gap-2 text-sm"><input type="checkbox" {...form.register('isDefault')} className="size-4 accent-primary" /> Endereço padrão deste tipo</label>
      </div>
    </SidePanel>
  )
}
