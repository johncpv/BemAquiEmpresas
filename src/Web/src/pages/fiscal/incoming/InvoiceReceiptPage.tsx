import { Link, useNavigate, useParams } from '@tanstack/react-router'
import { useState } from 'react'
import { previewInvoiceReceipt, registerInvoiceReceipt, requestInvoicePriceCorrection, useGetInvoiceReceiptDefaults } from '@/api/generated/bemaqui'
import type { InvoiceReceiptDefaults, InvoiceReceiptInput, InvoiceReceiptPreview } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { Money } from '@/components/ui/money'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { formatQuantity } from '@/pages/inventory/stock/stock'
import { RECEIPT_DICAS } from '@/pages/purchasing/receipts/receipts'
import { PageHeading } from '@/shell/PageHeading'
import { formatPrice, RECEIVE_DICAS } from './incoming'

const primary = 'inline-flex min-h-11 items-center gap-1 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-50'
const secondary = 'inline-flex min-h-11 items-center gap-1 rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold disabled:opacity-50'

interface LineDraft {
  poLineId: string
  lotNumber: string
  lotExpiry: string
  locationId: string
}

/**
 * Nota de entrada → "Receber pela nota" (FIS-04b, `/app/fiscal/entrada/$id/recebimento`): o recebimento da CMP-04 preenchido pela nota
 * conferida, com a faixa "Valores da NF-e {número} · {fornecedor}". Quantidades, frete, seguro, outras despesas e IPI vêm da nota; o
 * usuário confere o lote (sugerido pelo XML) e o local. Preço diferente do pedido bloqueia com "Pedir correção de preço". Confirma no
 * diálogo grande com o impacto: custo, títulos por duplicata, itens sem estoque, regra de crédito e previsto do pedido.
 */
export function InvoiceReceiptPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const query = useGetInvoiceReceiptDefaults(id, { query: { retry: false } })
  if (query.isPending) return <div className="max-w-5xl space-y-3"><Skeleton className="h-10 w-2/3" /><Skeleton className="h-16" /><Skeleton className="h-64" /></div>
  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return <ErrorState title={notFound ? 'Nota de entrada não encontrada.' : 'Não foi possível abrir o recebimento pela nota.'} message={notFound ? 'Ela pode ter sido aberta em outra empresa. Volte às notas de entrada.' : 'Verifique a conexão e tente de novo.'} onRetry={notFound ? undefined : () => query.refetch()} />
  }

  const d = query.data
  if (d.stage === 'Received') {
    return (
      <>
        <PageHeading title={`Receber pela nota — NF-e ${d.invoiceNumber}`} />
        <EmptyState title={`A NF-e ${d.invoiceNumber} já foi recebida.`} description="O material já entrou no estoque e os títulos a pagar nasceram pelas duplicatas da nota."
          action={d.goodsReceiptId ? <Link to={'/app/compras/recebimentos/$id' as never} params={{ id: d.goodsReceiptId } as never} className={primary}>Ver o recebimento</Link> : undefined} />
      </>
    )
  }

  if (d.blockerCode === 'incoming_invoice_not_checked' || d.blockerCode === 'incoming_invoice_without_order') {
    return (
      <>
        <PageHeading title={`Receber pela nota — NF-e ${d.invoiceNumber}`} />
        <EmptyState title={d.blocker ?? 'A nota ainda não pode ser recebida.'}
          description={d.blockerCode === 'incoming_invoice_without_order' ? 'Na aba Pedido e divergências da nota, escolha o pedido de compra. Depois, receba pela nota.' : 'Ligue os itens e o pedido e clique em Conferir nota. Depois, receba pela nota.'}
          action={<Link to="/app/fiscal/entrada/$id" params={{ id: d.invoiceId }} className={primary}>Abrir a nota</Link>} />
      </>
    )
  }

  return <ReceiveForm key={`${d.invoiceVersion}-${d.orderVersion}`} defaults={d} onReload={() => query.refetch()} />
}

