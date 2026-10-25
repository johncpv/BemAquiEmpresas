import { useEffect, useRef, useState } from 'react'
import {
  completeFabricationOrder, reverseFabricationOrderCompletion, setFabricationOrderProduct, useGetFabricationOrderCompletion, useGetFabricationOrderCompletionCost,
  useListFabricationProductOptions,
} from '@/api/generated/bemaqui'
import type { CompletionStockView, FabricationOrderDetail, OrderCompletionPreview } from '@/api/generated/model'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Skeleton } from '@/components/ui/states'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { formatDate, formatMoney } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import {
  canShowComplete, completionImpact, FABRICATION_DICAS, FINISHED_GOODS, reversalImpact, serialsProblem, stockCompletionImpact, stockReversalImpact, unitCost,
} from './fabrication'

const primary = 'rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground'
const secondary = 'rounded-md border border-border px-3 py-2 text-sm font-semibold'
const SLOW_AFTER_MS = 30_000

function Impact({ lines, loading, failed }: { lines: string[]; loading: boolean; failed: boolean }) {
  if (loading) return <Skeleton className="h-16" />
  if (failed) return <p className="text-sm text-crit-ink">Não foi possível ler o impacto. Feche e abra de novo.</p>
  return <ul className="list-disc space-y-1 pl-5 text-sm">{lines.map((l) => <li key={l}>{l}</li>)}</ul>
}

/** Custo apurado da OS (CA 2 e 3), só com projects.view_cost, lido em DTO separado. */
function CompletionCost({ orderId, open }: { orderId: string; open: boolean }) {
  const cost = useGetFabricationOrderCompletionCost(orderId, { query: { enabled: open } })
  if (cost.isPending) return <Skeleton className="h-6" />
  if (cost.isError) return <p className="text-sm text-crit-ink">Não foi possível ler o custo apurado.</p>
  const c = cost.data
  return (
    <p className="text-sm">
      <span className="inline-flex items-center font-semibold">Custo apurado da OS<Dica nome="Custo apurado da OS" texto={FABRICATION_DICAS.custoApurado} /></span>{' '}
      <span className="tabular-nums">{formatMoney(c.total)}</span>
      <span className="block text-xs text-muted-foreground tabular-nums">Horas {formatMoney(c.labor)} · material {formatMoney(c.material)} · custo unitário {unitCost(c.unit)}</span>
    </p>
  )
}

/** Local de entrada e séries (CA 2 e 4): local sugerido com a tag "Sugerido"; séries geradas e editáveis, em lista com rolagem. */
function StockFields({ stock, locationId, onLocation, serials, onSerials }: {
  stock: CompletionStockView; locationId: string; onLocation: (id: string) => void; serials: string[]; onSerials: (s: string[]) => void
}) {
  return (
    <div className="space-y-3">
      <label className="block space-y-1 text-sm font-medium">
        <span className="flex items-center gap-2">Local de entrada{locationId === stock.suggestedLocationId ? <Tag>Sugerido</Tag> : null}</span>
        <select value={locationId} onChange={(e) => onLocation(e.target.value)} className={inputClass}>
          {stock.locations.map((l) => <option key={l.id} value={l.id}>{l.code} — {l.name}</option>)}
        </select>
      </label>
      {stock.tracksSerial ? (
        <fieldset className="space-y-1">
          <legend className="flex items-center text-sm font-medium">Número de série<Dica nome="Número de série" texto={FABRICATION_DICAS.serie} /></legend>
          <ol className="max-h-48 space-y-1 overflow-y-auto pr-1">
            {serials.map((s, i) => (
              <li key={i}>
                <label className="flex items-center gap-2 text-sm">
                  <span className="w-16 shrink-0 text-muted-foreground tabular-nums">Unid. {i + 1}</span>
                  <input value={s} maxLength={60} onChange={(e) => onSerials(serials.map((x, j) => (j === i ? e.target.value : x)))} className={`${inputClass} tabular-nums`} aria-label={`Número de série da unidade ${i + 1}`} />
                </label>
              </li>
            ))}
          </ol>
        </fieldset>
      ) : null}
    </div>
  )
}

/**
 * "Concluir OS" (PRD-06a1 CA 2 a 5; PRD-06a2 CA 1 a 7): com production.approve, na OS "Pronta para concluir"; fora dela, o botão fica
 * desabilitado com o motivo do servidor (sem produto, com o atalho "Escolher item do produto"). O diálogo (tela cheia a 400 px) mostra a OS
 * concluída, o projeto Fabricado, as parcelas e, com o Estoque liberado, o que entra no estoque (quantidade, local sugerido, séries editáveis,
 * reservas liberadas), o custo apurado com projects.view_cost e o aviso âmbar do custo zerado, que pede a confirmação explícita (CA 7).
 */
