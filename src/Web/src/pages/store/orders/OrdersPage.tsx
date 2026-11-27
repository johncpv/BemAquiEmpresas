import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { Plus } from 'lucide-react'
import { useCallback } from 'react'
import { useListSalesOrders } from '@/api/generated/bemaqui'
import type { SalesOrderListItem, SalesOrderNextStep, SalesOrderOrigin } from '@/api/generated/model'
import { Field, FormGrid } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { Money } from '@/components/ui/money'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { cn } from '@/lib/utils'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { isNextStep, NEXT_STEPS, nextStepLabel, ORDER_DICAS, ORIGIN_LABEL, PROBLEMS, statusChip } from './orders'

type Filters = { passo?: string; origem?: string; de?: string; ate?: string; parados?: string; q?: string; app?: string }

const text = (v: unknown) => (typeof v === 'string' && v ? v : undefined)
const dateTime = (iso: string) => new Date(iso).toLocaleString('pt-BR', { timeZone: 'America/Sao_Paulo', day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })

function useOrderFilters(): [Filters, (patch: Filters) => void] {
  const raw = useSearch({ strict: false }) as Record<string, unknown>
  const navigate = useNavigate()
  const filters: Filters = { passo: text(raw.passo), origem: text(raw.origem), de: text(raw.de), ate: text(raw.ate), parados: text(raw.parados), q: text(raw.q), app: text(raw.app) }
  const set = useCallback((patch: Filters) => {
    void navigate({
      to: '.', replace: true,
      search: (prev: Record<string, unknown>) => {
        const next = { ...prev, ...patch } as Record<string, unknown>
        for (const key of Object.keys(next)) if (next[key] === undefined || next[key] === '') delete next[key]
        return next
      },
    } as never)
  }, [navigate])
  return [filters, set]
}

/** Faixa lateral do cartão: vermelho com problema crítico, âmbar com o prazo de pagamento perto de acabar. */
function stripe(o: SalesOrderListItem) {
  if (o.problems.some((p) => PROBLEMS[p]?.tone === 'crit')) return 'border-l-4 border-l-crit'
  if (o.late || o.problems.length > 0) return 'border-l-4 border-l-warn'
  return ''
}

function OrderCard({ o }: { o: SalesOrderListItem }) {
  const chip = statusChip(o.status)
  const next = nextStepLabel(o.nextStep)
  return (
    <li className={cn('rounded-md border border-border bg-card p-3', stripe(o))}>
      <div className="flex flex-wrap items-start justify-between gap-2">
        <Link to={'/app/loja/pedidos/$id' as never} params={{ id: o.id } as never} className="font-semibold text-primary hover:underline tabular-nums">{o.number}</Link>
        <span className="text-lg font-semibold"><Money value={Number(o.total)} /></span>
      </div>
      <p className="truncate text-sm">{o.customerName}</p>
      <div className="mt-1 flex flex-wrap items-center gap-1 text-xs text-muted-foreground">
        <StatusChip tone={chip.tone}>{chip.label}</StatusChip>
        {o.problems.map((p) => <StatusChip key={p} tone={PROBLEMS[p]?.tone ?? 'warn'}>{PROBLEMS[p]?.label ?? p}</StatusChip>)}
        {o.origin !== 'Backoffice' ? <StatusChip tone="info">{o.sourceAppName ?? ORIGIN_LABEL[o.origin]}</StatusChip> : null}
        <span className="tabular-nums">{dateTime(o.updatedAt)}</span>
        {o.late ? <span className="text-warn-ink">· prazo de pagamento perto do fim</span> : null}
      </div>
      {next && o.status !== 'Cancelled' ? <p className="mt-1 text-sm">Próximo passo: <b>{next}</b></p> : null}
    </li>
  )
}

/**
 * Loja → Pedidos (VND-02a): os pedidos agrupados pelo próximo passo (confirmar, cobrar, aguardando pagamento, pagos) com a contagem de cada grupo,
 * filtros na URL (origem, período, parados há mais de N dias), cartões com faixa lateral para atraso e problema (a 400 px, sem rolagem lateral).
 * Carregando, vazio com "Novo pedido" e erro com "Tentar de novo".
 */
