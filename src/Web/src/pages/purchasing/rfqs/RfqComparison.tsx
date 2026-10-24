import { type ReactNode, useState } from 'react'
import { closeRfq, getRfq } from '@/api/generated/bemaqui'
import type { RfqDetail, RfqLineView, RfqSupplierView } from '@/api/generated/model'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { EmptyState, InlineError } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { formatInstantDate } from '@/lib/format'
import { formatQuantity } from '@/pages/inventory/stock/stock'
import { chooseAll, initialSelection, missingForClose, needsReason, REASON_OPTIONS, REASONS, type ReasonDraft, RFQ_DICAS } from './rfqs'

const secondary = 'inline-flex min-h-11 items-center rounded-md border border-border px-3 text-sm font-semibold'

/** Cartão por fornecedor (a 400 px é o resumo principal): custo total, prazo, condição e "Escolher tudo de {fornecedor}". */
function SupplierSummary({ supplier, canChoose, onChooseAll }: { supplier: RfqSupplierView; canChoose: boolean; onChooseAll: () => void }) {
  return (
    <li className="space-y-1 rounded-md border border-border bg-card p-3 text-sm">
      <p className="flex flex-wrap items-center gap-2 font-semibold">{supplier.partyName}{supplier.isCheapest ? <StatusChip tone="ok">Menor custo</StatusChip> : null}</p>
      <p className="flex items-center text-xs text-muted-foreground">Custo total<Dica nome="Custo total" texto={RFQ_DICAS.custoTotal} /></p>
      <Money value={supplier.total} className="text-base font-semibold" />
      <p className="text-xs tabular-nums text-muted-foreground">Itens <Money value={supplier.subtotal} /> · frete <Money value={supplier.freight} /> · IPI <Money value={supplier.ipiAmount} /></p>
      <p className="text-xs tabular-nums">Prazo {supplier.maxLeadDays ?? '—'} d · {supplier.paymentTermName ?? '—'}</p>
      {!supplier.coversAll ? <p className="text-xs text-muted-foreground">Não cotou todos os itens.</p> : null}
      {canChoose ? <button type="button" onClick={onChooseAll} className={secondary}>Escolher tudo de {supplier.partyName}</button> : null}
    </li>
  )
}

/**
 * Aba Comparativo (CA 7, 8): por linha e fornecedor, o preço, o total da linha e o menor custo; um grupo de rádio por linha
 * (<c>radiogroup</c> "Vencedor de {item}", pelo teclado), pré-marcado no menor custo com a tag "Sugerido"; motivo quando a escolha é
 * outra; "Concluir cotação". A 400 px: cartões por fornecedor e a tabela em rolagem horizontal com a primeira coluna fixa.
 */
