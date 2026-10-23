import { useState } from 'react'
import { createBom, copyBom, releaseBom, useGetBomReleasePreview, useListBomCopySources } from '@/api/generated/bemaqui'
import type { BomDetail, BomReleasePreview } from '@/api/generated/model'
import { useIdempotencyKey } from '@/api/idempotency'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { formatInstantDate } from '@/lib/format'
import { BOM_DICAS, qty } from './boms'

const CHANGE: Record<string, string> = { Added: 'Incluído', Removed: 'Removido', Changed: 'Alterado', Unchanged: 'Sem mudança' }

/**
 * "Liberar BOM" e "Liberar revisão" (CA 8, 9, 13, 14; padrão 4): diálogo de impacto com a prévia por item (necessidade, disponível,
 * vai reservar, falta; na revisão, antes → depois, a reservar a mais e a liberar). BOM vazia: "BOM sem material". A 400 px, tela cheia.
 */
export function ReleaseBomDialog({ bom, dirty, open, onOpenChange, onDone, onReload }: {
  bom: BomDetail
  dirty: boolean
  open: boolean
  onOpenChange: (open: boolean) => void
  onDone: (preview: BomReleasePreview | undefined) => Promise<void>
  onReload: () => Promise<unknown>
}) {
  const preview = useGetBomReleasePreview(bom.id, { query: { enabled: open, staleTime: 0 } })
  const p = preview.data
  const revision = p?.isRevision ?? bom.revision > 0
  const verb = revision ? 'Liberar revisão' : 'Liberar BOM'
  return (
    <ConfirmDialog size="lg" open={open} onOpenChange={onOpenChange} verb={verb} done={revision ? 'Revisão liberada. Ajustando as reservas…' : 'BOM liberada. Reservando material…'}
      title={revision ? `Liberar a revisão ${bom.revision} da BOM?` : 'Liberar a BOM desta OS?'}
      description={(
        <span className="inline-flex items-center">
          {p?.empty ? 'BOM sem material: esta OS usa só mão de obra.' : revision ? 'A revisão anterior fica substituída e as reservas mudam pela diferença.' : 'O estoque reserva o material e o comprador vê o que falta.'}
          <Dica nome="Liberar" texto={BOM_DICAS.liberar} />
        </span>
      )}
      preconditions={[{ ok: !dirty, text: 'Salve a BOM antes de liberar.' }, { ok: !!p || preview.isError, text: 'Carregando a prévia…' }]}
      onReload={onReload}
      onConfirm={async () => {
        await releaseBom(bom.id, { version: bom.version })
        await onDone(p)
      }}
      impact={preview.isPending ? <Skeleton className="h-24" /> : preview.isError ? <p>Não foi possível carregar a prévia. Feche e abra de novo.</p> : p && !p.empty ? <ReleaseImpact preview={p} /> : null} />
  )
}

