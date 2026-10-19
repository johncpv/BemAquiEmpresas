import { deleteBankAccount, inactivateBankAccount, reactivateBankAccount } from '@/api/generated/bemaqui'
import type { BankAccountDetail } from '@/api/generated/model'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { toastDone } from '@/components/ui/toast'
import { Can } from '@/pages/ModuleGate'

/** Inativar, reativar e excluir (CA 7 e 8). Excluir recusado por movimentação oferece "Inativar" no próprio diálogo. */
export function BankAccountActions({ account, onDone }: { account: BankAccountDetail; onDone: () => void }) {
  return (
    <span className="mr-auto flex flex-wrap gap-2">
      <Can perm="finance.edit">
        {account.isActive ? (
          <ConfirmDialog verb="Inativar" done="Inativada" title={`Inativar a conta ${account.name}?`} description="Ela some dos lançamentos novos e continua no histórico."
            onConfirm={async () => { await inactivateBankAccount(account.id); onDone() }}
            trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Inativar</button>} />
        ) : (
          <button type="button" onClick={async () => { await reactivateBankAccount(account.id); toastDone('Reativada'); onDone() }} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Reativar</button>
        )}
      </Can>
      <Can perm="finance.delete">
        <ConfirmDialog verb="Excluir" done="Excluída" destructive title={`Excluir a conta ${account.name}?`} description="Só dá para excluir conta sem movimentação."
          onConfirm={async () => { await deleteBankAccount(account.id); onDone() }}
          recovery={{ verb: 'Inativar', done: 'Inativada', when: (code) => code === 'bank_account_in_use', onSelect: async () => { await inactivateBankAccount(account.id); onDone() } }}
          trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold text-crit-ink">Excluir conta bancária</button>} />
      </Can>
    </span>
  )
}
