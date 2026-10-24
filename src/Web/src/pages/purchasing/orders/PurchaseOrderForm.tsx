import { useNavigate } from '@tanstack/react-router'
import { useRef, useState } from 'react'
import { listPurchaseItemOptions, listPurchaseProjectOptions, listPurchaseSupplierOptions, useListPurchasePaymentTermOptions } from '@/api/generated/bemaqui'
import type { PurchaseItemOption, PurchaseOrderLineInput } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Combobox, type ComboOption } from '@/components/form/combobox'
import { Field, FormError, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput, DecimalInput, MoneyInput, PercentInput, QuantityInput } from '@/components/form/inputs'
import { RowsEditor } from '@/components/form/rows-editor'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DialogMessage } from '@/components/ui/dialog-message'
import { Money } from '@/components/ui/money'
import { InlineError } from '@/components/ui/states'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { formatQuantity } from '@/pages/inventory/stock/stock'
import { todayIso } from '../purchasing'
import { lineTotal, ORDER_DICAS, orderTotals } from './purchase-orders'

export interface OrderLine {
  item: ComboOption | null
  quantity: string | null
  unitId: string
  unitPrice: string | null
  /** O preço ainda é o sugerido (tag "Sugerido"). */
  priceSuggested: boolean
  priceSource: string | null
  requestLineIds: string[]
  requestNumbers: string[]
}

export interface OrderDraft {
  party: ComboOption | null
  partySuggested: boolean
  paymentTermId: string
  expectedDate: string
  general: boolean
  project: ComboOption | null
  freight: string | null
  ipiPct: string | null
  notes: string
  lines: OrderLine[]
}

export interface OrderPayload {
  partyId: string
  projectId: string | null
  paymentTermId: string
  expectedDate: string
  freight: number
  ipiPct: number
  notes: string | null
  lines: PurchaseOrderLineInput[]
}

interface Props {
  initial: OrderDraft
  options: PurchaseItemOption[]
  /** Pedido fora do rascunho (CA 15): salvar abre o diálogo de impacto e volta para a aprovação. */
  reapproval?: { wasSent: boolean }
  /** Destino travado pelas linhas de SC (CA 1). */
  fromRequests: boolean
  saveLabel: string
  sendingLabel: string
  onSubmit: (payload: OrderPayload) => Promise<void>
  onBack: () => void
}

const num = (value: string | null | undefined) => (value ? Number(value) : 0)
const label = (i: PurchaseItemOption) => `${i.code} — ${i.name}`

/**
 * Formulário do pedido (CA 1, 3, 4, 15): fornecedor (sugerido pelo último do item), condição (padrão do fornecedor), previsão de
 * entrega, destino (o das SC quando vier delas), frete, IPI e as linhas com o preço sugerido; o rodapé soma ao vivo e o servidor
 * recalcula tudo. A 400 px cada linha vira cartão (RowsEditor).
 */
