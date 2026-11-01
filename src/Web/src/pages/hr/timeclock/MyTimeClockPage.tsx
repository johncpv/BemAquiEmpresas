import { Lock, MapPin, MapPinOff } from 'lucide-react'
import { useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { useListMyTimeEntries } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { useNavigation } from '@/navigation/navigation-context'
import { PageHeading } from '@/shell/PageHeading'
import { ModuleNotReleased } from '../../ModulePage'
import { dayLabel, entryLabel, formatTime, groupByDay, locationText, NOT_LINKED, sourceLabel, TIME_CLOCK_DICAS } from './time-clock'
import type { AdjustmentMode, VoidTarget } from './AdjustmentDialogs'
import { MyAdjustmentDialog, MyTimeAdjustments } from './MyTimeAdjustments'
import { MyTimesheet } from './MyTimesheet'
import { TimeClockPanel } from './TimeClockPanel'

/** "Minhas marcações" (CA 13): as próprias dos últimos 60 dias, com a própria localização; só carrega quando a pessoa abre. */
function MyEntries({ onVoid }: { onVoid: (target: VoidTarget) => void }) {
  const query = useListMyTimeEntries({ query: { retry: (count, error) => !(error instanceof ApiError && error.status === 403) && count < 2 } })
  if (query.isPending) return <div className="space-y-2"><Skeleton className="h-16" /><Skeleton className="h-16" /></div>
  if (query.isError) {
    const notLinked = query.error instanceof ApiError && query.error.code === 'employee_not_active'
    return notLinked ? <EmptyState title="Marcações indisponíveis" description={NOT_LINKED} /> : <ErrorState title="Não foi possível carregar as suas marcações." onRetry={() => query.refetch()} />
  }

  if (query.data.length === 0) return <EmptyState title="Nenhuma marcação nos últimos 60 dias." description="Use o botão acima para registrar a sua entrada." />
  return (
    <ul aria-label="Minhas marcações" className="space-y-3">
      {groupByDay(query.data).map((g) => (
        <li key={g.day} className="rounded-md border border-border bg-card">
          <p className="border-b border-border px-3 py-2 text-sm font-semibold">{dayLabel(g.day)}</p>
          <ul className="divide-y divide-border">
            {[...g.items].reverse().map((e) => (
              <li key={e.id} className="flex flex-wrap items-center justify-between gap-2 px-3 py-2 text-sm">
                <span className="font-semibold">{entryLabel(e.type)} <span className="tabular-nums">{formatTime(e.at)}</span></span>
                <span className="flex flex-wrap items-center gap-2 text-muted-foreground">
                  {sourceLabel(e.source, e.deviceName)}
                  {e.status === 'Voided' ? <StatusChip tone="neutral">Desconsiderada</StatusChip> : null}
                  {e.location === 'Captured' ? <span className="inline-flex items-center gap-1 tabular-nums"><MapPin aria-hidden="true" className="size-3.5" />{locationText(e.latitude, e.longitude, e.accuracyM)}</span> : null}
                  {e.location === 'Missing' ? <StatusChip tone="neutral"><MapPinOff aria-hidden="true" className="size-3" />Sem localização</StatusChip> : null}
                  {e.status === 'Valid' ? <button type="button" onClick={() => onVoid({ id: e.id, type: e.type, at: e.at })} className="min-h-11 rounded-md border border-border px-2 text-xs font-semibold text-foreground">Desconsiderar esta marcação</button> : null}
                </span>
              </li>
            ))}
          </ul>
        </li>
      ))}
    </ul>
  )
}

/**
 * "Meu ponto" no backoffice e no celular (/app/meu-ponto, RH-01a, ADR 0035): o mesmo painel do tablet e, abaixo, "Minhas marcações" e
 * (RH-01b) "Pedir ajuste" e "Meus pedidos" (?aba=pedidos abre os pedidos) e (RH-01c) "Meu espelho" com "Li e confirmo" (?aba=espelho).
 * Do funcionário, sem ação no grupo; a página confere o módulo liberado e o vínculo vem da API.
 */
export function MyTimeClockPage() {
  const { all, status } = useNavigation()
  const hr = all.find((m) => m.code === 'hr')
  const search = useSearch({ strict: false }) as Record<string, unknown>
  const [showMine, setShowMine] = useState(false)
  const [request, setRequest] = useState<{ mode: AdjustmentMode; target?: VoidTarget } | null>(null)
  if (status === 'loading') return null
  if (hr?.locked) return <ModuleNotReleased module={hr} />
  return (
    <>
      <PageHeading title="Meu ponto">
        <p className="mt-1 inline-flex items-center text-sm text-muted-foreground">
          <Lock aria-hidden="true" className="mr-1 size-4" />
          Só você e o RH veem as suas marcações.
          <Dica nome="Hora do servidor" texto={TIME_CLOCK_DICAS.horaServidor} />
        </p>
      </PageHeading>
      <div className="max-w-xl space-y-6">
        <TimeClockPanel onRequestAdjustment={() => setRequest({ mode: 'add' })} />
        <section aria-labelledby="minhas-marcacoes" className="space-y-2">
          <div className="flex items-center justify-between gap-2">
            <h2 id="minhas-marcacoes" className="text-lg font-bold">Minhas marcações</h2>
            <button type="button" onClick={() => setShowMine((v) => !v)} aria-expanded={showMine} className="min-h-11 rounded-md border border-border px-3 text-sm font-semibold">
              {showMine ? 'Ocultar' : 'Ver os últimos 60 dias'}
            </button>
          </div>
          {showMine ? <MyEntries onVoid={(target) => setRequest({ mode: 'void', target })} /> : null}
        </section>
        <MyTimesheet initiallyOpen={search.aba === 'espelho'} />
        <MyTimeAdjustments initiallyOpen={search.aba === 'pedidos'} />
        <MyAdjustmentDialog mode={request?.mode ?? null} target={request?.target} onClose={() => setRequest(null)} />
      </div>
    </>
  )
}