export function OrdersPage() {
  const [f, set] = useOrderFilters()
  const canCreate = useCan('sales.create')
  const step = isNextStep(f.passo) ? (f.passo as SalesOrderNextStep) : undefined
  const query = useListSalesOrders({
    Step: step, Origin: (f.origem as SalesOrderOrigin | undefined) ?? undefined, App: f.app, From: f.de, To: f.ate, StaleDays: f.parados ? Number(f.parados) : undefined, Q: f.q,
    IncludeCancelled: f.passo === 'cancelados' ? true : undefined,
  })
  const newButton = canCreate
    ? <Link to={'/app/loja/pedidos/novo' as never} className="inline-flex min-h-11 items-center gap-1 rounded-md bg-primary px-3 text-sm font-semibold text-primary-foreground"><Plus aria-hidden="true" className="size-4" />Novo pedido</Link>
    : null
  const counts = query.data?.counts ?? []

  return (
    <>
      <PageHeading title="Pedidos" description="Pedidos de venda até o pagamento: do telefone, do balcão e dos aplicativos conectados." actions={newButton} />
      <nav aria-label="Próximo passo" className="mb-3 flex flex-wrap gap-2">
        <button type="button" onClick={() => set({ passo: undefined })} aria-pressed={!f.passo}
          className={cn('min-h-11 rounded-md border px-3 text-sm font-semibold', !f.passo ? 'border-primary bg-primary text-primary-foreground' : 'border-border bg-card')}>Todos</button>
        {NEXT_STEPS.map((s) => {
          const count = counts.find((c) => c.step === s.step)?.count ?? 0
          const active = f.passo === s.step
          return (
            <button key={s.step} type="button" onClick={() => set({ passo: s.step })} aria-pressed={active}
              className={cn('min-h-11 rounded-md border px-3 text-sm font-semibold', active ? 'border-primary bg-primary text-primary-foreground' : 'border-border bg-card')}>
              {s.label} <span className="tabular-nums">({count})</span>
            </button>
          )
        })}
        <button type="button" onClick={() => set({ passo: 'cancelados' })} aria-pressed={f.passo === 'cancelados'}
          className={cn('min-h-11 rounded-md border px-3 text-sm font-semibold', f.passo === 'cancelados' ? 'border-primary bg-primary text-primary-foreground' : 'border-border bg-card')}>Com cancelados</button>
      </nav>
      <FormGrid>
        <Field label="Buscar" size="md">{(p) => <input {...p} value={f.q ?? ''} placeholder="Número ou id externo" onChange={(e) => set({ q: e.target.value })} className={inputClass} />}</Field>
        <Field label="Origem" size="md">
          {(p) => (
            <select {...p} value={f.origem ?? ''} onChange={(e) => set({ origem: e.target.value || undefined })} className={inputClass}>
              <option value="">Todas</option>
              {Object.entries(ORIGIN_LABEL).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
            </select>
          )}
        </Field>
        <Field label="De" size="sm">{(p) => <input {...p} type="date" value={f.de ?? ''} onChange={(e) => set({ de: e.target.value })} className={inputClass} />}</Field>
        <Field label="Até" size="sm">{(p) => <input {...p} type="date" value={f.ate ?? ''} onChange={(e) => set({ ate: e.target.value })} className={inputClass} />}</Field>
        <Field label="Parados há mais de (dias)" size="sm" dica={ORDER_DICAS.prazo}>
          {(p) => <input {...p} inputMode="numeric" maxLength={3} value={f.parados ?? ''} onChange={(e) => set({ parados: e.target.value.replace(/[^0-9]/g, '') })} className={inputClass + ' tabular-nums'} />}
        </Field>
      </FormGrid>
      <div className="mt-4">
        {query.isPending ? <div className="space-y-2" aria-busy="true"><Skeleton className="h-20" /><Skeleton className="h-20" /><Skeleton className="h-20" /></div>
          : query.isError ? <ErrorState title="Não foi possível carregar os pedidos." message="Confira a conexão e tente de novo." onRetry={() => void query.refetch()} />
            : query.data.items.length === 0 ? (
              <EmptyState title="Nenhum pedido ainda." description={Object.values(f).some(Boolean) ? 'Nenhum pedido neste filtro. Tire um filtro para ver mais.' : 'Registre o pedido de um cliente: o estoque fica reservado até o pagamento.'}
                action={newButton} />
            ) : (
              <>
                <p className="mb-2 text-sm text-muted-foreground tabular-nums">{query.data.total} {query.data.total === 1 ? 'pedido' : 'pedidos'}
                  {query.data.total > query.data.items.length ? ` (mostrando os ${query.data.items.length} mais recentes)` : ''}
                  <Dica nome="Reservado" texto={ORDER_DICAS.reservado} /></p>
                <ul className="grid gap-2 md:grid-cols-2 xl:grid-cols-3" aria-label="Pedidos">{query.data.items.map((o) => <OrderCard key={o.id} o={o} />)}</ul>
              </>
            )}
      </div>
    </>
  )
}
