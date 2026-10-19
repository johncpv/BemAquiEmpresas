import { listBanks } from '@/api/generated/bemaqui'
import { ToggleGroup } from '@/components/form/choice'
import { Combobox } from '@/components/form/combobox'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput, MoneyInput } from '@/components/form/inputs'
import { Dica } from '@/help/Dica'
import { type Draft, today } from './bank-account-draft'

/** Bancos da lista estática (COMPE), pelo código ou pelo nome (CA 1). */
const searchBanks = async (term: string) => {
  const t = term.trim().toLowerCase()
  return (await listBanks()).filter((b) => !t || b.code.includes(t) || b.name.toLowerCase().includes(t)).map((b) => ({ id: b.code, label: `${b.code} ${b.name}` }))
}

interface Props { draft: Draft; onChange: (patch: Partial<Draft>) => void; readOnly: boolean; isDefault: boolean; focusBalance?: boolean; error: { message: string; fields: Record<string, string[]> } | null }

/** Campos da conta (CA 1 a 3 e 6): Caixa não pede banco; a data do saldo vem com hoje quando o saldo é digitado. */
export function BankAccountFields({ draft, onChange, readOnly, isDefault, focusBalance, error }: Props) {
  const fieldError = (name: string) => error?.fields[name]?.[0]
  const negative = draft.balance !== null && Number(draft.balance) < 0
  return (
    <fieldset disabled={readOnly} className="space-y-3">
      <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
      {readOnly ? <p className="text-sm text-muted-foreground">Você pode consultar esta conta. Agência e conta aparecem mascaradas.</p> : null}
      <Field label="Nome" required error={fieldError('name')} hint="Como a conta aparece nos lançamentos (ex.: Inter PJ, Caixa da fábrica).">
        {(p) => <input {...p} value={draft.name} onChange={(e) => onChange({ name: e.target.value })} maxLength={100} className={inputClass} />}
      </Field>
      <ToggleGroup label="Tipo" value={draft.kind} onChange={(kind) => onChange({ kind })} disabled={readOnly} options={[{ value: 'Bank', label: 'Banco' }, { value: 'Cash', label: 'Caixa' }]} />
      {draft.kind === 'Bank' ? (
        <>
          <Field label="Banco" required error={fieldError('bankCode')}>
            {(p) => (
              <Combobox {...p} queryKey={['banks']} search={searchBanks} value={draft.bankCode} disabled={readOnly} placeholder="Busque pelo nome ou pelo código"
                selected={draft.bankCode ? { id: draft.bankCode, label: `${draft.bankCode} ${draft.bankName ?? ''}` } : null}
                onChange={(o) => onChange({ bankCode: o?.id ?? null, bankName: o ? o.label.slice(4) : null })} />
            )}
          </Field>
          <div className="grid grid-cols-2 gap-3">
            <Field label="Agência" required error={fieldError('agency')} hint="Com o dígito, se houver: 1234-5.">
              {(p) => <input {...p} value={draft.agency} onChange={(e) => onChange({ agency: e.target.value })} inputMode="numeric" maxLength={10} className={inputClass} />}
            </Field>
            <Field label="Conta" required error={fieldError('account')} hint="Com o dígito: 12345678-9.">
              {(p) => <input {...p} value={draft.account} onChange={(e) => onChange({ account: e.target.value })} inputMode="numeric" maxLength={20} className={inputClass} />}
            </Field>
          </div>
        </>
      ) : null}
      <div className="grid grid-cols-2 gap-3">
        <Field label="Saldo inicial" error={fieldError('openingBalance')} warning={negative ? 'Saldo inicial negativo. Confira se a conta começa no cheque especial.' : undefined}
          dica="O saldo da conta no dia em que você começa a usar o BemAqui. O fluxo de caixa parte dele. Pode ficar para depois.">
          {(p) => <MoneyInput {...p} autoFocus={focusBalance} value={draft.balance} onChange={(v) => onChange({ balance: v, date: v !== null && !draft.date ? today() : draft.date })} />}
        </Field>
        <Field label="Data do saldo" error={fieldError('openingDate')}>
          {(p) => <DateInput {...p} value={draft.date} onChange={(v) => onChange({ date: v })} />}
        </Field>
      </div>
      <div className="flex items-center gap-1">
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" checked={draft.isDefault} disabled={readOnly || isDefault} onChange={(e) => onChange({ isDefault: e.target.checked })} className="size-4 accent-primary" />
          Conta padrão
        </label>
        <Dica texto="Vem escolhida nos lançamentos novos. Só uma conta é padrão por vez; para trocar, marque outra." />
      </div>
    </fieldset>
  )
}
