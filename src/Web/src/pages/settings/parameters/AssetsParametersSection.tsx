import { useQueryClient } from '@tanstack/react-query'
import { QrCode } from 'lucide-react'
import { useId, useState } from 'react'
import { getGetAssetParametersQueryKey, updateAssetParameters, useGetAssetParameters } from '@/api/generated/bemaqui'
import type { AssetParametersView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { RecordHistory } from '@/components/history/RecordHistory'
import { notifySuccess } from '@/components/ui/messages'
import { Skeleton } from '@/components/ui/states'
import { Dica } from '@/help/Dica'
import { useCan } from '@/pages/use-can'
import { LABEL_DICAS, labelSizeError } from '@/pages/assets/labels'

/**
 * Patrimônio (PAT-01b CA 1): o tamanho da etiqueta (`assets.label_size`, 50 × 25 mm até a empresa informar a impressora, P-13), com a
 * prévia de uma etiqueta. Aparece para quem vê o patrimônio; salvar com settings.edit, com o histórico de alterações.
 */
export function AssetsParametersSection({ canEdit }: { canEdit: boolean }) {
  const canSee = useCan('assets.view')
  const query = useGetAssetParameters({ query: { enabled: canSee } })
  if (!canSee) return null
  if (query.isPending) return <Skeleton className="h-24" />
  if (query.isError) return null
  const p = query.data
  return <AssetsForm key={`${p.labelWidthMm}x${p.labelHeightMm}`} params={p} canEdit={canEdit} />
}

function AssetsForm({ params, canEdit }: { params: AssetParametersView; canEdit: boolean }) {
  const client = useQueryClient()
  const [width, setWidth] = useState(String(params.labelWidthMm))
  const [height, setHeight] = useState(String(params.labelHeightMm))
  const [error, setError] = useState<string | undefined>()
  const [saving, setSaving] = useState(false)
  const widthId = useId()
  const heightId = useId()
  const invalid = labelSizeError(width, height)
  const dirty = Number(width) !== params.labelWidthMm || Number(height) !== params.labelHeightMm
  const save = async () => {
    setSaving(true)
    try {
      const saved = await updateAssetParameters({ labelWidthMm: Number(width), labelHeightMm: Number(height) })
      client.setQueryData(getGetAssetParametersQueryKey(), saved)
      void client.invalidateQueries({ predicate: (q) => String(q.queryKey[0]).startsWith('/api/v1/audit/AssetParameter/') })
      notifySuccess('Tamanho da etiqueta salvo')
      setError(undefined)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar o tamanho da etiqueta.')
    } finally {
      setSaving(false)
    }
  }
  const digits = (v: string) => v.replace(/[^0-9]/g, '').slice(0, 3)
  return (
    <FormSection title="Patrimônio" description="O tamanho da etiqueta com QR code dos bens, como no rolo da impressora de etiquetas.">
      <fieldset disabled={!canEdit} className="space-y-3">
        <legend className="flex items-center text-sm font-semibold">Tamanho da etiqueta (mm)<Dica nome="Tamanho da etiqueta" texto={LABEL_DICAS.tamanho} /></legend>
        <div className="flex flex-wrap items-end gap-3">
          <div className="space-y-1">
            <label htmlFor={widthId} className="block text-sm">Largura</label>
            <input id={widthId} inputMode="numeric" value={width} onChange={(e) => setWidth(digits(e.target.value))} aria-invalid={!!invalid} className={inputClass + ' w-24 tabular-nums'} />
          </div>
          <span aria-hidden="true" className="pb-2 text-muted-foreground">×</span>
          <div className="space-y-1">
            <label htmlFor={heightId} className="block text-sm">Altura</label>
            <input id={heightId} inputMode="numeric" value={height} onChange={(e) => setHeight(digits(e.target.value))} aria-invalid={!!invalid} className={inputClass + ' w-24 tabular-nums'} />
          </div>
        </div>
        {invalid ? <p role="alert" className="text-sm text-crit-ink">{invalid}</p> : <p className="text-sm text-muted-foreground">Valor inicial: 50 × 25 mm. Confira com o rolo da impressora de etiquetas.</p>}
        {!invalid ? <LabelPreview width={Number(width)} height={Number(height)} /> : null}
        {error ? <p role="alert" className="text-sm text-crit-ink">{error}</p> : null}
        {canEdit ? <button type="button" disabled={!dirty || !!invalid || saving} onClick={() => void save()} className="min-h-[44px] rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">{saving ? 'Salvando…' : 'Salvar tamanho da etiqueta'}</button> : null}
      </fieldset>
      {params.labelSizeId ? (
        <details className="rounded-md border border-border bg-card px-3 py-2 text-sm">
          <summary className="cursor-pointer font-semibold">Histórico de alterações</summary>
          <div className="mt-3"><RecordHistory entity="AssetParameter" id={params.labelSizeId} /></div>
        </details>
      ) : null}
    </FormSection>
  )
}

/** Prévia em escala (4 px por mm, até 320 px de largura): o QR à esquerda, o número, o nome e a empresa à direita, como no PDF. */
function LabelPreview({ width, height }: { width: number; height: number }) {
  const scale = Math.min(4, 320 / width)
  const qr = Math.min(height - 3, width * 0.5) * scale
  return (
    <figure className="space-y-1">
      <div role="img" aria-label={`Prévia da etiqueta de ${width} × ${height} mm`} style={{ width: width * scale, height: height * scale }}
        className="flex items-center gap-1.5 overflow-hidden rounded-sm border border-border bg-card p-1.5 text-foreground">
        <QrCode aria-hidden="true" style={{ width: qr, height: qr }} className="shrink-0" />
        <div className="min-w-0 leading-tight">
          <p className="truncate font-display text-sm font-bold tabular-nums">PAT-00001</p>
          <p className="line-clamp-2 text-[10px]">Torno CNC Romi GL 240</p>
          <p className="truncate text-[9px]">Nome da empresa</p>
        </div>
      </div>
      <figcaption className="text-xs text-muted-foreground tabular-nums">Prévia: {width} × {height} mm</figcaption>
    </figure>
  )
}
