import { useState } from 'react'
import { createParty, searchPaymentMethodAcquirers, useListBankAccounts } from '@/api/generated/bemaqui'
import type { PaymentMethodDetail, PaymentMethodRequest, PaymentMethodType } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { type ComboOption, Combobox } from '@/components/form/combobox'
import { Field, FormError, FormGrid } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { UnsavedChangesGuard } from '@/components/form/UnsavedChangesGuard'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { useNavigation } from '@/navigation/navigation-context'
import { acceptsAcquirer, defaultNfeCode, NFE_CODES, requiresAcquirer, TYPE_LABELS } from './payment-methods'

const TYPES = Object.keys(TYPE_LABELS) as PaymentMethodType[]

interface Draft { name: string; type: PaymentMethodType; accountId: string; acquirer: ComboOption | null; nfeCode: string }

/** Ação de outro módulo liberada (o mesmo critério do <Can>). */
function useCan(perm: string) {
  const { all } = useNavigation()
  const [code, action] = perm.split('.')
  const module = all.find((m) => m.code === code)
  return !!module && !module.locked && module.actions.includes(action)
}

/**
 * Forma de pagamento (PAG-01 CA 1, 7 e 8): nome, tipo, conta de destino (Dinheiro só em Caixa; nos demais vem a conta bancária padrão),
 * adquirente nos cartões com cadastro rápido do terceiro e o meio de pagamento da nota (tPag), preenchido pelo tipo.
 */
