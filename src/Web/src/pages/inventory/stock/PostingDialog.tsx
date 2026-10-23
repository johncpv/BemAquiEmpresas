import { useState } from 'react'
import {
  createManualPosting, createOpeningBalance, searchItems, searchStockProjects, useGetItemStockContext, useListItemConversions, useListLocations,
} from '@/api/generated/bemaqui'
import type { ManualPostingDetail, ManualPostingKind } from '@/api/generated/model'
import { useIdempotencyKey } from '@/api/idempotency'
import { Combobox, type ComboOption } from '@/components/form/combobox'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput, MoneyInput, QuantityInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { Tag } from '@/components/ui/tag'
import { DICAS, formatQuantity, formatUnitCost, KIND, previewImpact, rememberedUnit, rememberUnit, toStockQuantity } from './stock'

const VERBS: Record<ManualPostingKind, { verb: string; done: string; title: string; description: string }> = {
  Receipt: { verb: 'Lançar entrada', done: 'Entrada lançada', title: 'Lançar entrada manual', description: 'Material que chegou sem pedido de compra (doação, devolução de obra, sobra). Entrada com pedido é pelo recebimento.' },
  Issue: { verb: 'Lançar saída', done: 'Saída lançada', title: 'Lançar saída avulsa', description: 'Consumo interno, amostra, perda ou material entregue direto na obra. A saída para OS é pela requisição.' },
  Transfer: { verb: 'Transferir', done: 'Transferência lançada', title: 'Transferir entre locais', description: 'Muda o material de local. O custo médio do item não muda.' },
  OpeningBalance: { verb: 'Lançar saldo inicial', done: 'Saldo inicial lançado', title: 'Lançar saldo inicial', description: 'Quantidade e custo do item no fim da data de corte, enquanto a implantação do estoque está aberta.' },
}

const search = async (term: string): Promise<ComboOption[]> =>
  (await searchItems({ q: term || undefined })).filter((i) => i.type !== 'Service').map((i) => ({ id: i.id, label: `${i.code} — ${i.name}` }))

const searchProjects = async (term: string): Promise<ComboOption[]> =>
  (await searchStockProjects({ q: term || undefined })).map((p) => ({ id: p.id, label: `${p.number} — ${p.name}` }))

const todayIso = () => new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo' }).format(new Date())
const num = (v: string | null) => (v ? Number(v) : 0)

interface Props {
  kind: ManualPostingKind
  onClose: () => void
  onPosted: (detail: ManualPostingDetail) => void
  item?: ComboOption | null
}

/**
 * Lançamento manual (CA 1, 6 a 9): item, local (o padrão vem sugerido), quantidade em qualquer unidade com conversão, custo
 * (o médio atual vem sugerido), lote, motivo e projeto. O impacto antes → depois e o aviso de aprovação aparecem antes de
 * confirmar (padrão 4 da Fase 3); tudo vai pelo diálogo grande, em tela cheia a 400 px.
 */
