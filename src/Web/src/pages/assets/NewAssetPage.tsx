import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { createAsset, useGetAsset } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { FormError } from '@/components/form/form'
import { UnsavedChangesGuard } from '@/components/form/UnsavedChangesGuard'
import { notifySuccess } from '@/components/ui/messages'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { AssetForm } from './AssetForm'
import { type AssetDraft, assetDraftErrors, assetInput, draftFromAsset, emptyAssetDraft } from './assets'

/** Novo bem (PAT-01a CA 2): página única em seções. <c>?de=</c> é o "Duplicar bem": copia tudo menos a série e a etiqueta. */
export function NewAssetPage() {
  const { de } = useSearch({ strict: false }) as { de?: string }
  const source = useGetAsset(de ?? '', { query: { enabled: !!de } })
  if (de && source.isPending) return <Skeleton className="h-96 max-w-3xl" />
  if (de && source.isError) return <ErrorState title="Não foi possível carregar o bem a duplicar." onRetry={() => source.refetch()} />
  return <NewAssetForm key={de ?? 'novo'} initial={de && source.data ? draftFromAsset(source.data, true) : emptyAssetDraft()} duplicateOf={de && source.data ? source.data.tag : undefined} />
}

function NewAssetForm({ initial, duplicateOf }: { initial: AssetDraft; duplicateOf?: string }) {
  const navigate = useNavigate()
  const canEditValue = useCan('assets.edit')
  const [draft, setDraft] = useState(initial)
  const [submitted, setSubmitted] = useState(false)
  const [saving, setSaving] = useState(false)
  const [server, setServer] = useState<{ message: string; fields: Record<string, string[]>; code?: string; assetId?: string } | null>(null)
  const local = assetDraftErrors(draft)
  const errors: Partial<Record<string, string>> = { ...(submitted ? local : {}), ...Object.fromEntries(Object.entries(server?.fields ?? {}).map(([k, v]) => [k, v[0]])) }
  const dirty = JSON.stringify(draft) !== JSON.stringify(initial)

  const save = async () => {
    setSubmitted(true)
    if (Object.keys(local).length || saving) return false
    setSaving(true)
    try {
      const saved = await createAsset(assetInput(draft, canEditValue))
      notifySuccess(`Bem ${saved.tag} salvo`)
      await navigate({ to: '/app/patrimonio/bens/$id', params: { id: saved.id }, search: { novo: '1' }, ignoreBlocker: true } as never)
      return true
    } catch (e) {
      setServer(e instanceof ApiError
        ? { message: e.message, fields: e.fields, code: e.code, assetId: typeof e.data.assetId === 'string' ? e.data.assetId : undefined }
        : { message: 'Não foi possível salvar o bem.', fields: {} })
      return false
    } finally {
      setSaving(false)
    }
  }

  return (
    <>
      <PageHeading title={duplicateOf ? `Novo bem (cópia de ${duplicateOf})` : 'Novo bem'}>
        <p className="mt-2 text-muted-foreground">Nome e tipo bastam. A etiqueta é gerada ao salvar.</p>
      </PageHeading>
      <form noValidate onSubmit={(e) => { e.preventDefault(); void save() }} className="max-w-3xl space-y-4">
        <UnsavedChangesGuard when={dirty} onSave={save} />
        <FormError message={server && Object.keys(server.fields).length === 0 ? server.message : undefined} />
        {server?.assetId ? <Link to="/app/patrimonio/bens/$id" params={{ id: server.assetId }} className="inline-flex min-h-11 items-center text-sm font-semibold text-primary">Abrir bem</Link> : null}
        <AssetForm draft={draft} onChange={(next) => { setServer(null); setDraft(next) }} errors={errors} canEditValue={canEditValue} />
        <div className="flex justify-end gap-2">
          <Link to="/app/patrimonio/bens" className="inline-flex min-h-11 items-center rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</Link>
          <button type="submit" disabled={saving} className="min-h-11 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">{saving ? 'Salvando…' : 'Salvar bem'}</button>
        </div>
      </form>
    </>
  )
}
