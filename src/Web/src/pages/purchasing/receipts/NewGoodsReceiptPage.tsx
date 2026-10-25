import { Link, useNavigate, useParams } from '@tanstack/react-router'
import { useState } from 'react'
import { previewGoodsReceipt, registerGoodsReceipt, useGetGoodsReceiptDefaults } from '@/api/generated/bemaqui'
import type { GoodsReceiptDefaults, GoodsReceiptInput, GoodsReceiptPreview } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { formatQuantity } from '@/pages/inventory/stock/stock'
import { PageHeading } from '@/shell/PageHeading'
import { balanceLabel, proportionalShare, RECEIPT_DICAS, receiptTotals, toStock } from './receipts'

interface LineDraft {
  poLineId: string
  quantity: string
  inSupplierUnit: boolean
  lotNumber: string
  lotExpiry: string
  locationId: string
}

const num = (text: string) => {
  const trimmed = text.trim()
  const value = Number(trimmed.includes(',') ? trimmed.replace(/\./g, '').replace(',', '.') : trimmed)
  return text.trim() === '' || Number.isNaN(value) ? 0 : value
}
const money = (text: string) => num(text)
const toText = (value: number) => String(value).replace('.', ',')

/** Linhas já preenchidas (CA 1, propositivo): o saldo de cada linha, o local padrão e lote vazio. */
const initialLines = (d: GoodsReceiptDefaults): LineDraft[] => d.lines.map((l) => ({
  poLineId: l.poLineId, quantity: l.open > 0 ? toText(l.open) : '0', inSupplierUnit: false, lotNumber: '', lotExpiry: '', locationId: d.defaultLocationId ?? '',
}))

