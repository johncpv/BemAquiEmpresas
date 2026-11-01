import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { deleteAsset, getGetAssetQueryKey, getListAssetsQueryKey, updateAsset, useGetAsset } from '@/api/generated/bemaqui'
import type { AssetDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { FormError } from '@/components/form/form'
import { RecordHistory } from '@/components/history/RecordHistory'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { NextSteps } from '@/components/ui/next-steps'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { useUrlTab } from '@/components/ui/url-tab'
import { UrlTabs } from '@/components/ui/url-tabs'
import { Dica } from '@/help/Dica'
import { Can } from '@/pages/ModuleGate'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { AssetDocuments } from './AssetDocuments'
import { AssetLabelButtons } from './AssetLabelActions'
import { printLabel } from './label-downloads'
import { AssetForm } from './AssetForm'
import { AssetPhoto } from './AssetPhoto'
import { ASSET_DICAS, assetDraftErrors, assetInput, draftFromAsset, hasMaintenancePlan, STATUS_LABELS, STATUS_TONES, TYPE_LABELS } from './assets'

const button = 'inline-flex min-h-11 items-center rounded-md border border-border px-3 py-2 text-sm font-semibold'

/** Bem (PAT-01a CA 10, molde "detalhe com abas"): Dados, Documentos, Movimentações, Manutenção e Histórico de alterações, na URL. */
export function AssetPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const search = useSearch({ strict: false }) as Record<string, unknown>
  const navigate = useNavigate()
  const client = useQueryClient()
  const query = useGetAsset(id)
  const canEdit = useCan('assets.edit')
  const [created, setCreated] = useState(search.novo === '1')
  const [, setTab] = useUrlTab('dados')

  if (query.isPending) return <div className="space-y-3"><Skeleton className="h-10 w-2/3" /><Skeleton className="h-96" /></div>
  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return <ErrorState title={notFound ? 'Bem não encontrado.' : 'Não foi possível carregar o bem.'} message={notFound ? 'Ele pode ter sido excluído. Volte à lista de bens.' : undefined} onRetry={notFound ? undefined : () => query.refetch()} />
  }

  const asset = query.data
  const setAsset = (a: AssetDetail) => {
    client.setQueryData(getGetAssetQueryKey(id), a)
    void client.invalidateQueries({ queryKey: getListAssetsQueryKey() })
  }

  // Próximo passo da criação (CA 2): a foto e, para máquina e veículo, o plano de manutenção (aba Manutenção, PAT-03a); "Duplicar bem"
  // para itens iguais; "Imprimir etiqueta" com o QR code (PAT-01b).
  const steps = [
    { label: 'Imprimir etiqueta', onSelect: () => { void printLabel(asset.id, asset.tag); setCreated(false) } },
    ...(canEdit ? [{ label: 'Anexar foto', onSelect: () => { setTab('documentos'); setCreated(false) } }] : []),
    ...(hasMaintenancePlan(asset.type) ? [{ label: 'Criar plano de manutenção', onSelect: () => { setTab('manutencao'); setCreated(false) } }] : []),
    { label: 'Duplicar bem', onSelect: () => void navigate({ to: '/app/patrimonio/bens/novo', search: { de: asset.id } } as never) },
  ]

  return (
    <>
      <PageHeading title={asset.name}>
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <div className="flex flex-wrap items-center gap-1.5">
            <span className="inline-flex items-center text-sm font-semibold tabular-nums">{asset.tag}<Dica nome="Etiqueta" texto={ASSET_DICAS.etiqueta} /></span>
            <StatusChip tone={STATUS_TONES[asset.status]}>{STATUS_LABELS[asset.status]}</StatusChip>
            <Tag>{TYPE_LABELS[asset.type]}</Tag>
            <Link to="/app/patrimonio/bens" className="ml-2 inline-flex min-h-11 items-center text-sm font-semibold text-primary">Voltar à lista</Link>
          </div>
          <div className="flex flex-wrap gap-2">
            <AssetLabelButtons id={asset.id} tag={asset.tag} />
            <Can perm="assets.create">
              <Link to="/app/patrimonio/bens/novo" search={{ de: asset.id } as never} className={button}>Duplicar bem</Link>
            </Can>
            <Can perm="assets.delete">
              <ConfirmDialog verb="Excluir" done="Bem excluído" destructive title={`Excluir ${asset.tag}?`}
                description="Só dá para excluir bem sem movimentação, manutenção nem custo. O bem com histórico é baixado, e a etiqueta nunca é reaproveitada."
                onConfirm={async () => { await deleteAsset(id); void client.invalidateQueries({ queryKey: getListAssetsQueryKey() }); await navigate({ to: '/app/patrimonio/bens' }) }}
                trigger={<button type="button" className={button + ' text-crit-ink'}>Excluir</button>} />
            </Can>
          </div>
        </div>
      </PageHeading>
      {asset.responsibleTerminated ? (
        <div role="status" className="mb-4 flex flex-wrap items-center gap-2 rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">
          <b>{asset.responsibleName} foi desligado.</b>
          <button type="button" onClick={() => setTab('movimentacoes')} className="font-semibold underline">Troque o responsável</button>
        </div>
      ) : null}
      {created ? <div className="mb-4"><NextSteps onDismiss={() => setCreated(false)} steps={steps} /></div> : null}
      <UrlTabs label="Seções do bem" fallback="dados" tabs={[
        { id: 'dados', label: 'Dados', keepMounted: true, content: <AssetData key={asset.version} asset={asset} canEdit={canEdit} onSaved={setAsset} onReload={() => query.refetch()} /> },
        { id: 'documentos', label: 'Documentos', content: <AssetDocuments asset={asset} canEdit={canEdit} onPhotoChanged={() => { void query.refetch(); void client.invalidateQueries({ queryKey: getListAssetsQueryKey() }) }} /> },
        {
          id: 'movimentacoes', label: 'Movimentações',
          content: <EmptyState title="Nenhuma movimentação registrada." description="A troca de local e de responsável e a baixa do bem ficam registradas aqui. Por enquanto, troque o local e o responsável na aba Dados." action={canEdit ? <button type="button" onClick={() => setTab('dados')} className={button}>Ir para Dados</button> : undefined} />,
        },
        {
          id: 'manutencao', label: 'Manutenção',
          content: <EmptyState title="Sem plano de manutenção." description={hasMaintenancePlan(asset.type) ? 'O plano preventivo e as ordens de manutenção desta máquina aparecem aqui.' : 'Os planos de manutenção são para máquinas e veículos.'} />,
        },
        { id: 'historico', label: 'Histórico de alterações', content: <RecordHistory entity="Asset" id={asset.id} /> },
      ]} />
    </>
  )
}

