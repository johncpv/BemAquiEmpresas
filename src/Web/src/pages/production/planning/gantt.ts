import type { GanttCenterBar, GanttCenterDay, GanttCenterRow, GanttOrder, GanttProject, GanttProjectsView, GanttStep } from '@/api/generated/model'
import type { GanttBar, GanttDay, GanttRow } from '@/components/ui/gantt'
import { addDays, type GanttScale } from '@/components/ui/gantt-scale'
import type { StatusTone } from '@/components/ui/status-chip'
import { formatDate } from '@/lib/format'
import { hoursText } from './planning'

/** Dicas do Cronograma (PLN-03): "Define a entrega", "Prometido", "Instalação (datas da OS)" e o tracejado. */
export const GANTT_DICAS = {
  defineEntrega: 'A etapa com o maior fim previsto do projeto: é ela que empurra a data de entrega. Antecipar o projeto na Fila ou liberar horas no centro dela é o que traz a data para antes.',
  prometido: 'A data de entrega combinada com o cliente, do contrato ou do último aditivo. A barra do projeto vai até a data prevista; se passar da marca, o projeto está em risco.',
  instalacao: 'A OS de instalação aparece com as datas digitadas nela, sem cálculo do plano. Para mudar, edite a própria OS de instalação.',
  tracejado: 'Tracejado é o que o plano prevê (ainda não aconteceu). Contínuo é o que já aconteceu, com o início e o fim reais. Na etapa em andamento, a parte preenchida é o que já foi feito.',
} as const

export type GanttVisao = 'projeto' | 'centro'
export type GanttPeriodo = '12s' | '6m' | '12m'

export const PERIOD_LABEL: Record<GanttPeriodo, string> = { '12s': '12 semanas', '6m': '6 meses', '12m': '12 meses' }

/** Acima disto, os projetos abrem recolhidos (CA 13). */
export const COLLAPSE_ABOVE = 50

/** Filtros do cronograma na URL (CA 6 e 9): ?visao=, ?escala=, ?periodo=, ?projeto=, ?cliente=, ?linha=, ?centro= e ?risco=1. */
export interface GanttFilters {
  view: GanttVisao
  scale: GanttScale
  period: GanttPeriodo
  project?: string
  party?: string
  line?: string
  center?: string
  atRisk: boolean
}

export function parseGanttFilters(search: Record<string, unknown>): GanttFilters {
  const text = (v: unknown) => (typeof v === 'string' && v.trim() ? v.trim() : undefined)
  const scale = search.escala === 'dia' || search.escala === 'mes' ? search.escala : 'semana'
  const period = search.periodo === '6m' || search.periodo === '12m' ? search.periodo : '12s'
  return {
    view: search.visao === 'centro' ? 'centro' : 'projeto', scale, period, project: text(search.projeto), party: text(search.cliente), line: text(search.linha),
    center: text(search.centro), atRisk: search.risco === '1' || search.risco === 1 || search.risco === true,
  }
}

/** O que vai para a URL: só o que difere do padrão (a visão vai sempre, como no CA 1). */
export function ganttFiltersToSearch(f: GanttFilters): Record<string, string | undefined> {
  return {
    visao: f.view, escala: f.scale === 'semana' ? undefined : f.scale, periodo: f.period === '12s' ? undefined : f.period, projeto: f.project, cliente: f.party,
    linha: f.line, centro: f.center, risco: f.atRisk ? '1' : undefined,
  }
}

/** Hoje em America/Sao_Paulo ("AAAA-MM-DD"). */
export function businessToday(now = new Date()): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo' }).format(now)
}

/** Período pedido à API: 12 semanas = o padrão do servidor (sem datas); 6 e 12 meses a partir de 7 dias antes de hoje (CA 6). */
export function periodRange(period: GanttPeriodo, today: string): { from?: string; to?: string } {
  if (period === '12s') return {}
  const from = addDays(today, -7)
  return { from, to: addDays(from, period === '6m' ? 182 : 365) }
}

/** "19/02" no ano de hoje; "19/02/2027" em outro ano. */
export function short(date: string, today: string): string {
  const full = formatDate(date)
  return date.slice(0, 4) === today.slice(0, 4) ? full.slice(0, 5) : full
}

/** "+3 dias" (ok), "0 dia" e "−7 dias" (em risco), com o sinal de menos tipográfico. */
export function slackText(days: number): string {
  const abs = Math.abs(days)
  const unit = abs === 1 ? 'dia' : 'dias'
  return days > 0 ? `+${abs} ${unit}` : days < 0 ? `−${abs} ${unit}` : `0 ${unit}`
}

