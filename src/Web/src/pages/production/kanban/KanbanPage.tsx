import { keepPreviousData, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from '@tanstack/react-router'
import { type KeyboardEvent, useRef } from 'react'
import { getGetProductionBoardQueryKey, getListFabricationOrdersQueryKey, useGetProductionBoard } from '@/api/generated/bemaqui'
import type { BoardCard as Card, BoardColumn, ProductionBoard } from '@/api/generated/model'
import { useTableFilters } from '@/components/data-table/table-search'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StageIndicator } from '@/components/ui/stage-indicator'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { cn } from '@/lib/utils'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { ProductionTabs } from '../ProductionTabs'
import { FABRICATION_STATUS } from '../orders/fabrication'
import { ForecastsContext, useOrderForecasts } from '../planning/use-forecasts'
import { BoardCard } from './BoardCard'
import { boardParams, hasFilters, hoursText, KANBAN_DICAS, KANBAN_FILTERS, promisedText, stripe } from './kanban'
import { FALLBACK_POLL_MS, useBoardLive } from './useBoardLive'

const button = 'rounded-md border border-border px-3 py-2 text-sm font-semibold'
const primary = 'rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground'
const select = 'rounded-md border border-input bg-card px-2 py-2 text-sm'

/**
 * Fábrica → Produção → Kanban (PRD-02): uma coluna por código de etapa (a letra do roteiro, D10), cartões na etapa atual,
 * contadores de atraso e risco que filtram, filtros na URL, tempo real pelo ProductionHub e a alternativa em lista. Sem arraste:
 * a etapa anda pelo apontamento (PRD-03a) ou por "Concluir etapa" no menu do cartão (production.approve).
 */
export function KanbanPage() {
  const filters = useTableFilters([...KANBAN_FILTERS])
  const navigate = useNavigate()
  const client = useQueryClient()
  const live = useBoardLive()
  const canApprove = useCan('production.approve')
  const query = useGetProductionBoard(boardParams(filters), { query: { placeholderData: keepPreviousData, refetchInterval: live === 'offline' ? FALLBACK_POLL_MS : false } })
  const setFilter = (key: string, value: string | undefined) =>
    void navigate({ to: '.', replace: true, search: (prev: Record<string, unknown>) => ({ ...prev, [key]: value || undefined }) } as never)
  const clear = () => void navigate({ to: '.', replace: true, search: (prev: Record<string, unknown>) => ({ ver: prev.ver, coluna: prev.coluna }) } as never)
  const refresh = async () => {
    await client.invalidateQueries({ queryKey: getGetProductionBoardQueryKey() })
    await client.invalidateQueries({ queryKey: getListFabricationOrdersQueryKey() })
  }
  const asList = filters.ver === 'lista'
  const board = query.data
  // PLN-01a CA 16: "Fim previsto" de cada cartão, lido do plano em uma leitura por lote de cartões.
  const forecasts = useOrderForecasts(board?.cards.map((c) => c.id) ?? [])

  return (
    <>
      <PageHeading title="Produção: Kanban">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">A etapa anda pelo apontamento ou por "Concluir etapa" no menu do cartão. O quadro não arrasta.</p>
          <StatusChip tone={live === 'live' ? 'ok' : 'neutral'}>{live === 'live' ? 'Ao vivo' : live === 'offline' ? 'Offline' : 'Conectando'}</StatusChip>
        </div>
      </PageHeading>
      <ProductionTabs />
      {live === 'offline' ? <p role="status" className="mb-3 rounded-md border border-border bg-muted px-3 py-2 text-sm">Atualização ao vivo pausada. Tentando reconectar…</p> : null}
      {query.isPending ? <BoardSkeleton /> : query.isError ? (
        <ErrorState title="Não foi possível carregar o quadro." message="Verifique a conexão e tente de novo. Se continuar, fale com o suporte." onRetry={() => query.refetch()} />
      ) : board && board.openCount === 0 ? (
        <EmptyState title="Nenhuma OS aberta. As OS nascem com a aprovação do orçamento." description="Equipamento vendido vira OS sozinho; componente fabricado, pelo botão na lista de OS."
          action={<Link to={'/app/producao/os' as never} className={primary}>Ver OS de fabricação</Link>} />
      ) : board ? (
        <>
          <Toolbar board={board} filters={filters} setFilter={setFilter} clear={clear} asList={asList} />
          {board.cards.length === 0 ? (
            <EmptyState title="Nenhuma OS com estes filtros." description="Mude ou limpe os filtros para ver as outras OS abertas."
              action={<button type="button" className={button} onClick={clear}>Limpar filtros</button>} />
          ) : asList ? <BoardList board={board} /> : (
            <ForecastsContext.Provider value={forecasts}>
              <Columns board={board} selected={filters.coluna} onSelect={(key) => setFilter('coluna', key)} canApprove={canApprove} onChanged={refresh} />
            </ForecastsContext.Provider>
          )}
        </>
      ) : null}
    </>
  )
}

