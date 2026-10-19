import { useState } from 'react'
import { createCostCenter, deleteCostCenter, inactivateCostCenter, reactivateCostCenter, updateCostCenter, useSuggestCostCenter } from '@/api/generated/bemaqui'
import type { CostCenterDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { RecordHistory } from '@/components/history/RecordHistory'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { SidePanel } from '@/components/ui/side-panel'
import { toastDone } from '@/components/ui/toast'
import { Can } from '@/pages/ModuleGate'
import { useCan } from '@/pages/use-can'

/** Painel do centro de custo (CAD-05a CA 16): as mesmas regras do plano de contas, com o código sugerido. */
export function CostCenterPanel({ center, parent, all, onClose, onSaved }: { center: CostCenterDetail | null; parent: CostCenterDetail | null; all: CostCenterDetail[]; onClose: () => void; onSaved: () => void }) {
  const start = { code: center?.code ?? '', name: center?.name ?? '', parentId: center ? center.parentId ?? '' : parent?.id ?? '' }
  const [draft, setDraft] = useState(start)
  const [initial, setInitial] = useState(start)
  const [applied, setApplied] = useState(!!center)
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const canWrite = useCan(center ? 'finance.edit' : 'finance.create')
  const suggestion = useSuggestCostCenter({ parentId: parent?.id }, { query: { enabled: !center } })
  if (!applied && suggestion.data) {
    const next = { ...draft, code: suggestion.data.code ?? draft.code }
    setApplied(true)
    setDraft(next)
    setInitial(next)
  }
  const dirty = JSON.stringify(draft) !== JSON.stringify(initial)

  const save = async () => {
    const input = { code: draft.code.trim(), name: draft.name.trim(), parentId: draft.parentId || null }
    try {
      if (center) await updateCostCenter(center.id, input)
      else await createCostCenter(input)
      toastDone('Centro de custo salvo')
      onSaved()
      return true
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar o centro de custo.', fields: {} })
      return false
    }
  }

  const actions = center ? (
    <span className="mr-auto flex flex-wrap gap-2">
      <Can perm="finance.edit">
        {center.isActive ? (
          <ConfirmDialog verb="Inativar" done="Inativado" title={`Inativar o centro ${center.code}?`} description="Ele some dos seletores e continua nos relatórios."
            onConfirm={async () => { await inactivateCostCenter(center.id); onSaved() }} trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Inativar</button>} />
        ) : <button type="button" onClick={async () => { await reactivateCostCenter(center.id); toastDone('Reativado'); onSaved() }} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Reativar</button>}
      </Can>
      <Can perm="finance.delete">
        <ConfirmDialog verb="Excluir" done="Excluído" destructive title={`Excluir o centro ${center.code}?`} description="Só dá para excluir centro sem lançamentos e sem centros filhos."
          onConfirm={async () => { await deleteCostCenter(center.id); onSaved() }}
          recovery={center.isActive ? { verb: 'Inativar', done: 'Inativado', when: (code) => code === 'cost_center_in_use', onSelect: async () => { await inactivateCostCenter(center.id); onSaved() } } : undefined}
          trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold text-crit-ink">Excluir centro de custo</button>} />
      </Can>
    </span>
  ) : null

  return (
    <SidePanel open onOpenChange={(o) => !o && onClose()} title={center ? `Centro de custo ${center.code}` : parent ? `Novo centro filho de ${parent.code}` : 'Novo centro de custo'} dirty={dirty && canWrite} onSave={save}
      footer={(
        <>
          {actions}
          <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
          {canWrite ? <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar centro de custo</button> : null}
        </>
      )}
    >
      <fieldset disabled={!canWrite} className="space-y-3">
        <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
        <Field label="Centro pai" error={error?.fields.parentId?.[0]} hint="Vazio = centro de primeiro nível.">
          {(p) => (
            <select {...p} value={draft.parentId} onChange={(e) => setDraft({ ...draft, parentId: e.target.value })} className={inputClass}>
              <option value="">Nenhum (primeiro nível)</option>
              {all.filter((c) => c.id !== center?.id && c.code.split('.').length < 3).map((c) => <option key={c.id} value={c.id}>{c.code} {c.name}</option>)}
            </select>
          )}
        </Field>
        <Field label="Código" required error={error?.fields.code?.[0]} hint="Formato 9.9.99, começando com o código do centro pai.">
          {(p) => <input {...p} value={draft.code} maxLength={6} inputMode="decimal" onChange={(e) => setDraft({ ...draft, code: e.target.value })} className={inputClass + ' font-mono tabular-nums'} />}
        </Field>
        <Field label="Nome" required error={error?.fields.name?.[0]}>
          {(p) => <input {...p} value={draft.name} maxLength={100} onChange={(e) => setDraft({ ...draft, name: e.target.value })} className={inputClass} />}
        </Field>
      </fieldset>
      {center ? (
        <details className="mt-5 rounded-md border border-border bg-card px-3 py-2 text-sm">
          <summary className="cursor-pointer font-semibold">Histórico de alterações</summary>
          <div className="mt-3"><RecordHistory entity="CostCenter" id={center.id} /></div>
        </details>
      ) : null}
    </SidePanel>
  )
}
