import { useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import { Controller } from 'react-hook-form'
import { ChipGroup, ToggleGroup } from '@/components/form/choice'
import { Combobox, type ComboOption } from '@/components/form/combobox'
import { Field, FormError, FormSection } from '@/components/form/form'
import { inputClass, useZodForm } from '@/components/form/form-utils'
import { CepInput, DocumentInput, MoneyInput, PhoneInput } from '@/components/form/inputs'
import { SidePanel } from '@/components/ui/side-panel'
import { notifySuccess } from '@/components/ui/messages'
import { compareDecimal } from '@/lib/decimal'
import { documentKind, isValidDocument } from '@/lib/masks'
import { z } from '@/lib/zod'
import { PageHeading } from '@/shell/PageHeading'
import { MoldNav } from './MoldNav'
import { PAYMENT_TERMS, ROLES, searchTerms } from './sample'

const schema = z.object({
  document: z.string().refine((v) => v === '' || isValidDocument(v), 'Documento inválido. Confira os dígitos.'),
  kind: z.enum(['PF', 'PJ']),
  name: z.string().trim().min(2, 'Informe o nome ou a razão social.'),
  tradeName: z.string(),
  roles: z.array(z.string()).min(1, 'Escolha ao menos um papel.'),
  email: z.union([z.literal(''), z.email('Informe um e-mail válido.')]),
  phone: z.string(),
  cep: z.string().refine((v) => v === '' || v.length === 8, 'CEP com 8 dígitos.'),
  street: z.string(),
  number: z.string(),
  district: z.string(),
  city: z.string(),
  uf: z.string(),
  paymentTermId: z.string().nullable(),
  creditLimit: z.string().nullable(),
})

const DUPLICATE = '47225005000186'

/**
 * Molde 1 — cadastro em seções (FND-09d CA 2): página única de criação, só com o obrigatório e o que comanda o resto;
 * ações no topo, visíveis sem rolar, e repetidas no fim. Exemplo: "Novo terceiro".
 */
export default function CreateMold() {
  const navigate = useNavigate()
  const form = useZodForm(schema, {
    defaultValues: { document: '', kind: 'PJ', name: '', tradeName: '', roles: ['cliente'], email: '', phone: '', cep: '', street: '', number: '', district: '', city: '', uf: '', paymentTermId: '30', creditLimit: null },
  })
  const [kindTouched, setKindTouched] = useState(false)
  const [term, setTerm] = useState<ComboOption | null>(PAYMENT_TERMS[1])
  const [creating, setCreating] = useState<string | null>(null)
  const [newTerm, setNewTerm] = useState('')
  const document = form.watch('document')
  const limit = form.watch('creditLimit')
  const errors = form.formState.errors

  const onDocument = (value: string) => {
    form.setValue('document', value, { shouldDirty: true })
    const kind = documentKind(value)
    if (kind && !kindTouched) form.setValue('kind', kind)
  }

  const submit = form.handleSubmit(async () => {
    await new Promise((r) => setTimeout(r, 400))
    notifySuccess('Terceiro salvo')
    void navigate({ to: '/app/dev/moldes/detalhe', search: { novo: '1' } } as never)
  })

  const actions = (
    <div className="flex flex-wrap gap-2">
      <button type="button" onClick={() => window.history.back()} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
      <button type="submit" form="novo-terceiro" disabled={form.formState.isSubmitting} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">Salvar terceiro</button>
    </div>
  )

  return (
    <>
      <MoldNav current="cadastro" />
      <PageHeading title="Novo terceiro">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">Cliente, fornecedor, instalador ou transportadora. Um mesmo documento pode ter vários papéis.</p>
          {actions}
        </div>
      </PageHeading>

      <form id="novo-terceiro" onSubmit={submit} noValidate className="max-w-3xl space-y-4">
        <FormError message={errors.root?.server?.message} />

        <FormSection title="Identificação" description="O documento vem primeiro: ele define PF ou PJ e, na CAD-01b, preenche os dados pela Receita.">
          <Field label="CPF ou CNPJ" error={errors.document?.message} hint="Aceita o CNPJ com letras. Em branco, o terceiro fica com o aviso &quot;Sem documento&quot;." dica="Sem documento, o terceiro não pode receber nota fiscal nem boleto.">
            {(p) => <DocumentInput {...p} value={document} onChange={onDocument} onBlur={() => void form.trigger('document')} />}
          </Field>
          <div className="space-y-1">
            <span className="block text-sm font-medium">Tipo</span>
            <Controller control={form.control} name="kind" render={({ field }) => (
              <ToggleGroup label="Tipo de pessoa" value={field.value} onChange={(v) => { setKindTouched(true); field.onChange(v) }} options={[{ value: 'PJ', label: 'Pessoa jurídica' }, { value: 'PF', label: 'Pessoa física' }]} />
            )} />
          </div>
          {document === DUPLICATE ? (
            <div role="status" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink sm:col-span-2">
              Já existe um terceiro com este documento: <b>WSP Equipamentos por Elevação Ltda</b>.{' '}
              <button type="button" className="font-semibold underline">Abrir cadastro</button> ·{' '}
              <button type="button" className="font-semibold underline">Adicionar papel</button>
            </div>
          ) : null}
          <Field label={form.watch('kind') === 'PF' ? 'Nome' : 'Razão social'} error={errors.name?.message} required>
            {(p) => <input {...p} {...form.register('name')} className={inputClass} />}
          </Field>
          <Field label="Nome fantasia">{(p) => <input {...p} {...form.register('tradeName')} className={inputClass} />}</Field>
          <div className="sm:col-span-2">
            <Controller control={form.control} name="roles" render={({ field }) => (
              <ChipGroup legend="Papéis" required value={field.value} onChange={field.onChange} error={errors.roles?.message} options={ROLES.map((r) => ({ ...r }))} />
            )} />
          </div>
        </FormSection>

        <FormSection title="Contato">
          <Field label="E-mail" error={errors.email?.message}>{(p) => <input {...p} type="email" {...form.register('email')} className={inputClass} />}</Field>
          <Field label="Telefone">
            {(p) => <Controller control={form.control} name="phone" render={({ field }) => <PhoneInput {...p} value={field.value} onChange={field.onChange} />} />}
          </Field>
        </FormSection>

        <FormSection title="Endereço de cobrança" description="Opcional. Vira o endereço padrão; outros endereços (obra, entrega) entram na aba Endereços depois de salvar.">
          <Field label="CEP" error={errors.cep?.message} hint="Na CAD-01b, o CEP preenche rua, bairro e cidade.">
            {(p) => <Controller control={form.control} name="cep" render={({ field }) => <CepInput {...p} value={field.value} onChange={field.onChange} />} />}
          </Field>
          <Field label="Logradouro">{(p) => <input {...p} {...form.register('street')} className={inputClass} />}</Field>
          <Field label="Número">{(p) => <input {...p} {...form.register('number')} className={inputClass} />}</Field>
          <Field label="Bairro">{(p) => <input {...p} {...form.register('district')} className={inputClass} />}</Field>
          <Field label="Cidade">{(p) => <input {...p} {...form.register('city')} className={inputClass} />}</Field>
          <Field label="UF">{(p) => <input {...p} maxLength={2} {...form.register('uf')} className={inputClass} />}</Field>
        </FormSection>

        <FormSection title="Comercial">
          <Field label="Condição de pagamento padrão" hint="Vem preenchida com a padrão da empresa.">
            {(p) => <Combobox {...p} queryKey={['molde', 'condicoes']} search={searchTerms} selected={term} onChange={(o) => { setTerm(o); form.setValue('paymentTermId', o?.id ?? null) }} onCreate={(t) => { setNewTerm(t); setCreating(t) }} createLabel="Criar condição" />}
          </Field>
          <Field label="Limite de crédito" warning={limit && compareDecimal(limit, '100000') > 0 ? 'Acima de R$ 100.000,00. Confira com o financeiro antes de salvar.' : undefined}>
            {(p) => <Controller control={form.control} name="creditLimit" render={({ field }) => <MoneyInput {...p} value={field.value} onChange={field.onChange} />} />}
          </Field>
        </FormSection>

        <div className="flex justify-end">{actions}</div>
      </form>

      <SidePanel
        open={creating !== null}
        onOpenChange={(o) => !o && setCreating(null)}
        title="Nova condição de pagamento"
        description="Criação rápida: o formulário do terceiro continua aberto."
        dirty={creating !== null && newTerm !== creating}
        footer={(
          <>
            <button type="button" onClick={() => setCreating(null)} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
            <button type="button" onClick={() => { const o = { id: 'nova', label: newTerm }; setTerm(o); form.setValue('paymentTermId', o.id); setCreating(null); notifySuccess('Condição salva') }} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar condição</button>
          </>
        )}
      >
        <Field label="Nome" required>{(p) => <input {...p} value={newTerm} onChange={(e) => setNewTerm(e.target.value)} className={inputClass} />}</Field>
      </SidePanel>
    </>
  )
}
