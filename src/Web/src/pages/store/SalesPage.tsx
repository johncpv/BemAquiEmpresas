import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { X } from 'lucide-react'
import { useCallback } from 'react'
import { getExportSalesUrl, useListPaymentMethods, useListSales, useListStores } from '@/api/generated/bemaqui'
import type { SaleListItem, SaleListState } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableSearch } from '@/components/data-table/table-search'
import { Field, FormGrid } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { ExportMenu } from '@/components/ui/export-menu'
import { Money } from '@/components/ui/money'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { todayInBrazil } from './pos-pending'
import { DICAS, isSaleState, saleChips, STATE_OPTIONS } from './sales'

type FilterKey = 'de' | 'ate' | 'loja' | 'caixa' | 'operador' | 'forma' | 'estado'
type Filters = Partial<Record<FilterKey, string>>

const text = (v: unknown) => (typeof v === 'string' && v ? v : undefined)

/** Filtros da lista na URL (CA 2): link compartilhável; mudar um filtro volta para a página 1. Sem período, hoje. */
function useSalesFilters(): [Filters & { de: string; ate: string }, (patch: Filters) => void] {
  const raw = useSearch({ strict: false }) as Record<string, unknown>
  const navigate = useNavigate()
  const today = todayInBrazil()
  const filters = {
    de: text(raw.de) ?? today, ate: text(raw.ate) ?? today, loja: text(raw.loja), caixa: text(raw.caixa), operador: text(raw.operador), forma: text(raw.forma),
    estado: isSaleState(raw.estado) ? raw.estado : undefined,
  }
  const set = useCallback((patch: Filters) => {
    void navigate({
      to: '.', replace: true,
      search: (prev: Record<string, unknown>) => {
        const next = { ...prev, ...patch } as Record<string, unknown>
        delete next.page
        for (const key of Object.keys(next)) if (next[key] === undefined || next[key] === '') delete next[key]
        return next
      },
    } as never)
  }, [navigate])
  return [filters, set]
}

const dateTime = (iso: string) => new Date(iso).toLocaleString('pt-BR', { timeZone: 'America/Sao_Paulo', day: '2-digit', month: '2-digit', year: '2-digit', hour: '2-digit', minute: '2-digit' })

/**
 * Loja → Vendas (PDV-07 CA 1 a 3 e 5 a 7): a lista com número, data e hora, loja, caixa, operador, total, formas e os chips; filtros na URL; totais
 * do filtro no rodapé; exportação com o mesmo filtro. Margem só com sales.view_cost (sem ela, a API nem manda). O CPF nunca aparece aqui.
 */
