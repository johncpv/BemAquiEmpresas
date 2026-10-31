import { useRef, useState } from 'react'
import { getDownloadAssetFileUrl, uploadAssetDocument, uploadAssetPhoto, useListAssetFiles } from '@/api/generated/bemaqui'
import type { AssetDetail, AttachmentDto } from '@/api/generated/model'
import { ApiError, apiDownload } from '@/api/http'
import { FormError, FormSection } from '@/components/form/form'
import { notifySuccess } from '@/components/ui/messages'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { ASSET_DICAS } from './assets'

const attachClass = 'inline-flex min-h-11 cursor-pointer items-center rounded-md border border-border px-3 py-2 text-sm font-semibold'

/**
 * Aba Documentos (CA 8): a foto (PNG, JPG ou WebP até 5 MB, regravada sem EXIF nem GPS) e os documentos (PDF, PNG ou JPG até 10 MB: nota,
 * manual, garantia), pela rota do bem. A foto só aparece na ficha e na lista depois da verificação do antivírus.
 */
export function AssetDocuments({ asset, canEdit, onPhotoChanged }: { asset: AssetDetail; canEdit: boolean; onPhotoChanged: () => void }) {
  const files = useListAssetFiles(asset.id, { query: { gcTime: 0, staleTime: 0 } })
  if (files.isPending) return <Skeleton className="h-48 max-w-3xl" />
  if (files.isError || !files.data) return <ErrorState title="Não foi possível carregar a foto e os documentos." onRetry={() => files.refetch()} />
  const reload = async () => { await files.refetch() }
  return (
    <div className="max-w-3xl space-y-6">
      <Upload title="Foto" titleDica={ASSET_DICAS.foto} description="PNG, JPG ou WEBP até 5 MB. A localização gravada na foto é apagada." accept=".png,.jpg,.jpeg,.webp" inputId="foto-bem" verb="Enviar foto"
        canEdit={canEdit} items={files.data.photos} assetId={asset.id} empty="Sem foto. Uma foto ajuda a reconhecer o bem na lista."
        send={async (file) => { await uploadAssetPhoto(asset.id, { file }); notifySuccess('Foto enviada'); await reload(); onPhotoChanged() }} />
      <Upload title="Documentos" description="Nota, manual e garantia, em PDF, PNG ou JPG até 10 MB." accept=".pdf,.png,.jpg,.jpeg" inputId="documento-bem" verb="Anexar documento"
        canEdit={canEdit} items={files.data.documents} assetId={asset.id} empty="Nenhum documento anexado."
        send={async (file) => { await uploadAssetDocument(asset.id, { file }); notifySuccess('Documento anexado'); await reload() }} />
    </div>
  )
}

interface UploadProps {
  title: string
  titleDica?: string
  description: string
  accept: string
  inputId: string
  verb: string
  canEdit: boolean
  items: AttachmentDto[]
  assetId: string
  empty: string
  send: (file: File) => Promise<void>
}

function Upload({ title, titleDica, description, accept, inputId, verb, canEdit, items, assetId, empty, send }: UploadProps) {
  const input = useRef<HTMLInputElement>(null)
  const [error, setError] = useState<string>()
  const [sending, setSending] = useState(false)
  const upload = async (file: File) => {
    setSending(true)
    setError(undefined)
    try {
      await send(file)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível enviar o arquivo.')
    } finally {
      setSending(false)
      if (input.current) input.current.value = ''
    }
  }

  const attach = canEdit ? (
    <>
      <input ref={input} id={inputId} type="file" accept={accept} className="sr-only" onChange={(e) => { const f = e.target.files?.[0]; if (f) void upload(f) }} />
      <label htmlFor={inputId} className={attachClass}>{sending ? 'Enviando…' : verb}</label>
    </>
  ) : undefined

  return (
    <FormSection title={title} description={description}>
      <div className="col-span-full space-y-2">
        {titleDica ? <p className="inline-flex items-center text-xs text-muted-foreground">Sobre a foto<Dica nome="Foto" texto={titleDica} /></p> : null}
        <FormError message={error} />
        {items.length === 0 ? <EmptyState title={empty} action={attach} /> : (
          <>
            <ul aria-label={title} className="divide-y divide-border rounded-md border border-border bg-card text-sm">
              {items.map((f) => (
                <li key={f.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
                  <span className="flex flex-wrap items-center gap-2">
                    <button type="button" disabled={f.scanStatus !== 1} onClick={() => void apiDownload(getDownloadAssetFileUrl(assetId, f.id), f.fileName)} className="break-all font-semibold text-primary hover:underline disabled:text-muted-foreground">{f.fileName}</button>
                    {f.scanStatus === 1 ? null : <StatusChip tone="info">Em verificação</StatusChip>}
                  </span>
                  <span className="text-xs text-muted-foreground tabular-nums">{formatDateTime(f.createdAt)}</span>
                </li>
              ))}
            </ul>
            {attach}
          </>
        )}
      </div>
    </FormSection>
  )
}