/** Chip de folga (CA 1): positiva ok; zero informativo; negativa em risco ou atrasado. */
export function slackChip(project: Pick<GanttProject, 'slackDays' | 'risk'>): { tone: StatusTone; text: string } | null {
  if (project.slackDays === null || project.slackDays === undefined) return project.risk === 'Late' ? { tone: 'crit', text: 'Atrasado' } : null
  const tone: StatusTone = project.risk === 'Late' ? 'crit' : project.slackDays < 0 ? 'warn' : project.slackDays === 0 ? 'info' : 'ok'
  return { tone, text: slackText(project.slackDays) }
}

export function riskLabel(risk: GanttProject['risk']): string | undefined {
  return risk === 'Late' ? 'Atrasado' : risk === 'AtRisk' ? 'Em risco' : undefined
}

/**
 * Resumo textual acima do cronograma (CA 7): "{n} projetos no período; {m} em risco. O mais atrasado é {projeto}, {d} dias depois do
 * prometido."
 */
export function summaryLine(projects: GanttProject[]): string {
  const n = projects.length
  if (n === 0) return 'Nenhum projeto no período.'
  const risky = projects.filter((p) => p.risk !== 'None')
  const head = `${n === 1 ? '1 projeto' : `${n} projetos`} no período; ${risky.length === 0 ? 'nenhum em risco' : `${risky.length} em risco`}.`
  const worst = risky.filter((p) => (p.daysLate ?? 0) > 0).sort((a, b) => (b.daysLate ?? 0) - (a.daysLate ?? 0) || a.position - b.position)[0]
  if (!worst) return head
  const days = worst.daysLate === 1 ? '1 dia' : `${worst.daysLate} dias`
  return `${head} O mais atrasado é ${projectName(worst)}, ${days} depois do prometido.`
}

/** "PRJ-2026-0003 · Ed. Monte Castelo" (só o número sem nome). */
export function projectName(p: Pick<GanttProject, 'projectNumber' | 'projectName'>): string {
  return p.projectName ? `${p.projectNumber} · ${p.projectName}` : p.projectNumber
}

/** Fim da barra do projeto: o previsto ou, sem previsão, o maior fim das OS. */
export function projectEnd(p: GanttProject): string | null {
  if (p.forecastDate) return p.forecastDate
  return p.orders.map((o) => o.end).filter((d): d is string => !!d).sort().at(-1) ?? null
}

/** Datas da etapa no texto: "previsto de 03/11/2026 a 05/11/2026", "realizado de 01/11/2026 a 02/11/2026". */
export function rangeText(start: string, end: string, kind: 'previsto' | 'realizado' | 'digitado'): string {
  return start === end ? `${kind} em ${formatDate(start)}` : `${kind} de ${formatDate(start)} a ${formatDate(end)}`
}

/** A barra da etapa (CA 2): concluída real e contínua; em andamento com a parte feita; prevista tracejada; sem previsão não tem barra. */
export function stepBar(step: GanttStep, order: Pick<GanttOrder, 'orderId' | 'number'>, project: Pick<GanttProject, 'projectNumber' | 'projectName'>): GanttBar | null {
  const center = step.workCenterName ?? 'sem centro'
  const name = `${step.code ? `${step.code} · ` : ''}${step.name}`
  const hours = `Horas restantes: ${hoursText(step.remainingHours).replace(/ h$/, '')} de ${hoursText(step.standardHours)}`
  const base = { id: `s:${step.stepId}`, letter: step.code, emphasis: step.drivesDelivery, label: step.drivesDelivery ? 'Define a entrega' : undefined }
  const drive = step.drivesDelivery ? ', define a entrega' : ''
  const detail = (lines: string[]) => ({ title: `${order.number} · ${name}`, lines: [`Projeto: ${projectName(project)}`, `Centro: ${center}`, ...lines] })
  if (step.state === 'Done' && step.actualStart && step.actualEnd) {
    return {
      ...base, start: step.actualStart, end: step.actualEnd, kind: 'actual',
      ariaLabel: `Etapa ${name}, ${center}, OS ${order.number}, ${rangeText(step.actualStart, step.actualEnd, 'realizado')}, concluída`,
      detail: detail([`Concluída: ${rangeText(step.actualStart, step.actualEnd, 'realizado')}`]),
    }
  }

  if ((step.state === 'Planned' || step.state === 'InProgress') && step.plannedStart && step.plannedEnd) {
    const running = step.state === 'InProgress'
    const start = running && step.actualStart && step.actualStart < step.plannedStart ? step.actualStart : step.plannedStart
    const percent = Math.round(step.progress * 100)
    return {
      ...base, start, end: step.plannedEnd, kind: running ? 'progress' : 'planned', progress: running ? step.progress : undefined,
      ariaLabel: `Etapa ${name}, ${center}, OS ${order.number}, ${rangeText(step.plannedStart, step.plannedEnd, 'previsto')}, ${running ? `em andamento, ${percent}% feito` : 'prevista'}${drive}`,
      detail: detail([hours, `Previsto: ${rangeText(step.plannedStart, step.plannedEnd, 'previsto').replace(/^previsto /, '')}`, ...(running && step.actualStart ? [`Início real: ${formatDate(step.actualStart)}`, `Feito: ${percent}%`] : [])]),
    }
  }

  return null
}