/** Impacto do recebimento pela nota (CA 5 a 9): custo por item, títulos por duplicata, itens sem estoque, regra de crédito e previsto. */
function Impact({ preview, error }: { preview: InvoiceReceiptPreview | null; error?: string }) {
  if (error) return <p role="alert" className="text-sm text-crit-ink">{error}</p>
  if (!preview) return <Skeleton className="h-40" />
  const r = preview.receipt
  return (
    <div className="space-y-3">
      <ul className="divide-y divide-border rounded-md border border-border text-sm" aria-label="Entrada no estoque por item">
        {r.lines.map((l) => (
          <li key={l.poLineId} className="flex flex-wrap items-center justify-between gap-x-4 gap-y-1 p-2">
            <span className="min-w-0 flex-1 basis-48"><span className="font-mono">{l.itemCode}</span> {l.itemName}</span>
            <span className="tabular-nums"><b>{formatQuantity(l.quantity)} {l.unitCode}</b></span>
            <span className="inline-flex items-center tabular-nums">custo de entrada <Money value={l.unitCost} className="ml-1 font-semibold" /><Dica nome="Custo de entrada" texto={RECEIPT_DICAS.custoEntrada} /></span>
            {l.averageCostBefore != null && l.averageCostAfter != null ? (
              <span className="basis-full text-xs text-muted-foreground tabular-nums">Custo médio <Money value={l.averageCostBefore} /> → <Money value={l.averageCostAfter} /></span>
            ) : null}
          </li>
        ))}
      </ul>
      <p className="tabular-nums">Total da entrada: <Money value={r.amount} className="font-semibold" /> <span className="text-muted-foreground">(itens <Money value={r.subtotal} />, frete <Money value={r.freight} />, IPI <Money value={r.ipiAmount} />{r.insurance ? <>, seguro <Money value={r.insurance} /></> : null}{r.otherExpenses ? <>, outras despesas <Money value={r.otherExpenses} /></> : null})</span></p>
      <p className="text-sm">Regra usada: <b className="font-semibold">{preview.creditRule}</b>{r.taxCredit ? <> · crédito <Money value={r.taxCredit} /></> : null}</p>
      <div>
        <p className="flex items-center text-sm font-semibold">Títulos a pagar que nascem{preview.fromDuplicates ? ' (um por duplicata)' : ''}<Dica nome="Duplicatas" texto={RECEIVE_DICAS.duplicatas} /></p>
        {!preview.fromDuplicates ? <p className="flex items-center text-xs text-muted-foreground">{RECEIVE_DICAS.semDuplicatas}<Dica nome="Sem duplicatas" texto={RECEIVE_DICAS.semDuplicatas} /></p> : null}
        <table className="w-full text-left text-sm">
          <caption className="sr-only">Títulos a pagar do recebimento</caption>
          <thead><tr className="text-xs text-muted-foreground"><th scope="col">Parcela</th><th scope="col">Vencimento</th><th scope="col" className="text-right">Valor</th></tr></thead>
          <tbody>
            {r.installments.map((i) => (
              <tr key={i.number}><td>{i.number}/{r.installments.length}</td><td className="tabular-nums">{formatDate(i.dueDate)}</td><td className="text-right"><Money value={i.amount} /></td></tr>
            ))}
          </tbody>
        </table>
        {preview.noStock.length ? (
          <ul className="mt-2 space-y-1 text-sm" aria-label="Títulos dos itens sem estoque">
            {preview.noStock.map((n) => (
              <li key={`${n.accountName}-${n.number}`} className="flex flex-wrap justify-between gap-2 tabular-nums"><span>{n.accountName} · parcela {n.number}/{n.count} · {formatDate(n.dueDate)}</span><Money value={n.amount} /></li>
            ))}
          </ul>
        ) : null}
        {preview.duplicatesMismatch ? <p role="note" className="mt-2 rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">As duplicatas não somam o total da nota (<Money value={preview.invoiceTotal} />): os títulos ficam pelo valor recebido, na proporção das duplicatas, com o aviso para o financeiro conferir.</p> : null}
      </div>
      <p className="tabular-nums">Previsto do pedido no caixa: <Money value={r.plannedBefore} /> → <Money value={r.plannedAfter} className="font-semibold" />{r.orderCompletes ? ' (o pedido fica recebido)' : ''}</p>
      {r.soleOwner ? <p role="note" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">Você pediu ou aprovou este pedido: o recebimento leva a marca "Recebido pelo Proprietário único" e o Proprietário é avisado.</p> : null}
    </div>
  )
}

function ReceiveForm({ defaults: d, onReload }: { defaults: InvoiceReceiptDefaults; onReload: () => Promise<unknown> }) {
  const navigate = useNavigate()
  const idempotency = useIdempotencyKey()
  const [receivedOn, setReceivedOn] = useState(d.today)
  const [notes, setNotes] = useState('')
  const [lines, setLines] = useState<LineDraft[]>(() => d.lines.map((l) => ({ poLineId: l.poLineId, lotNumber: l.suggestedLot ?? '', lotExpiry: '', locationId: d.defaultLocationId ?? '' })))
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [preview, setPreview] = useState<InvoiceReceiptPreview | null>(null)
  const [previewError, setPreviewError] = useState<string | undefined>()
  const [asking, setAsking] = useState(false)
  const [askError, setAskError] = useState<string | null>(null)

  const set = (index: number, patch: Partial<LineDraft>) => setLines(lines.map((l, i) => (i === index ? { ...l, ...patch } : l)))
  const missingLot = lines.some((l, i) => d.lines[i].tracksLot && !l.lotNumber.trim())
  const body = (): InvoiceReceiptInput => ({
    receivedOn, notes: notes.trim() || null, orderVersion: d.orderVersion, invoiceVersion: d.invoiceVersion,
    lines: lines.map((l) => ({ poLineId: l.poLineId, lotNumber: l.lotNumber.trim() || null, lotExpiry: l.lotExpiry || null, locationId: l.locationId || null })),
  })
  const showErrors = (e: unknown) => {
    if (!(e instanceof ApiError)) return
    const fields: Record<string, string> = {}
    for (const [key, messages] of Object.entries(e.fields)) {
      const match = /^lines\[(\d+)\]\.(\w+)$/.exec(key)
      fields[match ? `${match[1]}.${match[2]}` : key] = messages[0]
    }

    setErrors(fields)
  }

  const loadPreview = async () => {
    setPreview(null)
    setPreviewError(undefined)
    try {
      setPreview(await previewInvoiceReceipt(d.invoiceId, body()))
      setErrors({})
    } catch (e) {
      showErrors(e)
      setPreviewError(e instanceof ApiError ? e.message : 'Não foi possível calcular o impacto.')
    }
  }

  const askCorrection = async () => {
    setAsking(true)
    setAskError(null)
    try {
      const result = await requestInvoicePriceCorrection(d.invoiceId)
      notifySuccess(result.notified === 1 ? 'Pedido de correção enviado a 1 aprovador' : `Pedido de correção enviado a ${result.notified} aprovadores`)
    } catch (e) {
      setAskError(e instanceof ApiError ? e.message : 'Não foi possível enviar o pedido de correção. Tente de novo.')
    } finally {
      setAsking(false)
    }
  }

  const blocked = d.blocker
  return (
    <>
      <PageHeading title={`Receber pela nota — ${d.orderNumber ?? 'pedido'}`}>
        <p className="mt-2 flex flex-wrap items-center gap-2 text-muted-foreground">
          <span>{d.supplierName}{d.projectNumber ? ` · ${d.projectNumber}` : ' · Estoque geral'}</span>
          <Link to="/app/fiscal/entrada/$id" params={{ id: d.invoiceId }} className="font-semibold text-primary">Voltar à nota</Link>
          {d.orderId ? <Link to={'/app/compras/pedidos/$id' as never} params={{ id: d.orderId } as never} className="font-semibold text-primary">Abrir o pedido</Link> : null}
        </p>
      </PageHeading>
      <p role="note" className="mb-3 rounded-md border border-l-4 border-info-soft border-l-info bg-info-soft px-3 py-2 text-sm text-info-ink">
        <b className="font-semibold">Valores da NF-e {d.invoiceNumber} · {d.supplierName}</b> — emitida em <span className="tabular-nums">{formatDate(d.issuedOn)}</span>, total <Money value={d.total} />. Quantidades, frete e impostos vêm da nota; o preço é o do pedido.
      </p>
      {blocked ? <p role="note" className="mb-3 rounded-md border border-l-4 border-warn-soft border-l-warn bg-warn-soft px-3 py-2 text-sm text-warn-ink">{blocked}</p> : null}
      <form className="max-w-5xl space-y-4" onSubmit={(e) => e.preventDefault()}>
        <div className="grid gap-3 sm:grid-cols-3">
          <Field label="Data de recebimento" required error={errors.receivedOn} hint="Hoje ou antes.">
            {(p) => <input {...p} type="date" max={d.today} value={receivedOn} onChange={(e) => setReceivedOn(e.target.value)} className={inputClass + ' tabular-nums'} />}
          </Field>
        </div>
        <ul className="space-y-3" aria-label="Itens da nota">
          {lines.map((l, i) => {
            const source = d.lines[i]
            return (
              <li key={l.poLineId} className={`rounded-md border border-border bg-card p-3 ${source.priceDiffers ? 'border-l-4 border-l-crit' : ''}`}>
                <p className="flex flex-wrap items-center justify-between gap-2 text-sm">
                  <span><span className="font-mono">{source.itemCode}</span> {source.itemName} <span className="text-xs text-muted-foreground">(linha {source.sequence} do pedido · item{source.invoiceItems.length > 1 ? 's' : ''} {source.invoiceItems.join(', ')} da nota)</span></span>
                  <span className="tabular-nums"><b>{formatQuantity(source.quantity)} {source.unitCode}</b> <span className="text-muted-foreground">da nota · saldo {formatQuantity(source.open)} {source.unitCode}</span></span>
                </p>
                <p className="mt-1 flex flex-wrap items-center gap-2 text-sm tabular-nums">
                  <span>Preço do pedido <Money value={source.orderPrice} /> × nota {formatPrice(source.invoicePrice)}</span>
                  {source.priceDiffers ? <StatusChip tone="crit">Preço diferente do pedido</StatusChip> : <StatusChip tone="ok">Preço igual ao pedido</StatusChip>}
                </p>
                <div className="mt-2 grid gap-3 sm:grid-cols-3">
                  <Field label="Lote" required={source.tracksLot} error={errors[`${i}.lotNumber`]} hint={source.tracksLot ? 'Este item controla lote.' : 'Opcional.'}>
                    {(p) => <input {...p} maxLength={60} value={l.lotNumber} onChange={(e) => set(i, { lotNumber: e.target.value })} className={inputClass} />}
                  </Field>
                  <Field label="Validade do lote" error={errors[`${i}.lotExpiry`]} hint="Opcional.">
                    {(p) => <input {...p} type="date" value={l.lotExpiry} disabled={!l.lotNumber.trim()} onChange={(e) => set(i, { lotExpiry: e.target.value })} className={inputClass + ' tabular-nums'} />}
                  </Field>
                  <Field label="Local" error={errors[`${i}.locationId`]}>
                    {(p) => (
                      <select {...p} value={l.locationId} onChange={(e) => set(i, { locationId: e.target.value })} className={inputClass}>
                        {d.locations.map((loc) => <option key={loc.id} value={loc.id}>{loc.code} — {loc.name}</option>)}
                      </select>
                    )}
                  </Field>
                </div>
                <p className="mt-1 flex flex-wrap gap-2 text-xs text-muted-foreground">
                  {source.suggestedLot && l.lotNumber.trim() === source.suggestedLot ? <span><Tag>Sugerido</Tag> lote do XML da nota</span> : null}
                  {l.locationId === d.defaultLocationId ? <span><Tag>Sugerido</Tag> local padrão do estoque</span> : null}
                </p>
              </li>
            )
          })}
        </ul>
        {d.noStock.length ? (
          <section aria-label="Itens sem estoque" className="rounded-md border border-border p-3 text-sm">
            <h2 className="mb-1 font-semibold">Sem item de estoque</h2>
            <p className="mb-2 text-muted-foreground">Não entram no estoque: viram títulos a pagar próprios na conta escolhida, um por duplicata.</p>
            <ul className="space-y-1">
              {d.noStock.map((n) => <li key={n.sequence} className="flex flex-wrap justify-between gap-2"><span>{n.description} · {n.accountName ?? 'conta removida'}</span><Money value={n.amount} /></li>)}
            </ul>
          </section>
        ) : null}
        <div className="grid gap-3 rounded-md border border-border p-3 text-sm tabular-nums sm:grid-cols-4">
          <p><span className="block text-xs text-muted-foreground">Frete da nota</span><Money value={d.freight} /> <span className="text-xs text-muted-foreground">(pedido até <Money value={d.freightMax} />)</span></p>
          <p><span className="block text-xs text-muted-foreground">IPI da nota</span><Money value={d.ipiAmount} /> <span className="text-xs text-muted-foreground">(pedido até <Money value={d.ipiMax} />)</span></p>
          <p><span className="block text-xs text-muted-foreground">Seguro</span><Money value={d.insurance} /></p>
          <p><span className="block text-xs text-muted-foreground">Outras despesas</span><Money value={d.otherExpenses} /></p>
          <p className="flex items-center text-xs text-muted-foreground sm:col-span-4">Divididos pelo valor de cada linha; a última fica com o centavo.<Dica nome="Rateio" texto={RECEIPT_DICAS.rateio} /></p>
        </div>
        <section aria-label="Duplicatas" className="rounded-md border border-border p-3 text-sm">
          <h2 className="mb-1 flex items-center font-semibold">Duplicatas<Dica nome="Duplicatas" texto={RECEIVE_DICAS.duplicatas} /></h2>
          {d.duplicates.length === 0
            ? <p className="flex items-center text-muted-foreground">{RECEIVE_DICAS.semDuplicatas}<Dica nome="Sem duplicatas" texto={RECEIVE_DICAS.semDuplicatas} /></p>
            : (
              <ul className="space-y-1">
                {d.duplicates.map((dup) => <li key={dup.number} className="flex flex-wrap justify-between gap-2 tabular-nums"><span>{dup.number}{dup.dueDate ? ` · vence em ${formatDate(dup.dueDate)}` : ''}</span><Money value={dup.amount} /></li>)}
              </ul>
            )}
        </section>
        <p className="flex flex-wrap items-center gap-2 text-sm">
          <span className="inline-flex items-center">Crédito de impostos: <b className="ml-1 font-semibold">{d.creditRule}</b><Dica nome="Crédito de impostos" texto={RECEIVE_DICAS.credito} /></span>
          {!d.creditConfirmed ? <StatusChip tone="warn">A confirmar com a contadora</StatusChip> : null}
        </p>
        <Field label="Observação" hint="Opcional, até 500 caracteres.">
          {(p) => <textarea {...p} rows={2} maxLength={500} value={notes} onChange={(e) => setNotes(e.target.value)} className={inputClass} />}
        </Field>
        {errors.lines ? <p role="alert" className="text-sm text-crit-ink">{errors.lines}</p> : null}
        {errors.freight || errors.ipiAmount ? <p role="alert" className="text-sm text-crit-ink">{errors.freight ?? errors.ipiAmount}</p> : null}
        <div className="flex flex-wrap items-center gap-2">
          {d.priceBlocked ? (
            <>
              <button type="button" disabled aria-describedby="receber-motivo" className={primary}>Receber pela nota</button>
              <button type="button" disabled={asking} onClick={() => void askCorrection()} className={secondary}>Pedir correção de preço</button>
              <p id="receber-motivo" className="basis-full text-sm text-muted-foreground">{blocked}</p>
              {askError ? <p role="alert" className="basis-full text-sm text-crit-ink">{askError}</p> : null}
            </>
          ) : (
            <ConfirmDialog size="lg" verb="Receber pela nota" done="Recebimento registrado" title={`Receber a NF-e ${d.invoiceNumber}?`}
              description="Na mesma hora: o material entra no estoque pelo custo da nota, os títulos a pagar nascem pelas duplicatas, o previsto do pedido cai e a nota fica recebida."
              onOpen={() => void loadPreview()} impact={<Impact preview={preview} error={previewError} />}
              preconditions={[
                { ok: !blocked, text: blocked ?? 'Você pode receber.' },
                { ok: !missingLot, text: 'Lote informado nos itens que controlam lote.' },
                { ok: preview !== null, text: previewError ? 'Corrija o que o impacto apontou.' : 'Impacto calculado.' },
              ]}
              onReload={onReload}
              onConfirm={async () => {
                try {
                  const saved = await registerInvoiceReceipt(d.invoiceId, body(), idempotency.options)
                  idempotency.renew()
                  notifySuccess(`Recebimento registrado pela nota ${d.invoiceNumber}`)
                  void navigate({ to: '/app/compras/recebimentos/$id', params: { id: saved.id }, search: { registrado: 1 } } as never)
                } catch (e) {
                  showErrors(e)
                  throw e
                }
              }}
              trigger={<button type="button" disabled={!!blocked} title={blocked ?? undefined} className={primary}>Receber pela nota</button>} />
          )}
          <button type="button" onClick={() => void navigate({ to: '/app/fiscal/entrada/$id', params: { id: d.invoiceId } })} className={secondary}>Voltar</button>
        </div>
      </form>
    </>
  )
}
