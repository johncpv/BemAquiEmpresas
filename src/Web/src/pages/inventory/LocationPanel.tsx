import { useState } from 'react'
import { createLocation, deleteLocation, inactivateLocation, reactivateLocation, updateLocation } from '@/api/generated/bemaqui'
import type { LocationDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { RecordHistory } from '@/components/history/RecordHistory'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { SidePanel } from '@/components/ui/side-panel'
import { toastDone } from '@/components/ui/toast'
import { Can } from '@/pages/ModuleGate'
import { useCan } from '@/pages/use-can'

type Draft = { code: string; name: string; isDefault: boolean }
const toDraft = (l: LocationDetail | null): Draft => ({ code: l?.code ?? '', name: l?.name ?? '', isDefault: l?.isDefault ?? false })

/** Painel do local (CAD-03 CA 2 a 7): código e nome, "tornar padrão", inativar e excluir com a alternativa "Inativar". */
export function LocationPanel({ location, onClose, onSaved }: { location: LocationDetail | null; onClose: () => void; onSaved: (created: boolean) => void }) {
  const [draft, setDraft] = useState<Draft>(() => toDraft(location))
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const canWrite = useCan(location ? 'inventory.edit' : 'inventory.create')
  const dirty = JSON.stringify(draft) !== JSON.stringify(toDraft(location))
  const field = (name: string) => error?.fields[name]?.[0]

  const save = async () => {
    const input = { code: draft.code.trim().toUpperCase(), name: draft.name.trim(), isDefault: draft.isDefault }
    try {
      if (location) await updateLocation(location.id, input)
      else await createLocation(input)
      toastDone('Local salvo')
      onSaved(!location)
      return true
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar o local.', fields: {} })
      return false
    }
  }

  return (
    <SidePanel open onOpenChange={(o) => !o && onClose()} title={location ? `Local ${location.code}` : 'Novo local'} dirty={dirty && canWrite} onSave={save}
      footer={(
        <>
          {location ? <LocationActions location={location} onDone={() => onSaved(false)} /> : null}
          <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
          {canWrite ? <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar local</button> : null}
        </>
      )}
    >
      <fieldset disabled={!canWrite} className="space-y-3">
        <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
        <Field label="Código" required error={field('code')} hint="Curto e único, como ALM ou EXP. Até 20 letras, números ou hífen.">
          {(p) => <input {...p} value={draft.code} maxLength={20} onChange={(e) => setDraft({ ...draft, code: e.target.value.toUpperCase() })} className={inputClass + ' font-mono uppercase'} />}
        </Field>
        <Field label="Nome" required error={field('name')}>
          {(p) => <input {...p} value={draft.name} maxLength={100} onChange={(e) => setDraft({ ...draft, name: e.target.value })} className={inputClass} />}
        </Field>
        {location?.isDefault ? <p className="text-sm text-muted-foreground">Este é o local padrão: vem escolhido nos movimentos.</p> : (
          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" checked={draft.isDefault} onChange={(e) => setDraft({ ...draft, isDefault: e.target.checked })} className="size-4" />
            Tornar este o local padrão
          </label>
        )}
      </fieldset>
      {location ? (
        <details className="mt-5 rounded-md border border-border bg-card px-3 py-2 text-sm">
          <summary className="cursor-pointer font-semibold">Histórico de alterações</summary>
          <div className="mt-3"><RecordHistory entity="Location" id={location.id} /></div>
        </details>
      ) : null}
    </SidePanel>
  )
}

/** Inativar, reativar e excluir (CA 5 e 6). Excluir recusado por uso oferece "Inativar" no próprio diálogo. */
function LocationActions({ location, onDone }: { location: LocationDetail; onDone: () => void }) {
  return (
    <span className="mr-auto flex flex-wrap gap-2">
      <Can perm="inventory.edit">
        {location.isActive ? (
          <ConfirmDialog verb="Inativar" done="Inativado" title={`Inativar o local ${location.code}?`} description="Ele some dos seletores e continua visível onde já foi usado."
            onConfirm={async () => { await inactivateLocation(location.id); onDone() }}
            trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Inativar local</button>} />
        ) : (
          <button type="button" onClick={async () => { await reactivateLocation(location.id); toastDone('Reativado'); onDone() }} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Reativar</button>
        )}
      </Can>
      <Can perm="inventory.delete">
        <ConfirmDialog verb="Excluir" done="Excluído" destructive title={`Excluir o local ${location.code}?`} description="Só dá para excluir local sem uso e sem saldo."
          onConfirm={async () => { await deleteLocation(location.id); onDone() }}
          recovery={{ verb: 'Inativar', done: 'Inativado', when: (code) => code === 'location_in_use', onSelect: async () => { await inactivateLocation(location.id); onDone() } }}
          trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold text-crit-ink">Excluir local</button>} />
      </Can>
    </span>
  )
}
