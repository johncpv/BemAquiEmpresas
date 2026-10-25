import { Link } from '@tanstack/react-router'
import { useState } from 'react'
import { createStockAdjustment, getItemStockContext, listItemConversions, searchItems, useListInventoryCounts, useListLocations } from '@/api/generated/bemaqui'
import type { StockAdjustmentDetail, StockAdjustmentMode, StockAdjustmentReason } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { Combobox, type ComboOption } from '@/components/form/combobox'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { QuantityInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifyError } from '@/components/ui/messages'
import { Money } from '@/components/ui/money'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import {
  ADJUSTMENT_DICAS, ADJUSTMENT_MODE, ADJUSTMENT_REASON, adjustmentProblems, type AdjustmentRow, clearQuantities, hasQuantities, previewRows, relocate, rowFromContext,
  toLineInputs, totalsOf,
} from './adjustments'
import { formatQuantity } from './stock'

const search = async (term: string): Promise<ComboOption[]> =>
  (await searchItems({ q: term || undefined })).filter((i) => i.type !== 'Service').map((i) => ({ id: i.id, label: `${i.code} — ${i.name}` }))

const REASONS = Object.keys(ADJUSTMENT_REASON) as StockAdjustmentReason[]

interface Props {
  onClose: () => void
  onDone: (detail: StockAdjustmentDetail) => void
  /** Local sugerido (o padrão, se não vier). */
  locationId?: string
}

/**
 * "Lançar ajuste" (CA 2 e 4): local, modo do ajuste inteiro ("Quantidade contada", o padrão, ou "Diferença"), motivo e as linhas em
 * cartões, cada uma com o saldo do sistema preenchido (físico, reservado e disponível), a quantidade, a diferença e o valor estimado;
 * o total de sobras e faltas ao vivo no rodapé. Trocar o modo com quantidades preenchidas pede confirmação e limpa as quantidades.
 * A linha recusada pelo reservado leva a "Ver reservas do item". Em tela cheia a 400 px.
 */
