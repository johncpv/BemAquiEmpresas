import { useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useState } from 'react'
import { createAssetSite, deleteAssetSite, getListAssetSitesQueryKey, updateAssetSite, useListAssetSites } from '@/api/generated/bemaqui'
import type { AssetSiteView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { SidePanel } from '@/components/ui/side-panel'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Can } from '@/pages/ModuleGate'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { AssetsTabs } from './AssetsTabs'

const primary = 'min-h-11 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground'
const secondary = 'min-h-11 rounded-md border border-border px-3 py-2 text-sm font-semibold'

/** Patrimônio → Locais (PAT-01a CA 5, ADR 0038): setores onde ficam os bens, com o seed Fábrica e Escritório; cadastro no painel lateral. */
export function AssetSitesPage() {
  const query = useListAssetSites({ includeInactive: true })
  const client = useQueryClient()
  const [editing, setEditing] = useState<{ site: AssetSiteView | null } | null>(null)
  const newButton = <Can perm="assets.edit"><button type="button" onClick={() => setEditing({ site: null })} className={primary}>Novo local</button></Can>
  const reload = () => { void client.invalidateQueries({ queryKey: getListAssetSitesQueryKey() }); setEditing(null) }
  return (
    <>
      <PageHeading title="Patrimônio: Locais">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">Os setores onde ficam os bens.</p>
          {newButton}
        </div>
      </PageHeading>
      <AssetsTabs />
      {query.isPending ? <Skeleton className="h-48" /> : query.isError ? <ErrorState onRetry={() => query.refetch()} /> : query.data.length === 0 ? (
        <EmptyState title="Cadastre os setores da empresa." description="O local é escolhido no cadastro de cada bem." action={newButton} />
      ) : (
        <ul aria-label="Locais" className="divide-y divide-border rounded-md border border-border">
          {query.data.map((s) => (
            <li key={s.id} className="flex flex-wrap items-center justify-between gap-2 px-3 py-2 text-sm">
              <button type="button" onClick={() => setEditing({ site: s })} className="min-h-11 font-semibold text-primary hover:underline">{s.name}</button>
              <span className="flex items-center gap-2">
                <Link to="/app/patrimonio/bens" search={{ local: s.id } as never} className="text-muted-foreground tabular-nums hover:underline">{s.assets === 1 ? '1 bem' : `${s.assets} bens`}</Link>
                <StatusChip tone={s.isActive ? 'ok' : 'neutral'}>{s.isActive ? 'Ativo' : 'Inativo'}</StatusChip>
              </span>
            </li>
          ))}
        </ul>
      )}
      {editing ? <SitePanel site={editing.site} onClose={() => setEditing(null)} onSaved={reload} /> : null}
    </>
  )
}

function SitePanel({ site, onClose, onSaved }: { site: AssetSiteView | null; onClose: () => void; onSaved: () => void }) {
  const [name, setName] = useState(site?.name ?? '')
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const canWrite = useCan('assets.edit')
  const save = async (isActive?: boolean) => {
    try {
      if (site) await updateAssetSite(site.id, { name, isActive: isActive ?? site.isActive })
      else await createAssetSite({ name })
      notifySuccess(isActive === undefined ? 'Local salvo' : isActive ? 'Reativado' : 'Inativado')
      onSaved()
      return true
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar o local.', fields: {} })
      return false
    }
  }

  return (
    <SidePanel open onOpenChange={(o) => !o && onClose()} title={site ? site.name : 'Novo local'} dirty={canWrite && name !== (site?.name ?? '')} onSave={() => save()}
      footer={(
        <>
          {site && canWrite ? <button type="button" onClick={() => void save(!site.isActive)} className={secondary + ' mr-auto'}>{site.isActive ? 'Inativar' : 'Reativar'}</button> : null}
          {site ? (
            <Can perm="assets.delete">
              <ConfirmDialog verb="Excluir" done="Local excluído" destructive title={`Excluir ${site.name}?`} description="Só dá para excluir local sem bens. Com bens, inative o local."
                onConfirm={async () => { await deleteAssetSite(site.id); onSaved() }}
                trigger={<button type="button" className={secondary + ' text-crit-ink'}>Excluir</button>} />
            </Can>
          ) : null}
          <button type="button" onClick={onClose} className={secondary}>Voltar</button>
          {canWrite ? <button type="button" onClick={() => void save()} className={primary}>Salvar local</button> : null}
        </>
      )}
    >
      <fieldset disabled={!canWrite} className="space-y-3">
        <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
        <Field label="Nome" required error={error?.fields.name?.[0]} hint="Ex.: Fábrica, Escritório, Almoxarifado.">
          {(p) => <input {...p} value={name} maxLength={100} onChange={(e) => { setError(null); setName(e.target.value) }} className={inputClass} />}
        </Field>
      </fieldset>
    </SidePanel>
  )
}
