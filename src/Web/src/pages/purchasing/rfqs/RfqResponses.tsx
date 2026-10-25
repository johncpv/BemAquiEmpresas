import { useState } from 'react'
import { addRfqSupplier, declineRfqSupplier, getRfq, listPurchaseSupplierOptions, removeRfqSupplier, saveRfqResponse, useListPurchasePaymentTermOptions } from '@/api/generated/bemaqui'
import type { RfqDetail, RfqSupplierView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Combobox, type ComboOption } from '@/components/form/combobox'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput, DecimalInput, MoneyInput, PercentInput, QuantityInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { Money } from '@/components/ui/money'
import { InlineError } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate, formatDateTime, formatInstantDate, formatMoney } from '@/lib/format'
import { formatQuantity } from '@/pages/inventory/stock/stock'
import { AttachProposal } from './RfqTabs'
import { type QuoteDraft, responseState, responseTotal, RFQ_DICAS } from './rfqs'

const secondary = 'inline-flex min-h-11 items-center rounded-md border border-border px-3 text-sm font-semibold'
const primary = 'inline-flex min-h-11 items-center rounded-md bg-primary px-3 text-sm font-semibold text-primary-foreground disabled:opacity-60'

type SetRfq = (rfq: RfqDetail) => void

/** Rascunho da resposta: a resposta gravada ou, sem ela, a quantidade pedida, prazo vazio e a condição do fornecedor. */
function draftOf(rfq: RfqDetail, s: RfqSupplierView): { freight: string | null; ipiPct: string | null; termId: string; validUntil: string; notes: string; quotes: Record<string, QuoteDraft> } {
  const quotes: Record<string, QuoteDraft> = {}
  for (const l of rfq.lines) {
    const q = s.quotes.find((x) => x.lineId === l.id)
    quotes[l.id] = q
      ? { price: String(q.supplierUnitPrice ?? q.unitPrice), inSupplierUnit: q.supplierUnitPrice != null, available: String(q.availableQuantity), leadDays: String(q.leadDays) }
      : { price: null, inSupplierUnit: false, available: String(l.quantity), leadDays: '' }
  }
  return { freight: s.responded ? String(s.freight) : null, ipiPct: s.responded ? String(s.ipiPct) : null, termId: s.paymentTermId ?? '', validUntil: s.validUntil ?? '', notes: s.notes ?? '', quotes }
}

