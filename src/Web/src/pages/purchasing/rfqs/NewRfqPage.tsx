import { useNavigate, useSearch } from '@tanstack/react-router'
import { X } from 'lucide-react'
import { useRef, useState } from 'react'
import { createRfq, listPurchaseItemOptions, listPurchaseSupplierOptions, useGetRfqDefaults } from '@/api/generated/bemaqui'
import type { PurchaseItemOption, RfqDefaults } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { Combobox, type ComboOption } from '@/components/form/combobox'
import { Field, FormError, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { QuantityInput } from '@/components/form/inputs'
import { RowsEditor } from '@/components/form/rows-editor'
import { notifySuccess } from '@/components/ui/messages'
import { Money } from '@/components/ui/money'
import { ErrorState, InlineError, Skeleton } from '@/components/ui/states'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { formatInstantDate } from '@/lib/format'
import { formatQuantity } from '@/pages/inventory/stock/stock'
import { PageHeading } from '@/shell/PageHeading'
import { idsFromSearch, RFQ_DICAS } from './rfqs'

interface Supplier { id: string; label: string; suggested: boolean }
interface TypedLine { item: ComboOption | null; quantity: string | null; unitId: string }

const itemLabel = (i: PurchaseItemOption) => `${i.code} — ${i.name}`

function RfqForm({ defaults, requestLineIds }: { defaults: RfqDefaults; requestLineIds: string[] }) {
  const navigate = useNavigate()
  const idempotency = useIdempotencyKey()
  const options = useRef(new Map<string, PurchaseItemOption>())
  const fromRequests = requestLineIds.length > 0
  const [suppliers, setSuppliers] = useState<Supplier[]>(defaults.suppliers.map((s) => ({ id: s.id, label: s.name, suggested: s.suggested })))
  const [lines, setLines] = useState<TypedLine[]>([{ item: null, quantity: null, unitId: '' }])
  const [notes, setNotes] = useState('')
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [failure, setFailure] = useState<string>()
  const [sending, setSending] = useState(false)

  const searchSuppliers = async (term: string): Promise<ComboOption[]> =>
    (await listPurchaseSupplierOptions({ q: term || undefined })).filter((s) => !suppliers.some((x) => x.id === s.id)).map((s) => ({ id: s.id, label: s.name, hint: s.document ?? undefined }))
  const searchItems = async (term: string): Promise<ComboOption[]> => {
    const found = await listPurchaseItemOptions({ q: term || undefined })
    for (const o of found) options.current.set(o.id, o)
    return found.map((o) => ({ id: o.id, label: itemLabel(o), hint: o.unitCode }))
  }

  const save = async () => {
    const missing: Record<string, string> = {}
    if (suppliers.length === 0) missing.supplierIds = 'Escolha pelo menos um fornecedor.'
    if (!fromRequests) {
      lines.forEach((l, i) => {
        if (!l.item) missing[`lines[${i}].itemId`] = 'Escolha o item.'
        if (!l.quantity || Number(l.quantity) <= 0) missing[`lines[${i}].quantity`] = 'Informe a quantidade, maior que zero.'
      })
    }
    setErrors(missing)
    setFailure(undefined)
    if (Object.keys(missing).length) return
    setSending(true)
    try {
      const saved = await createRfq({
        requestLineIds, supplierIds: suppliers.map((s) => s.id), notes: notes.trim() || null,
        lines: fromRequests ? [] : lines.map((l) => {
          const option = options.current.get(l.item!.id)
          return { itemId: l.item!.id, quantity: Number(l.quantity), unitId: !l.unitId || l.unitId === option?.unitId ? null : l.unitId }
        }),
      }, idempotency.options)
      idempotency.renew()
      notifySuccess('Cotação criada')
      void navigate({ to: '/app/compras/cotacoes/$id', params: { id: saved.id }, search: { criada: 1 } } as never)
    } catch (e) {
      if (e instanceof ApiError && Object.keys(e.fields).length) setErrors(Object.fromEntries(Object.entries(e.fields).map(([k, v]) => [k, v[0]])))
      setFailure(e instanceof ApiError ? e.message : 'Não foi possível criar a cotação. Tente de novo.')
    } finally {
      setSending(false)
    }
  }

  return (
    <div className="max-w-5xl space-y-6">
      <FormSection title="Itens" description={fromRequests ? 'As quantidades das solicitações somadas por item. Cada linha fica ligada às solicitações de origem.' : 'Compra pontual, sem solicitação: digite os itens e as quantidades.'}>
        <div className="sm:col-span-2">
          {fromRequests ? (
            <ul aria-label="Itens da cotação" className="divide-y divide-border rounded-md border border-border bg-card">
              {defaults.lines.map((l) => (
                <li key={l.item.id} className="flex flex-wrap items-baseline justify-between gap-2 px-3 py-2 text-sm">
                  <span><span className="font-mono">{l.item.code}</span> {l.item.name}<span className="block text-xs text-muted-foreground">Atende {l.requestNumbers.join(', ')}</span></span>
                  <span className="text-right">
                    <b className="tabular-nums">{formatQuantity(l.quantity)} {l.item.unitCode}</b>
                    {l.lastPurchase ? <span className="block text-xs text-muted-foreground">Última compra: <Money value={l.lastPurchase.unitPrice} /> em {formatInstantDate(l.lastPurchase.approvedAt)} de {l.lastPurchase.partyName}</span> : null}
                  </span>
                </li>
              ))}
            </ul>
          ) : (
            <RowsEditor<TypedLine> noun="item" rows={lines} onChange={setLines} min={1} max={50} newRow={() => ({ item: null, quantity: null, unitId: '' })}
              columns={[
                {
                  label: 'Item', width: 'minmax(0, 2fr)', render: (l, i, update) => (
                    <div>
                      <Combobox aria-label={`Item da linha ${i + 1}`} aria-required aria-invalid={!!errors[`lines[${i}].itemId`]} queryKey={['rfq-items']} search={searchItems} value={l.item?.id ?? null}
                        selected={l.item} placeholder="Código ou nome" onChange={(o) => update({ item: o, unitId: o ? options.current.get(o.id)?.unitId ?? '' : '' })} />
                      <InlineError id={`cot-linha-${i}-item`} message={errors[`lines[${i}].itemId`]} />
                    </div>
                  ),
                },
                {
                  label: 'Quantidade', width: '11rem', render: (l, i, update) => {
                    const option = l.item ? options.current.get(l.item.id) : undefined
                    return (
                      <div>
                        <span className="flex items-center gap-2">
                          <QuantityInput aria-label={`Quantidade da linha ${i + 1}`} aria-invalid={!!errors[`lines[${i}].quantity`]} value={l.quantity} onChange={(v) => update({ quantity: v })} />
                          {option && option.units.length > 1 ? (
                            <select aria-label={`Unidade da linha ${i + 1}`} value={l.unitId} onChange={(e) => update({ unitId: e.target.value })} className={`${inputClass} w-20`}>
                              {option.units.map((u) => <option key={u.unitId} value={u.unitId}>{u.code}</option>)}
                            </select>
                          ) : <span className="text-sm text-muted-foreground">{option?.unitCode}</span>}
                        </span>
                        <InlineError id={`cot-linha-${i}-qtd`} message={errors[`lines[${i}].quantity`] ?? errors[`lines[${i}].unitId`]} />
                      </div>
                    )
                  },
                },
              ]} />
          )}
        </div>
      </FormSection>

      <FormSection title="Fornecedores" description="Quem vai receber o pedido de preço. Você liga ou manda e-mail do seu jeito e registra a resposta aqui.">
        <div className="space-y-2 sm:col-span-2">
          {suppliers.length ? (
            <ul aria-label="Fornecedores da cotação" className="flex flex-wrap gap-2">
              {suppliers.map((s) => (
                <li key={s.id} className="inline-flex min-h-11 items-center gap-2 rounded-md border border-border bg-card px-3 py-1 text-sm">
                  {s.label}
                  {s.suggested ? <span className="inline-flex items-center"><Tag>Sugerido</Tag><Dica nome="Fornecedor sugerido" texto={RFQ_DICAS.fornecedoresSugeridos} /></span> : null}
                  <button type="button" aria-label={`Tirar ${s.label}`} onClick={() => setSuppliers((list) => list.filter((x) => x.id !== s.id))} className="grid size-8 place-items-center rounded-md hover:bg-muted">
                    <X aria-hidden="true" className="size-4" />
                  </button>
                </li>
              ))}
            </ul>
          ) : <p className="text-sm text-muted-foreground">Nenhum fornecedor ainda. Inclua pelo menos um.</p>}
          <Field label="Incluir fornecedor" error={errors.supplierIds}>
            {(p) => <Combobox {...p} queryKey={['rfq-suppliers', suppliers.length]} search={searchSuppliers} value={null} selected={null} placeholder="Nome ou documento"
              onChange={(o) => { if (o) setSuppliers((list) => (list.some((x) => x.id === o.id) ? list : [...list, { id: o.id, label: o.label, suggested: false }])) }} />}
          </Field>
        </div>
      </FormSection>

      <FormSection title="Observação" description="Opcional: o que o fornecedor precisa saber (prazo desejado, local de entrega).">
        <Field label="Observação (opcional)" error={errors.notes}>
          {(p) => <textarea {...p} value={notes} maxLength={500} rows={2} onChange={(e) => setNotes(e.target.value)} className={inputClass} />}
        </Field>
      </FormSection>

      <FormError message={failure} />
      <div className="flex flex-wrap justify-end gap-2">
        <button type="button" onClick={() => void navigate({ to: fromRequests ? '/app/compras/solicitacoes' : '/app/compras/cotacoes' } as never)} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
        <button type="button" disabled={sending} onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">{sending ? 'Criando…' : 'Criar cotação'}</button>
      </div>
    </div>
  )
}

/**
 * Fábrica → Compras → Cotações → Nova (CMP-02 CA 1): a partir das linhas de SC em aberto (<c>?sc=</c>, "Cotar estes itens"), com os
 * itens somados e os últimos fornecedores desses itens já escolhidos (tag "Sugerido"); sem SC, com os itens digitados.
 */
export function NewRfqPage() {
  const { sc } = useSearch({ strict: false }) as { sc?: string }
  const requestLineIds = idsFromSearch(sc)
  const defaults = useGetRfqDefaults({ requestLineIds: requestLineIds.length ? requestLineIds : undefined })
  return (
    <>
      <PageHeading title="Nova cotação">
        <p className="mt-2 text-muted-foreground">Escolha quem vai cotar. Depois registre as respostas, compare e gere os pedidos do vencedor.</p>
      </PageHeading>
      {defaults.isPending ? <Skeleton className="h-96 max-w-5xl" /> : defaults.isError || !defaults.data
        ? <ErrorState title="Não foi possível abrir a cotação." message="As solicitações escolhidas podem já estar em cotação, ter virado pedido ou sido canceladas. Volte às solicitações e tente de novo." onRetry={() => defaults.refetch()} />
        : <RfqForm defaults={defaults.data} requestLineIds={requestLineIds} />}
    </>
  )
}