export function CompleteOrderDialog({ order: o, onDone, open: controlled, onOpenChange }: {
  order: FabricationOrderDetail; onDone: (preview: OrderCompletionPreview | undefined) => Promise<void>; open?: boolean; onOpenChange?: (open: boolean) => void
}) {
  const canApprove = useCan('production.approve')
  const canEdit = useCan('production.edit')
  const canSeeCost = useCan('projects.view_cost')
  // PRD-06b CA 9: o próximo passo "Concluir OS" abre este diálogo de fora.
  const [own, setOwn] = useState(false)
  const open = controlled ?? own
  const setOpen = (v: boolean) => { setOwn(v); onOpenChange?.(v) }
  const [choosing, setChoosing] = useState(false)
  const [locationId, setLocationId] = useState('')
  const [serials, setSerials] = useState<string[]>([])
  const [zeroConfirmed, setZeroConfirmed] = useState(false)
  const [busy, setBusy] = useState(false)
  const [slow, setSlow] = useState(false)
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null)
  const preview = useGetFabricationOrderCompletion(o.id, { query: { enabled: open } })
  const p = preview.data
  const stock = p?.stock ?? undefined
  // As séries e o local sugeridos chegam com a prévia, uma vez por abertura; o usuário edita a partir deles (padrão 6).
  const [initialized, setInitialized] = useState(false)
  if (open && stock && !initialized) {
    setInitialized(true)
    setLocationId(stock.suggestedLocationId ?? stock.locations[0]?.id ?? '')
    setSerials(stock.suggestedSerials)
  }
  useEffect(() => () => { if (timer.current) clearTimeout(timer.current) }, [])
  if (!canShowComplete(o, canApprove)) return null
  if (o.completionBlocker) {
    const noProduct = o.completionBlocker === FINISHED_GOODS.produtoObrigatorio
    return (
      <span className="inline-flex flex-wrap items-center gap-2">
        <button type="button" disabled aria-describedby={`concluir-${o.id}`} className={`${primary} opacity-50`}>Concluir OS</button>
        <span id={`concluir-${o.id}`} className="text-xs text-muted-foreground">{o.completionBlocker}</span>
        {noProduct && canEdit ? (
          <>
            <button type="button" onClick={() => setChoosing(true)} className="text-xs font-semibold text-primary underline">Escolher item do produto</button>
            <ChooseProductDialog order={o} open={choosing} onOpenChange={setChoosing} onDone={() => onDone(undefined)} />
          </>
        ) : null}
      </span>
    )
  }
  const enabled = stock?.enabled === true
  const location = stock?.locations.find((l) => l.id === locationId)
  const lines = p ? [...completionImpact(p, (v) => formatMoney(v), formatDate), ...(stock ? stockCompletionImpact(stock, location?.name ?? null, serials.length) : [])] : []
  const serialIssue = enabled && stock?.tracksSerial ? serialsProblem(serials, stock.suggestedSerials.length) : null
  const edited = enabled && stock?.tracksSerial && serials.some((s, i) => s.trim() !== stock.suggestedSerials[i])
  const confirm = async () => {
    setBusy(true)
    timer.current = setTimeout(() => setSlow(true), SLOW_AFTER_MS)
    try {
      await completeFabricationOrder(o.id, {
        version: p?.version ?? o.version, locationId: enabled ? locationId || null : null, serialNumbers: edited ? serials.map((s) => s.trim()) : null, confirmZeroCost: zeroConfirmed,
      })
      await onDone(p)
    } finally {
      if (timer.current) clearTimeout(timer.current)
      setBusy(false)
      setSlow(false)
    }
  }
  return (
    <ConfirmDialog size="lg" verb="Concluir OS" done={enabled ? FINISHED_GOODS.concluida : 'OS concluída'} title={`Concluir a ${o.number}?`} open={open}
      onOpenChange={(v) => { setOpen(v); setInitialized(false); if (v) setZeroConfirmed(false) }}
      description="A OS conclui com a quantidade inteira. A conclusão pode ser estornada enquanto a parcela não for recebida e o produto não for expedido."
      preconditions={[
        { ok: !!p?.canComplete, text: p?.completeBlocker ?? 'Lendo o impacto da conclusão.' },
        { ok: !serialIssue, text: serialIssue ?? '' },
        { ok: !stock?.zeroCost || zeroConfirmed, text: 'Confirme a entrada com custo 0.' },
      ]}
      impact={(
        <div className="space-y-2">
          <Impact lines={lines} loading={preview.isPending} failed={preview.isError} />
          {enabled && canSeeCost ? <CompletionCost orderId={o.id} open={open} /> : null}
          {p?.stockNotice ? <p className="flex items-center text-sm text-muted-foreground">{p.stockNotice}<Dica nome="Estoque" texto={FABRICATION_DICAS.estoque} /></p> : null}
        </div>
      )}
      onConfirm={confirm}
      trigger={<button type="button" className={primary}>Concluir OS</button>}>
      {enabled && stock ? <StockFields stock={stock} locationId={locationId} onLocation={setLocationId} serials={serials} onSerials={setSerials} /> : null}
      {stock?.zeroCost ? (
        <div role="note" className="space-y-2 rounded-md border border-l-4 border-warn-soft border-l-warn bg-warn-soft px-3 py-2 text-sm text-warn-ink">
          <p>{FINISHED_GOODS.custoZerado}</p>
          <label className="flex items-center gap-2 font-semibold">
            <input type="checkbox" checked={zeroConfirmed} onChange={(e) => setZeroConfirmed(e.target.checked)} />
            Confirmo a entrada com custo 0
          </label>
        </div>
      ) : null}
      {busy ? <p role="status" className="text-sm text-muted-foreground">Concluindo a OS…{slow ? ` ${FINISHED_GOODS.demora}` : ''}</p> : null}
    </ConfirmDialog>
  )
}