/** Rótulo da linha da etapa: a letra e o nome; o centro ou o motivo sem previsão na segunda linha. */
export function stepSublabel(step: GanttStep): string {
  if (step.state === 'NoForecast') return `Sem previsão: ${step.noForecastReason ?? 'aguardando o plano'}`
  return step.workCenterName ?? 'Sem centro'
}

/** A barra da OS: instalação hachurada com as datas digitadas (CA 4); fabricação a faixa das etapas. */
export function orderBar(order: GanttOrder, project: GanttProject): GanttBar | null {
  if (!order.start || !order.end) return null
  if (order.kind === 'Installation') {
    return {
      id: `o:${order.orderId}`, start: order.start, end: order.end, kind: 'readonly', label: 'Instalação (datas da OS)',
      ariaLabel: `Instalação ${order.number}, ${rangeText(order.start, order.end, 'digitado')}, só leitura`,
      detail: { title: `${order.number} · Instalação (datas da OS)`, lines: [`Projeto: ${projectName(project)}`, `Datas da OS: ${formatDate(order.start)} → ${formatDate(order.end)}`, 'Só leitura: mude na própria OS.'] },
    }
  }

  return {
    id: `o:${order.orderId}`, start: order.start, end: order.end, kind: 'summary',
    ariaLabel: `OS ${order.number}, de ${formatDate(order.start)} a ${formatDate(order.end)}`,
    detail: { title: order.number, lines: [`Projeto: ${projectName(project)}`, `De ${formatDate(order.start)} a ${formatDate(order.end)}`, `${order.steps.length} etapas`] },
  }
}

/** A faixa do projeto: do início da primeira etapa até a data prevista, com o prometido e a folga no rótulo (CA 1). */
export function projectBar(p: GanttProject, today: string): GanttBar | null {
  const end = projectEnd(p)
  if (!p.start || !end) return null
  const parts = [`previsto ${p.forecastDate ? formatDate(p.forecastDate) : 'sem data'}`, p.promisedDate ? `prometido ${formatDate(p.promisedDate)}` : 'sem prometido']
  if (p.slackDays !== null && p.slackDays !== undefined) parts.push(`folga ${slackText(p.slackDays)}`)
  const risk = riskLabel(p.risk)
  return {
    id: `p:${p.projectId}`, start: p.start, end: end < p.start ? p.start : end, kind: 'summary', inert: true,
    ariaLabel: `Projeto ${projectName(p)}, início ${formatDate(p.start)}, ${parts.join(', ')}${risk ? `, ${risk.toLowerCase()}` : ''}`,
    detail: { title: projectName(p), lines: [`Início: ${formatDate(p.start)}`, `Previsto: ${p.forecastDate ? formatDate(p.forecastDate) : 'sem previsão'}`, `Prometido: ${p.promisedDate ? short(p.promisedDate, today) : '—'}`, ...(risk ? [risk] : [])] },
  }
}

/** Projetos abertos de início: todos até 50, nenhum acima disso (CA 13); as OS abrem junto com o projeto. */
export function defaultOpen(view: GanttProjectsView): Set<string> {
  if (view.projects.length > COLLAPSE_ABOVE) return new Set()
  return new Set(view.projects.flatMap((p) => [`p:${p.projectId}`, ...p.orders.map((o) => `o:${o.orderId}`)]))
}

