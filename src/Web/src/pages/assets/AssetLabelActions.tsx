import { useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { Printer } from 'lucide-react'
import { useState } from 'react'
import { getPrintAssetLabelUrl, getReprintAssetLabelUrl, startAssetLabelBatch } from '@/api/generated/bemaqui'
import { AssetLabelLayout, type AssetLabelFilter } from '@/api/generated/model'
import { apiDownload, ApiError } from '@/api/http'
import { ToggleGroup } from '@/components/form/choice'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifyError, notifySuccess } from '@/components/ui/messages'
import { Dica } from '@/help/Dica'
import { Can } from '@/pages/ModuleGate'
import { downloadLabelBatch } from './label-downloads'
import { LABEL_BATCH_MAX, LABEL_DICAS, LABEL_INLINE_LIMIT } from './labels'

const button = 'inline-flex min-h-11 items-center gap-1.5 rounded-md border border-border px-3 py-2 text-sm font-semibold disabled:opacity-60'

/** O histórico do bem mostra a impressão na hora (CA 12). */
function useRefreshHistory() {
  const client = useQueryClient()
  return () => void client.invalidateQueries({ predicate: (q) => String(q.queryKey[0]).startsWith('/api/v1/audit/Asset/') })
}

/**
 * "Imprimir etiqueta" (CA 1, `assets.view`) e "Reimprimir etiqueta" (CA 4, `assets.edit`) na ficha do bem: o PDF no tamanho de Configurações →
 * Parâmetros → Patrimônio; a etiqueta nunca muda e as duas ficam no histórico.
 */
export function AssetLabelButtons({ id, tag }: { id: string; tag: string }) {
  const [busy, setBusy] = useState<'print' | 'reprint' | null>(null)
  const refresh = useRefreshHistory()
  const run = async (kind: 'print' | 'reprint') => {
    setBusy(kind)
    try {
      await apiDownload(kind === 'print' ? getPrintAssetLabelUrl(id) : getReprintAssetLabelUrl(id), `etiqueta-${tag}.pdf`)
      notifySuccess(kind === 'print' ? 'Etiqueta pronta para imprimir' : 'Etiqueta reimpressa')
      refresh()
    } catch (e) {
      notifyError(e instanceof ApiError ? e.message : 'Não foi possível gerar a etiqueta. Tente de novo.')
    } finally {
      setBusy(null)
    }
  }
  return (
    <>
      <button type="button" disabled={!!busy} onClick={() => void run('print')} className={button}><Printer aria-hidden="true" className="size-4" />{busy === 'print' ? 'Gerando…' : 'Imprimir etiqueta'}</button>
      <Can perm="assets.edit">
        <button type="button" disabled={!!busy} onClick={() => void run('reprint')} className={button}>{busy === 'reprint' ? 'Gerando…' : 'Reimprimir etiqueta'}</button>
      </Can>
    </>
  )
}

/**
 * "Imprimir etiquetas" na lista (CA 3): os selecionados ou todos do filtro (até 500), uma por página (impressora de etiquetas) ou em
 * folha A4. Até 50 o PDF baixa na hora; acima, sai em segundo plano e o sino avisa quando estiver pronto.
 */
export function PrintLabelsDialog({ selected, filter, filteredTotal, onDone }: { selected: string[]; filter: AssetLabelFilter; filteredTotal: number; onDone: () => void }) {
  const [scope, setScope] = useState<'selected' | 'filter'>(selected.length ? 'selected' : 'filter')
  const [layout, setLayout] = useState<AssetLabelLayout>(AssetLabelLayout.Label)
  const refresh = useRefreshHistory()
  const count = scope === 'selected' ? selected.length : filteredTotal
  const queued = count > LABEL_INLINE_LIMIT
  return (
    <ConfirmDialog verb="Imprimir etiquetas" done={queued ? 'Gerando as etiquetas. O sino avisa quando o PDF estiver pronto' : 'Etiquetas prontas para imprimir'}
      title="Imprimir etiquetas" size="lg"
      description="Cada etiqueta leva o QR code, o número do bem, o nome e o nome da empresa. Nenhum valor nem dado do responsável."
      onOpen={() => { setScope(selected.length ? 'selected' : 'filter'); setLayout(AssetLabelLayout.Label) }}
      preconditions={[
        { ok: count > 0, text: 'Selecione ao menos um bem na lista ou use todos do filtro.' },
        { ok: count <= LABEL_BATCH_MAX, text: `Imprima até ${LABEL_BATCH_MAX} etiquetas por vez. Refine o filtro da lista.` },
      ]}
      impact={<p className="text-sm tabular-nums">{count === 1 ? '1 etiqueta' : `${count.toLocaleString('pt-BR')} etiquetas`}{queued ? ' · gerada em segundo plano' : ''}</p>}
      onConfirm={async () => {
        const result = await startAssetLabelBatch({ assetIds: scope === 'selected' ? selected : null, allFromFilter: scope === 'filter', filter: scope === 'filter' ? filter : null, layout })
        if (!result.queued && result.attachmentId) await downloadLabelBatch(result.attachmentId)
        refresh()
        onDone()
      }}
      trigger={<button type="button" className={button}><Printer aria-hidden="true" className="size-4" />Imprimir etiquetas</button>}>
      <div className="space-y-4">
        <div className="space-y-1">
          <p className="text-sm font-semibold">Quais bens</p>
          <ToggleGroup label="Quais bens" value={scope} onChange={setScope} options={[
            { value: 'selected', label: selected.length === 1 ? '1 selecionado' : `${selected.length} selecionados` },
            { value: 'filter', label: `Todos do filtro (${filteredTotal.toLocaleString('pt-BR')})` },
          ]} />
        </div>
        <div className="space-y-1">
          <p className="flex items-center text-sm font-semibold">Como imprimir<Dica nome="Folha A4" texto={LABEL_DICAS.folhaA4} /></p>
          <ToggleGroup label="Como imprimir" value={layout} onChange={setLayout} options={[
            { value: AssetLabelLayout.Label, label: 'Impressora de etiquetas' },
            { value: AssetLabelLayout.A4Sheet, label: 'Folha A4' },
          ]} />
        </div>
      </div>
    </ConfirmDialog>
  )
}

/** Aviso do sino (CA 3): o link `?lote=` traz o PDF pronto para baixar; expirado depois de 7 dias, o motivo e como resolver. */
export function LabelBatchReady({ attachmentId }: { attachmentId: string }) {
  const navigate = useNavigate()
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const close = () => void navigate({ to: '.', search: (prev: Record<string, unknown>) => ({ ...prev, lote: undefined }) } as never)
  const download = async () => {
    setBusy(true)
    setError(null)
    try {
      await downloadLabelBatch(attachmentId)
      notifySuccess('Etiquetas baixadas')
      close()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível baixar as etiquetas. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }
  return (
    <div role="status" className="mb-4 flex flex-wrap items-center gap-2 rounded-md border border-ok-soft bg-ok-soft px-3 py-2 text-sm text-ok-ink">
      <b>As etiquetas estão prontas.</b>
      <button type="button" disabled={busy} onClick={() => void download()} className="font-semibold underline disabled:opacity-60">{busy ? 'Baixando…' : 'Baixar etiquetas'}</button>
      <button type="button" onClick={close} className="ml-auto font-semibold">Fechar</button>
      {error ? <p role="alert" className="w-full text-crit-ink">{error}</p> : null}
    </div>
  )
}
