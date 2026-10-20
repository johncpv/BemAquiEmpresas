import { deletePosition, inactivatePosition, reactivatePosition } from '@/api/generated/bemaqui'
import type { PositionDetail } from '@/api/generated/model'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { Can } from '@/pages/ModuleGate'

/** Inativar, reativar e excluir o cargo (CA 9). Excluir recusado por funcionários oferece "Inativar". */
export function PositionActions({ position, onDone }: { position: PositionDetail; onDone: () => void }) {
  return (
    <span className="mr-auto flex flex-wrap gap-2">
      <Can perm="hr.edit">
        {position.isActive ? (
          <ConfirmDialog verb="Inativar" done="Inativado" title={`Inativar o cargo ${position.name}?`} description="Ele some dos seletores e continua nos registros que já o usam."
            onConfirm={async () => { await inactivatePosition(position.id); onDone() }} trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Inativar</button>} />
        ) : <button type="button" onClick={async () => { await reactivatePosition(position.id); notifySuccess('Reativado'); onDone() }} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Reativar</button>}
      </Can>
      <Can perm="hr.delete">
        <ConfirmDialog verb="Excluir" done="Excluído" destructive title={`Excluir o cargo ${position.name}?`} description="Só dá para excluir cargo sem funcionários."
          onConfirm={async () => { await deletePosition(position.id); onDone() }}
          recovery={position.isActive ? { verb: 'Inativar', done: 'Inativado', when: (code) => code === 'position_in_use', onSelect: async () => { await inactivatePosition(position.id); onDone() } } : undefined}
          trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold text-crit-ink">Excluir</button>} />
      </Can>
    </span>
  )
}
