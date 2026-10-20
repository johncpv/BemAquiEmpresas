import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { createUnit, deleteUnit, getListUnitsQueryKey, inactivateUnit, reactivateUnit, updateUnit, useListUnits } from '@/api/generated/bemaqui'
import type { UnitDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { SidePanel } from '@/components/ui/side-panel'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { notifySuccess } from '@/components/ui/messages'
import { Can } from '@/pages/ModuleGate'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { InventoryTabs } from './InventoryTabs'

/** Estoque → Unidades (CAD-02a CA 1 e 2): as do seed já vêm prontas; em uso por item, não inativa nem exclui. */
export function UnitsPage() {
  const query = useListUnits({ includeInactive: true })
  const client = useQueryClient()
  const [editing, setEditing] = useState<{ unit: UnitDetail | null } | null>(null)
  const refresh = () => void client.invalidateQueries({ queryKey: getListUnitsQueryKey() })

  return (
    <>
      <PageHeading title="Estoque: Unidades">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">Como cada item é contado no estoque: unidade, quilo, metro, peça.</p>
          <Can perm="inventory.create"><button type="button" onClick={() => setEditing({ unit: null })} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Nova unidade</button></Can>
        </div>
      </PageHeading>
      <InventoryTabs />
      {query.isPending ? <Skeleton className="h-64" /> : query.isError ? <ErrorState onRetry={() => query.refetch()} /> : (
        <div className="overflow-x-auto rounded-md border border-border">
          <table className="w-full text-sm">
            <caption className="sr-only">Unidades de medida</caption>
            <thead className="bg-muted text-left text-xs text-muted-foreground">
              <tr><th scope="col" className="px-3 py-2">Código</th><th scope="col" className="px-3 py-2">Nome</th><th scope="col" className="px-3 py-2 text-right">Itens</th><th scope="col" className="px-3 py-2">Situação</th></tr>
            </thead>
            <tbody>
              {query.data.map((u) => (
                <tr key={u.id} className="border-t border-border">
                  <td className="px-3 py-2"><button type="button" onClick={() => setEditing({ unit: u })} className="font-mono font-semibold text-primary hover:underline">{u.code}</button></td>
                  <td className="px-3 py-2">{u.name}</td>
                  <td className="px-3 py-2 text-right tabular-nums">{u.itemCount}</td>
                  <td className="px-3 py-2"><StatusChip tone={u.isActive ? 'ok' : 'neutral'}>{u.isActive ? 'Ativa' : 'Inativa'}</StatusChip></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {editing ? <UnitPanel unit={editing.unit} onClose={() => setEditing(null)} onSaved={() => { refresh(); setEditing(null) }} /> : null}
    </>
  )
}

function UnitPanel({ unit, onClose, onSaved }: { unit: UnitDetail | null; onClose: () => void; onSaved: () => void }) {
  const [draft, setDraft] = useState({ code: unit?.code ?? '', name: unit?.name ?? '' })
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const canWrite = useCan(unit ? 'inventory.edit' : 'inventory.create')
  const dirty = draft.code !== (unit?.code ?? '') || draft.name !== (unit?.name ?? '')
  const save = async () => {
    try {
      if (unit) await updateUnit(unit.id, draft)
      else await createUnit(draft)
      notifySuccess('Unidade salva')
      onSaved()
      return true
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar a unidade.', fields: {} })
      return false
    }
  }
  return (
    <SidePanel open onOpenChange={(o) => !o && onClose()} title={unit ? `Unidade ${unit.code}` : 'Nova unidade'} dirty={dirty && canWrite} onSave={save}
      footer={(
        <>
          {unit ? (
            <span className="mr-auto flex flex-wrap gap-2">
              <Can perm="inventory.edit">
                {unit.isActive ? (
                  <ConfirmDialog verb="Inativar" done="Unidade inativada" title={`Inativar a unidade ${unit.code}?`} description="Ela some dos seletores. Unidade em uso por item não pode ser inativada."
                    onConfirm={async () => { await inactivateUnit(unit.id); onSaved() }} trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Inativar unidade</button>} />
                ) : <button type="button" onClick={async () => { await reactivateUnit(unit.id); notifySuccess('Unidade reativada'); onSaved() }} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Reativar</button>}
              </Can>
              <Can perm="inventory.delete">
                <ConfirmDialog verb="Excluir" done="Unidade excluída" destructive title={`Excluir a unidade ${unit.code}?`} description="Só dá para excluir unidade que nenhum item usa."
                  onConfirm={async () => { await deleteUnit(unit.id); onSaved() }} trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold text-crit-ink">Excluir unidade</button>} />
              </Can>
            </span>
          ) : null}
          <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
          {canWrite ? <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar unidade</button> : null}
        </>
      )}
    >
      <fieldset disabled={!canWrite} className="space-y-3">
        <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
        <Field label="Código" required error={error?.fields.code?.[0]} hint="Até 6 letras ou números, como KG ou CX.">
          {(p) => <input {...p} value={draft.code} maxLength={6} onChange={(e) => setDraft({ ...draft, code: e.target.value.toUpperCase() })} className={inputClass + ' font-mono uppercase'} />}
        </Field>
        <Field label="Nome" required error={error?.fields.name?.[0]}>
          {(p) => <input {...p} value={draft.name} maxLength={60} onChange={(e) => setDraft({ ...draft, name: e.target.value })} className={inputClass} />}
        </Field>
      </fieldset>
    </SidePanel>
  )
}