export function AdjustmentDialog({ onClose, onDone, locationId: initialLocation }: Props) {
  const locations = useListLocations({ pageSize: 100 })
  const active = (locations.data?.items ?? []).filter((l) => l.isActive)
  const [location, setLocation] = useState(initialLocation ?? '')
  const [mode, setMode] = useState<StockAdjustmentMode>('Counted')
  const [pendingMode, setPendingMode] = useState<StockAdjustmentMode | null>(null)
  const [reason, setReason] = useState<StockAdjustmentReason | ''>('')
  const [detail, setDetail] = useState('')
  const [rows, setRows] = useState<AdjustmentRow[]>([])
  const [adding, setAdding] = useState<ComboOption | null>(null)
  const [errors, setErrors] = useState<Record<number, { message: string; link?: string }>>({})
  const idempotency = useIdempotencyKey()
  const suggested = active.find((l) => l.isDefault)?.id ?? ''
  const locationId = location || suggested
  // EST-05 CA 6: com inventário em contagem no local, o ajuste é recusado; a faixa avisa antes e leva ao inventário.
  const counting = useListInventoryCounts({ status: 'Counting', locationId: locationId || undefined }, { query: { enabled: !!locationId } })
  const blockingCount = locationId ? counting.data?.items?.[0] : undefined
  const previews = previewRows(mode, rows)
  const totals = totalsOf(previews)
  const update = (key: string, change: Partial<AdjustmentRow>) => setRows((all) => all.map((r) => (r.key === key ? { ...r, ...change } : r)))

  const chooseLocation = (id: string) => {
    setLocation(id)
    setRows((all) => relocate(all, id))
  }

  const chooseMode = (next: StockAdjustmentMode) => {
    if (next === mode) return
    if (hasQuantities(rows)) {
      setPendingMode(next)
      return
    }

    setMode(next)
  }

  const add = async (option: ComboOption | null) => {
    setAdding(option)
    if (!option) return
    try {
      const [context, conversions] = await Promise.all([getItemStockContext(option.id), listItemConversions(option.id)])
      setRows((all) => [...all, rowFromContext(context, conversions, locationId)])
      setAdding(null)
    } catch (e) {
      notifyError(e instanceof ApiError ? e.message : 'Não foi possível carregar o saldo do item.')
    }
  }

  const submit = async () => {
    setErrors({})
    const result = await createStockAdjustment({ locationId, mode, reason: reason as StockAdjustmentReason, reasonDetail: detail.trim() || null, lines: toLineInputs(rows) }, idempotency.options)
    idempotency.renew()
    onDone(result)
  }

  const quantityLabel = mode === 'Counted' ? 'Quantidade contada' : 'Diferença (+ sobra, − falta)'
  return (
    <ConfirmDialog open onOpenChange={(o) => { if (!o) onClose() }} size="lg" verb="Enviar para aprovação" done="Ajuste enviado para aprovação" title="Lançar ajuste de estoque"
      description="Sobra ou falta sem documento de origem. O saldo só muda quando outra pessoa com permissão de aprovar no Estoque aprovar."
      preconditions={[
        ...(blockingCount ? [{ ok: false, text: `Inventário ${blockingCount.number} em contagem neste local: ajustes ficam bloqueados até fechar ou cancelar.` }] : []),
        ...adjustmentProblems(mode, locationId, reason, detail, rows).map((text) => ({ ok: false, text })),
      ]} onConfirm={submit}
      onFieldErrors={(e) => {
        const byLine: Record<number, { message: string; link?: string }> = {}
        for (const [field, messages] of Object.entries(e.fields)) {
          const match = /^lines\[(\d+)\]/.exec(field)
          if (match) byLine[Number(match[1])] = { message: messages[0], link: typeof e.data.link === 'string' ? e.data.link : undefined }
        }
        setErrors(byLine)
        return Object.keys(byLine).length > 0
      }}
      impact={rows.length > 0 ? (
        <div aria-live="polite" className="space-y-1">
          <p>Sobras: <Money value={totals.surplus} className="font-semibold" /> · Faltas: <Money value={totals.shortage} className="font-semibold" /></p>
          <p className="text-xs text-muted-foreground">Valor estimado pelo custo médio de agora. Nada muda no saldo até a aprovação.</p>
        </div>
      ) : null}>
      {blockingCount ? (
        <p role="note" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">
          Inventário {blockingCount.number} em contagem neste local.{' '}
          <Link to={'/app/estoque/inventarios/$id' as never} params={{ id: blockingCount.id } as never} onClick={onClose} className="font-semibold underline">Ver inventário</Link>
        </p>
      ) : null}
      <div className="grid gap-3 sm:grid-cols-2">
        <Field label="Local" required>
          {(p) => (
            <span className="flex items-center gap-2">
              <select {...p} value={locationId} onChange={(e) => chooseLocation(e.target.value)} className={inputClass}>
                <option value="">Escolha</option>
                {active.map((l) => <option key={l.id} value={l.id}>{l.code} — {l.name}</option>)}
              </select>
              {!location && locationId ? <Tag>Sugerido</Tag> : null}
            </span>
          )}
        </Field>
        <fieldset className="space-y-1">
          <legend className="inline-flex items-center text-sm font-medium">Modo do ajuste<Dica nome="Quantidade contada × diferença" texto={ADJUSTMENT_DICAS.modo} /></legend>
          <div className="flex flex-wrap gap-3 text-sm">
            {(['Counted', 'Delta'] as const).map((m) => (
              <label key={m} className="inline-flex min-h-[44px] items-center gap-2">
                <input type="radio" name="modo-ajuste" checked={mode === m} onChange={() => chooseMode(m)} />
                {ADJUSTMENT_MODE[m]}
              </label>
            ))}
          </div>
        </fieldset>
        {pendingMode ? (
          <div role="alert" className="space-y-2 rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink sm:col-span-2">
            <p>Trocar para "{ADJUSTMENT_MODE[pendingMode]}" limpa as quantidades digitadas. O modo vale para o ajuste inteiro.</p>
            <div className="flex gap-2">
              <button type="button" className="rounded-md border border-border bg-card px-3 py-1.5 font-semibold text-foreground" onClick={() => { setMode(pendingMode); setRows(clearQuantities); setPendingMode(null) }}>Trocar o modo</button>
              <button type="button" className="rounded-md border border-border bg-card px-3 py-1.5 font-semibold text-foreground" onClick={() => setPendingMode(null)}>Manter</button>
            </div>
          </div>
        ) : null}
        <Field label="Motivo" required>
          {(p) => (
            <select {...p} value={reason} onChange={(e) => setReason(e.target.value as StockAdjustmentReason | '')} className={inputClass}>
              <option value="">Escolha</option>
              {REASONS.map((r) => <option key={r} value={r}>{ADJUSTMENT_REASON[r]}</option>)}
            </select>
          )}
        </Field>
        <Field label={reason === 'Other' ? 'Descrição do motivo' : 'Descrição (opcional)'} required={reason === 'Other'}>
          {(p) => <input {...p} value={detail} maxLength={200} onChange={(e) => setDetail(e.target.value)} className={inputClass} />}
        </Field>
      </div>
      <ul className="space-y-3" aria-label="Itens do ajuste">
        {rows.map((r, i) => {
          const preview = previews[i]
          const error = errors[i]
          return (
            <li key={r.key} className={preview.refusal ? 'space-y-2 rounded-md border border-l-4 border-border border-l-crit bg-card p-3' : 'space-y-2 rounded-md border border-border bg-card p-3'}>
              <p className="text-sm"><b><span className="font-mono">{r.itemCode}</span> {r.itemName}</b></p>
              <p className="text-sm tabular-nums">Disponível {formatQuantity(r.available)} {r.unitCode} · sistema {formatQuantity(preview.system)}{r.tracksLot ? ' no lote' : ''}</p>
              <details className="text-xs text-muted-foreground">
                <summary className="cursor-pointer">Físico e reservado</summary>
                <p className="tabular-nums">Físico {formatQuantity(r.onHand)} {r.unitCode} · Reservado para OS {formatQuantity(r.reserved)} · Depois do ajuste {formatQuantity(preview.positionAfter)}</p>
              </details>
              <div className="grid gap-2 sm:grid-cols-2">
                {r.tracksLot ? (
                  <label className="block text-sm">
                    <span className="font-medium">Lote</span>
                    <select value={r.lotId ?? ''} onChange={(e) => update(r.key, { lotId: e.target.value || null })} className={inputClass} aria-label={`Lote de ${r.itemCode}`}>
                      <option value="">Escolha</option>
                      {r.lots.map((l) => <option key={l.lotId} value={l.lotId}>{l.lotNumber} — {formatQuantity(l.onHand)} {r.unitCode}</option>)}
                    </select>
                  </label>
                ) : null}
                <label className="block text-sm">
                  <span className="font-medium">{quantityLabel}</span>
                  <span className="flex items-center gap-2">
                    <QuantityInput value={r.quantity} onChange={(v) => update(r.key, { quantity: v })} aria-label={`${quantityLabel} de ${r.itemCode}`} />
                    {r.unitOptions.length > 1 ? (
                      <select aria-label={`Unidade de ${r.itemCode}`} value={r.unitId} onChange={(e) => update(r.key, { unitId: e.target.value })} className={`${inputClass} w-24`}>
                        {r.unitOptions.map((u) => <option key={u.id} value={u.id}>{u.code}</option>)}
                      </select>
                    ) : <span className="text-sm text-muted-foreground">{r.unitCode}</span>}
                  </span>
                </label>
              </div>
              <p className="text-sm tabular-nums" aria-live="polite">
                Diferença {preview.delta === null ? '—' : <b>{formatQuantity(preview.delta, true)} {r.unitCode}</b>} · <Money value={preview.value} />
              </p>
              {preview.refusal || error ? (
                <p role="alert" className="text-sm text-crit-ink">
                  {error?.message ?? preview.refusal}{' '}
                  {(error?.link || preview.refusal?.includes('reservado')) ? (
                    <Link to={'/app/estoque/itens/$id' as never} params={{ id: r.itemId } as never} search={{ aba: 'reservas' } as never} onClick={onClose} className="font-semibold underline">Ver reservas do item</Link>
                  ) : null}
                </p>
              ) : null}
              <button type="button" className="text-sm font-semibold text-primary" onClick={() => setRows((all) => all.filter((x) => x.key !== r.key))}>Tirar do ajuste</button>
            </li>
          )
        })}
      </ul>
      <div className="space-y-1">
        <p className="text-sm font-medium">Adicionar item</p>
        <Combobox queryKey={['adjustment-items']} search={search} value={adding?.id ?? null} selected={adding} placeholder={locationId ? 'Busque o item pelo código ou nome' : 'Escolha o local primeiro'}
          onChange={(o) => void add(o)} aria-label="Adicionar item ao ajuste" disabled={!locationId} />
      </div>
    </ConfirmDialog>
  )
}