/** Impacto do recebimento (CA 5): por item, os títulos que nascem e o previsto do pedido antes → depois. */
function Impact({ preview, error }: { preview: GoodsReceiptPreview | null; error?: string }) {
  if (error) return <p role="alert" className="text-sm text-crit-ink">{error}</p>
  if (!preview) return <Skeleton className="h-40" />
  return (
    <div className="space-y-3">
      <ul className="divide-y divide-border rounded-md border border-border text-sm" aria-label="Entrada no estoque por item">
        {preview.lines.map((l) => (
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
      <p className="tabular-nums">Total da entrada: <Money value={preview.amount} className="font-semibold" /> <span className="text-muted-foreground">(itens <Money value={preview.subtotal} />, frete <Money value={preview.freight} />, IPI <Money value={preview.ipiAmount} />)</span></p>
      <div>
        <p className="text-sm font-semibold">Títulos a pagar que nascem</p>
        <table className="w-full text-left text-sm">
          <caption className="sr-only">Títulos a pagar do recebimento</caption>
          <thead><tr className="text-xs text-muted-foreground"><th scope="col">Parcela</th><th scope="col">Vencimento</th><th scope="col" className="text-right">Valor</th></tr></thead>
          <tbody>
            {preview.installments.map((i) => (
              <tr key={i.number}><td>{i.number}/{preview.installments.length}</td><td className="tabular-nums">{formatDate(i.dueDate)}</td><td className="text-right"><Money value={i.amount} /></td></tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="tabular-nums">Previsto do pedido no caixa: <Money value={preview.plannedBefore} /> → <Money value={preview.plannedAfter} className="font-semibold" />{preview.orderCompletes ? ' (o pedido fica recebido)' : ''}</p>
      {preview.soleOwner ? <p role="note" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">Você pediu ou aprovou este pedido: o recebimento leva a marca "Recebido pelo Proprietário único" e o Proprietário é avisado.</p> : null}
    </div>
  )
}

/**
 * Pedido → "Registrar recebimento" (CMP-04 CA 1 a 7, `/app/compras/pedidos/$id/recebimento`): as linhas do pedido com o saldo, a
 * quantidade (padrão o saldo; na unidade de estoque ou do fornecedor), lote, validade e local; o preço do pedido só leitura; frete e
 * IPI proporcionais, editáveis só para baixo. Confirma no diálogo grande com o impacto (custo, títulos, previsto). A 400 px cada linha é
 * um cartão e o diálogo ocupa a tela.
 */
export function NewGoodsReceiptPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const query = useGetGoodsReceiptDefaults(id)
  if (query.isPending) return <Skeleton className="h-96 max-w-5xl" />
  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return <ErrorState title={notFound ? 'Pedido não encontrado.' : 'Não foi possível abrir o recebimento.'} message={notFound ? 'Ele pode ter sido aberto em outra empresa. Volte aos pedidos.' : 'Verifique a conexão e tente de novo.'} onRetry={notFound ? undefined : () => query.refetch()} />
  }

  return <ReceiptForm key={query.data.version} defaults={query.data} onReload={() => query.refetch()} />
}

function ReceiptForm({ defaults: d, onReload }: { defaults: GoodsReceiptDefaults; onReload: () => Promise<unknown> }) {
  const navigate = useNavigate()
  const idempotency = useIdempotencyKey()
  const [receivedOn, setReceivedOn] = useState(d.today)
  const [lines, setLines] = useState<LineDraft[]>(() => initialLines(d))
  const [freight, setFreight] = useState<string | null>(null)
  const [ipi, setIpi] = useState<string | null>(null)
  const [notes, setNotes] = useState('')
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [preview, setPreview] = useState<GoodsReceiptPreview | null>(null)
  const [previewError, setPreviewError] = useState<string | undefined>()

  const set = (index: number, patch: Partial<LineDraft>) => setLines(lines.map((l, i) => (i === index ? { ...l, ...patch } : l)))
  const stockQuantity = (l: LineDraft) => {
    const source = d.lines.find((x) => x.poLineId === l.poLineId)!
    return l.inSupplierUnit ? toStock(num(l.quantity), source.supplierFactor) : num(l.quantity)
  }
  const drafts = lines.map((l) => {
    const source = d.lines.find((x) => x.poLineId === l.poLineId)!
    return { poLineId: l.poLineId, quantity: stockQuantity(l), unitPrice: source.unitPrice, open: source.open }
  })
  const received = drafts.filter((l) => l.quantity > 0)
  const totals = receiptTotals(drafts.map((l) => ({ ...l, quantity: Math.max(0, l.quantity) })))
  const freightMax = proportionalShare(d.orderFreight, d.orderSubtotal, totals.subtotal, d.freightUsed, totals.completes)
  const ipiMax = proportionalShare(d.orderIpiAmount, d.orderSubtotal, totals.subtotal, d.ipiUsed, totals.completes)
  const freightValue = freight === null ? freightMax : money(freight)
  const ipiValue = ipi === null ? ipiMax : money(ipi)
  const missingLot = lines.some((l, i) => d.lines[i].tracksLot && stockQuantity(l) > 0 && !l.lotNumber.trim())

  const body = (): GoodsReceiptInput => ({
    receivedOn, freight: freightValue, ipiAmount: ipiValue, notes: notes.trim() || null, version: d.version,
    lines: lines.filter((l) => num(l.quantity) > 0).map((l) => ({
      poLineId: l.poLineId, quantity: num(l.quantity), inSupplierUnit: l.inSupplierUnit, lotNumber: l.lotNumber.trim() || null, lotExpiry: l.lotExpiry || null, locationId: l.locationId || null,
    })),
  })
  const showErrors = (e: unknown) => {
    if (!(e instanceof ApiError)) return
    const fields: Record<string, string> = {}
    for (const [key, messages] of Object.entries(e.fields)) {
      const match = /^lines\[(\d+)\]\.(\w+)$/.exec(key)
      if (match) {
        const sent = body().lines![Number(match[1])]
        const index = lines.findIndex((l) => l.poLineId === sent?.poLineId)
        fields[`${index}.${match[2]}`] = messages[0]
      } else fields[key] = messages[0]
    }

    setErrors(fields)
  }

  const loadPreview = async () => {
    setPreview(null)
    setPreviewError(undefined)
    try {
      setPreview(await previewGoodsReceipt(d.orderId, body()))
      setErrors({})
    } catch (e) {
      showErrors(e)
      setPreviewError(e instanceof ApiError ? e.message : 'Não foi possível calcular o impacto.')
    }
  }

  const blocked = d.blocker
  return (
    <>
      <PageHeading title={`Registrar recebimento — ${d.orderNumber}`}>
        <p className="mt-2 flex flex-wrap items-center gap-2 text-muted-foreground">
          <span>{d.partyName}{d.projectNumber ? ` · ${d.projectNumber}` : ' · Estoque geral'}</span>
          <Link to={'/app/compras/pedidos/$id' as never} params={{ id: d.orderId } as never} className="font-semibold text-primary">Voltar ao pedido</Link>
        </p>
      </PageHeading>
      {blocked ? <p role="note" className="mb-3 rounded-md border border-l-4 border-warn-soft border-l-warn bg-warn-soft px-3 py-2 text-sm text-warn-ink">{blocked}</p> : null}
      <form className="max-w-5xl space-y-4" onSubmit={(e) => e.preventDefault()}>
        <div className="grid gap-3 sm:grid-cols-3">
          <Field label="Data de recebimento" required error={errors.receivedOn} hint="Hoje ou antes.">
            {(p) => <input {...p} type="date" max={d.today} value={receivedOn} onChange={(e) => setReceivedOn(e.target.value)} className={inputClass + ' tabular-nums'} />}
          </Field>
          <p className="self-end text-sm text-muted-foreground sm:col-span-2">
            <span className="inline-flex items-center">Tolerância: {d.tolerancePct.toLocaleString('pt-BR')}%<Dica nome="Tolerância" texto={RECEIPT_DICAS.tolerancia} /></span>
          </p>
        </div>
        <ul className="space-y-3" aria-label="Itens recebidos">
          {lines.map((l, i) => {
            const source = d.lines[i]
            return (
              <li key={l.poLineId} className="rounded-md border border-border bg-card p-3">
                <p className="flex flex-wrap items-center justify-between gap-2 text-sm">
                  <span><span className="font-mono">{source.itemCode}</span> {source.itemName}</span>
                  <span className="tabular-nums text-muted-foreground">{balanceLabel(source.open, source.unitCode, formatQuantity)} · preço do pedido <Money value={source.unitPrice} /></span>
                </p>
                <div className="mt-2 grid gap-3 sm:grid-cols-4">
                  <Field label={`Recebido (${l.inSupplierUnit ? source.supplierUnit : source.unitCode})`} error={errors[`${i}.quantity`]}
                    hint={l.inSupplierUnit && source.supplierFactor ? `= ${formatQuantity(stockQuantity(l))} ${source.unitCode}` : `Até ${formatQuantity(source.maxQuantity)} ${source.unitCode}`}>
                    {(p) => <input {...p} inputMode="decimal" value={l.quantity} disabled={source.open <= 0 && source.maxQuantity <= 0} onChange={(e) => set(i, { quantity: e.target.value.replace(/[^0-9,.]/g, '') })} className={inputClass + ' tabular-nums'} />}
                  </Field>
                  {source.supplierUnit ? (
                    <label className="flex items-center gap-2 self-center text-sm">
                      <input type="checkbox" checked={l.inSupplierUnit} onChange={(e) => set(i, { inSupplierUnit: e.target.checked })} className="size-4 accent-primary" />
                      Na unidade do fornecedor ({source.supplierUnit})
                    </label>
                  ) : null}
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
                {l.locationId === d.defaultLocationId ? <p className="mt-1 text-xs text-muted-foreground"><Tag>Sugerido</Tag> local padrão do estoque</p> : null}
              </li>
            )
          })}
        </ul>
        <div className="grid gap-3 rounded-md border border-border p-3 sm:grid-cols-3">
          <Field label="Frete deste recebimento" error={errors.freight} hint={`Até ${freightMax.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })}: só baixa.`} dica={RECEIPT_DICAS.rateio}>
            {(p) => <input {...p} inputMode="decimal" value={freight ?? toText(freightMax)} onChange={(e) => setFreight(e.target.value.replace(/[^0-9,.]/g, ''))} className={inputClass + ' tabular-nums'} />}
          </Field>
          <Field label="IPI deste recebimento" error={errors.ipiAmount} hint={`Até ${ipiMax.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })}: só baixa.`}>
            {(p) => <input {...p} inputMode="decimal" value={ipi ?? toText(ipiMax)} onChange={(e) => setIpi(e.target.value.replace(/[^0-9,.]/g, ''))} className={inputClass + ' tabular-nums'} />}
          </Field>
          <p className="self-end text-sm tabular-nums">Itens <Money value={totals.subtotal} /> · total <Money value={Math.round((totals.subtotal + freightValue + ipiValue) * 100) / 100} className="font-semibold" /></p>
        </div>
        <Field label="Observação" hint="Opcional, até 500 caracteres.">
          {(p) => <textarea {...p} rows={2} maxLength={500} value={notes} onChange={(e) => setNotes(e.target.value)} className={inputClass} />}
        </Field>
        {errors.lines ? <p role="alert" className="text-sm text-crit-ink">{errors.lines}</p> : null}
        <div className="flex flex-wrap gap-2">
          <ConfirmDialog size="lg" verb="Registrar recebimento" done="Recebimento registrado" title={`Registrar o recebimento do ${d.orderNumber}?`}
            description="Na mesma hora: o material entra no estoque pelo custo de entrada, os títulos a pagar do recebimento nascem em aberto e o previsto do pedido cai para o saldo."
            onOpen={() => void loadPreview()} impact={<Impact preview={preview} error={previewError} />}
            preconditions={[
              { ok: !blocked, text: blocked ?? 'Você pode registrar.' },
              { ok: received.length > 0, text: 'Pelo menos um item com quantidade recebida.' },
              { ok: !missingLot, text: 'Lote informado nos itens que controlam lote.' },
              { ok: preview !== null, text: previewError ? 'Corrija o que o impacto apontou.' : 'Impacto calculado.' },
            ]}
            onReload={onReload}
            onConfirm={async () => {
              try {
                const saved = await registerGoodsReceipt(d.orderId, body(), idempotency.options)
                idempotency.renew()
                void navigate({ to: '/app/compras/recebimentos/$id', params: { id: saved.id }, search: { registrado: 1 } } as never)
              } catch (e) {
                showErrors(e)
                throw e
              }
            }}
            trigger={<button type="button" disabled={!!blocked} title={blocked ?? undefined} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">Registrar recebimento</button>} />
          <button type="button" onClick={() => void navigate({ to: '/app/compras/pedidos/$id', params: { id: d.orderId } } as never)} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
        </div>
      </form>
    </>
  )
}
