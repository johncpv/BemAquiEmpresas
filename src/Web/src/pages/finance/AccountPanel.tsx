import { useState } from 'react'
import { createAccount, updateAccount, useSuggestAccount } from '@/api/generated/bemaqui'
import { type AccountDetail, AccountType, DreGroup } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { RecordHistory } from '@/components/history/RecordHistory'
import { SidePanel } from '@/components/ui/side-panel'
import { toastDone } from '@/components/ui/toast'
import { useCan } from '@/pages/use-can'
import { AccountActions } from './AccountActions'
import { ACCOUNT_TYPES, DRE_GROUPS } from './chart'

type Draft = { code: string; name: string; type: AccountType; dreGroup: DreGroup; parentId: string }

/**
 * Painel da conta (CAD-05a CA 5 a 14). Conta filha nova vem com o próximo código do nível e o tipo e o grupo da DRE do pai
 * (R-PRP-1), sugeridos pelo servidor.
 */
export function AccountPanel({ account, parent, all, onClose, onSaved }: { account: AccountDetail | null; parent: AccountDetail | null; all: AccountDetail[]; onClose: () => void; onSaved: () => void }) {
  const start: Draft = account
    ? { code: account.code, name: account.name, type: account.type, dreGroup: account.dreGroup, parentId: account.parentId ?? '' }
    : { code: '', name: '', type: parent?.type ?? AccountType.Expense, dreGroup: parent?.dreGroup ?? DreGroup.OperatingExpenses, parentId: parent?.id ?? '' }
  const [draft, setDraft] = useState<Draft>(start)
  const [initial, setInitial] = useState<Draft>(start)
  const [applied, setApplied] = useState(!!account)
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const canWrite = useCan(account ? 'finance.edit' : 'finance.create')
  const suggestion = useSuggestAccount({ parentId: parent?.id }, { query: { enabled: !account } })
  // Aplica a sugestão do servidor uma vez, quando ela chega (CA 5).
  if (!applied && suggestion.data) {
    const s = suggestion.data
    const next = { ...draft, code: s.code ?? draft.code, type: s.type ?? draft.type, dreGroup: s.dreGroup ?? draft.dreGroup }
    setApplied(true)
    setDraft(next)
    setInitial(next)
  }
  const dirty = JSON.stringify(draft) !== JSON.stringify(initial)
  const field = (name: string) => error?.fields[name]?.[0]

  const save = async () => {
    const input = { code: draft.code.trim(), name: draft.name.trim(), type: draft.type, dreGroup: draft.dreGroup, parentId: draft.parentId || null }
    try {
      if (account) await updateAccount(account.id, input)
      else await createAccount(input)
      toastDone('Conta contábil salva')
      onSaved()
      return true
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar a conta.', fields: {} })
      return false
    }
  }

  return (
    <SidePanel open onOpenChange={(o) => !o && onClose()} title={account ? `Conta ${account.code}` : parent ? `Nova conta filha de ${parent.code}` : 'Nova conta contábil'} dirty={dirty && canWrite} onSave={save}
      footer={(
        <>
          {account ? <AccountActions account={account} onDone={onSaved} /> : null}
          <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
          {canWrite ? <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar conta contábil</button> : null}
        </>
      )}
    >
      <fieldset disabled={!canWrite} className="space-y-3">
        <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
        <Field label="Conta pai" error={field('parentId')} hint="Vazio = conta de primeiro nível.">
          {(p) => (
            <select {...p} value={draft.parentId} onChange={(e) => setDraft({ ...draft, parentId: e.target.value })} className={inputClass}>
              <option value="">Nenhuma (primeiro nível)</option>
              {all.filter((a) => a.id !== account?.id && a.code.split('.').length < 3).map((a) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}
            </select>
          )}
        </Field>
        <Field label="Código" required error={field('code')} hint="Formato 9.9.99, começando com o código da conta pai.">
          {(p) => <input {...p} value={draft.code} maxLength={6} inputMode="decimal" onChange={(e) => setDraft({ ...draft, code: e.target.value })} className={inputClass + ' font-mono tabular-nums'} />}
        </Field>
        <Field label="Nome" required error={field('name')}>
          {(p) => <input {...p} value={draft.name} maxLength={100} onChange={(e) => setDraft({ ...draft, name: e.target.value })} className={inputClass} />}
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Tipo" error={field('type')} hint="O mesmo da conta pai.">
            {(p) => (
              <select {...p} value={draft.type} onChange={(e) => setDraft({ ...draft, type: e.target.value as AccountType })} className={inputClass}>
                {(Object.keys(ACCOUNT_TYPES) as AccountType[]).map((t) => <option key={t} value={t}>{ACCOUNT_TYPES[t]}</option>)}
              </select>
            )}
          </Field>
          <Field label="Grupo da DRE" error={field('dreGroup')}>
            {(p) => (
              <select {...p} value={draft.dreGroup} onChange={(e) => setDraft({ ...draft, dreGroup: e.target.value as DreGroup })} className={inputClass}>
                {(Object.keys(DRE_GROUPS) as DreGroup[]).map((g) => <option key={g} value={g}>{DRE_GROUPS[g]}</option>)}
              </select>
            )}
          </Field>
        </div>
      </fieldset>
      {account ? (
        <details className="mt-5 rounded-md border border-border bg-card px-3 py-2 text-sm">
          <summary className="cursor-pointer font-semibold">Histórico de alterações</summary>
          <div className="mt-3"><RecordHistory entity="Account" id={account.id} /></div>
        </details>
      ) : null}
    </SidePanel>
  )
}