/** "Registrar resposta" (CA 3, 4, 6): preço por item (na unidade de estoque ou do fornecedor), disponível, prazo; frete, IPI, condição, validade. */
function ResponseForm({ rfq, supplier, set, onClose }: { rfq: RfqDetail; supplier: RfqSupplierView; set: SetRfq; onClose: () => void }) {
  const terms = useListPurchasePaymentTermOptions()
  const [draft, setDraft] = useState(() => draftOf(rfq, supplier))
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [failure, setFailure] = useState<string>()
  const [sending, setSending] = useState(false)
  const [attached, setAttached] = useState(supplier.attachmentsCount)
  const units = supplier.supplierUnits.map((u) => ({ itemId: u.itemId, factor: u.factor }))
  const totals = responseTotal(rfq.lines, draft.quotes, units, Number(draft.freight ?? 0), Number(draft.ipiPct ?? 0))
  const needsAttachment = totals.total > rfq.attachmentLimit
  const limit = formatMoney(rfq.attachmentLimit)
  const setQuote = (lineId: string, patch: Partial<QuoteDraft>) => setDraft((d) => ({ ...d, quotes: { ...d.quotes, [lineId]: { ...d.quotes[lineId], ...patch } } }))
  const withLast = rfq.lines.filter((l) => l.lastPurchase)

  const save = async () => {
    const sent = rfq.lines.filter((l) => Number(draft.quotes[l.id]?.price ?? 0) > 0)
    const missing: Record<string, string> = {}
    if (sent.length === 0) missing.quotes = 'Informe o preço de pelo menos um item. Se o fornecedor não cotou, use Não cotou.'
    for (const l of sent) if (draft.quotes[l.id].leadDays.trim() === '') missing[`prazo-${l.id}`] = 'Informe o prazo em dias.'
    if (!draft.termId) missing.paymentTermId = 'Escolha a condição de pagamento.'
    setErrors(missing)
    setFailure(undefined)
    if (Object.keys(missing).length) return
    setSending(true)
    try {
      const saved = await saveRfqResponse(rfq.id, supplier.id, {
        freight: Number(draft.freight ?? 0), ipiPct: Number(draft.ipiPct ?? 0), paymentTermId: draft.termId || null, validUntil: draft.validUntil || null, notes: draft.notes.trim() || null,
        quotes: sent.map((l) => {
          const q = draft.quotes[l.id]
          return { lineId: l.id, unitPrice: Number(q.price), inSupplierUnit: q.inSupplierUnit, availableQuantity: q.available ? Number(q.available) : null, leadDays: Number(q.leadDays) }
        }),
        version: rfq.version,
      })
      set(saved)
      notifySuccess('Resposta salva')
      onClose()
    } catch (e) {
      if (e instanceof ApiError && Object.keys(e.fields).length) {
        const mapped: Record<string, string> = {}
        for (const [key, value] of Object.entries(e.fields)) {
          const m = /^quotes\[(\d+)\]\.(\w+)$/.exec(key)
          mapped[m ? `${m[2]}-${sent[Number(m[1])]?.id}` : key] = value[0]
        }
        setErrors(mapped)
      }
      setFailure(e instanceof ApiError ? e.message : 'Não foi possível salvar a resposta. Tente de novo.')
    } finally {
      setSending(false)
    }
  }

  return (
    <div className="mt-3 space-y-4 border-t border-border pt-3">
      {withLast.length ? (
        <button type="button" className={secondary}
          onClick={() => setDraft((d) => ({ ...d, quotes: Object.fromEntries(Object.entries(d.quotes).map(([id, q]) => {
            const last = rfq.lines.find((l) => l.id === id)?.lastPurchase
            return [id, last && !q.price ? { ...q, price: String(last.unitPrice), inSupplierUnit: false } : q]
          })) }))}>
          Copiar preços da última compra
        </button>
      ) : null}
      <ul aria-label={`Preços de ${supplier.partyName}`} className="space-y-3">
        {rfq.lines.map((l) => {
          const q = draft.quotes[l.id]
          const unit = supplier.supplierUnits.find((u) => u.itemId === l.itemId)
          return (
            <li key={l.id} className="grid gap-2 rounded-md border border-border p-3 sm:grid-cols-[minmax(0,2fr)_12rem_9rem_7rem]">
              <div className="text-sm">
                <p><span className="font-mono">{l.itemCode}</span> {l.itemName}</p>
                <p className="text-xs text-muted-foreground tabular-nums">Pedido: {formatQuantity(l.quantity)} {l.unitCode}</p>
                {l.lastPurchase ? <p className="text-xs text-muted-foreground">Última compra: <Money value={l.lastPurchase.unitPrice} /> em {formatInstantDate(l.lastPurchase.approvedAt)} de {l.lastPurchase.partyName}</p> : null}
              </div>
              <div>
                <DecimalInput scale={6} prefix="R$" aria-label={`Preço unitário de ${l.itemCode}`} aria-invalid={!!errors[`unitPrice-${l.id}`]} value={q.price} onChange={(v) => setQuote(l.id, { price: v })} />
                {unit ? (
                  <label className="mt-1 flex min-h-11 items-center gap-2 text-xs">
                    <input type="checkbox" checked={q.inSupplierUnit} onChange={(e) => setQuote(l.id, { inSupplierUnit: e.target.checked })} className="size-4 accent-primary" />
                    Preço por {unit.unit} (1 {unit.unit} = {formatQuantity(unit.factor)} {l.unitCode})
                    <Dica nome="Unidade do fornecedor" texto={RFQ_DICAS.unidadeFornecedor} />
                  </label>
                ) : <p className="mt-1 text-xs text-muted-foreground">Preço por {l.unitCode}</p>}
                <InlineError id={`preco-${supplier.id}-${l.id}`} message={errors[`unitPrice-${l.id}`] ?? errors[`inSupplierUnit-${l.id}`]} />
              </div>
              <div>
                <QuantityInput aria-label={`Quantidade disponível de ${l.itemCode}`} value={q.available} onChange={(v) => setQuote(l.id, { available: v })} />
                <p className="text-xs text-muted-foreground">Disponível</p>
                <InlineError id={`disp-${supplier.id}-${l.id}`} message={errors[`availableQuantity-${l.id}`]} />
              </div>
              <div>
                <input aria-label={`Prazo em dias de ${l.itemCode}`} inputMode="numeric" value={q.leadDays} onChange={(e) => setQuote(l.id, { leadDays: e.target.value.replace(/\D/g, '').slice(0, 3) })} className={`${inputClass} text-right tabular-nums`} />
                <p className="text-xs text-muted-foreground">Prazo (dias)</p>
                <InlineError id={`prazo-${supplier.id}-${l.id}`} message={errors[`prazo-${l.id}`] ?? errors[`leadDays-${l.id}`]} />
              </div>
            </li>
          )
        })}
      </ul>
      <InlineError id={`quotes-${supplier.id}`} message={errors.quotes} />
      <div className="grid gap-3 sm:grid-cols-2">
        <Field label="Frete" error={errors.freight}>{(p) => <MoneyInput {...p} value={draft.freight} onChange={(v) => setDraft((d) => ({ ...d, freight: v }))} />}</Field>
        <Field label="IPI" error={errors.ipiPct} hint="Percentual sobre o subtotal dos itens.">{(p) => <PercentInput {...p} scale={4} value={draft.ipiPct} onChange={(v) => setDraft((d) => ({ ...d, ipiPct: v }))} />}</Field>
        <Field label="Condição de pagamento" required error={errors.paymentTermId} hint="Vem do cadastro do fornecedor.">
          {(p) => (
            <select {...p} value={draft.termId} onChange={(e) => setDraft((d) => ({ ...d, termId: e.target.value }))} className={inputClass}>
              <option value="">Escolha</option>
              {(terms.data ?? []).map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
            </select>
          )}
        </Field>
        <Field label="Validade da proposta (opcional)" error={errors.validUntil}>{(p) => <DateInput {...p} value={draft.validUntil} onChange={(v) => setDraft((d) => ({ ...d, validUntil: v }))} />}</Field>
      </div>
      <section aria-label={`Custo total de ${supplier.partyName}`} className="grid grid-cols-2 gap-3 rounded-md border border-border bg-card p-3 text-sm tabular-nums sm:grid-cols-4">
        <div><p className="text-xs text-muted-foreground">Subtotal</p><Money value={totals.subtotal} /></div>
        <div><p className="text-xs text-muted-foreground">Frete</p><Money value={Number(draft.freight ?? 0)} /></div>
        <div><p className="text-xs text-muted-foreground">IPI</p><Money value={totals.ipi} /></div>
        <div><p className="flex items-center text-xs text-muted-foreground">Custo total<Dica nome="Custo total" texto={RFQ_DICAS.custoTotal} /></p><Money value={totals.total} className="text-base font-semibold" /></div>
      </section>
      {needsAttachment ? (
        <div role="note" className="space-y-2 rounded-md border border-l-4 border-warn-soft border-l-warn bg-warn-soft px-3 py-2 text-sm text-warn-ink">
          <p className="inline-flex items-center">Acima de {limit}: anexe a proposta do fornecedor antes de salvar.<Dica nome="Anexo da proposta" texto={RFQ_DICAS.anexo(limit)} /></p>
          <p className="tabular-nums">{attached > 0 ? `${attached} ${attached === 1 ? 'proposta anexada' : 'propostas anexadas'}.` : 'Nenhuma proposta anexada ainda.'}</p>
          <AttachProposal supplier={supplier} compact onAttached={() => setAttached((n) => n + 1)} />
        </div>
      ) : null}
      <FormError message={failure} />
      <div className="flex flex-wrap justify-end gap-2">
        <button type="button" onClick={onClose} className={secondary}>Voltar</button>
        <button type="button" disabled={sending} onClick={() => void save()} className={primary}>{sending ? 'Salvando…' : 'Salvar resposta'}</button>
      </div>
    </div>
  )
}

function SupplierCard({ rfq, supplier, set, editing, onEdit }: { rfq: RfqDetail; supplier: RfqSupplierView; set: SetRfq; editing: boolean; onEdit: (open: boolean) => void }) {
  const state = responseState(supplier)
  return (
    <li className="rounded-md border border-border bg-card p-3">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="space-y-1 text-sm">
          <p className="flex flex-wrap items-center gap-2 font-semibold">{supplier.partyName}<StatusChip tone={state.tone}>{state.label}</StatusChip>
            {supplier.isCheapest ? <StatusChip tone="ok">Menor custo</StatusChip> : null}
            {supplier.attachmentRequired ? <StatusChip tone={supplier.attachmentsCount > 0 ? 'ok' : 'warn'}>{supplier.attachmentsCount > 0 ? 'Proposta anexada' : 'Proposta obrigatória'}</StatusChip> : null}
          </p>
          {supplier.respondedAt ? <p className="text-xs text-muted-foreground">{supplier.declined ? 'Marcado' : 'Lançada'} por {supplier.respondedByName ?? 'usuário'} em {formatDateTime(supplier.respondedAt)}</p> : null}
          {supplier.responded ? (
            <p className="flex flex-wrap gap-x-3 text-xs tabular-nums">
              <span className="inline-flex items-center">Custo total <Money value={supplier.total} className="ml-1 font-semibold" /></span>
              <span>Prazo {supplier.maxLeadDays ?? '—'} d</span>
              <span>{supplier.paymentTermName ?? '—'}</span>
              {supplier.validUntil ? <span>Válida até {formatDate(supplier.validUntil)}</span> : null}
            </p>
          ) : null}
        </div>
        {rfq.canEdit && !editing ? (
          <span className="flex flex-wrap gap-2">
            <button type="button" onClick={() => onEdit(true)} className={primary}>{supplier.responded ? 'Alterar resposta' : 'Registrar resposta'}</button>
            {!supplier.declined ? (
              <ConfirmDialog verb="Não cotou" done="Marcado como Não cotou" title={`${supplier.partyName} não cotou?`}
                description={supplier.responded ? 'Os preços lançados saem do comparativo (ficam no histórico).' : 'O fornecedor fica sem resposta nesta cotação.'}
                onConfirm={async () => set(await declineRfqSupplier(rfq.id, supplier.id, { version: rfq.version }))} onReload={async () => set(await getRfq(rfq.id))}
                trigger={<button type="button" className={secondary}>Não cotou</button>} />
            ) : null}
            {!supplier.respondedAt ? (
              <ConfirmDialog verb="Remover fornecedor" done="Fornecedor removido" title={`Remover ${supplier.partyName} da cotação?`} description="Ele ainda não respondeu. Você pode incluir de novo depois."
                onConfirm={async () => set(await removeRfqSupplier(rfq.id, supplier.id, { version: rfq.version }))} onReload={async () => set(await getRfq(rfq.id))}
                trigger={<button type="button" className={secondary}>Remover</button>} />
            ) : null}
          </span>
        ) : null}
      </div>
      {editing ? <ResponseForm rfq={rfq} supplier={supplier} set={set} onClose={() => onEdit(false)} /> : null}
    </li>
  )
}

/** Incluir fornecedor (CA 2): só com o papel Fornecedor e ativo; o mesmo não se repete. */
function AddSupplier({ rfq, set }: { rfq: RfqDetail; set: SetRfq }) {
  const [error, setError] = useState<string>()
  const search = async (term: string): Promise<ComboOption[]> =>
    (await listPurchaseSupplierOptions({ q: term || undefined })).filter((s) => !rfq.suppliers.some((x) => x.partyId === s.id)).map((s) => ({ id: s.id, label: s.name, hint: s.document ?? undefined }))
  return (
    <Field label="Incluir fornecedor" error={error}>
      {(p) => (
        <Combobox {...p} queryKey={['rfq-add-supplier', rfq.id, rfq.suppliers.length]} search={search} value={null} selected={null} placeholder="Nome ou documento"
          onChange={async (o) => {
            if (!o) return
            setError(undefined)
            try {
              set(await addRfqSupplier(rfq.id, { partyId: o.id, version: rfq.version }))
              notifySuccess('Fornecedor incluído')
            } catch (e) {
              setError(e instanceof ApiError ? e.message : 'Não foi possível incluir o fornecedor.')
            }
          }} />
      )}
    </Field>
  )
}

/** Aba Respostas (padrão, CA 2 a 6): fornecedores com a resposta e quem lançou; incluir, remover, registrar e "Não cotou". */
export function RfqResponses({ rfq, set }: { rfq: RfqDetail; set: SetRfq }) {
  const [editing, setEditing] = useState<string | null>(null)
  return (
    <div className="mt-3 space-y-3">
      <ul aria-label="Fornecedores da cotação" className="space-y-3">
        {rfq.suppliers.map((s) => <SupplierCard key={s.id} rfq={rfq} supplier={s} set={set} editing={editing === s.id} onEdit={(open) => setEditing(open ? s.id : null)} />)}
      </ul>
      {rfq.canEdit ? <div className="max-w-md"><AddSupplier rfq={rfq} set={set} /></div> : null}
    </div>
  )
}