function ReleaseImpact({ preview }: { preview: BomReleasePreview }) {
  const cell = 'p-1.5 text-right tabular-nums'
  return (
    <div className="space-y-2">
      {!preview.stockConnected ? <p className="text-xs text-muted-foreground">O saldo do estoque ainda não aparece. A falta é calculada quando o estoque estiver ligado.</p> : null}
      {preview.stockConnected && !preview.reservationsConnected ? <p className="text-xs text-muted-foreground">Quanto vai ser reservado e o que já foi baixado ainda não aparecem nesta versão.</p> : null}
      {preview.reservationsConnected ? <p className="text-xs text-muted-foreground">Ao liberar, o estoque reserva o disponível de cada item para esta OS e o que faltar vira falta para compras. Acompanhe na aba Material.</p> : null}
      {preview.inactiveCount > 0 ? <p className="rounded-md border border-warn-soft bg-warn-soft px-2 py-1 text-xs text-warn-ink">{preview.inactiveCount} {preview.inactiveCount === 1 ? 'item inativo' : 'itens inativos'} na BOM. Confira antes de liberar.</p> : null}
      <div className="overflow-x-auto">
        <table className="w-full text-xs">
          <caption className="sr-only">Prévia da liberação por item</caption>
          <thead>
            <tr className="text-left text-muted-foreground">
              <th className="p-1.5 font-semibold">Item</th>
              <th className={cell}>Necessidade</th>
              {preview.isRevision ? <><th className={cell}>A reservar a mais</th><th className={cell}>A liberar</th></> : null}
              <th className={`${cell} max-sm:hidden`}>Disponível</th>
              {preview.reservationsConnected ? <th className={cell}>Vai reservar</th> : null}
              <th className={cell}>Falta</th>
            </tr>
          </thead>
          <tbody>
            {preview.lines.map((l) => (
              <tr key={l.itemId} className="border-t border-border">
                <td className="p-1.5">
                  <span className="font-semibold">{l.itemCode}</span> {l.itemName}
                  {preview.isRevision && l.change !== 'Unchanged' ? <span className="ml-1 text-muted-foreground">({CHANGE[l.change]})</span> : null}
                  {!l.itemActive && l.change !== 'Removed' ? <span className="ml-1"><StatusChip tone="warn">Inativo</StatusChip></span> : null}
                </td>
                <td className={cell}>{preview.isRevision ? `${qty(l.previousRequired)} → ${qty(l.required, l.stockUnitCode)}` : qty(l.required, l.stockUnitCode)}</td>
                {preview.isRevision ? <><td className={cell}>{qty(l.moreToReserve)}</td><td className={cell}>{qty(l.toRelease)}</td></> : null}
                <td className={`${cell} max-sm:hidden`}>{qty(l.available)}</td>
                {preview.reservationsConnected ? <td className={cell}>{qty(l.toReserve)}</td> : null}
                <td className={cell}>{(l.shortage ?? 0) > 0 ? <StatusChip tone="crit">Falta {qty(l.shortage)}</StatusChip> : qty(l.shortage)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {preview.lines.filter((l) => l.issuedWarning).map((l) => <p key={l.itemId} role="note" className="rounded-md border border-warn-soft bg-warn-soft px-2 py-1 text-xs text-warn-ink">{l.issuedWarning}</p>)}
      {preview.stockConnected ? <p className="font-semibold">{preview.shortageCount === 0 ? 'Todo o material está disponível.' : `${preview.shortageCount} ${preview.shortageCount === 1 ? 'item em falta' : 'itens em falta'} pelo saldo disponível.`}</p> : null}
    </div>
  )
}

/**
 * "Copiar BOM" (CA 5, padrão 6): escolhe a BOM liberada de outra OS, com a última do mesmo produto marcada "Sugerido". Sem rascunho, cria
 * a BOM já copiada; com rascunho, troca as linhas dele.
 */
export function CopyBomDialog({ orderId, draft, open, onOpenChange, onDone }: {
  orderId: string
  draft: BomDetail | null
  open: boolean
  onOpenChange: (open: boolean) => void
  onDone: (bom: BomDetail) => Promise<void>
}) {
  const [term, setTerm] = useState('')
  const [picked, setPicked] = useState<string | null>(null)
  const { options, renew } = useIdempotencyKey()
  const sources = useListBomCopySources(orderId, { q: term.trim() || undefined }, { query: { enabled: open } })
  const list = sources.data ?? []
  const chosen = picked ?? list.find((s) => s.suggested)?.bomId ?? null
  return (
    <ConfirmDialog size="lg" open={open} onOpenChange={onOpenChange} verb="Copiar BOM" done="BOM copiada" onOpen={() => { setPicked(null); setTerm('') }}
      title="Copiar a BOM de outra OS"
      description={draft && draft.lines.length > 0 ? 'As linhas do rascunho serão trocadas pelas da BOM escolhida. Nada vai para o estoque até liberar.' : 'As linhas entram no rascunho desta OS, com a necessidade calculada pela quantidade desta OS.'}
      preconditions={[{ ok: !!chosen, text: list.length === 0 && !sources.isPending ? 'Nenhuma OS com BOM liberada para copiar.' : 'Escolha a BOM de origem.' }]}
      onConfirm={async () => {
        const bom = draft ? await copyBom(draft.id, { sourceBomId: chosen!, version: draft.version }) : await createBom({ productionOrderId: orderId, copyFromBomId: chosen }, options)
        renew()
        await onDone(bom)
      }}>
      <label className="block space-y-1 text-sm font-medium">
        Buscar pela OS ou pelo produto
        <input value={term} onChange={(e) => setTerm(e.target.value)} className={inputClass} />
      </label>
      {sources.isPending ? <Skeleton className="h-20" /> : sources.isError ? <p role="alert" className="text-sm text-crit-ink">Não foi possível carregar as BOMs. Tente de novo.</p> : (
        <fieldset className="space-y-2">
          <legend className="sr-only">BOM de origem</legend>
          {list.length === 0 ? <p className="text-sm text-muted-foreground">Nenhuma OS com BOM liberada encontrada.</p> : list.map((s) => (
            <label key={s.bomId} className="flex items-start gap-2 rounded-md border border-border p-2 text-sm">
              <input type="radio" name="origem-bom" className="mt-1 size-4 accent-primary" checked={chosen === s.bomId} onChange={() => setPicked(s.bomId)} />
              <span className="flex-1">
                <b className="font-semibold">{s.orderNumber}</b> · revisão {s.revision}{s.suggested ? <Tag className="ml-2">Sugerido</Tag> : null}
                <span className="block text-muted-foreground">{s.productCode ? `${s.productCode} · ` : ''}{s.productName ?? 'Sem item de estoque'} · {s.lineCount} {s.lineCount === 1 ? 'item' : 'itens'} · liberada em {formatInstantDate(s.releasedAt)}</span>
              </span>
            </label>
          ))}
        </fieldset>
      )}
    </ConfirmDialog>
  )
}
