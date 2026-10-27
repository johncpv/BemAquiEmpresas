import { useNavigate } from '@tanstack/react-router'
import { CalendarClock } from 'lucide-react'
import { useEffect, useState } from 'react'
import { newQuoteRevision, useSimulateQuoteSchedule } from '@/api/generated/bemaqui'
import type { QuoteDetail, QuoteScheduleSimulationView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { inputClass } from '@/components/form/form-utils'
import { notifyError } from '@/components/ui/messages'
import { SidePanel } from '@/components/ui/side-panel'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { SIMULATION_DICAS, SIMULATION_TEXT, lateLine, noForecastText, positionText, promiseChip, promiseLine, simulationHeadline, validPosition } from './schedule-simulation'

type Mode = 'end' | 'at'

const button = 'inline-flex min-h-[44px] items-center justify-center gap-1.5 rounded-md px-3 py-2 text-sm font-semibold'

/**
 * "Simular prazo" do orçamento (PLN-05): painel (tela cheia a 400 px) com a posição na fila, o resultado só com datas e "Usar este prazo"
 * (rascunho: preenche o campo sem gravar) ou "Criar nova revisão com este prazo" (enviado, fluxo da COM-02). Nada é gravado pela simulação.
 */
export function ScheduleSimulationPanel({ quote, open, onOpenChange, onUseDays }: {
  quote: QuoteDetail
  open: boolean
  onOpenChange: (open: boolean) => void
  onUseDays: (days: number) => void
}) {
  const navigate = useNavigate()
  const canEdit = useCan('quotes.edit')
  const canCost = useCan('quotes.view_cost')
  const canInventory = useCan('inventory.view')
  const canEngineering = useCan('engineering.view')
  const canCapacity = canInventory && canEngineering
  const mutation = useSimulateQuoteSchedule()
  const [mode, setMode] = useState<Mode>('end')
  const [position, setPosition] = useState('1')
  const [result, setResult] = useState<QuoteScheduleSimulationView | null>(null)
  const [failure, setFailure] = useState<'error' | 'limited' | null>(null)

  const simulate = async (at: number | null) => {
    setFailure(null)
    try {
      setResult(await mutation.mutateAsync({ id: quote.id, data: { queuePosition: at } }))
    } catch (error) {
      setResult(null)
      setFailure(error instanceof ApiError && error.status === 429 ? 'limited' : 'error')
    }
  }

  // Abriu: simula no fim da fila (padrão do CA 3), uma vez por abertura.
  useEffect(() => {
    if (open) void simulate(null)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  const close = (next: boolean) => {
    if (!next) {
      setResult(null)
      setFailure(null)
      setMode('end')
    }
    onOpenChange(next)
  }

  const queueSize = result?.simulated ? result.queueSize : 0
  const chosen = mode === 'end' ? null : validPosition(position, queueSize)
  const days = result?.simulated ? result.deliveryDays : null
  const applyDays = async () => {
    if (days === null || days === undefined) return
    if (quote.status === 'Draft') {
      onUseDays(days)
      close(false)
      return
    }
    try {
      const created = await newQuoteRevision(quote.id, { version: quote.version })
      close(false)
      await navigate({ to: '/app/orcamentos/$id', params: { id: created.id }, search: { prazo: days } } as never)
    } catch (error) {
      notifyError(error instanceof ApiError ? error.message : 'Não foi possível criar a nova revisão.')
    }
  }

  const footer = result?.simulated && days !== null && days !== undefined && canEdit && quote.isCurrent ? (
    <button type="button" onClick={() => void applyDays()} className={`${button} bg-primary text-primary-foreground`}>
      {quote.status === 'Draft' ? 'Usar este prazo' : 'Criar nova revisão com este prazo'}
    </button>
  ) : undefined

  return (
    <SidePanel open={open} onOpenChange={close} title="Simular prazo" description="Se o cliente aprovar hoje, quando a fábrica entrega?" footer={footer}>
      <div className="space-y-4" aria-busy={mutation.isPending}>
        {result?.simulated || mutation.isPending ? (
          <fieldset className="space-y-2" disabled={mutation.isPending}>
            <legend className="flex items-center text-sm font-semibold">Posição na fila<Dica nome="Na posição" texto={SIMULATION_DICAS.posicao} /></legend>
            <label className="flex min-h-[44px] items-center gap-2 text-sm"><input type="radio" name="posicao" checked={mode === 'end'} onChange={() => setMode('end')} />No fim da fila</label>
            <div className="flex flex-wrap items-center gap-2">
              <label className="flex min-h-[44px] items-center gap-2 text-sm"><input type="radio" name="posicao" checked={mode === 'at'} onChange={() => setMode('at')} />Na posição</label>
              <input aria-label="Posição na fila" inputMode="numeric" maxLength={4} value={position} disabled={mode !== 'at'}
                onChange={(e) => setPosition(e.target.value.replace(/\D/g, ''))} className={`${inputClass} w-20 tabular-nums`} />
              {result?.simulated ? <span className="text-xs text-muted-foreground tabular-nums">de 1 a {queueSize + 1}</span> : null}
            </div>
            <button type="button" disabled={mode === 'at' && chosen === null} onClick={() => void simulate(chosen)} className={`${button} border border-border`}>
              <CalendarClock aria-hidden="true" className="size-4" />Simular de novo
            </button>
          </fieldset>
        ) : null}

        {mutation.isPending ? (
          <div role="status" className="space-y-2"><p className="text-sm">{SIMULATION_TEXT.loading}</p><Skeleton className="h-24" /></div>
        ) : failure === 'limited' ? (
          <p role="alert" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">{SIMULATION_TEXT.limited}</p>
        ) : failure === 'error' ? (
          <ErrorState title="Não foi possível simular o prazo." message={SIMULATION_TEXT.error} onRetry={() => void simulate(chosen)} />
        ) : result && !result.simulated ? (
          <div role="status" className="space-y-2 rounded-md border border-dashed border-border bg-card p-4 text-sm">
            <p>{SIMULATION_TEXT.noHours}</p>
            {canCost ? (
              <button type="button" className="font-semibold text-primary hover:underline" onClick={() => { close(false); void navigate({ to: '.', search: (prev: Record<string, unknown>) => ({ ...prev, aba: 'itens' }) } as never) }}>
                Abrir a composição de custo
              </button>
            ) : <p className="text-muted-foreground">Peça a quem cuida dos custos para informar as horas por centro de trabalho.</p>}
          </div>
        ) : result ? (
          <Result view={result} canCapacity={canCapacity} onCapacity={() => { close(false); void navigate({ to: '/app/estoque/capacidade' } as never) }} />
        ) : null}
      </div>
    </SidePanel>
  )
}

function Result({ view, canCapacity, onCapacity }: { view: QuoteScheduleSimulationView; canCapacity: boolean; onCapacity: () => void }) {
  const headline = simulationHeadline(view)
  const promise = promiseLine(view)
  const chip = promiseChip(view)
  const late = lateLine(view)
  return (
    <section aria-label="Resultado da simulação" className="space-y-3">
      {headline ? <p className="font-semibold">{headline}</p> : <p role="alert" className="rounded-md border border-crit-soft bg-card px-3 py-2 text-sm text-crit-ink">{noForecastText(view.noForecastReason)}</p>}
      {view.queueSize === 0 ? <p className="text-sm text-muted-foreground">{SIMULATION_TEXT.idle}</p> : null}
      <dl className="grid grid-cols-1 gap-3 rounded-md border border-border bg-card p-4 text-sm sm:grid-cols-2">
        <Fact label="Fim da fabricação" value={view.fabricationEnd ? formatDate(view.fabricationEnd) : '—'} />
        <Fact label="Entrega prevista" value={view.deliveryDate ? `${formatDate(view.deliveryDate)} · ${view.deliveryDays} dias corridos` : '—'} />
        <Fact label="Posição na fila" value={positionText(view)} />
        <Fact label="Prazo prometido" value={promise ? promise.replace('Prazo prometido no orçamento: ', '') : 'Sem prazo no orçamento'} />
      </dl>
      {promise ? (
        <p className="flex flex-wrap items-center gap-2 text-sm">{promise}{chip ? <StatusChip tone={chip.tone}>{chip.label}</StatusChip> : null}</p>
      ) : null}
      {late ? <p className={`text-sm ${view.newlyLateCount > 0 ? 'font-semibold text-crit-ink' : 'text-muted-foreground'}`}>{late}</p> : null}
      <p role="note" className="rounded-md border border-border bg-muted px-3 py-2 text-sm">
        {SIMULATION_TEXT.material}
        {canCapacity ? <> <button type="button" onClick={onCapacity} className="font-semibold text-primary hover:underline">Ver capacidade de produzir</button></> : null}
      </p>
    </section>
  )
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div className="min-w-0">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="break-words tabular-nums">{value}</dd>
    </div>
  )
}
