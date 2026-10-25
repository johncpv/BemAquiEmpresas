import { useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { AlertTriangle, RefreshCw } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import { refreshDashboard, useGetDashboardOverview } from '@/api/generated/bemaqui'
import type { AttentionAlert } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { cn } from '@/lib/utils'
import { PageHeading } from '@/shell/PageHeading'
import { BalancesKpi, CashForecastPanel, CommercialPanel, DecisionsPanel, OverdueKpi, RevenueKpi } from './cards'
import { greetingOf, longDateOf, MOBILE_ORDER } from './dashboard'

const TITLE = 'Painel do proprietário'
export const NO_CARDS_MESSAGE = 'Você ainda não tem acesso aos indicadores. Fale com o administrador.'
export const REFRESH_LIMITED = 'Aguarde alguns segundos para atualizar de novo.'

/** Cada card na posição do desktop e com a ordem do celular (400 px). */
const LAYOUT: Record<string, { Card: (p: { className?: string }) => ReactNode; span: string }> = {
  balances: { Card: BalancesKpi, span: 'lg:col-span-4' },
  revenue: { Card: RevenueKpi, span: 'lg:col-span-4' },
  overdue: { Card: OverdueKpi, span: 'lg:col-span-4' },
  'cash-forecast': { Card: CashForecastPanel, span: 'lg:col-span-8' },
  decisions: { Card: DecisionsPanel, span: 'lg:col-span-4' },
  commercial: { Card: CommercialPanel, span: 'lg:col-span-6' },
}

/** Ordem no HTML (= desktop): linha de KPIs, caixa e decisões, comercial. */
const DESKTOP = ['balances', 'revenue', 'overdue', 'cash-forecast', 'decisions', 'commercial']

/**
 * Visão geral → Painel do proprietário (DSH-01a): cabeçalho com saudação, data e frase-resumo, "Atualizar", faixa "Atenção"
 * e os cards que o usuário vê, cada um com o seu endpoint e os seus estados (CA 2, 10, 11 e 13).
 */
export function DashboardPage() {
  const overview = useGetDashboardOverview()
  const client = useQueryClient()
  const [refreshing, setRefreshing] = useState(false)
  const [notice, setNotice] = useState<string>()

  const refresh = async () => {
    setNotice(undefined)
    setRefreshing(true)
    try {
      await refreshDashboard()
      await client.invalidateQueries({ predicate: (q) => String(q.queryKey[0]).startsWith('/api/v1/dashboard') })
    } catch (e) {
      setNotice(e instanceof ApiError && e.status === 429 ? REFRESH_LIMITED : 'Não foi possível atualizar agora. Tente de novo.')
    } finally {
      setRefreshing(false)
    }
  }

  const data = overview.data
  const heading = data ? greetingOf(data.greeting, data.firstName) : TITLE
  return (
    <>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <PageHeading title={heading} documentTitle={TITLE}>
          {data ? <p className="mt-1 text-muted-foreground">{longDateOf(data.today)}.{data.summary ? ' ' + data.summary : ''}</p> : null}
        </PageHeading>
        {data && data.cards.length > 0 ? (
          <div className="flex flex-col items-end gap-1">
            <button type="button" onClick={() => void refresh()} disabled={refreshing}
              className="inline-flex min-h-[44px] items-center gap-2 rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold disabled:opacity-60">
              <RefreshCw aria-hidden="true" className={cn('size-4', refreshing && 'animate-spin')} />
              {refreshing ? 'Atualizando…' : 'Atualizar'}
            </button>
            <p role="status" className="text-xs text-warn-ink">{notice}</p>
          </div>
        ) : null}
      </div>

      {overview.isPending ? (
        <div aria-busy="true" aria-label="Carregando o painel" className="grid gap-4 lg:grid-cols-3">
          <Skeleton className="h-28" /><Skeleton className="h-28" /><Skeleton className="h-28" /><Skeleton className="h-64 lg:col-span-2" /><Skeleton className="h-64" />
        </div>
      ) : overview.isError || !data ? (
        <ErrorState title="Não foi possível montar o painel." message="Verifique a conexão e tente de novo. Se continuar, fale com o suporte." onRetry={() => void overview.refetch()} />
      ) : data.cards.length === 0 ? (
        <EmptyState title={NO_CARDS_MESSAGE} description="O painel mostra o caixa, o faturamento, a inadimplência e o comercial conforme os módulos a que você tem acesso." />
      ) : (
        <div className="space-y-4">
          <Attention alerts={data.attention} />
          <div className="grid grid-cols-1 gap-4 lg:grid-cols-12">
            {DESKTOP.filter((code) => data.cards.includes(code)).map((code) => {
              const { Card, span } = LAYOUT[code]
              return <Card key={code} className={cn(MOBILE_ORDER[code], 'lg:order-none', span)} />
            })}
          </div>
        </div>
      )}
    </>
  )
}

/** Faixa "Atenção" (CA 10): até 3 alertas, cada um um link para a ação; sem alerta, não aparece. */
function Attention({ alerts }: { alerts: AttentionAlert[] }) {
  if (alerts.length === 0) return null
  return (
    <section aria-labelledby="atencao" className="space-y-2">
      <h2 id="atencao" className="sr-only">Atenção</h2>
      <ul className="grid gap-2 lg:grid-cols-3">
        {alerts.slice(0, 3).map((a) => (
          <li key={a.code}>
            <Link to={a.url as never} data-tone={a.tone}
              className={cn('flex h-full min-h-[44px] gap-2 rounded-md border border-l-4 bg-card p-3 text-sm hover:bg-muted', a.tone === 'crit' ? 'border-crit-soft border-l-crit' : 'border-warn-soft border-l-warn')}>
              <AlertTriangle aria-hidden="true" className={cn('mt-0.5 size-4 flex-none', a.tone === 'crit' ? 'text-crit-ink' : 'text-warn-ink')} />
              <span><b className="block">{a.title}</b><span className="text-muted-foreground">{a.detail}</span></span>
            </Link>
          </li>
        ))}
      </ul>
    </section>
  )
}
