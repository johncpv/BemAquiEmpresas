import { deleteAccount, inactivateAccount, reactivateAccount } from '@/api/generated/bemaqui'
import type { AccountDetail } from '@/api/generated/model'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { Can } from '@/pages/ModuleGate'

/** Inativar, reativar e excluir a conta (CA 12 a 14). Excluir recusado por lançamento oferece "Inativar". */
export function AccountActions({ account, onDone }: { account: AccountDetail; onDone: () => void }) {
  return (
    <span className="mr-auto flex flex-wrap gap-2">
      <Can perm="finance.edit">
        {account.isActive ? (
          <ConfirmDialog verb="Inativar" done="Inativada" title={`Inativar a conta ${account.code}?`} description="Ela some dos seletores e continua nos relatórios e na árvore."
            onConfirm={async () => { await inactivateAccount(account.id); onDone() }}
            trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Inativar</button>} />
        ) : (
          <button type="button" onClick={async () => { await reactivateAccount(account.id); notifySuccess('Reativada'); onDone() }} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Reativar</button>
        )}
      </Can>
      <Can perm="finance.delete">
        <ConfirmDialog verb="Excluir" done="Excluída" destructive title={`Excluir a conta ${account.code}?`} description="Só dá para excluir conta sem lançamentos e sem contas filhas."
          onConfirm={async () => { await deleteAccount(account.id); onDone() }}
          recovery={account.isActive ? { verb: 'Inativar', done: 'Inativada', when: (code) => code === 'account_in_use', onSelect: async () => { await inactivateAccount(account.id); onDone() } } : undefined}
          trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold text-crit-ink">Excluir conta contábil</button>} />
      </Can>
    </span>
  )
}
