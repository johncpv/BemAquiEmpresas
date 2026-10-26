import type { KioskCard, KioskColumn, KioskDelayRow, KioskQueueRow } from '@/api/generated/model'
import { StageIndicator } from '@/components/ui/stage-indicator'
import { cn } from '@/lib/utils'
import { columnCards, elapsedText, hoursText, meter, operatorLine, PANEL_TEXT, shortDate, VIEW_NAMES } from '@/pages/tv/panel'

/**
 * Kit TV das visões (PNL-01b, D18, ADR 0030 §5): cartão de 4 linhas, coluna com até 3 cartões e "+{n} OS", linha da fila e de atrasos,
 * e o indicador da visão. Leitura a distância pelos tokens --tv-title (32 px), --tv-line (28 px) e --tv-count (48 px); números em
 * tabular-nums; cor só por token (crítico e alerta para atraso e risco; o âmbar do sinal só na letra da etapa).
 */

const riskBand = (risk: KioskCard['risk']) => (risk === 'Late' ? 'crit' : risk === 'AtRisk' ? 'warn' : null)

/** Cartão de exatamente 4 linhas, sem o produto nem o cliente (CA 2 e 4). */
export function TvCard({ card, now }: { card: KioskCard; now: Date }) {
  const band = riskBand(card.risk)
  const hours = meter(card.appliedHours, card.standardHours)
  return (
    <article aria-label={`${card.number}, ${card.projectName}`} data-risk={card.risk} data-tv-card=""
      className={cn('flex flex-col rounded-md border border-border bg-card px-4 py-2 text-[length:var(--tv-line)] leading-[1.15]', band && 'border-l-8', band === 'crit' && 'border-l-crit', band === 'warn' && 'border-l-warn')}>
      <p data-line="1" className="flex min-w-0 items-baseline gap-3">
        <span className="shrink-0 font-display font-extrabold tabular-nums font-wider">{card.number}</span>
        <span className="min-w-0 truncate text-muted-foreground">{card.projectName}</span>
      </p>
      <p data-line="2" className={cn('min-w-0 truncate font-semibold', card.pausedReason ? 'text-warn-ink' : card.operator ? 'text-foreground' : 'text-muted-foreground')}>
        {operatorLine(card)}
      </p>
      <p data-line="3" className="flex min-w-0 items-center gap-3 tabular-nums">
        {card.stepStartedAt ? <span aria-label="Tempo na etapa" className="shrink-0 font-semibold">{elapsedText(card.stepStartedAt, now)}</span> : null}
        <span className={cn('shrink-0', hours.over ? 'text-crit-ink' : 'text-muted-foreground')}>{hoursText(card.appliedHours, card.standardHours)}</span>
        <span aria-hidden="true" className="h-2 min-w-8 flex-1 overflow-hidden rounded-full bg-muted">
          <span className={cn('block h-full rounded-full', hours.over ? 'bg-crit' : 'bg-primary')} style={{ width: `${Math.round(hours.ratio * 100)}%` }} />
        </span>
      </p>
      <p data-line="4" className={cn('font-semibold tabular-nums', band === 'crit' ? 'text-crit-ink' : band === 'warn' ? 'text-warn-ink' : 'text-muted-foreground')}>
        {card.risk === 'Late' ? PANEL_TEXT.late : card.risk === 'AtRisk' ? PANEL_TEXT.atRisk : card.plannedEnd ? `Previsto ${shortDate(card.plannedEnd)}` : PANEL_TEXT.noForecast}
      </p>
    </article>
  )
}

/** Coluna por etapa: a letra no display de andar, o nome (32 px), a contagem (48 px), até 3 cartões e "+{n} OS" (CA 1 e 3). */
export function TvColumn({ column, cards, now }: { column: KioskColumn; cards: readonly KioskCard[]; now: Date }) {
  const { shown, more } = columnCards(cards, column.key)
  return (
    <section aria-label={`${column.name}: ${column.count} OS`} data-tv-column={column.key} className="flex min-w-0 flex-col gap-3">
      <header className="flex items-center justify-between gap-3 border-b border-border pb-2">
        <div className="flex min-w-0 items-center gap-3">
          {column.code ? <StageIndicator stages={[column.name]} letters={[column.code]} current={0} size="tv" /> : null}
          <h2 className="min-w-0 truncate font-display text-[length:var(--tv-title)] font-extrabold leading-tight font-wider">{column.name}</h2>
        </div>
        <span className="font-display text-[length:var(--tv-count)] font-extrabold leading-none tabular-nums font-wider">{column.count}</span>
      </header>
      {shown.map((card) => <TvCard key={card.id} card={card} now={now} />)}
      {more > 0 ? <p className="text-[length:var(--tv-line)] font-semibold text-muted-foreground tabular-nums">+{more} OS</p> : null}
    </section>
  )
}