export function SalesPage() {
  const [search] = useTableSearch()
  const [f, setFilters] = useSalesFilters()
  const seeCost = useCan('sales.view_cost')
  const seeMethods = useCan('finance.view')
  const stores = useListStores()
  const methods = useListPaymentMethods({ pageSize: 100 }, { query: { enabled: seeMethods } })
  const valid = f.de <= f.ate
  const params = {
    From: f.de, To: f.ate, StoreId: f.loja, RegisterId: f.caixa, OperatorId: f.operador, PaymentMethodId: f.forma, State: f.estado as SaleListState | undefined,
  }
  const query = useListSales({ ...params, Page: search.page, PageSize: search.pageSize, Sort: search.sort }, { query: { enabled: valid } })
  const registers = (stores.data ?? []).filter((s) => !f.loja || s.id === f.loja)
    .flatMap((s) => s.registers.map((r) => ({ id: r.id, label: `${s.name} · Caixa ${String(r.number).padStart(2, '0')}` })))
  const operatorName = f.operador ? query.data?.items.find((i) => i.operatorId === f.operador)?.operatorName ?? 'o operador escolhido' : null
  const totals = query.data?.totals

  const columns: ColumnDef<SaleListItem>[] = [
    {
      id: 'number', header: 'Venda', meta: { sortField: 'number' },
      cell: ({ row: { original: s } }) => <Link to="/app/loja/vendas/$id" params={{ id: s.id }} className="font-semibold tabular-nums text-primary hover:underline">{s.number}</Link>,
    },
    { id: 'at', header: 'Quando', meta: { sortField: 'at' }, cell: ({ row: { original: s } }) => <span className="tabular-nums">{dateTime(s.at)}</span> },
    { id: 'store', header: 'Loja', meta: { secondary: true }, cell: ({ row: { original: s } }) => s.storeName ?? 'Sem loja' },
    { id: 'register', header: 'Caixa', meta: { secondary: true }, cell: ({ row: { original: s } }) => s.registerLabel ?? '—' },
    {
      id: 'operator', header: 'Operador', meta: { secondary: true },
      cell: ({ row: { original: s } }) => (s.operatorId && s.operatorName ? (
        <button type="button" onClick={() => setFilters({ operador: s.operatorId ?? undefined })} className="text-left hover:underline" title="Ver só as vendas deste operador">{s.operatorName}</button>
      ) : '—'),
    },
    { id: 'methods', header: 'Formas', meta: { secondary: true }, cell: ({ row: { original: s } }) => s.paymentMethods.join(', ') || '—' },
    {
      id: 'state', header: 'Estado',
      cell: ({ row: { original: s } }) => <span className="flex flex-wrap gap-1">{saleChips(s).map((c) => <StatusChip key={c.label} tone={c.tone}>{c.label}</StatusChip>)}</span>,
    },
    { id: 'total', header: 'Total', meta: { numeric: true, sortField: 'total' }, cell: ({ row: { original: s } }) => <Money value={Number(s.total)} /> },
    ...(seeCost ? [{
      id: 'margin', header: 'Margem', meta: { numeric: true, secondary: true },
      cell: ({ row: { original: s } }) => (s.margin == null ? '—' : <Money value={Number(s.margin)} highlightNegative />),
    } as ColumnDef<SaleListItem>] : []),
  ]

  return (
    <>
      <PageHeading title="Vendas" description="Todas as vendas das lojas: filtre pelo período, pela loja, pelo caixa e pelo estado, e abra uma venda para ver tudo o que aconteceu com ela." />
      <FormGrid>
        <Field label="De" required size="sm" dica="Data da venda, no horário de Brasília. Escolha até um ano por vez.">
          {(p) => <input {...p} type="date" value={f.de} onChange={(e) => setFilters({ de: e.target.value })} className={inputClass} />}
        </Field>
        <Field label="Até" required size="sm">
          {(p) => <input {...p} type="date" value={f.ate} onChange={(e) => setFilters({ ate: e.target.value })} className={inputClass} />}
        </Field>
        <Field label="Loja" size="md">
          {(p) => (
            <select {...p} value={f.loja ?? ''} onChange={(e) => setFilters({ loja: e.target.value, caixa: undefined })} className={inputClass}>
              <option value="">Todas as lojas</option>
              {(stores.data ?? []).map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
            </select>
          )}
        </Field>
        <Field label="Caixa" size="md">
          {(p) => (
            <select {...p} value={f.caixa ?? ''} onChange={(e) => setFilters({ caixa: e.target.value })} className={inputClass}>
              <option value="">Todos os caixas</option>
              {registers.map((r) => <option key={r.id} value={r.id}>{r.label}</option>)}
            </select>
          )}
        </Field>
        {seeMethods ? (
          <Field label="Forma de pagamento" size="md">
            {(p) => (
              <select {...p} value={f.forma ?? ''} onChange={(e) => setFilters({ forma: e.target.value })} className={inputClass}>
                <option value="">Todas as formas</option>
                {(methods.data?.items ?? []).map((m) => <option key={m.id} value={m.id}>{m.name}</option>)}
              </select>
            )}
          </Field>
        ) : null}
        <Field label="Estado" size="md">
          {(p) => (
            <select {...p} value={f.estado ?? ''} onChange={(e) => setFilters({ estado: e.target.value })} className={inputClass}>
              <option value="">Todos</option>
              {STATE_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
            </select>
          )}
        </Field>
      </FormGrid>
      {operatorName ? (
        <p className="mt-2 flex items-center gap-2 text-sm">
          Só as vendas de <b>{operatorName}</b>
          <button type="button" onClick={() => setFilters({ operador: undefined })} className="inline-flex min-h-9 items-center gap-1 rounded-md border border-border px-2 font-semibold">
            <X aria-hidden="true" className="size-4" />Tirar o filtro
          </button>
        </p>
      ) : null}
      <p className="mt-2 text-sm text-muted-foreground">
        Os chips mostram o que merece atenção: <b>Com divergência</b><Dica nome="Com divergência" texto={DICAS.divergencia} /> e <b>Feita sem internet</b><Dica nome="Feita sem internet" texto={DICAS.semInternet} />.
      </p>
      <div className="mt-4">
        {!valid ? <p role="alert" className="text-sm text-crit-ink">A data final tem de ser igual ou depois da inicial.</p> : (
          <DataTable
            label="Vendas"
            columns={columns}
            query={query}
            getRowId={(s) => s.id}
            rowHref={(s) => `/app/loja/vendas/${s.id}`}
            searchable={false}
            rowClassName={(s) => (s.hasDivergence ? 'border-l-4 border-l-crit' : s.stockStatus === 'Shortfall' ? 'border-l-4 border-l-warn' : undefined)}
            toolbar={<ExportMenu formats={['xlsx', 'pdf']} fileBase="vendas" empty={query.data?.total === 0} url={(format) => getExportSalesUrl({ ...params, format })} />}
            totals={totals ? {
              number: <span className="tabular-nums">{totals.count} {totals.count === 1 ? 'venda' : 'vendas'}{totals.cancelledCount ? ` (${totals.cancelledCount} cancelada${totals.cancelledCount === 1 ? '' : 's'})` : ''}</span>,
              total: <Money value={Number(totals.total)} />,
              ...(seeCost && totals.margin != null ? { margin: <Money value={Number(totals.margin)} highlightNegative /> } : {}),
            } : undefined}
            empty={{
              title: 'Nenhuma venda no período.',
              description: 'As vendas aparecem aqui assim que os caixas enviam. Mude o período ou os filtros, ou abra o PDV para vender.',
              action: <a href="/pdv" className="inline-flex min-h-11 items-center rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Abrir o PDV</a>,
            }}
          />
        )}
      </div>
    </>
  )
}
