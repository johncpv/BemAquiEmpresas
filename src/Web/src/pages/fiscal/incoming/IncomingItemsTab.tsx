import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useState } from 'react'
import { listIncomingAccountOptions, listIncomingItemOptions, mapIncomingInvoiceItem } from '@/api/generated/bemaqui'
import type { IncomingInvoiceItemView, IncomingInvoiceView, IncomingItemMappingRequest } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Combobox } from '@/components/form/combobox'
import { inputClass } from '@/components/form/form-utils'
import { Money } from '@/components/ui/money'
import { EmptyState } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { cn } from '@/lib/utils'
import { DICAS, formatPrice, formatQty } from './incoming'

const small = 'inline-flex min-h-11 items-center rounded-md border border-border bg-card px-3 py-1.5 text-sm font-semibold disabled:opacity-50'

/**
 * Itens e de-para (FIS-04a CA 7 e 8): cada item da nota em cartão, com o item do estoque "Lembrado" ou "Sugerido", a busca, "Sem item de
 * estoque" com a conta do plano e o fator de conversão (obrigatório quando a unidade do fornecedor não tem conversão).
 */
export function IncomingItemsTab({ view, onChange }: { view: IncomingInvoiceView; onChange: (v: IncomingInvoiceView) => Promise<void> }) {
  const editable = view.status === 'Imported' && view.can.import
  if (view.items.length === 0) return <EmptyState title="A nota não tem itens." />
  return (
    <div className="space-y-3">
      <p className="flex flex-wrap items-center gap-1 text-sm text-muted-foreground">
        De-para<Dica nome="De-para" texto={DICAS.dePara} /> ·
        {view.pendingItems > 0
          ? <span className="font-semibold text-warn-ink">{view.pendingItems === 1 ? 'Falta ligar 1 item' : `Faltam ligar ${view.pendingItems} itens`}</span>
          : <span className="font-semibold text-ok-ink">Todos os itens ligados</span>}
      </p>
      <ul className="space-y-3">
        {view.items.map((item) => <ItemCard key={item.id} invoiceId={view.id} item={item} editable={editable} canCreateItem={view.can.createItem} onChange={onChange} />)}
      </ul>
    </div>
  )
}