const slackTone = (risk: KioskQueueRow['risk']) => (risk === 'Late' ? 'crit' : risk === 'AtRisk' ? 'warn' : 'ok')
const slackLabel = (risk: KioskQueueRow['risk']) => (risk === 'Late' ? 'Atrasado' : risk === 'AtRisk' ? 'Em risco' : 'No prazo')

/** Chip grande de estado (no prazo, em risco, atrasado): cor semântica com o texto. */
export function TvChip({ tone, children }: { tone: 'ok' | 'warn' | 'crit'; children: string }) {
  return (
    <span data-tone={tone} className={cn('inline-flex items-center gap-2 rounded-full px-4 py-1 font-semibold', tone === 'ok' && 'bg-ok-soft text-ok-ink', tone === 'warn' && 'bg-warn-soft text-warn-ink', tone === 'crit' && 'bg-crit-soft text-crit-ink')}>
      <span aria-hidden="true" className={cn('size-3 rounded-full', tone === 'ok' && 'bg-ok', tone === 'warn' && 'bg-warn', tone === 'crit' && 'bg-crit')} />
      {children}
    </span>
  )
}

/** Linha da fila (CA 5): posição, projeto, início na linha, previsão de entrega × prometido e a folga. */
export function TvQueueRow({ row }: { row: KioskQueueRow }) {
  const slack = row.slackDays
  return (
    <tr data-tv-queue-row="" className="border-b border-border text-[length:var(--tv-line)] tabular-nums">
      <td className="py-3 pr-6 font-display text-[length:var(--tv-count)] font-extrabold leading-none font-wider">{row.position}º</td>
      <td className="max-w-[18ch] py-3 pr-6">
        <span className="block font-semibold">{row.projectNumber}</span>
        <span className="block truncate text-muted-foreground">{row.projectName}</span>
      </td>
      <td className="py-3 pr-6">{row.firstStart ? `início ${shortDate(row.firstStart)}` : '—'}</td>
      <td className="py-3 pr-6 font-semibold">{row.forecastDate ? shortDate(row.forecastDate) : <span className="text-muted-foreground">{PANEL_TEXT.noForecast}</span>}</td>
      <td className="py-3 pr-6">{row.promisedDate ? shortDate(row.promisedDate) : '—'}</td>
      <td className="py-3">
        <TvChip tone={slackTone(row.risk)}>{slack === null || slack === undefined ? slackLabel(row.risk) : `${slackLabel(row.risk)} · ${slack > 0 ? '+' : ''}${slack} d`}</TvChip>
      </td>
    </tr>
  )
}

/** Linha de "Atrasos e riscos" (CA 6): OS, projeto, etapa atual, operador, dias e a etapa que define a entrega. */
export function TvDelayRow({ row }: { row: KioskDelayRow }) {
  const crit = row.risk === 'Late'
  return (
    <tr data-tv-delay-row="" data-risk={row.risk} className={cn('border-b border-border text-[length:var(--tv-line)] tabular-nums', crit ? 'border-l-8 border-l-crit' : 'border-l-8 border-l-warn')}>
      <td className="py-3 pl-4 pr-6">
        <span className="block font-display font-extrabold font-wider">{row.number}</span>
        <span className="block max-w-[16ch] truncate text-muted-foreground">{row.projectName}</span>
      </td>
      <td className="py-3 pr-6">
        {row.stepCode ? <StageIndicator stages={[row.stepName ?? row.stepCode]} letters={[row.stepCode]} current={0} size="tv" showLabel /> : '—'}
      </td>
      <td className="py-3 pr-6">{row.operator ?? <span className="text-muted-foreground">Sem operador</span>}</td>
      <td className="py-3 pr-6"><TvChip tone={crit ? 'crit' : 'warn'}>{`${crit ? 'Atrasada' : 'Em risco'} · ${row.days} d`}</TvChip></td>
      <td className="py-3 pr-4 text-muted-foreground">{row.definingStepName ? `${row.definingStepName}${row.definingStepEnd ? ` até ${shortDate(row.definingStepEnd)}` : ''}` : '—'}</td>
    </tr>
  )
}

/** Indicador discreto da visão atual ("1/3") com o nome (CA 7). */
export function TvViewIndicator({ view, index, count }: { view: string; index: number; count: number }) {
  return (
    <p aria-label={`Visão ${index + 1} de ${count}: ${VIEW_NAMES[view] ?? view}`} data-tv-view={view} className="flex items-center gap-3 text-[length:var(--tv-line)] text-muted-foreground tabular-nums">
      <span className="font-semibold text-foreground">{VIEW_NAMES[view] ?? view}</span>
      {count > 1 ? <span>{index + 1}/{count}</span> : null}
    </p>
  )
}
