import { useState } from 'react'
import { createBankAccount, updateBankAccount } from '@/api/generated/bemaqui'
import type { BankAccountDetail, BankAccountInput } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { ImportedTag } from '@/components/ui/imported-tag'
import { SidePanel } from '@/components/ui/side-panel'
import { notifySuccess } from '@/components/ui/messages'
import { BankAccountActions } from './BankAccountActions'
import { type Draft, toDraft } from './bank-account-draft'
import { BankAccountFields } from './BankAccountFields'

/**
 * Painel da conta (CAD-05b CA 1 a 9). Quem só vê recebe os dados mascarados do servidor e o painel fica só leitura; a troca
 * de banco, agência ou conta é conferida no servidor (CA 9) e o motivo da recusa aparece no formulário.
 */
export function BankAccountPanel({ account, focusBalance, onClose, onSaved }: { account: BankAccountDetail | null; focusBalance?: boolean; onClose: () => void; onSaved: (created: boolean) => void }) {
  const [draft, setDraft] = useState<Draft>(() => toDraft(account))
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const readOnly = account?.masked === true
  const dirty = JSON.stringify(draft) !== JSON.stringify(toDraft(account))

  const save = async () => {
    const bank = draft.kind === 'Bank'
    const input: BankAccountInput = {
      name: draft.name.trim(), kind: draft.kind, bankCode: bank ? draft.bankCode : null,
      agency: bank ? draft.agency.trim() || null : null, account: bank ? draft.account.trim() || null : null,
      openingBalance: draft.balance === null ? null : Number(draft.balance), openingDate: draft.date || null, isDefault: draft.isDefault,
    }
    try {
      if (account) await updateBankAccount(account.id, input)
      else await createBankAccount(input)
      notifySuccess('Conta bancária salva')
      onSaved(!account)
      return true
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar a conta.', fields: {} })
      return false
    }
  }

  return (
    <SidePanel open onOpenChange={(o) => !o && onClose()} title={account ? `Conta ${account.name}` : 'Nova conta bancária'} dirty={dirty && !readOnly} onSave={save}
      footer={(
        <>
          {account ? <BankAccountActions account={account} onDone={() => onSaved(false)} /> : null}
          <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
          {readOnly ? null : <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar conta bancária</button>}
        </>
      )}
    >
      {account?.importSource ? <div className="mb-3"><ImportedTag source={account.importSource} /></div> : null}
      <BankAccountFields draft={draft} onChange={(patch) => setDraft((d) => ({ ...d, ...patch }))} readOnly={readOnly} isDefault={account?.isDefault ?? false} focusBalance={focusBalance} error={error} />
    </SidePanel>
  )
}