function ItemCard({ invoiceId, item, editable, canCreateItem, onChange }: {
  invoiceId: string; item: IncomingInvoiceItemView; editable: boolean; canCreateItem: boolean; onChange: (v: IncomingInvoiceView) => Promise<void>
}) {
  const [editing, setEditing] = useState(false)
  const [factor, setFactor] = useState(item.conversionFactor != null ? String(item.conversionFactor) : '')
  const [noStock, setNoStock] = useState(item.noStockItem)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const accounts = useQuery({ queryKey: ['incoming-account-options'], queryFn: () => listIncomingAccountOptions(), enabled: editable && noStock, staleTime: 60_000 })
  const mapped = item.itemId || item.noStockItem
  const showEditor = editable && (!mapped || editing || item.factorRequired)

  const save = async (body: IncomingItemMappingRequest) => {
    setBusy(true)
    setError(null)
    try {
      await onChange(await mapIncomingInvoiceItem(invoiceId, item.id, body))
      setEditing(false)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar o de-para. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }
  const parsedFactor = factor.trim() ? Number(factor.replace(',', '.')) : null
  const pickItem = (itemId: string, suggested = false) => save({ itemId, conversionFactor: null, noStockItem: false, expenseAccountId: null, poLineId: null, suggested })

  return (
    <li className={cn('rounded-md border border-border bg-card p-3 text-sm', !item.resolved && 'border-l-4 border-l-warn')}>
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="font-semibold"><span className="tabular-nums">{item.sequence}.</span> {item.description}</p>
          <p className="text-xs text-muted-foreground">
            Código do fornecedor <span className="tabular-nums">{item.supplierCode}</span>
            {item.ncm ? <> · NCM <span className="tabular-nums">{item.ncm}</span></> : null}
            {item.cfop ? <> · CFOP <span className="tabular-nums">{item.cfop}</span></> : null}
            {item.lotNumber ? <> · lote {item.lotNumber}</> : null}
          </p>
        </div>
        <p className="text-right tabular-nums">
          {formatQty(item.quantity)} {item.unit} × {formatPrice(item.unitPrice)} = <Money value={item.amount} className="font-semibold" />
        </p>
      </div>

      <div className="mt-2 rounded-md bg-muted p-2">
        {item.noStockItem ? (
          <p className="flex flex-wrap items-center gap-2"><Tag>Sem item de estoque</Tag><Dica nome="Sem item de estoque" texto={DICAS.semItem} /> Conta: {item.expenseAccountName ?? '—'}</p>
        ) : item.itemId ? (
          <div className="space-y-1">
            <p className="flex flex-wrap items-center gap-2">
              <span>Nosso item: <b className="font-semibold">{item.itemCode} — {item.itemName}</b></span>
              {item.mappingSource === 'Remembered' ? <><Tag>Lembrado</Tag><Dica nome="Lembrado" texto={DICAS.lembrado} /></> : null}
              {item.mappingSource === 'Suggested' ? <Tag>Sugerido</Tag> : null}
            </p>
            {item.stockQuantity != null && item.stockUnitPrice != null ? (
              <p className="tabular-nums text-muted-foreground">
                Em unidade de estoque: {formatQty(item.stockQuantity)} {item.stockUnit} × {formatPrice(item.stockUnitPrice)}
                {item.conversionFactor != null && item.conversionFactor !== 1 ? <> · fator {formatQty(item.conversionFactor)}</> : null}
              </p>
            ) : <p className="font-semibold text-warn-ink">Falta o fator de conversão de {item.unit} para {item.stockUnit}.</p>}
          </div>
        ) : <p className="font-semibold text-warn-ink">Sem item do estoque ligado.</p>}

        {editable && mapped && !editing && !item.factorRequired ? (
          <button type="button" onClick={() => setEditing(true)} className={cn(small, 'mt-2')}>Trocar</button>
        ) : null}
      </div>

      {showEditor ? (
        <div className="mt-2 space-y-2">
          {item.itemId && !item.noStockItem ? (
            <div className="flex flex-wrap items-end gap-2">
              <div>
                <label htmlFor={`fator-${item.id}`} className="flex items-center gap-1 text-xs font-medium">Fator de conversão (1 {item.unit} = ? {item.stockUnit})<Dica nome="Fator de conversão" texto={DICAS.fator} /></label>
                <input id={`fator-${item.id}`} inputMode="decimal" value={factor} onChange={(e) => setFactor(e.target.value)} className={cn(inputClass, 'w-40 tabular-nums')} />
              </div>
              <button type="button" disabled={busy || !parsedFactor || parsedFactor <= 0} className={small}
                onClick={() => void save({ itemId: item.itemId!, conversionFactor: parsedFactor, noStockItem: false, expenseAccountId: null, poLineId: item.poLineId ?? null })}>
                Salvar fator
              </button>
            </div>
          ) : null}

          {item.suggestions.length > 0 && !item.itemId ? (
            <div>
              <p className="flex items-center gap-1 text-xs font-medium">Sugestões<Dica nome="Sugerido" texto={DICAS.sugerido} /></p>
              <ul className="mt-1 space-y-1">
                {item.suggestions.map((s) => (
                  <li key={s.itemId} className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border px-2 py-1">
                    <span className="flex flex-wrap items-center gap-2"><Tag>Sugerido</Tag>{s.code} — {s.name} <span className="text-xs text-muted-foreground">{s.unitCode}{s.sameNcm ? ' · mesmo NCM' : ''}</span></span>
                    <button type="button" disabled={busy} onClick={() => void pickItem(s.itemId, true)} className={small}>Usar</button>
                  </li>
                ))}
              </ul>
            </div>
          ) : null}

          <div className="grid gap-2 sm:grid-cols-[1fr_auto]">
            <div>
              <span className="text-xs font-medium" id={`busca-${item.id}`}>Item do estoque</span>
              <Combobox aria-label="Item do estoque" queryKey={['incoming-item-options']} placeholder="Buscar pelo código ou nome"
                search={async (term) => (await listIncomingItemOptions({ q: term })).map((o) => ({ id: o.id, label: `${o.code} — ${o.name}`, hint: o.unitCode }))}
                onChange={(o) => { if (o) void pickItem(o.id) }} disabled={busy} />
            </div>
            {canCreateItem ? <Link to="/app/estoque/itens/novo" target="_blank" className={cn(small, 'self-end')}>Cadastrar item</Link> : null}
          </div>

          <div className="space-y-1">
            <label className="flex min-h-11 items-center gap-2">
              <input type="checkbox" checked={noStock} onChange={(e) => setNoStock(e.target.checked)} className="size-5" />
              Sem item de estoque<Dica nome="Sem item de estoque" texto={DICAS.semItem} />
            </label>
            {noStock ? (
              <select aria-label="Conta de despesa" disabled={busy || accounts.isPending} defaultValue={item.expenseAccountId ?? ''}
                onChange={(e) => { if (e.target.value) void save({ itemId: null, conversionFactor: null, noStockItem: true, expenseAccountId: e.target.value, poLineId: null }) }}
                className={inputClass}>
                <option value="">{accounts.isPending ? 'Carregando as contas…' : accounts.isError ? 'Não foi possível carregar as contas' : 'Escolha a conta de despesa'}</option>
                {(accounts.data ?? []).map((a) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}
              </select>
            ) : null}
          </div>
          {editing ? <button type="button" onClick={() => setEditing(false)} className={small}>Cancelar</button> : null}
        </div>
      ) : null}
      {!item.resolved && !editable ? <StatusChip tone="warn" className="mt-2">Item sem de-para</StatusChip> : null}
      {error ? <p role="alert" className="mt-2 text-crit-ink">{error}</p> : null}
    </li>
  )
}