export function PurchaseOrderForm({ initial, options: known, reapproval, fromRequests, saveLabel, sendingLabel, onSubmit, onBack }: Props) {
  const navigate = useNavigate()
  const terms = useListPurchasePaymentTermOptions()
  const options = useRef(new Map<string, PurchaseItemOption>(known.map((o) => [o.id, o])))
  const [draft, setDraft] = useState<OrderDraft>(initial)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [failure, setFailure] = useState<{ message: string; paymentOrderId?: string }>()
  const [sending, setSending] = useState(false)
  const set = (patch: Partial<OrderDraft>) => setDraft((d) => ({ ...d, ...patch }))
  const totals = orderTotals(draft.lines.map((l) => ({ quantity: num(l.quantity), unitPrice: num(l.unitPrice) })), num(draft.freight), num(draft.ipiPct))

  const searchItems = async (term: string): Promise<ComboOption[]> => {
    const found = await listPurchaseItemOptions({ q: term || undefined })
    for (const o of found) options.current.set(o.id, o)
    return found.map((o) => ({ id: o.id, label: label(o), hint: o.unitCode }))
  }
  const searchSuppliers = async (term: string): Promise<ComboOption[]> =>
    (await listPurchaseSupplierOptions({ q: term || undefined })).map((s) => ({ id: s.id, label: s.name, hint: s.document ?? undefined, defaultTerm: s.defaultPaymentTermId } as ComboOption))
  const searchProjects = async (term: string): Promise<ComboOption[]> =>
    (await listPurchaseProjectOptions({ q: term || undefined })).map((p) => ({ id: p.id, label: `${p.number} — ${p.name}` }))

  const validate = () => {
    const missing: Record<string, string> = {}
    if (!draft.party) missing.partyId = 'Escolha o fornecedor.'
    if (!draft.paymentTermId) missing.paymentTermId = 'Escolha a condição de pagamento.'
    if (!draft.expectedDate) missing.expectedDate = 'Informe a previsão de entrega.'
    if (!draft.general && !draft.project) missing.projectId = 'Escolha o projeto ou marque Estoque geral.'
    draft.lines.forEach((l, i) => {
      if (!l.item) missing[`lines[${i}].itemId`] = 'Escolha o item.'
      if (num(l.quantity) <= 0) missing[`lines[${i}].quantity`] = 'Informe a quantidade, maior que zero.'
      if (num(l.unitPrice) <= 0) missing[`lines[${i}].unitPrice`] = 'Informe o preço unitário, maior que zero.'
    })
    setErrors(missing)
    return Object.keys(missing).length === 0
  }

  const payload = (): OrderPayload => ({
    partyId: draft.party!.id, projectId: draft.general ? null : draft.project!.id, paymentTermId: draft.paymentTermId, expectedDate: draft.expectedDate, freight: num(draft.freight),
    ipiPct: num(draft.ipiPct), notes: draft.notes.trim() || null,
    lines: draft.lines.map((l) => {
      const option = options.current.get(l.item!.id)
      return { itemId: l.item!.id, quantity: num(l.quantity), unitId: !l.unitId || l.unitId === option?.unitId ? null : l.unitId, unitPrice: num(l.unitPrice), requestLineIds: l.requestLineIds }
    }),
  })

  const fail = (e: unknown) => {
    if (e instanceof ApiError && Object.keys(e.fields).length) setErrors(Object.fromEntries(Object.entries(e.fields).map(([k, v]) => [k, v[0]])))
    const paymentOrderId = e instanceof ApiError && typeof e.data.paymentOrderId === 'string' ? e.data.paymentOrderId : undefined
    setFailure({ message: e instanceof ApiError ? e.message : 'Não foi possível salvar o pedido. Tente de novo.', paymentOrderId })
  }

  const save = async () => {
    setFailure(undefined)
    if (!validate()) return
    setSending(true)
    try {
      await onSubmit(payload())
    } catch (e) {
      fail(e)
    } finally {
      setSending(false)
    }
  }

  const saveButton = reapproval ? (
    <ConfirmDialog size="lg" verb={saveLabel} done="Pedido enviado para aprovação" title="Salvar e voltar para a aprovação?"
      description="O pedido volta para Aguardando aprovação e a alçada é resolvida de novo. As decisões de antes ficam no histórico."
      impact={
        <ul className="space-y-1">
          <li>Novo total: <Money value={totals.total} className="font-semibold" /></li>
          <li>Os títulos previstos de antes continuam no caixa até a nova aprovação, que os troca pelas parcelas novas.</li>
          {reapproval.wasSent ? <li>O pedido já foi enviado ao fornecedor: reenvie depois da aprovação.</li> : null}
        </ul>
      }
      preconditions={[{ ok: !!draft.party && draft.lines.every((l) => l.item && num(l.quantity) > 0 && num(l.unitPrice) > 0), text: 'Fornecedor e itens com quantidade e preço.' }]}
      onConfirm={async () => {
        if (!validate()) throw new DialogMessage('Corrija os campos marcados no formulário.')
        try {
          await onSubmit(payload())
        } catch (e) {
          fail(e)
          throw e
        }
      }}
      trigger={<button type="button" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">{saveLabel}</button>} />
  ) : (
    <button type="button" disabled={sending} onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">{sending ? sendingLabel : saveLabel}</button>
  )

  return (
    <div className="max-w-5xl space-y-6">
      <FormSection title="Fornecedor e condição" description="O fornecedor precisa ter o papel Fornecedor. A condição vem do cadastro dele.">
        <Field label="Fornecedor" required error={errors.partyId} dica={draft.partySuggested ? ORDER_DICAS.sugerido : undefined}>
          {(p) => (
            <span className="flex items-center gap-2">
              <span className="min-w-0 flex-1">
                <Combobox {...p} queryKey={['purchase-suppliers']} search={searchSuppliers} value={draft.party?.id ?? null} selected={draft.party} placeholder="Nome ou documento"
                  onChange={(o) => {
                    const term = (o as (ComboOption & { defaultTerm?: string | null }) | null)?.defaultTerm
                    set({ party: o, partySuggested: false, ...(term ? { paymentTermId: term } : {}) })
                  }} />
              </span>
              {draft.partySuggested ? <Tag title="O último fornecedor deste item">Sugerido</Tag> : null}
            </span>
          )}
        </Field>
        <Field label="Condição de pagamento" required error={errors.paymentTermId}>
          {(p) => (
            <select {...p} value={draft.paymentTermId} onChange={(e) => set({ paymentTermId: e.target.value })} className={inputClass}>
              <option value="">Escolha</option>
              {(terms.data ?? []).map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
            </select>
          )}
        </Field>
        <Field label="Previsão de entrega" required error={errors.expectedDate} hint="As parcelas previstas no caixa contam a partir desta data.">
          {(p) => <DateInput {...p} value={draft.expectedDate} onChange={(v) => set({ expectedDate: v })} />}
        </Field>
      </FormSection>

      <FormSection title="Itens" description="Quantidade na unidade de estoque ou noutra com conversão. Preço unitário com até 6 casas.">
        <div className="sm:col-span-2">
          <RowsEditor<OrderLine> noun="item" rows={draft.lines} onChange={(lines) => set({ lines })} min={1} max={100}
            newRow={() => ({ item: null, quantity: null, unitId: '', unitPrice: null, priceSuggested: false, priceSource: null, requestLineIds: [], requestNumbers: [] })}
            rowError={(i) => errors[`lines[${i}]`]}
            columns={[
              {
                label: 'Item', width: 'minmax(0, 2fr)', render: (l, i, update) => {
                  const error = errors[`lines[${i}].itemId`] ?? errors[`lines[${i}].requestLineIds`]
                  return (
                    <div>
                      <Combobox aria-label={`Item da linha ${i + 1}`} aria-required aria-invalid={!!error} aria-describedby={error ? `pc-linha-${i}-item` : undefined}
                        queryKey={['purchase-items']} search={searchItems} value={l.item?.id ?? null} selected={l.item} placeholder="Código ou nome" disabled={l.requestLineIds.length > 0}
                        onChange={(o) => update({ item: o, unitId: o ? options.current.get(o.id)?.unitId ?? '' : '' })} />
                      {l.requestNumbers.length ? <p className="text-xs text-muted-foreground">Atende {l.requestNumbers.join(', ')}</p> : null}
                      <InlineError id={`pc-linha-${i}-item`} message={error} />
                    </div>
                  )
                },
              },
              {
                label: 'Quantidade', width: '11rem', render: (l, i, update) => {
                  const option = l.item ? options.current.get(l.item.id) : undefined
                  const unit = option?.units.find((u) => u.unitId === l.unitId)
                  const converted = unit && !unit.isStockUnit && l.quantity ? Number(l.quantity) * unit.toStock : null
                  const error = errors[`lines[${i}].quantity`] ?? errors[`lines[${i}].unitId`]
                  return (
                    <div>
                      <span className="flex items-center gap-2">
                        <QuantityInput aria-label={`Quantidade da linha ${i + 1}`} aria-invalid={!!error} value={l.quantity} onChange={(v) => update({ quantity: v })} />
                        {option && option.units.length > 1 ? (
                          <select aria-label={`Unidade da linha ${i + 1}`} value={l.unitId} onChange={(e) => update({ unitId: e.target.value })} className={`${inputClass} w-20`}>
                            {option.units.map((u) => <option key={u.unitId} value={u.unitId}>{u.code}</option>)}
                          </select>
                        ) : <span className="text-sm text-muted-foreground">{option?.unitCode}</span>}
                      </span>
                      {converted !== null ? <p aria-live="polite" className="text-xs text-muted-foreground">= {formatQuantity(converted)} {option?.unitCode}</p> : null}
                      <InlineError id={`pc-linha-${i}-qtd`} message={error} />
                    </div>
                  )
                },
              },
              {
                label: 'Preço unitário', width: '12rem', render: (l, i, update) => {
                  const error = errors[`lines[${i}].unitPrice`]
                  return (
                    <div>
                      <span className="flex items-center gap-2">
                        <DecimalInput scale={6} prefix="R$" aria-label={`Preço unitário da linha ${i + 1}`} aria-invalid={!!error} value={l.unitPrice} onChange={(v) => update({ unitPrice: v, priceSuggested: false })} />
                        {l.priceSuggested ? <Tag title={l.priceSource ?? undefined}>Sugerido</Tag> : null}
                      </span>
                      <InlineError id={`pc-linha-${i}-preco`} message={error} />
                    </div>
                  )
                },
              },
              { label: 'Total', width: '8rem', render: (l) => <Money value={lineTotal(num(l.quantity), num(l.unitPrice))} className="block pt-2 text-right" /> },
            ]} />
        </div>
      </FormSection>

      <FormSection title="Destino, frete e IPI" description="Para um projeto em andamento ou para o estoque geral. Frete e IPI do fornecedor vão nas mesmas parcelas.">
        {fromRequests ? <p className="text-sm sm:col-span-2">O destino vem das solicitações: o projeto quando todas são do mesmo projeto; senão, o estoque geral.</p> : null}
        <label className="flex items-center gap-2 text-sm sm:col-span-2">
          <input type="checkbox" checked={draft.general} disabled={fromRequests} onChange={(e) => set({ general: e.target.checked })} className="size-4 accent-primary" />
          Estoque geral (sem projeto)
        </label>
        {!draft.general ? (
          <Field label="Projeto" required error={errors.projectId}>
            {(p) => <Combobox {...p} queryKey={['purchase-projects']} search={searchProjects} value={draft.project?.id ?? null} selected={draft.project} placeholder="Projeto em andamento" onChange={(o) => set({ project: o })} disabled={fromRequests} />}
          </Field>
        ) : null}
        <Field label="Frete" error={errors.freight}>{(p) => <MoneyInput {...p} value={draft.freight} onChange={(v) => set({ freight: v })} />}</Field>
        <Field label="IPI" error={errors.ipiPct} hint="Percentual sobre o subtotal dos itens.">{(p) => <PercentInput {...p} scale={4} value={draft.ipiPct} onChange={(v) => set({ ipiPct: v })} />}</Field>
        <Field label="Observação (opcional)" error={errors.notes}>
          {(p) => <textarea {...p} value={draft.notes} maxLength={500} rows={2} onChange={(e) => set({ notes: e.target.value })} className={inputClass} />}
        </Field>
      </FormSection>

      <section aria-label="Totais do pedido" className="sticky bottom-20 z-10 grid grid-cols-2 gap-3 rounded-md border border-border bg-card p-4 text-sm tabular-nums sm:grid-cols-4">
        <div><p className="text-xs text-muted-foreground">Subtotal</p><Money value={totals.subtotal} /></div>
        <div><p className="text-xs text-muted-foreground">Frete</p><Money value={num(draft.freight)} /></div>
        <div><p className="text-xs text-muted-foreground">IPI</p><Money value={totals.ipi} /></div>
        <div><p className="flex items-center text-xs text-muted-foreground">Total<Dica nome="Total do pedido" texto="Soma das linhas (quantidade × preço, em centavos) mais frete e IPI. O sistema recalcula ao salvar." /></p><Money value={totals.total} className="text-base font-semibold" /></div>
      </section>

      {failure ? (
        <div className="space-y-1">
          <FormError message={failure.message} />
          {failure.paymentOrderId ? <button type="button" onClick={() => void navigate({ to: '/app/financeiro/ordens-de-pagamento/$id', params: { id: failure.paymentOrderId! } } as never)} className="text-sm font-semibold text-primary underline">Ver OP</button> : null}
        </div>
      ) : null}
      <div className="flex flex-wrap justify-end gap-2">
        <button type="button" onClick={onBack} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
        {saveButton}
      </div>
      <p className="sr-only" aria-live="polite">{draft.expectedDate && draft.expectedDate < todayIso() ? 'A previsão de entrega já passou.' : ''}</p>
    </div>
  )
}