/** Aba Dados (CA 2, 4 e 9): o formulário do bem; com assets.view só, modo leitura. */
function AssetData({ asset, canEdit, onSaved, onReload }: { asset: AssetDetail; canEdit: boolean; onSaved: (a: AssetDetail) => void; onReload: () => Promise<unknown> }) {
  const [initial] = useState(() => draftFromAsset(asset))
  const [draft, setDraft] = useState(initial)
  const [submitted, setSubmitted] = useState(false)
  const [saving, setSaving] = useState(false)
  const [server, setServer] = useState<{ message: string; fields: Record<string, string[]>; code?: string } | null>(null)
  const local = assetDraftErrors(draft)
  const errors: Partial<Record<string, string>> = { ...(submitted ? local : {}), ...Object.fromEntries(Object.entries(server?.fields ?? {}).map(([k, v]) => [k, v[0]])) }
  const dirty = JSON.stringify(draft) !== JSON.stringify(initial)
  const terminated = asset.responsibleTerminated && asset.responsibleEmployeeId ? { id: asset.responsibleEmployeeId, name: asset.responsibleName ?? 'Responsável' } : null

  const save = async () => {
    setSubmitted(true)
    if (Object.keys(local).length || saving) return
    setSaving(true)
    try {
      onSaved(await updateAsset(asset.id, assetInput(draft, canEdit, asset.version)))
      notifySuccess('Bem salvo')
    } catch (e) {
      setServer(e instanceof ApiError ? { message: e.message, fields: e.fields, code: e.code } : { message: 'Não foi possível salvar o bem.', fields: {} })
    } finally {
      setSaving(false)
    }
  }

  return (
    <form noValidate onSubmit={(e) => { e.preventDefault(); void save() }} className="max-w-3xl space-y-4">
      <div className="flex flex-wrap items-start gap-4">
        <AssetPhoto assetId={asset.id} photoId={asset.photoId} name={asset.name} size="large" />
        {!canEdit ? <p className="text-sm text-muted-foreground">Modo leitura: para alterar o bem, peça a permissão de editar o patrimônio.</p> : null}
      </div>
      <FormError message={server && Object.keys(server.fields).length === 0 ? server.message : undefined} />
      {server?.code === 'concurrency_conflict' ? <button type="button" onClick={() => void onReload()} className={button}>Recarregar</button> : null}
      <AssetForm draft={draft} onChange={(next) => { setServer(null); setDraft(next) }} errors={errors} canEditValue={canEdit} disabled={!canEdit} terminatedResponsible={terminated} />
      {canEdit ? (
        <div className="flex justify-end">
          <button type="submit" disabled={!dirty || saving} className="min-h-11 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">{saving ? 'Salvando…' : 'Salvar bem'}</button>
        </div>
      ) : null}
    </form>
  )
}