export function RfqComparison({ rfq, set }: { rfq: RfqDetail; set: (rfq: RfqDetail) => void }) {
  const responded = rfq.suppliers.filter((s) => s.responded && s.quotes.length > 0)
  const [selection, setSelection] = useState<Record<string, string>>(() => initialSelection(rfq.lines))
  const [reasons, setReasons] = useState<Record<string, ReasonDraft>>({})
  if (responded.length === 0) {
    return <EmptyState className="mt-3" title="Registre a resposta de ao menos um fornecedor." description="O comparativo mostra os preços lado a lado assim que a primeira resposta é salva na aba Respostas." />
  }

  const open = rfq.status === 'Open'
  const canChoose = open && rfq.canClose
  const quoted = new Set(responded.flatMap((s) => s.quotes.map((q) => q.lineId)))
  const missing = missingForClose(rfq.lines, quoted, selection, reasons)
  const columns = `minmax(12rem, 16rem) repeat(${responded.length}, minmax(10.5rem, 1fr))`
  const quoteOf = (s: RfqSupplierView, l: RfqLineView) => s.quotes.find((q) => q.lineId === l.id)
  const chosenOf = (l: RfqLineView) => (open ? selection[l.id] : l.selectedSupplierId ?? undefined)
  const setReason = (lineId: string, patch: Partial<ReasonDraft>) => setReasons((r) => ({ ...r, [lineId]: { ...(r[lineId] ?? { reason: '', note: '' }), ...patch } }))
  const winners = responded.map((s) => ({ s, lines: rfq.lines.filter((l) => chosenOf(l) === s.id) })).filter((w) => w.lines.length > 0)
  const footer = (label: string, value: (s: RfqSupplierView) => ReactNode, dica?: string) => (
    <div className="grid border-t border-border text-sm" style={{ gridTemplateColumns: columns }}>
      <div className="sticky left-0 z-10 flex items-center bg-muted px-3 py-2 font-semibold">{label}{dica ? <Dica nome={label} texto={dica} /> : null}</div>
      {responded.map((s) => <div key={s.id} className="px-3 py-2 text-right tabular-nums">{value(s)}</div>)}
    </div>
  )

  return (
    <div className="mt-3 space-y-4">
      <ul aria-label="Resumo por fornecedor" className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {responded.map((s) => <SupplierSummary key={s.id} supplier={s} canChoose={canChoose} onChooseAll={() => setSelection((sel) => chooseAll(sel, s))} />)}
      </ul>

      <div className="overflow-x-auto rounded-md border border-border bg-card" role="region" aria-label="Comparativo por item" tabIndex={0}>
        <div className="min-w-max">
          <div className="grid border-b border-border bg-muted text-xs font-semibold text-muted-foreground" style={{ gridTemplateColumns: columns }}>
            <div className="sticky left-0 z-10 bg-muted px-3 py-2">Item</div>
            {responded.map((s) => <div key={s.id} className="px-3 py-2 text-right">{s.partyName}</div>)}
          </div>
          {rfq.lines.map((l) => {
            const chosen = chosenOf(l)
            const reason = reasons[l.id]
            const reasonNeeded = open && needsReason(l, chosen)
            return (
              <div key={l.id} className="border-b border-border last:border-b-0">
                <div role="radiogroup" aria-label={`Vencedor de ${l.itemCode} ${l.itemName}`} className="grid" style={{ gridTemplateColumns: columns }}>
                  <div className="sticky left-0 z-10 bg-card px-3 py-2 text-sm">
                    <p><span className="font-mono">{l.itemCode}</span> {l.itemName}</p>
                    <p className="text-xs tabular-nums text-muted-foreground">{formatQuantity(l.quantity)} {l.unitCode}</p>
                    {l.lastPurchase ? <p className="text-xs text-muted-foreground">Última compra: <Money value={l.lastPurchase.unitPrice} /> em {formatInstantDate(l.lastPurchase.approvedAt)} de {l.lastPurchase.partyName}</p> : null}
                    {!quoted.has(l.id) ? <p className="text-xs text-muted-foreground">Sem resposta: volta para a solicitação na conclusão.</p> : null}
                  </div>
                  {responded.map((s) => {
                    const q = quoteOf(s, l)
                    if (!q) return <div key={s.id} className="px-3 py-2 text-right text-xs text-muted-foreground">Não cotou</div>
                    const isChosen = chosen === s.id
                    const short = q.availableQuantity < l.quantity
                    return (
                      <label key={s.id} className={`flex min-h-11 cursor-pointer items-start justify-end gap-2 px-3 py-2 text-right text-sm ${isChosen ? 'bg-primary-soft' : ''}`}>
                        <span className="space-y-0.5 tabular-nums">
                          <span className="block"><Money value={q.unitPrice} /> / {l.unitCode}</span>
                          <span className="block text-xs text-muted-foreground">Total <Money value={q.lineTotal} /> · com frete e IPI <Money value={q.landedCost} /></span>
                          <span className="flex flex-wrap justify-end gap-1">
                            {q.isCheapest ? <StatusChip tone="ok">Menor custo</StatusChip> : null}
                            {q.isCheapest && open ? <Tag title={RFQ_DICAS.sugerido}>Sugerido</Tag> : null}
                            {!open && isChosen ? <StatusChip tone="info">Vencedor</StatusChip> : null}
                            {short ? <StatusChip tone="warn">Tem só {formatQuantity(q.availableQuantity)}</StatusChip> : null}
                          </span>
                        </span>
                        <input type="radio" name={`vencedor-${l.id}`} value={s.id} checked={isChosen} disabled={!canChoose} aria-label={`${s.partyName}: ${q.unitPrice.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL', maximumFractionDigits: 6 })}`}
                          onChange={() => setSelection((sel) => ({ ...sel, [l.id]: s.id }))} className="mt-1 size-5 shrink-0 accent-primary" />
                      </label>
                    )
                  })}
                </div>
                {reasonNeeded ? (
                  <div className="sticky left-0 flex max-w-[calc(100vw-48px)] flex-wrap items-start gap-2 bg-warn-soft px-3 py-2 text-sm">
                    <label className="flex flex-col gap-1">
                      <span className="inline-flex items-center font-medium">Motivo da escolha (obrigatório)<Dica nome="Motivo da escolha" texto={RFQ_DICAS.motivo} /></span>
                      <select aria-label={`Motivo da escolha de ${l.itemCode}`} value={reason?.reason ?? ''} onChange={(e) => setReason(l.id, { reason: e.target.value as ReasonDraft['reason'] })} className={`${inputClass} w-56`}>
                        <option value="">Escolha</option>
                        {REASON_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
                      </select>
                    </label>
                    <label className="flex min-w-0 flex-1 flex-col gap-1">
                      <span className="font-medium">{reason?.reason === 'Other' ? 'Qual o motivo (obrigatório)' : 'Detalhe (opcional)'}</span>
                      <input aria-label={`Detalhe do motivo de ${l.itemCode}`} maxLength={200} value={reason?.note ?? ''} onChange={(e) => setReason(l.id, { note: e.target.value })} className={inputClass} />
                    </label>
                  </div>
                ) : null}
                {!open && l.selectionReason ? (
                  <p className="px-3 pb-2 text-xs text-muted-foreground">Motivo da escolha: {REASONS[l.selectionReason]}{l.selectionNote ? ` — ${l.selectionNote}` : ''}</p>
                ) : null}
              </div>
            )
          })}
          {footer('Subtotal', (s) => <Money value={s.subtotal} />)}
          {footer('Frete', (s) => <Money value={s.freight} />)}
          {footer('IPI', (s) => <Money value={s.ipiAmount} />)}
          {footer('Custo total', (s) => <span className="inline-flex items-center gap-1"><Money value={s.total} className="font-semibold" />{s.isCheapest ? <StatusChip tone="ok">Menor custo</StatusChip> : null}</span>, RFQ_DICAS.custoTotal)}
          {footer('Prazo', (s) => `${s.maxLeadDays ?? '—'} d`)}
          {footer('Condição', (s) => s.paymentTermName ?? '—')}
        </div>
      </div>

      {canChoose ? (
        <div className="flex flex-wrap items-center justify-end gap-2">
          <InlineError id="concluir-cotacao" message={missing[0]} />
          <ConfirmDialog verb="Concluir cotação" done="Cotação concluída" title={`Concluir ${rfq.number}?`}
            description="Os vencedores ficam gravados e a cotação não muda mais. Depois, gere os pedidos de compra."
            preconditions={missing.length ? missing.map((m) => ({ ok: false, text: m })) : [{ ok: true, text: 'Todos os itens cotados têm vencedor.' }]}
            impact={
              <ul className="space-y-1">
                {winners.map((w) => <li key={w.s.id}>{w.s.partyName}: {w.lines.length} {w.lines.length === 1 ? 'item' : 'itens'}</li>)}
                {rfq.quotesCount === 1 ? <li className="text-warn-ink">{RFQ_DICAS.umaCotacao}</li> : null}
              </ul>
            }
            onConfirm={async () => set(await closeRfq(rfq.id, {
              selections: Object.entries(selection).filter(([lineId]) => quoted.has(lineId)).map(([lineId, supplierId]) => {
                const line = rfq.lines.find((l) => l.id === lineId)!
                const r = reasons[lineId]
                return needsReason(line, supplierId) ? { lineId, supplierId, reason: r?.reason || null, note: r?.note.trim() || null } : { lineId, supplierId, reason: null, note: null }
              }),
              version: rfq.version,
            }))}
            onReload={async () => set(await getRfq(rfq.id))}
            trigger={<button type="button" className="inline-flex min-h-11 items-center rounded-md bg-primary px-3 text-sm font-semibold text-primary-foreground">Concluir cotação</button>} />
        </div>
      ) : null}
    </div>
  )
}