/**
 * "Escolher item do produto" (PRD-06a2 CA 1): a OS sem item de estoque ganha o produto acabado ou componente, com production.edit,
 * antes de concluir.
 */
function ChooseProductDialog({ order: o, open, onOpenChange, onDone }: { order: FabricationOrderDetail; open: boolean; onOpenChange: (v: boolean) => void; onDone: () => Promise<void> }) {
  const [term, setTerm] = useState('')
  const [chosen, setChosen] = useState<string | null>(null)
  const options = useListFabricationProductOptions(o.id, { q: term || undefined }, { query: { enabled: open } })
  return (
    <ConfirmDialog verb="Escolher item" done="Item do produto escolhido" title="Escolher o item de estoque do produto" open={open} onOpenChange={onOpenChange}
      onOpen={() => { setTerm(''); setChosen(null) }}
      description={`O item entra no estoque quando a ${o.number} for concluída.`}
      preconditions={[{ ok: !!chosen, text: 'Escolha um item.' }]}
      onConfirm={async () => { await setFabricationOrderProduct(o.id, { itemId: chosen!, version: o.version }); await onDone() }}>
      <label className="block space-y-1 text-sm font-medium">Buscar produto<input value={term} onChange={(e) => setTerm(e.target.value)} className={inputClass} placeholder="Código ou nome" /></label>
      {options.isPending ? <Skeleton className="h-16" /> : options.isError ? <p className="text-sm text-crit-ink">Não foi possível buscar os itens.</p> : options.data.length === 0 ? (
        <p className="text-sm text-muted-foreground">Nenhum produto acabado ou componente com esse nome. Cadastre o item no Estoque.</p>
      ) : (
        <ul className="max-h-56 space-y-1 overflow-y-auto">
          {options.data.map((i) => (
            <li key={i.id}>
              <label className="flex items-center gap-2 text-sm">
                <input type="radio" name="produto" checked={chosen === i.id} onChange={() => setChosen(i.id)} />
                <span>{i.code} — {i.name}</span>{i.tracksSerial ? <Tag>Com série</Tag> : null}
              </label>
            </li>
          ))}
        </ul>
      )}
    </ConfirmDialog>
  )
}

/**
 * "Estornar conclusão" (PRD-06a1 CA 10 e 11; PRD-06a2 CA 10): com production.approve, na OS concluída; motivo obrigatório (até 500),
 * diálogo destructive com o impacto (inclusive o estorno da entrada no estoque e as séries canceladas). Parcela já recebida, projeto além
 * da fabricação, produto expedido ou sem saldo: o servidor recusa e o motivo aparece no diálogo.
 */
export function ReverseCompletionDialog({ order: o, onDone }: { order: FabricationOrderDetail; onDone: () => Promise<void> }) {
  const canApprove = useCan('production.approve')
  const [open, setOpen] = useState(false)
  const [reason, setReason] = useState('')
  const preview = useGetFabricationOrderCompletion(o.id, { query: { enabled: open } })
  if (!canApprove || o.status !== 'Completed') return null
  const p = preview.data
  const lines = p ? [...reversalImpact(p), ...(p.stock ? stockReversalImpact(p.stock) : [])] : []
  return (
    <ConfirmDialog size="lg" destructive verb="Estornar conclusão" done="Conclusão estornada" title={`Estornar a conclusão da ${o.number}?`} open={open} onOpenChange={setOpen}
      description="A OS volta para a última etapa. O título a receber não é estornado: a decisão é do Financeiro."
      onOpen={() => setReason('')}
      preconditions={[
        { ok: !p || p.canReverse, text: p?.reverseBlocker ?? '' },
        { ok: reason.trim().length > 0, text: 'Informe o motivo.' },
      ]}
      impact={<Impact lines={lines} loading={preview.isPending} failed={preview.isError} />}
      onConfirm={async () => { await reverseFabricationOrderCompletion(o.id, { reason: reason.trim(), version: o.version }); await onDone() }}
      trigger={<button type="button" className={secondary}>Estornar conclusão</button>}>
      <label className="mt-3 block space-y-1 text-sm font-medium">Motivo<textarea rows={3} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} className={inputClass} /></label>
    </ConfirmDialog>
  )
}
