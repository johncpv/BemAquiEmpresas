import type { ReactNode } from 'react'
import type { KioskBoard, KioskDelays, KioskQueueView, KioskScreenContext } from '@/api/generated/model'
import { type TvConnection, TvHeader, TvNotice } from '@/components/tv/kit'
import { cn } from '@/lib/utils'
import { TvColumn, TvDelayRow, TvQueueRow, TvViewIndicator } from '@/components/tv/panel'
import { useMinuteClock } from './useMinuteClock'
import { columnPage, columnsPerPage, headline, PANEL_TEXT } from './panel'

/** Estado de uma visão como as consultas entregam (dados, carregando, erro). */
export interface TvQuery<T> {
  data: T | undefined
  isError: boolean
}

export interface TvData {
  board: TvQuery<KioskBoard>
  queue: TvQuery<KioskQueueView>
  delays: TvQuery<KioskDelays>
}

/**
 * Superfície das visões (PNL-01b): cabeçalho do kit TV ("{linha} — {n} OS em andamento, {m} atrasadas", relógio e conexão), o indicador
 * da visão e a visão da vez, em rotação. A mesma superfície serve à TV e à prévia do backoffice.
 */
export function TvSurface({ screen, data, status, updatedAt, width, views, rotation, actions, now, className = 'min-h-[calc(100dvh-64px)]' }: {
  screen: KioskScreenContext
  data: TvData
  status: TvConnection
  updatedAt: Date | null
  /** Largura útil, para quantas colunas cabem (5 a 1920 px, 3 a 1366 px). */
  width: number
  /** Visões da rotação (as da tela, sem "Atrasos e riscos" vazia) e a da vez (useRotation, de quem monta a superfície). */
  views: readonly string[]
  rotation: { index: number; tick: number }
  actions?: ReactNode
  now?: Date
  className?: string
}) {
  const view = views[rotation.index] ?? 'board'
  const clock = useMinuteClock(now)
  return (
    <div className={cn('flex flex-col gap-6', className)} data-tv-surface="">
      <TvHeader title={headline(screen.line, data.board.data)} status={status} updatedAt={updatedAt} actions={actions} now={now} />
      <div className="flex flex-wrap items-center justify-between gap-4">
        {views.length > 0 ? <TvViewIndicator view={view} index={rotation.index} count={views.length} /> : <span />}
        <p className="text-[length:var(--tv-line)] text-muted-foreground">{screen.name}</p>
      </div>
      <div key={view} className="tv-fade flex-1">
        {views.length === 0 ? <TvNotice title={PANEL_TEXT.empty} /> : null}
        {view === 'board' && views.length > 0 ? <BoardView query={data.board} width={width} tick={rotation.tick} now={clock} /> : null}
        {view === 'queue' ? <QueueView query={data.queue} /> : null}
        {view === 'late' ? <DelaysView query={data.delays} /> : null}
      </div>
    </div>
  )
}

function Loading<T>({ query, children }: { query: TvQuery<T>; children: (data: T) => ReactNode }) {
  if (query.data !== undefined) return <>{children(query.data)}</>
  if (query.isError) return <TvNotice title="Não foi possível atualizar o painel." hint="A TV tenta de novo sozinha em instantes." tone="crit" />
  return <TvNotice title={PANEL_TEXT.loading} />
}

/** Quadro por etapa (CA 1 a 4 e 11): as colunas que cabem; com mais, a TV rola as colunas a cada rotação. */
function BoardView({ query, width, tick, now }: { query: TvQuery<KioskBoard>; width: number; tick: number; now: Date }) {
  return (
    <Loading query={query}>
      {(board) => {
        if (board.cards.length === 0) return <TvNotice title={PANEL_TEXT.empty} />
        const perPage = columnsPerPage(width)
        const { items, page, pages } = columnPage(board.columns, perPage, tick)
        return (
          <section aria-label="Quadro por etapa" className="flex flex-col gap-3">
            <div className="grid gap-6" style={{ gridTemplateColumns: `repeat(${Math.min(perPage, Math.max(items.length, 1))}, minmax(0, 1fr))` }}>
              {items.map((column) => <TvColumn key={column.key} column={column} cards={board.cards} now={now} />)}
            </div>
            {pages > 1 ? <p className="text-[length:var(--tv-line)] text-muted-foreground tabular-nums">Colunas {page + 1}/{pages}</p> : null}
          </section>
        )
      }}
    </Loading>
  )
}

/** Fila e previsões (CA 5): até 6 projetos da fila com OS na linha. */
function QueueView({ query }: { query: TvQuery<KioskQueueView> }) {
  return (
    <Loading query={query}>
      {(queue) => queue.rows.length === 0 ? <TvNotice title={PANEL_TEXT.queueEmpty} /> : (
        <section aria-label="Fila e previsões">
          <table className="w-full text-left">
            <thead className="text-[length:var(--tv-line)] text-muted-foreground">
              <tr>
                <th scope="col" className="pb-2 pr-6 font-semibold">Fila</th>
                <th scope="col" className="pb-2 pr-6 font-semibold">Projeto</th>
                <th scope="col" className="pb-2 pr-6 font-semibold">Na linha</th>
                <th scope="col" className="pb-2 pr-6 font-semibold">Previsão</th>
                <th scope="col" className="pb-2 pr-6 font-semibold">Prometido</th>
                <th scope="col" className="pb-2 font-semibold">Folga</th>
              </tr>
            </thead>
            <tbody>{queue.rows.map((row) => <TvQueueRow key={row.projectId} row={row} />)}</tbody>
          </table>
          {queue.total > queue.rows.length ? <p className="mt-3 text-[length:var(--tv-line)] text-muted-foreground tabular-nums">+{queue.total - queue.rows.length} projetos na fila</p> : null}
        </section>
      )}
    </Loading>
  )
}

/** Atrasos e riscos (CA 6): até 6 OS, do maior atraso para o menor. */
function DelaysView({ query }: { query: TvQuery<KioskDelays> }) {
  return (
    <Loading query={query}>
      {(delays) => (
        <section aria-label="Atrasos e riscos">
          <table className="w-full text-left">
            <thead className="text-[length:var(--tv-line)] text-muted-foreground">
              <tr>
                <th scope="col" className="pb-2 pl-4 pr-6 font-semibold">OS</th>
                <th scope="col" className="pb-2 pr-6 font-semibold">Etapa</th>
                <th scope="col" className="pb-2 pr-6 font-semibold">Operador</th>
                <th scope="col" className="pb-2 pr-6 font-semibold">Situação</th>
                <th scope="col" className="pb-2 pr-4 font-semibold">Define a entrega</th>
              </tr>
            </thead>
            <tbody>{delays.rows.map((row) => <TvDelayRow key={row.id} row={row} />)}</tbody>
          </table>
          {delays.total > delays.rows.length ? <p className="mt-3 text-[length:var(--tv-line)] text-muted-foreground tabular-nums">+{delays.total - delays.rows.length} OS</p> : null}
        </section>
      )}
    </Loading>
  )
}
