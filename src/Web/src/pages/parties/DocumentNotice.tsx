import type { PartyRoleKind } from '@/api/generated/model'
import { ROLE_LABELS } from './labels'

export interface ExistingParty {
  id: string
  name: string
  deleted: boolean
  roles: PartyRoleKind[]
}

/**
 * Documento já cadastrado (CA 4 e 6): em vez de criar um segundo, oferece abrir o cadastro, adicionar o papel que falta
 * ou restaurar o cadastro excluído.
 */
export function DocumentNotice({ existing, wanted, kindLabel, busy, onOpen, onAddRoles, onRestore }: {
  existing: ExistingParty
  wanted: PartyRoleKind[]
  kindLabel: string
  busy: boolean
  onOpen: () => void
  onAddRoles: (roles: PartyRoleKind[]) => void
  onRestore: () => void
}) {
  const missing = wanted.filter((r) => !existing.roles.includes(r))
  return (
    <div role="status" className="space-y-2 rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink sm:col-span-2">
      {existing.deleted ? (
        <>
          <p>Este {kindLabel} pertence a um cadastro excluído: <b>{existing.name}</b>.</p>
          <button type="button" disabled={busy} onClick={onRestore} className="rounded-md border border-border bg-card px-3 py-1.5 font-semibold text-foreground">Restaurar cadastro</button>
        </>
      ) : (
        <>
          <p>Este {kindLabel} já está cadastrado como <b>{existing.name}</b>.</p>
          <div className="flex flex-wrap gap-2">
            {missing.length > 0 ? (
              <button type="button" disabled={busy} onClick={() => onAddRoles(missing)} className="rounded-md border border-border bg-card px-3 py-1.5 font-semibold text-foreground">
                Adicionar papel {missing.map((r) => ROLE_LABELS[r]).join(' e ')}
              </button>
            ) : null}
            <button type="button" onClick={onOpen} className="rounded-md border border-border bg-card px-3 py-1.5 font-semibold text-foreground">Abrir cadastro</button>
          </div>
        </>
      )}
    </div>
  )
}