export function PostingDialog({ kind, onClose, onPosted, item: initialItem = null }: Props) {
  const [item, setItem] = useState<ComboOption | null>(initialItem)
  const context = useGetItemStockContext(item?.id ?? '', { query: { enabled: !!item } })
  const conversions = useListItemConversions(item?.id ?? '', { query: { enabled: !!item } })
  const locations = useListLocations({ pageSize: 100 })
  const active = (locations.data?.items ?? []).filter((l) => l.isActive)
  const [location, setLocation] = useState('')
  const [target, setTarget] = useState('')
  const [quantity, setQuantity] = useState<string | null>(null)
  const [unit, setUnit] = useState('')
  const [cost, setCost] = useState<string | null | undefined>(undefined)
  const [lot, setLot] = useState<string | undefined>(undefined)
  const [lotId, setLotId] = useState<string | undefined>(undefined)
  const [reason, setReason] = useState('')
  const [project, setProject] = useState<ComboOption | null>(null)
  const [cutoff, setCutoff] = useState(todayIso())
  const [errors, setErrors] = useState<Record<string, string>>({})
  const idempotency = useIdempotencyKey()
  const { verb, done, title, description } = VERBS[kind]
  const entry = kind === 'Receipt' || kind === 'OpeningBalance'

  const ctx = context.data
  const suggestedLocation = ctx?.defaultLocationId ?? active.find((l) => l.isDefault)?.id ?? ''
  const locationId = location || suggestedLocation
  const stockUnit = ctx?.unitId ?? ''
  const remembered = item ? rememberedUnit(item.id) : null
  const unitOptions = ctx ? [{ id: ctx.unitId, code: ctx.unitCode }, ...(conversions.data ?? []).map((c) => (c.fromUnitId === ctx.unitId ? { id: c.toUnitId, code: c.toUnitCode } : { id: c.fromUnitId, code: c.fromUnitCode }))] : []
  const unitId = unit || (remembered && unitOptions.some((u) => u.id === remembered) ? remembered : stockUnit)
  const converted = ctx && quantity ? toStockQuantity(num(quantity), unitId, stockUnit, conversions.data ?? []) : null
  const suggestedCost = ctx && ctx.averageCost > 0 ? String(ctx.averageCost) : null
  const unitCost = cost === undefined ? suggestedCost : cost
  const lotsHere = (ctx?.lots ?? []).filter((l) => l.locationId === locationId)
  const suggestedLot = entry ? (ctx?.suggestedLotNumber ?? '') : (lotsHere[0]?.lotId ?? '')
  const lotNumber = lot ?? (entry ? suggestedLot : '')
  const chosenLot = lotId ?? (entry ? '' : suggestedLot)
  const impact = ctx && converted ? previewImpact(kind, ctx, locationId, converted, entry ? num(unitCost) : null, target) : null
  const needsReason = kind === 'Receipt' || kind === 'Issue'
  const approval = impact?.approval ?? null
  const hasMovementHere = ctx?.positions.find((p) => p.locationId === locationId)?.hasMovement ?? false

  const preconditions = [
    { ok: !!item, text: 'Item escolhido.' },
    { ok: !!locationId, text: 'Local escolhido.' },
    { ok: (converted ?? 0) > 0, text: quantity && converted === null ? 'Conversão cadastrada para a unidade escolhida.' : 'Quantidade maior que zero.' },
    ...(entry ? [{ ok: num(unitCost) > 0, text: 'Custo unitário maior que zero.' }] : []),
    ...(ctx?.tracksLot ? [{ ok: entry ? !!lotNumber.trim() : !!chosenLot, text: 'Lote informado (o item controla lote).' }] : []),
    ...(needsReason ? [{ ok: reason.trim().length > 0, text: 'Motivo escrito.' }] : []),
    ...(kind === 'Transfer' ? [{ ok: !!target && target !== locationId, text: 'Local de destino, diferente do de origem.' }] : []),
    ...(kind === 'OpeningBalance' ? [{ ok: !hasMovementHere, text: 'Item sem movimento neste local (senão, use uma entrada ou um ajuste).' }, { ok: !!cutoff && cutoff <= todayIso(), text: 'Data de corte até hoje.' }] : []),
    ...(impact?.insufficient ? [{ ok: false, text: impact.reserved > 0
      ? `Disponível suficiente no local (há ${formatQuantity(impact.available)} ${ctx?.unitCode ?? ''} disponível; ${formatQuantity(impact.reserved)} está reservado para OS).`
      : `Saldo suficiente no local (há ${formatQuantity(impact.onHandBefore)} ${ctx?.unitCode ?? ''}).` }] : []),
  ]

  const submit = async () => {
    setErrors({})
    const body = { itemId: item!.id, locationId, quantity: num(quantity), unitId: unitId === stockUnit ? null : unitId }
    const result = kind === 'OpeningBalance'
      ? await createOpeningBalance({ ...body, cutoffDate: cutoff, unitCost: num(unitCost), lotNumber: ctx?.tracksLot ? lotNumber.trim() : null }, idempotency.options)
      : await createManualPosting({
        ...body, kind, toLocationId: kind === 'Transfer' ? target : null, unitCost: kind === 'Receipt' ? num(unitCost) : null,
        lotId: !entry && ctx?.tracksLot ? chosenLot : null, lotNumber: entry && ctx?.tracksLot ? lotNumber.trim() : null,
        reason: reason.trim() || null, projectId: kind !== 'Transfer' ? project?.id ?? null : null,
      }, idempotency.options)
    idempotency.renew()
    rememberUnit(item!.id, unitId)
    onPosted(result)
  }

  return (
    <ConfirmDialog open onOpenChange={(o) => { if (!o) onClose() }} size="lg" verb={approval ? 'Enviar para aprovação' : verb} done={approval ? 'Lançamento enviado para aprovação' : done}
      title={title} description={description} preconditions={preconditions} onConfirm={submit}
      onFieldErrors={(e) => { setErrors(Object.fromEntries(Object.entries(e.fields).map(([k, v]) => [k, v[0]]))); return true }}
      impact={impact ? (
        <div aria-live="polite" className="space-y-1">
          <p>Saldo no local {formatQuantity(impact.onHandBefore)} → <b>{formatQuantity(impact.onHandAfter)}</b> {ctx?.unitCode}</p>
          {impact.reserved > 0 ? <p className="tabular-nums">Disponível no local {formatQuantity(impact.available)} {ctx?.unitCode} · {formatQuantity(impact.reserved)} reservado para OS</p> : null}
          {impact.target ? <p>Saldo no destino {formatQuantity(impact.target.before)} → <b>{formatQuantity(impact.target.after)}</b> {ctx?.unitCode}</p> : null}
          <p>Custo médio {formatUnitCost(impact.averageBefore)} → <b>{formatUnitCost(impact.averageAfter)}</b></p>
          <p>Valor do lançamento: <Money value={impact.value} className="font-semibold" /></p>
          {approval ? <p role="status" className="rounded border border-warn-soft bg-warn-soft px-2 py-1 text-xs text-warn-ink">Este lançamento precisa da aprovação de outra pessoa. Motivo: {approval}. O saldo só muda quando for aprovado.</p> : null}
        </div>
      ) : null}>
      <div className="grid gap-3 sm:grid-cols-2">
        <div className="sm:col-span-2">
          <Field label="Item" required error={errors.itemId}>
            {(p) => <Combobox {...p} queryKey={['stock-items']} search={search} value={item?.id ?? null} selected={item} placeholder="Busque pelo código ou nome"
              onChange={(o) => { setItem(o); setUnit(''); setCost(undefined); setLot(undefined); setLotId(undefined); setQuantity(null) }} />}
          </Field>
        </div>
        {kind === 'OpeningBalance' ? (
          <Field label="Data de corte" required dica={DICAS.saldoInicial} error={errors.cutoffDate} hint="O saldo vale no fim desse dia (23:59:59).">
            {(p) => <DateInput {...p} value={cutoff} onChange={setCutoff} />}
          </Field>
        ) : null}
        <Field label={kind === 'Transfer' ? 'De' : 'Local'} required error={errors.locationId}>
          {(p) => (
            <span className="flex items-center gap-2">
              <select {...p} value={locationId} onChange={(e) => setLocation(e.target.value)} className={inputClass}>
                <option value="">Escolha</option>
                {active.map((l) => <option key={l.id} value={l.id}>{l.code} — {l.name}</option>)}
              </select>
              {!location && locationId && locationId === suggestedLocation ? <Tag>Sugerido</Tag> : null}
            </span>
          )}
        </Field>
        {kind === 'Transfer' ? (
          <Field label="Para" required error={errors.toLocationId}>
            {(p) => (
              <select {...p} value={target} onChange={(e) => setTarget(e.target.value)} className={inputClass}>
                <option value="">Escolha</option>
                {active.filter((l) => l.id !== locationId).map((l) => <option key={l.id} value={l.id}>{l.code} — {l.name}</option>)}
              </select>
            )}
          </Field>
        ) : null}
        <Field label="Quantidade" required error={errors.quantity}
          hint={ctx && quantity && unitId !== stockUnit ? (converted !== null ? `= ${formatQuantity(converted)} ${ctx.unitCode}` : 'Sem conversão cadastrada para esta unidade.') : undefined} hintLive>
          {(p) => (
            <span className="flex items-center gap-2">
              <QuantityInput {...p} value={quantity} onChange={setQuantity} />
              {unitOptions.length > 1 ? (
                <select aria-label="Unidade da quantidade" value={unitId} onChange={(e) => setUnit(e.target.value)} className={`${inputClass} w-24`}>
                  {unitOptions.map((u) => <option key={u.id} value={u.id}>{u.code}</option>)}
                </select>
              ) : <span className="text-sm text-muted-foreground">{ctx?.unitCode}</span>}
            </span>
          )}
        </Field>
        {entry ? (
          <Field label="Custo unitário" required error={errors.unitCost} dica={DICAS.custoMedio}>
            {(p) => (
              <span className="flex items-center gap-2">
                <MoneyInput {...p} value={unitCost} onChange={setCost} />
                {cost === undefined && suggestedCost ? <Tag title="Custo médio atual do item">Sugerido</Tag> : null}
              </span>
            )}
          </Field>
        ) : null}
        {ctx?.tracksLot ? (
          entry ? (
            <Field label="Lote" required error={errors.lotNumber} hint="Lote novo nasce com esta entrada.">
              {(p) => (
                <span className="flex items-center gap-2">
                  <input {...p} value={lotNumber} maxLength={40} onChange={(e) => setLot(e.target.value)} className={inputClass} />
                  {lot === undefined && suggestedLot ? <Tag title="Último lote do item">Sugerido</Tag> : null}
                </span>
              )}
            </Field>
          ) : (
            <Field label="Lote" required error={errors.lotId ?? errors.lotNumber}>
              {(p) => (
                <select {...p} value={chosenLot} onChange={(e) => setLotId(e.target.value)} className={inputClass}>
                  <option value="">Escolha</option>
                  {lotsHere.map((l) => <option key={l.lotId} value={l.lotId}>{l.lotNumber} — {formatQuantity(l.onHand)} {ctx.unitCode}</option>)}
                </select>
              )}
            </Field>
          )
        ) : null}
        {kind !== 'Transfer' && kind !== 'OpeningBalance' ? (
          <Field label="Projeto (opcional)" error={errors.projectId} hint={kind === 'Issue' ? 'Material entregue direto na obra entra no custo do projeto.' : undefined}>
            {(p) => <Combobox {...p} queryKey={['stock-projects']} search={searchProjects} value={project?.id ?? null} selected={project} placeholder="Projeto em andamento" onChange={setProject} />}
          </Field>
        ) : null}
        {kind !== 'OpeningBalance' ? (
          <div className="sm:col-span-2">
            <Field label={needsReason ? 'Motivo' : 'Motivo (opcional)'} required={needsReason} error={errors.reason}>
              {(p) => <textarea {...p} value={reason} maxLength={200} rows={2} onChange={(e) => setReason(e.target.value)} className={inputClass} />}
            </Field>
          </div>
        ) : null}
      </div>
      {item && context.isError ? <p role="alert" className="text-sm text-crit-ink">Não foi possível carregar o saldo do item. Feche e tente de novo.</p> : null}
      <p className="text-xs text-muted-foreground">{KIND[kind]} · todo lançamento fica no histórico de movimentos e não pode ser apagado, só estornado.</p>
    </ConfirmDialog>
  )
}
