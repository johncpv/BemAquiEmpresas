import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { ArrowLeft, Trash2 } from 'lucide-react'
import { useEffect, useState } from 'react'
import { createSalesOrder, getSalesOrder, useListSalesOrderCustomerOptions, useListSalesOrderProductOptions } from '@/api/generated/bemaqui'
import type { SalesOrderCustomerOption, SalesOrderProductOption } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { MoneyInput } from '@/components/form/inputs'
import { notifySuccess } from '@/components/ui/messages'
import { Money } from '@/components/ui/money'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { PageHeading } from '@/shell/PageHeading'
import { type DraftLine, draftTotals, ORDER_DICAS, shortLines } from './orders'

const qty = (n: number) => n.toLocaleString('pt-BR', { maximumFractionDigits: 4 })
const toNumber = (v: string | null) => (v ? Number(v) : 0)

function CustomerPicker({ value, onChange }: { value: SalesOrderCustomerOption | null; onChange: (c: SalesOrderCustomerOption | null) => void }) {
  const [q, setQ] = useState('')
  const options = useListSalesOrderCustomerOptions({ q }, { query: { enabled: !value && q.trim().length >= 2 } })
  if (value) {
    return (
      <div className="flex flex-wrap items-center gap-2 rounded-md border border-border bg-card p-2 text-sm">
        <b>{value.name}</b>{value.document ? <span className="text-muted-foreground tabular-nums">{value.document}</span> : null}
        <button type="button" onClick={() => onChange(null)} className="ml-auto min-h-9 rounded-md border border-border px-2 font-semibold">Trocar cliente</button>
      </div>
    )
  }

  return (
    <Field label="Cliente" required hint="Digite ao menos 2 letras do nome ou do documento.">
      {(p) => (
        <div className="space-y-1">
          <input {...p} autoFocus value={q} onChange={(e) => setQ(e.target.value)} className={inputClass} autoComplete="off" />
          {options.data && options.data.length > 0 ? (
            <ul className="max-h-56 overflow-auto rounded-md border border-border bg-card text-sm" aria-label="Clientes encontrados">
              {options.data.map((c) => (
                <li key={c.id}><button type="button" onClick={() => onChange(c)} className="flex min-h-11 w-full items-center gap-2 px-3 text-left hover:bg-muted">{c.name}<span className="text-muted-foreground tabular-nums">{c.document}</span></button></li>
              ))}
            </ul>
          ) : null}
          {options.data && options.data.length === 0 ? (
            <p className="text-sm text-muted-foreground">Nenhum cliente com esse nome. <Link to={'/app/cadastros/terceiros/novo' as never} className="font-semibold text-primary">Cadastrar cliente</Link></p>
          ) : null}
        </div>
      )}
    </Field>
  )
}

function ProductSearch({ onPick }: { onPick: (p: SalesOrderProductOption) => void }) {
  const [q, setQ] = useState('')
  const options = useListSalesOrderProductOptions({ q }, { query: { enabled: q.trim().length >= 2 } })
  return (
    <Field label="Adicionar produto" hint="Código de barras ou nome. Mostra o preço da tabela e o disponível no local.">
      {(p) => (
        <div className="space-y-1">
          <input {...p} value={q} onChange={(e) => setQ(e.target.value)} className={inputClass} autoComplete="off" />
          {options.data && options.data.length > 0 ? (
            <ul className="max-h-64 overflow-auto rounded-md border border-border bg-card text-sm" aria-label="Produtos encontrados">
              {options.data.map((o) => (
                <li key={o.productId}>
                  <button type="button" disabled={o.price == null} onClick={() => { onPick(o); setQ('') }} className="flex min-h-11 w-full flex-wrap items-center gap-2 px-3 text-left hover:bg-muted disabled:opacity-60">
                    <span className="min-w-0 flex-1 truncate">{o.description}</span>
                    {o.price == null ? <span className="text-warn-ink">sem preço</span> : <Money value={Number(o.price)} />}
                    <span className="text-xs text-muted-foreground tabular-nums">disp. {qty(Number(o.available))} {o.unitCode}</span>
                  </button>
                </li>
              ))}
            </ul>
          ) : null}
          {options.data && options.data.length === 0 ? <p className="text-sm text-muted-foreground">Nenhum produto encontrado.</p> : null}
        </div>
      )}
    </Field>
  )
}

/**
 * Novo pedido (VND-02a CA 1): cliente pela busca (com "Cadastrar cliente"), linhas pela consulta rápida com o disponível de cada uma, desconto e total
 * (prévia; o servidor recalcula com o preço da tabela). Local de estoque: o padrão. ?de= reaproveita os itens de um pedido cancelado ("Recriar pedido").
 */
