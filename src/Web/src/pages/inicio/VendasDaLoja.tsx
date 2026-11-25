import { Link } from '@tanstack/react-router'
import { AlertTriangle, ChevronRight } from 'lucide-react'
import type { ReactNode } from 'react'
import { useGetDashboardStoreSales } from '@/api/generated/bemaqui'
import type { StoreSalesCard } from '@/api/generated/model'
import { Money } from '@/components/ui/money'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { useHasProfile, useNavigation } from '@/navigation/navigation-context'
import { changeText, DICAS, openFor } from '../store/sales'
import { canIn } from '../use-can'

/** Perfil com os cards da loja: o Comércio. A fábrica (Konebras) não vê os cards nem faz a chamada (CA 11). */
const STORE_PROFILES = ['retail'] as const

function Card({ title, to, search, children }: { title: string; to: string; search?: Record<string, string>; children: ReactNode }) {
  return (
    <section className="flex flex-col rounded-md border border-border bg-card p-4" aria-label={title}>
      <h3 className="mb-2 font-semibold">
        <Link to={to as never} search={search as never} className="inline-flex items-center gap-1 hover:text-primary">
          {title}<ChevronRight aria-hidden="true" className="size-4 text-muted-foreground" />
        </Link>
      </h3>
      <div className="flex-1 text-sm">{children}</div>
    </section>
  )
}

/** Os três cards (CA 8 a 10), com os dados já carregados. */
export function VendasDaLojaCards({ card }: { card: StoreSalesCard }) {
  const change = changeText(card.changePct)
  return (
    <div className="grid gap-3 md:grid-cols-3">
      <Card title="Vendas de hoje" to="/app/loja/vendas">
        {card.todayCount === 0 ? <p className="text-muted-foreground">Nenhuma venda hoje ainda.</p> : (
          <>
            <p className="text-2xl font-semibold tabular-nums"><Money value={Number(card.todayAmount)} /></p>
            <p className="tabular-nums">{card.todayCount} {card.todayCount === 1 ? 'venda' : 'vendas'}</p>
          </>
        )}
        <p className="mt-1 text-muted-foreground tabular-nums">
          Mesmo dia da semana passada: <Money value={Number(card.lastWeekAmount)} /> ({card.lastWeekCount})
          {change ? <> · <span className={Number(card.changePct) < 0 ? 'text-crit-ink' : 'text-ok-ink'}>{change}</span></> : null}
        </p>
        {card.maybeUnsent ? (
          <p role="status" className="mt-2 flex items-start gap-1 text-warn-ink">
            <AlertTriangle aria-hidden="true" className="mt-0.5 size-4 flex-none" />
            <span>Pode haver vendas ainda não enviadas pelos caixas. <Link to="/app/loja/pendencias" className="font-semibold underline">Pendências do PDV</Link></span>
          </p>
        ) : null}
      </Card>
      <Card title="Caixas abertos" to="/app/loja/caixas" search={{ status: 'Open' }}>
        {card.openRegisters.length === 0 ? <p className="text-muted-foreground">Nenhum caixa aberto agora.</p> : (
          <>
            <p className="text-2xl font-semibold tabular-nums">{card.openRegisters.length}</p>
            <ul className="mt-1 space-y-1">
              {card.openRegisters.map((r) => (
                <li key={r.sessionId} className="flex flex-wrap items-center gap-x-2">
                  <span>{r.storeName} · Caixa {String(r.registerNumber).padStart(2, '0')}</span>
                  <span className="text-muted-foreground tabular-nums">{openFor(r.openMinutes)}</span>
                  {r.cancelAlert ? (
                    <span className="inline-flex items-center">
                      <StatusChip tone="crit">Cancelamentos acima de {Number(r.cancelAlertPercent).toLocaleString('pt-BR')}%</StatusChip>
                      <Dica nome="Alerta de cancelamentos" texto={`${DICAS.cancelamentos} Neste turno: ${Number(r.cancelledPercent).toLocaleString('pt-BR')}%.`} />
                    </span>
                  ) : null}
                </li>
              ))}
            </ul>
          </>
        )}
      </Card>
      <Card title="Ticket médio" to="/app/loja/vendas">
        {card.ticketToday == null ? <p className="text-muted-foreground">Nenhuma venda hoje ainda.</p> : (
          <p className="text-2xl font-semibold tabular-nums"><Money value={Number(card.ticketToday)} /></p>
        )}
        <p className="mt-1 text-muted-foreground tabular-nums">
          Últimos 7 dias: {card.ticketWeek == null ? '—' : <Money value={Number(card.ticketWeek)} />} ({card.weekCount} {card.weekCount === 1 ? 'venda' : 'vendas'})
        </p>
      </Card>
    </div>
  )
}

/** Início (PDV-07 CA 8 a 11): os cards da loja só no Comércio e com sales.view; esqueleto ao carregar e erro com "Tentar de novo". */
export function VendasDaLoja() {
  const { all } = useNavigation()
  const retail = useHasProfile(STORE_PROFILES)
  const enabled = retail && canIn(all, 'sales.view') && canIn(all, 'dashboard.view')
  const query = useGetDashboardStoreSales({ query: { enabled, refetchInterval: 60_000 } })
  if (!enabled) return null
  return (
    <section aria-labelledby="vendas-loja" className="mb-8">
      <h2 id="vendas-loja" className="mb-3 text-[17px] font-semibold">Sua loja hoje</h2>
      {query.isPending ? <Skeleton className="h-36" />
        : query.isError || !query.data ? <ErrorState title="Não foi possível carregar as vendas de hoje." onRetry={() => void query.refetch()} />
        : <VendasDaLojaCards card={query.data} />}
    </section>
  )
}