/** As linhas do Gantt por projeto (CA 1 a 4): projeto → OS → etapa, só o que está aberto. */
export function projectRows(view: GanttProjectsView, open: Set<string>): GanttRow[] {
  const rows: GanttRow[] = []
  for (const p of view.projects) {
    const id = `p:${p.projectId}`
    const bar = projectBar(p, view.today)
    rows.push({
      id, label: projectName(p), sublabel: p.forecastDate || !p.noForecastReason ? p.partyName ?? undefined : `Sem previsão: ${reasonText(p.noForecastReason)}`, depth: 0,
      expanded: p.orders.length > 0 ? open.has(id) : undefined, stripe: p.risk === 'Late' ? 'crit' : p.risk === 'AtRisk' ? 'warn' : undefined, stripeLabel: riskLabel(p.risk),
      chip: slackChip(p) ?? undefined, bars: bar ? [bar] : [], markers: p.promisedDate ? [{ date: p.promisedDate, label: `Prometido ${short(p.promisedDate, view.today)}` }] : [],
    })
    if (!open.has(id)) continue
    for (const o of p.orders) {
      const oid = `o:${o.orderId}`
      const ob = orderBar(o, p)
      rows.push({
        id: oid, label: o.number, sublabel: o.kind === 'Installation' ? 'Instalação (datas da OS)' : `${o.steps.length} etapas`, depth: 1,
        expanded: o.kind === 'Fabrication' && o.steps.length > 0 ? open.has(oid) : undefined, bars: ob ? [ob] : [],
      })
      if (o.kind !== 'Fabrication' || !open.has(oid)) continue
      for (const s of o.steps) {
        const sb = stepBar(s, o, p)
        rows.push({ id: `s:${s.stepId}`, label: `${s.code ? `${s.code} · ` : ''}${s.name}`, sublabel: stepSublabel(s), depth: 2, bars: sb ? [sb] : [] })
      }
    }
  }

  return rows
}

/** Motivo (código do PLN-01b) em texto. */
export function reasonText(reason: string | null | undefined): string {
  switch (reason) {
    case 'NoRouting':
      return 'OS sem roteiro'
    case 'NoWorkCenter':
      return 'etapa sem centro de trabalho'
    case 'NoCapacity':
      return 'centro sem capacidade'
    case 'NoFabricationOrder':
      return 'sem OS de fabricação'
    default:
      return reason ?? 'aguardando o plano'
  }
}

/** Dia do centro (CA 5): o tom da ocupação e o texto (o estado escrito junto da cor); feriado e parada com o nome. */
export function centerDay(day: GanttCenterDay): GanttDay {
  const date = formatDate(day.date).slice(0, 5)
  if (day.exceptionName) {
    const kind = day.exceptionType === 'Stoppage' ? 'Parada' : 'Feriado'
    return { date: day.date, tone: day.capacityHours <= 0 ? 'off' : 'normal', mark: day.exceptionName, title: `${date}: ${kind} ${day.exceptionName}${day.capacityHours > 0 ? ` (${hoursText(day.capacityHours)})` : ''}` }
  }

  if (day.band === 'NoCapacity') return { date: day.date, tone: 'off', title: `${date}: sem expediente` }
  const percent = day.percent === null ? '—' : `${day.percent.toLocaleString('pt-BR', { maximumFractionDigits: 1 })}%`
  const label = day.band === 'Over' ? 'Acima da capacidade' : day.band === 'Full' ? 'Lotado' : day.band === 'Idle' ? 'Folga' : ''
  const tone = day.band === 'Over' ? 'over' : day.band === 'Full' ? 'full' : day.band === 'Idle' ? 'idle' : 'normal'
  return { date: day.date, tone, title: `${date}: ${hoursText(day.allocatedHours).replace(/ h$/, '')} de ${hoursText(day.capacityHours)}, ${percent}${label ? `, ${label}` : ''}` }
}