export function NewOrderPage() {
  const navigate = useNavigate()
  const search = useSearch({ strict: false }) as { de?: string }
  const idempotency = useIdempotencyKey()
  const [customer, setCustomer] = useState<SalesOrderCustomerOption | null>(null)
  const [lines, setLines] = useState<DraftLine[]>([])
  const [orderDiscount, setOrderDiscount] = useState<string | null>(null)
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string>()

  useEffect(() => {
    if (!search.de) return
    void getSalesOrder(search.de).then((o) => {
      setCustomer({ id: o.customerId, name: o.customerName, document: null })
      setLines(o.lines.map((l) => ({ productId: l.productId, description: l.description, unitCode: l.unitCode, price: Number(l.unitPrice), available: Number(l.quantity), quantity: Number(l.quantity), discount: Number(l.discount) })))
    }).catch(() => undefined)
  }, [search.de])

  const add = (p: SalesOrderProductOption) => setLines((prev) => prev.some((l) => l.productId === p.productId)
    ? prev.map((l) => (l.productId === p.productId ? { ...l, quantity: l.quantity + 1 } : l))
    : [...prev, { productId: p.productId, description: p.description, unitCode: p.unitCode, price: Number(p.price ?? 0), available: Number(p.available), quantity: 1, discount: 0 }])
  const patch = (i: number, change: Partial<DraftLine>) => setLines((prev) => prev.map((l, n) => (n === i ? { ...l, ...change } : l)))
  const totals = draftTotals(lines, toNumber(orderDiscount))
  const short = shortLines(lines)

  const save = async () => {
    if (!customer || lines.length === 0) return
    setBusy(true)
    setError(undefined)
    try {
      const created = await createSalesOrder({
        customerId: customer.id, orderDiscount: toNumber(orderDiscount), note: note.trim() || null,
        lines: lines.map((l) => ({ productId: l.productId, quantity: l.quantity, discount: l.discount })),
      }, idempotency.options)
      idempotency.renew()
      notifySuccess(`Pedido ${created.number} salvo como rascunho`)
      void navigate({ to: '/app/loja/pedidos/$id' as never, params: { id: created.id } as never })
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar o pedido. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <PageHeading title="Novo pedido" description="O pedido nasce como rascunho. Confirme depois para reservar o estoque." />
      <Link to={'/app/loja/pedidos' as never} className="mb-4 inline-flex items-center gap-1 text-sm font-semibold text-primary"><ArrowLeft aria-hidden="true" className="size-4" />Pedidos</Link>
      <div className="max-w-3xl space-y-4">
        <CustomerPicker value={customer} onChange={setCustomer} />
        <ProductSearch onPick={add} />
        {lines.length === 0 ? <p className="rounded-md border border-dashed border-border p-4 text-center text-sm text-muted-foreground">Nenhum item ainda. Busque o produto acima.</p> : (
          <ul className="divide-y divide-border rounded-md border border-border bg-card" aria-label="Itens do pedido">
            {lines.map((l, i) => (
              <li key={l.productId} className="grid gap-2 p-3 sm:grid-cols-[1fr_7rem_8rem_auto] sm:items-end">
                <div className="min-w-0 text-sm">
                  <b className="block truncate">{l.description}</b>
                  <span className="text-muted-foreground tabular-nums"><Money value={l.price} /> / {l.unitCode} · disponível {qty(l.available)}</span>
                  {l.quantity > l.available ? <StatusChip tone="warn" className="ml-1">Acima do disponível</StatusChip> : null}
                </div>
                <Field label="Quantidade">{(p) => <input {...p} inputMode="decimal" value={String(l.quantity).replace('.', ',')} onChange={(e) => patch(i, { quantity: Number(e.target.value.replace(',', '.')) || 0 })} className={inputClass + ' tabular-nums'} />}</Field>
                <Field label="Desconto">{(p) => <MoneyInput {...p} value={l.discount ? String(l.discount) : null} onChange={(v) => patch(i, { discount: toNumber(v) })} />}</Field>
                <button type="button" onClick={() => setLines((prev) => prev.filter((_, n) => n !== i))} aria-label={`Tirar ${l.description}`} className="inline-flex min-h-11 items-center justify-center rounded-md border border-border px-3"><Trash2 aria-hidden="true" className="size-4" /></button>
              </li>
            ))}
          </ul>
        )}
        <div className="grid gap-3 sm:grid-cols-2">
          <Field label="Desconto do pedido">{(p) => <MoneyInput {...p} value={orderDiscount} onChange={setOrderDiscount} />}</Field>
          <Field label="Observação">{(p) => <input {...p} maxLength={500} value={note} onChange={(e) => setNote(e.target.value)} className={inputClass} />}</Field>
        </div>
        <dl className="grid grid-cols-2 gap-1 rounded-md border border-border bg-card p-3 text-sm">
          <dt>Subtotal</dt><dd className="text-right"><Money value={totals.subtotal} /></dd>
          <dt>Descontos</dt><dd className="text-right"><Money value={-totals.discounts} /></dd>
          <dt className="font-semibold">Total</dt><dd className="text-right text-lg font-semibold"><Money value={totals.total} /></dd>
        </dl>
        <p className="text-sm text-muted-foreground">Local de estoque: o padrão do estoque.<Dica nome="Reservado" texto={ORDER_DICAS.reservado} /></p>
        {short.length > 0 ? <p role="status" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">Há itens acima do disponível. O rascunho salva, mas a confirmação só reserva com saldo para tudo.</p> : null}
        {error ? <p role="alert" className="text-sm text-crit-ink">{error}</p> : null}
        <button type="button" disabled={busy || !customer || lines.length === 0} onClick={() => void save()} className="min-h-11 rounded-md bg-primary px-4 text-sm font-semibold text-primary-foreground disabled:opacity-60">
          {busy ? 'Salvando…' : 'Salvar rascunho'}
        </button>
      </div>
    </>
  )
}