function Toolbar({ board, filters, setFilter, clear, asList }: {
  board: ProductionBoard; filters: Record<string, string | undefined>; setFilter: (key: string, value: string | undefined) => void; clear: () => void; asList: boolean
}) {
  const toggle = (key: 'atrasadas' | 'risco') => setFilter(key, filters[key] === '1' ? undefined : '1')
  const pick = (key: string, label: string, options: { value: string; label: string }[]) => options.length === 0 ? null : (
    <label className="flex flex-col gap-1 text-xs font-medium text-muted-foreground">
      {label}
      <select className={select} value={filters[key] ?? ''} onChange={(e) => setFilter(key, e.target.value)}>
        <option value="">Todos</option>
        {options.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
      </select>
    </label>
  )
  return (
    <div className="mb-4 space-y-3">
      <div className="flex flex-wrap items-center gap-2">
        <button type="button" aria-pressed={filters.atrasadas === '1'} onClick={() => toggle('atrasadas')}
          className={cn('rounded-full border px-3 py-1 text-sm font-semibold tabular-nums', filters.atrasadas === '1' ? 'border-crit bg-crit-soft text-crit-ink' : 'border-border')}>
          {board.lateCount} {board.lateCount === 1 ? 'atrasada' : 'atrasadas'}
        </button>
        <span className="inline-flex items-center">
          <button type="button" aria-pressed={filters.risco === '1'} onClick={() => toggle('risco')}
            className={cn('rounded-full border px-3 py-1 text-sm font-semibold tabular-nums', filters.risco === '1' ? 'border-warn bg-warn-soft text-warn-ink' : 'border-border')}>
            {board.atRiskCount} em risco
          </button>
          <Dica nome="Em risco" texto={KANBAN_DICAS.emRisco} />
        </span>
        <span className="ml-auto flex gap-2">
          <button type="button" className={button} onClick={() => setFilter('ver', asList ? undefined : 'lista')}>{asList ? 'Ver como quadro' : 'Ver como lista'}</button>
          {hasFilters(filters) ? <button type="button" className={button} onClick={clear}>Limpar filtros</button> : null}
        </span>
      </div>
      <div className="flex flex-wrap gap-3">
        {pick('projeto', 'Projeto', board.projects.map((p) => ({ value: p.id, label: p.label })))}
        {pick('cliente', 'Cliente', board.parties.map((p) => ({ value: p.id, label: p.label })))}
        {pick('linha', 'Linha', board.lines.map((l) => ({ value: l, label: l })))}
        {pick('roteiro', 'Roteiro', board.routings.map((r) => ({ value: r.id, label: r.label })))}
      </div>
    </div>
  )
}

/** Letra da coluna no display de andar; "Sem roteiro" e "Pronta para concluir" sem letra de etapa. */
function ColumnBadge({ column }: { column: BoardColumn }) {
  const letter = column.code ?? (column.kind === 'ReadyToComplete' ? '✓' : '–')
  return (
    <span aria-hidden="true" className="grid h-[26px] min-w-[30px] shrink-0 place-items-center rounded-[5px] bg-rail px-1.5 font-display text-[13px] font-extrabold tracking-wider text-signal">
      {letter}
    </span>
  )
}

/**
 * Colunas (CA 1 e 5): cada coluna é uma lista; setas para cima e para baixo andam entre os cartões da coluna, para os lados
 * entre colunas. A 400 px, uma coluna por vez com o seletor de etapa (abas com rolagem horizontal).
 */
function Columns({ board, selected, onSelect, canApprove, onChanged }: {
  board: ProductionBoard; selected: string | undefined; onSelect: (key: string) => void; canApprove: boolean; onChanged: () => Promise<void>
}) {
  const ref = useRef<HTMLDivElement>(null)
  const current = board.columns.find((c) => c.key === selected && c.count > 0)?.key ?? board.columns.find((c) => c.count > 0)?.key ?? board.columns[0]?.key
  const byColumn = (key: string) => board.cards.filter((c) => c.columnKey === key)
  const move = (event: KeyboardEvent) => {
    const target = event.target as HTMLElement
    if (!target.matches('[data-card-link]') || !['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'].includes(event.key)) return
    const columns = [...(ref.current?.querySelectorAll<HTMLElement>('[data-column]') ?? [])].filter((c) => c.offsetParent !== null || c.dataset.selected === 'true')
    const col = columns.findIndex((c) => c.contains(target))
    const links = (c: HTMLElement | undefined) => [...(c?.querySelectorAll<HTMLElement>('[data-card-link]') ?? [])]
    const row = links(columns[col]).indexOf(target)
    let next: HTMLElement | undefined
    if (event.key === 'ArrowDown') next = links(columns[col])[row + 1]
    if (event.key === 'ArrowUp') next = links(columns[col])[row - 1]
    if (event.key === 'ArrowRight' || event.key === 'ArrowLeft') {
      const step = event.key === 'ArrowRight' ? 1 : -1
      for (let i = col + step; i >= 0 && i < columns.length && !next; i += step) {
        const list = links(columns[i])
        next = list[Math.min(row, list.length - 1)]
      }
    }

    if (next) {
      event.preventDefault()
      next.focus()
    }
  }
  return (
    <div ref={ref} onKeyDown={move}>
      <div role="tablist" aria-label="Etapa" className="mb-3 flex gap-1 overflow-x-auto border-b border-border sm:hidden">
        {board.columns.map((c) => (
          <button key={c.key} type="button" role="tab" aria-selected={c.key === current} onClick={() => onSelect(c.key)}
            className="-mb-px flex shrink-0 items-center gap-1.5 whitespace-nowrap border-b-2 border-transparent px-2 py-2 text-sm font-semibold text-muted-foreground aria-selected:border-primary aria-selected:text-foreground">
            <ColumnBadge column={c} />{c.name}<span className="tabular-nums">({c.count})</span>
          </button>
        ))}
      </div>
      <div className="flex gap-3 overflow-x-auto pb-2">
        {board.columns.map((c, i) => (
          <section key={c.key} data-column="" data-selected={c.key === current} aria-labelledby={`col-${c.key}`}
            className={cn('flex w-full shrink-0 flex-col rounded-md bg-muted/50 p-2 sm:w-72', c.key !== current && 'max-sm:hidden')}>
            <header className="mb-2 flex items-start gap-2 px-1">
              <ColumnBadge column={c} />
              <div className="min-w-0">
                <h2 id={`col-${c.key}`} className="truncate font-semibold">{c.name}</h2>
                <p className="flex items-center text-xs text-muted-foreground tabular-nums">
                  {c.count} OS, {hoursText(c.remainingHours)} restantes
                  {i === 0 ? <Dica nome="Horas restantes" texto={KANBAN_DICAS.horasRestantes} /> : null}
                </p>
              </div>
            </header>
            <ul aria-label={`${c.code ? `Etapa ${c.code} ` : ''}${c.name}: ${c.count} OS`} className="flex flex-col gap-2">
              {byColumn(c.key).map((card) => (
                <li key={card.id}><BoardCard card={card} canApprove={canApprove} onChanged={onChanged} /></li>
              ))}
              {c.count === 0 ? <li className="px-1 py-4 text-center text-xs text-muted-foreground">Nenhuma OS nesta etapa.</li> : null}
            </ul>
          </section>
        ))}
      </div>
    </div>
  )
}

/** "Ver como lista" (CA 5): a mesma informação em tabela, para quem prefere ou usa leitor de tela. */
function BoardList({ board }: { board: ProductionBoard }) {
  const column = (card: Card) => board.columns.find((c) => c.key === card.columnKey)?.name ?? ''
  return (
    <div className="overflow-x-auto rounded-md border border-border">
      <table className="w-full text-sm">
        <caption className="sr-only">OS abertas por etapa</caption>
        <thead className="bg-muted text-left text-xs text-muted-foreground">
          <tr><th scope="col" className="px-3 py-2">OS</th><th scope="col" className="px-3 py-2">Produto</th><th scope="col" className="px-3 py-2 max-sm:hidden">Projeto e cliente</th><th scope="col" className="px-3 py-2">Etapa</th><th scope="col" className="px-3 py-2">Prazo</th><th scope="col" className="px-3 py-2 max-sm:hidden">Situação</th></tr>
        </thead>
        <tbody>
          {board.cards.map((card) => {
            const band = stripe(card)
            return (
              <tr key={card.id} className={cn('border-t border-border', band?.tone === 'crit' && 'border-l-4 border-l-crit', band?.tone === 'warn' && 'border-l-4 border-l-warn')}>
                <td className="px-3 py-2"><Link to={'/app/producao/os/$id' as never} params={{ id: card.id } as never} className="font-semibold text-primary tabular-nums hover:underline">{card.number}</Link></td>
                <td className="px-3 py-2">{card.productName}</td>
                <td className="px-3 py-2 max-sm:hidden">{card.projectNumber}<span className="block text-xs text-muted-foreground">{card.partyName}</span></td>
                <td className="px-3 py-2">{card.stepNames.length > 0 ? <StageIndicator stages={card.stepNames} letters={card.codes} current={card.currentStepIndex} /> : null}<span className="block text-xs text-muted-foreground">{column(card)}</span></td>
                <td className={cn('px-3 py-2 text-xs tabular-nums', band?.tone === 'crit' && 'font-semibold text-crit-ink', band?.tone === 'warn' && 'font-semibold text-warn-ink')}>{band?.text ?? promisedText(card)}</td>
                <td className="px-3 py-2 max-sm:hidden"><StatusChip tone={FABRICATION_STATUS[card.status]?.tone ?? 'neutral'}>{FABRICATION_STATUS[card.status]?.label ?? card.status}</StatusChip></td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

/** Carregando: colunas fantasmas. */
function BoardSkeleton() {
  return (
    <div className="flex gap-3 overflow-hidden" aria-busy="true" aria-label="Carregando o quadro">
      {[0, 1, 2, 3].map((i) => (
        <div key={i} className={cn('w-full shrink-0 space-y-2 sm:w-72', i > 0 && 'max-sm:hidden')}>
          <Skeleton className="h-10" /><Skeleton className="h-28" /><Skeleton className="h-28" />
        </div>
      ))}
    </div>
  )
}