/** Barra da etapa no centro (CA 5 e 8). */
export function centerBar(bar: GanttCenterBar, center: Pick<GanttCenterRow, 'name'>): GanttBar {
  const running = bar.state === 'InProgress'
  const name = `${bar.code ? `${bar.code} · ` : ''}${bar.stepName}`
  const project = bar.projectNumber ? `${bar.projectNumber}${bar.projectName ? ` · ${bar.projectName}` : ''}` : 'Projeto removido'
  const percent = Math.round(bar.progress * 100)
  return {
    id: `c:${bar.stepId}`, start: running && bar.actualStart && bar.actualStart < bar.plannedStart ? bar.actualStart : bar.plannedStart, end: bar.plannedEnd,
    kind: running ? 'progress' : 'planned', progress: running ? bar.progress : undefined, letter: bar.code, lane: bar.lane,
    ariaLabel: `Etapa ${name}, ${center.name}, OS ${bar.orderNumber}, ${rangeText(bar.plannedStart, bar.plannedEnd, 'previsto')}, ${running ? `em andamento, ${percent}% feito` : 'prevista'}`,
    detail: {
      title: `${bar.orderNumber} · ${name}`,
      lines: [`Projeto: ${project}`, `Centro: ${center.name}`, `Horas restantes: ${hoursText(bar.remainingHours).replace(/ h$/, '')} de ${hoursText(bar.standardHours)}`,
        `Previsto: ${rangeText(bar.plannedStart, bar.plannedEnd, 'previsto').replace(/^previsto /, '')}`, ...(running && bar.actualStart ? [`Início real: ${formatDate(bar.actualStart)}`] : [])],
    },
  }
}

/** As linhas por centro (CA 5): uma por centro, com as sub-linhas, as barras e os dias. */
export function centerRows(centers: GanttCenterRow[]): GanttRow[] {
  return centers.map((c) => {
    const full = c.days.filter((d) => d.band === 'Full' || d.band === 'Over').length
    return {
      id: `w:${c.workCenterId}`, label: c.name, sublabel: [c.line, c.bars.length === 1 ? '1 etapa' : `${c.bars.length} etapas`, full > 0 ? `lotado em ${full} ${full === 1 ? 'dia' : 'dias'}` : null].filter(Boolean).join(' · '),
      depth: 0, lanes: c.lanes, bars: c.bars.map((b) => centerBar(b, c)), days: c.days.map(centerDay),
    }
  })
}

/** Resumo da visão por centro: "{n} centros; {m} etapas no período. Mais ocupado: {centro}, lotado em {d} dias." */
export function centersSummary(centers: GanttCenterRow[]): string {
  if (centers.length === 0) return 'Nenhum centro com esse filtro.'
  const bars = centers.reduce((n, c) => n + c.bars.length, 0)
  const head = `${centers.length === 1 ? '1 centro' : `${centers.length} centros`}; ${bars === 1 ? '1 etapa' : `${bars} etapas`} no período.`
  const busy = centers.map((c) => ({ c, d: c.days.filter((d) => d.band === 'Full' || d.band === 'Over').length })).filter((x) => x.d > 0).sort((a, b) => b.d - a.d)[0]
  return busy ? `${head} Mais ocupado: ${busy.c.name}, lotado em ${busy.d === 1 ? '1 dia' : `${busy.d} dias`}.` : head
}

/** O id da OS da barra (s:, o:, c:), para abrir a OS (CA 8). */
export function orderOfBar(barId: string, view: GanttProjectsView | null, centers: GanttCenterRow[] | null): { orderId: string; installation: boolean } | null {
  const [kind, id] = [barId.slice(0, 1), barId.slice(2)]
  if (kind === 'o' && view) {
    const order = view.projects.flatMap((p) => p.orders).find((o) => o.orderId === id)
    return order ? { orderId: order.orderId, installation: order.kind === 'Installation' } : null
  }

  if (kind === 's' && view) {
    const order = view.projects.flatMap((p) => p.orders).find((o) => o.steps.some((s) => s.stepId === id))
    return order ? { orderId: order.orderId, installation: false } : null
  }

  if (kind === 'c' && centers) {
    const bar = centers.flatMap((c) => c.bars).find((b) => b.stepId === id)
    return bar ? { orderId: bar.orderId, installation: false } : null
  }

  return null
}

/** Posição e largura (em %) de uma barra simples na largura da tela (400 px), no período. */
export function simpleBar(start: string, end: string, from: string, to: string): { left: number; width: number } | null {
  const total = Math.max(dayDiff(from, to) + 1, 1)
  const s = Math.max(dayDiff(from, start), 0)
  const e = Math.min(dayDiff(from, end) + 1, total)
  if (e <= 0 || s >= total || e <= s) return null
  return { left: (s / total) * 100, width: Math.max(((e - s) / total) * 100, 1.5) }
}

function dayDiff(a: string, b: string): number {
  return Math.round((Date.parse(`${b}T00:00:00Z`) - Date.parse(`${a}T00:00:00Z`)) / 86_400_000)
}
