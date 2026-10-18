import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { deactivateGroup, duplicateGroup, getGetGroupQueryKey, getListGroupsQueryKey, updateGroup } from '@/api/generated/bemaqui'
import type { GroupDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { UnsavedChangesGuard } from '@/components/form/UnsavedChangesGuard'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { StatusChip } from '@/components/ui/status-chip'
import { toastDone } from '@/components/ui/toast'
import { Can } from '@/pages/ModuleGate'
import type { MatrixRow } from './group-matrix'
import { GroupMatrixTable } from './GroupMatrixTable'
import { GroupMembersDialog } from './GroupMembersDialog'

const initialMatrix = (detail: GroupDetail) =>
  Object.fromEntries(detail.modules.map((m) => [m.code, { enabled: m.enabled, actions: [...m.actions] }])) as Record<string, MatrixRow>

/** Detalhe do grupo (FND-06b): nome, 2FA, membros e matriz; "Salvar grupo" com o toast de quantos usuários recebem. */
export function GroupEditor({ detail, onSelect }: { detail: GroupDetail; onSelect: (id: string) => void }) {
  const client = useQueryClient()
  const g = detail.group
  const [name, setName] = useState(g.name)
  const [description, setDescription] = useState(g.description)
  const [requires2fa, setRequires2fa] = useState(g.requiresTwoFactor)
  const [matrix, setMatrix] = useState(() => initialMatrix(detail))
  const [error, setError] = useState<string>()
  const readOnly = g.isSystem || !g.isActive
  const dirty = !readOnly && (name !== g.name || description !== g.description || requires2fa !== g.requiresTwoFactor || JSON.stringify(matrix) !== JSON.stringify(initialMatrix(detail)))

  const refresh = async () => {
    await client.invalidateQueries({ queryKey: getListGroupsQueryKey() })
    await client.invalidateQueries({ queryKey: getGetGroupQueryKey(g.id) })
  }

  const save = async () => {
    setError(undefined)
    try {
      const modules = Object.entries(matrix).filter(([, r]) => r.enabled).map(([moduleCode, r]) => ({ moduleCode, enabled: true, actions: r.actions }))
      const saved = await updateGroup(g.id, { name, description, requiresTwoFactor: requires2fa, modules })
      await refresh()
      toastDone(`Grupo ${name} salvo. ${saved.affectedUsers} ${saved.affectedUsers === 1 ? 'usuário recebe' : 'usuários recebem'} as novas permissões no próximo acesso.`)
      return true
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar o grupo.')
      return false
    }
  }

  return (
    <section aria-labelledby="grupo-titulo" className="space-y-4 rounded-md border border-border bg-card p-4">
      <UnsavedChangesGuard when={dirty} onSave={save} />
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0 flex-1 space-y-2">
          <h2 id="grupo-titulo" className="sr-only">{g.name}</h2>
          <input aria-label="Nome do grupo" value={name} onChange={(e) => setName(e.target.value)} disabled={readOnly} className={inputClass + ' font-semibold'} />
          <input aria-label="Descrição do grupo" value={description} onChange={(e) => setDescription(e.target.value)} disabled={readOnly} className={inputClass} />
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {dirty ? <StatusChip tone="warn">Alterações não salvas</StatusChip> : null}
          <Can perm="identity.edit">
            {!readOnly ? <button type="button" onClick={() => void save()} disabled={!dirty} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">Salvar grupo</button> : null}
          </Can>
        </div>
      </div>

      {g.isSystem ? <StatusChip tone="info">Grupo do sistema: acesso total, não pode ser restringido</StatusChip> : null}
      {!g.isActive ? <StatusChip tone="warn">Inativo</StatusChip> : null}
      <FormError message={error} />

      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-muted-foreground">
          {detail.members.length === 0 ? 'Nenhum membro. Adicione usuários na aba Usuários.' : `${detail.members.length} ${detail.members.length === 1 ? 'membro' : 'membros'}: ${detail.members.map((m) => m.name || m.email).join(', ')}`}
        </p>
        <div className="flex flex-wrap gap-2">
          <Can perm="identity.edit"><GroupMembersDialog groupId={g.id} groupName={g.name} current={detail.members.map((m) => m.userId)} onSaved={refresh} /></Can>
          <Can perm="identity.create">
            {!g.isSystem ? <button type="button" onClick={async () => { const copy = await duplicateGroup(g.id); await refresh(); onSelect(copy.id); toastDone('Grupo duplicado') }} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Duplicar</button> : null}
          </Can>
          <Can perm="identity.edit">
            {!readOnly ? (
              <ConfirmDialog
                verb="Inativar"
                done="Grupo inativado"
                title={`Inativar o grupo ${g.name}?`}
                description={`${detail.members.length} ${detail.members.length === 1 ? 'usuário perde' : 'usuários perdem'} as permissões deste grupo no próximo acesso.`}
                destructive
                onConfirm={async () => { await deactivateGroup(g.id); await refresh() }}
                trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold text-crit-ink">Inativar</button>}
              />
            ) : null}
          </Can>
        </div>
      </div>

      {!g.isSystem ? (
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" checked={requires2fa} onChange={(e) => setRequires2fa(e.target.checked)} disabled={readOnly} className="size-4 accent-primary" />
          Exigir verificação em duas etapas dos membros
        </label>
      ) : null}

      <GroupMatrixTable modules={detail.modules} value={matrix} onChange={(code, row) => setMatrix((m) => ({ ...m, [code]: row }))} readOnly={readOnly} />
    </section>
  )
}