export function PaymentMethodForm({ method, canEdit, onSubmit }: { method: PaymentMethodDetail | null; canEdit: boolean; onSubmit: (input: PaymentMethodRequest) => Promise<void> }) {
  const accounts = useListBankAccounts({ pageSize: 100 })
  const active = (accounts.data?.items ?? []).filter((a) => a.isActive)
  const initial: Draft = {
    name: method?.name ?? '', type: method?.type ?? 'Pix', accountId: method?.destinationAccountId ?? '',
    acquirer: method?.acquirerPartyId ? { id: method.acquirerPartyId, label: method.acquirerName ?? 'Adquirente' } : null,
    nfeCode: method?.nfePaymentCode ?? defaultNfeCode('Pix'),
  }
  const [draft, setDraft] = useState<Draft>(initial)
  const [tried, setTried] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [creating, setCreating] = useState<string | null>(null)
  const [newAcquirer, setNewAcquirer] = useState('')
  const canCreateParty = useCan('parties.create')

  const cashOnly = draft.type === 'Cash'
  const choices = active.filter((a) => (cashOnly ? a.kind === 'Cash' : true))
  // Propositivo: sem conta escolhida, Dinheiro vai para o primeiro caixa e os demais para a conta bancária padrão (CA 7).
  const suggested = cashOnly ? choices[0]?.id : (active.find((a) => a.isDefault && a.kind === 'Bank') ?? active.find((a) => a.kind === 'Bank'))?.id
  const accountId = draft.accountId && choices.some((a) => a.id === draft.accountId) ? draft.accountId : suggested ?? ''
  const dirty = JSON.stringify({ ...draft, accountId }) !== JSON.stringify(initial)

  const errors = {
    name: draft.name.trim() ? undefined : 'Informe o nome da forma de pagamento.',
    account: accountId ? undefined : cashOnly ? 'Cadastre um caixa em Contas e caixas: Dinheiro cai numa conta do tipo Caixa.' : 'Escolha a conta onde o dinheiro cai.',
    acquirer: requiresAcquirer(draft.type) && !draft.acquirer ? 'Informe a adquirente (a empresa da maquininha).' : undefined,
  }

  const save = async () => {
    setTried(true)
    if (errors.name || errors.account || errors.acquirer) return false
    try {
      await onSubmit({
        name: draft.name.trim(), type: draft.type, destinationAccountId: accountId,
        acquirerPartyId: acceptsAcquirer(draft.type) ? draft.acquirer?.id ?? null : null, nfePaymentCode: draft.nfeCode,
      })
      setError(null)
      return true
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar a forma de pagamento.')
      return false
    }
  }

  const changeType = (type: PaymentMethodType) =>
    setDraft((d) => ({ ...d, type, nfeCode: d.nfeCode === defaultNfeCode(d.type) ? defaultNfeCode(type) : d.nfeCode, accountId: '' }))

  return (
    <form id="forma-de-pagamento" onSubmit={(e) => { e.preventDefault(); void save() }} noValidate>
      <UnsavedChangesGuard when={dirty && canEdit} onSave={save} />
      <fieldset disabled={!canEdit} className="space-y-5">
        <FormError message={error ?? undefined} />
        <FormGrid>
          <Field label="Nome" required size="lg" error={tried ? errors.name : undefined} hint="Como aparece no caixa. Ex.: Crédito Stone, Pix da loja.">
            {(p) => <input {...p} value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} maxLength={100} className={inputClass} />}
          </Field>
          <Field label="Tipo" required size="md">
            {(p) => (
              <select {...p} value={draft.type} onChange={(e) => changeType(e.target.value as PaymentMethodType)} className={inputClass}>
                {TYPES.map((t) => <option key={t} value={t}>{TYPE_LABELS[t]}</option>)}
              </select>
            )}
          </Field>
          <Field label="Conta de destino" required size="lg" error={tried ? errors.account : undefined}
            hint={cashOnly ? 'Dinheiro cai num caixa.' : 'Onde o dinheiro cai. Vem a conta bancária padrão.'}>
            {(p) => (
              <select {...p} value={accountId} onChange={(e) => setDraft({ ...draft, accountId: e.target.value })} className={inputClass}>
                {accountId ? null : <option value="">Escolha a conta</option>}
                {choices.map((a) => <option key={a.id} value={a.id}>{a.name}{a.kind === 'Cash' ? ' (caixa)' : ''}</option>)}
              </select>
            )}
          </Field>
          {acceptsAcquirer(draft.type) ? (
            <Field label="Adquirente" required={requiresAcquirer(draft.type)} size="lg" error={tried ? errors.acquirer : undefined}
              dica="A empresa da maquininha (Stone, Cielo, Rede, PagBank...). Ela cobra a taxa e paga as vendas no prazo combinado."
              hint={canCreateParty ? undefined : 'Não achou? Peça a quem cadastra terceiros para cadastrar a adquirente.'}>
              {(p) => (
                <Combobox {...p} queryKey={['payment-method-acquirers']} placeholder="Busque pelo nome" selected={draft.acquirer} value={draft.acquirer?.id}
                  search={async (q) => (await searchPaymentMethodAcquirers({ q })).map((a) => ({ id: a.id, label: a.name, hint: a.document ?? undefined }))}
                  onChange={(o) => setDraft({ ...draft, acquirer: o })}
                  onCreate={canCreateParty ? (term) => { setNewAcquirer(term); setCreating(term) } : undefined} createLabel="Cadastrar adquirente" />
              )}
            </Field>
          ) : null}
          <Field label="Meio de pagamento na nota fiscal" size="lg" dica="O código que a nota fiscal ao consumidor usa para esta forma (tPag). Vem preenchido pelo tipo.">
            {(p) => (
              <select {...p} value={draft.nfeCode} onChange={(e) => setDraft({ ...draft, nfeCode: e.target.value })} className={inputClass}>
                {NFE_CODES.map((c) => <option key={c.value} value={c.value}>{c.label}</option>)}
              </select>
            )}
          </Field>
        </FormGrid>
        <p className="text-sm text-muted-foreground">{cashOnly ? 'Dá troco.' : 'Não dá troco: troco só no dinheiro.'}</p>
      </fieldset>
      <ConfirmDialog open={creating !== null} onOpenChange={(o) => { if (!o) setCreating(null) }} verb="Cadastrar adquirente" done="Adquirente cadastrada"
        title="Cadastrar adquirente" description="Cria o terceiro com o papel Adquirente. CNPJ, endereço e contatos você completa depois em Terceiros."
        onOpen={() => setNewAcquirer(creating ?? '')}
        preconditions={[{ ok: newAcquirer.trim().length > 0, text: 'Informe o nome da adquirente.' }]}
        onConfirm={async () => {
          const party = await createParty({ kind: 'PJ', document: null, legalName: newAcquirer.trim(), tradeName: null, roles: ['Acquirer'] } as never)
          setDraft((d) => ({ ...d, acquirer: { id: party.id, label: party.legalName } }))
        }}>
        <label className="block space-y-1 text-sm">
          <span className="font-medium">Nome da adquirente</span>
          <input value={newAcquirer} onChange={(e) => setNewAcquirer(e.target.value)} maxLength={150} className={inputClass} />
        </label>
      </ConfirmDialog>
    </form>
  )
}
