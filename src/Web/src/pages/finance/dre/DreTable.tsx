import { History } from 'lucide-react'
import { useMemo } from 'react'
import type { DreReport, DreRow } from '@/api/generated/model'
import { StatusChip } from '@/components/ui/status-chip'
import { type TreeColumn, TreeTable } from '@/components/ui/tree-table'
import { Dica } from '@/help/Dica'
import { formatMoney } from '@/lib/format'
import { cn } from '@/lib/utils'
import { DRE_DICAS, type DreNode, formatPercent, formatSigned, isCritical, isDrillable, monthDelta, monthLabel, monthLong, monthPercent, toTree } from './dre'

/** Célula aberta no drill-down: conta ou grupo e o mês (nulo = o período todo). */
export interface DreCellTarget {
  row: DreRow
  month: string | null
}

const money = (v: number) => formatMoney(v)

/** Rótulo com as dicas dos termos que confundem e o chip "Provisório" na receita (CA 3). */
function Label({ node, provisional, classify }: { node: DreNode; provisional: boolean; classify: Set<string> }) {
  const r = node.row
  const strong = r.kind !== 'Account'
  const dica = (texto: string, nome: string) => <Dica texto={texto} nome={nome} />
  return (
    <span className={cn('inline-flex flex-wrap items-center gap-1', strong && 'font-semibold')}>
      {r.code ? <span className="tabular-nums text-muted-foreground">{r.code}</span> : null}
      <span>{r.label}</span>
      {r.key === 'group:GrossRevenue' && provisional ? <><StatusChip tone="info">Provisório</StatusChip>{dica(DRE_DICAS.provisorio, 'Provisório')}</> : null}
      {r.key === 'subtotal:NetRevenue' ? dica(DRE_DICAS.receitaLiquida, 'Receita líquida') : null}
      {r.key === 'subtotal:GrossProfit' ? <>{dica(DRE_DICAS.lucroBruto, 'Lucro bruto')}{r.percentOfNetRevenue != null ? <span className="text-xs font-normal text-muted-foreground">margem bruta {formatPercent(r.percentOfNetRevenue)}</span> : null}</> : null}
      {r.key === 'group:Costs' ? dica(DRE_DICAS.custos, 'Custos') : null}
      {r.key === 'group:FinancialResult' ? dica(DRE_DICAS.resultadoFinanceiro, 'Resultado financeiro') : null}
      {r.key === 'memo:Advances' ? dica(DRE_DICAS.adiantamentos, 'Adiantamentos de clientes') : null}
      {r.accountId && classify.has(r.accountId) ? dica(DRE_DICAS.aClassificar, 'A classificar') : null}
    </span>
  )
}

const valueClass = (row: DreRow, value: number | null | undefined) => cn(isCritical(row, value) && 'font-semibold text-crit-ink', row.kind !== 'Account' && 'font-semibold')

/**
 * Tabela do DRE (CA 2 e 12): `TreeTable` analítica com a coluna de nomes fixa e navegação por célula. No desktop, uma coluna
 * por mês, o Total, o "% da receita líquida" e o "Δ mês anterior"; a 400 px, um mês por vez (Mês, Δ e %).
 */
export function DreTable({ report, mobile, monthIndex, onOpen }: { report: DreReport; mobile: boolean; monthIndex: number; onOpen: (target: DreCellTarget) => void }) {
  const nodes = useMemo(() => toTree(report.rows), [report.rows])
  const net = report.rows.find((r) => r.key === 'subtotal:NetRevenue')
  const classify = useMemo(() => new Set(report.toClassify.map((c) => c.accountId)), [report.toClassify])
  const months = report.months

  const deltaHeader = <span className="inline-flex items-center">Δ mês anterior<Dica texto={DRE_DICAS.delta} nome="Δ mês anterior" /></span>
  let columns: TreeColumn<DreNode>[]
  let monthOf: (columnIndex: number) => string | null
  let drillable: (columnIndex: number) => boolean
  if (mobile) {
    const m = months[monthIndex]
    columns = [
      { key: 'mes', header: m ? monthLabel(m.month) : 'Mês', title: m ? monthLong(m.month) : 'Mês', numeric: true, cell: (n) => money(n.row.values[monthIndex] ?? 0) },
      { key: 'delta', header: deltaHeader, title: 'Δ mês anterior', numeric: true, cell: (n) => formatSigned(monthDelta(n.row.values, monthIndex).delta, money) },
      { key: 'pct', header: '%', title: '% da receita líquida', numeric: true, cell: (n) => (n.row.kind === 'Memo' ? '—' : formatPercent(monthPercent(n.row.values[monthIndex] ?? 0, net?.values[monthIndex] ?? 0))) },
    ]
    monthOf = () => m?.month ?? null
    drillable = (c) => c === 0
  } else {
    columns = [
      ...months.map((m, i): TreeColumn<DreNode> => ({
        key: m.month,
        title: `${monthLong(m.month)}${m.isProvisional ? ', provisório' : ''}${m.changed ? ', mudou desde a sua última consulta' : ''}`,
        header: (
          <span className="inline-flex items-center gap-1">
            {monthLabel(m.month)}
            {m.changed ? <History aria-label="Mudou" className="size-3.5 text-info-ink" /> : null}
          </span>
        ),
        numeric: true,
        cell: (n) => money(n.row.values[i] ?? 0),
      })),
      { key: 'total', header: 'Total', numeric: true, cell: (n) => money(n.row.total) },
      { key: 'pct', header: '% da receita líquida', numeric: true, cell: (n) => formatPercent(n.row.percentOfNetRevenue) },
      { key: 'delta', header: deltaHeader, title: 'Δ mês anterior', numeric: true, cell: (n) => <>{formatSigned(n.row.delta, money)}<span className="block text-xs text-muted-foreground">{formatPercent(n.row.deltaPercent)}</span></> },
    ]
    monthOf = (c) => (c < months.length ? months[c].month : null)
    drillable = (c) => c <= months.length
  }

  return (
    <TreeTable<DreNode>
      label="Demonstração de resultado"
      nodes={nodes}
      columns={columns}
      labelHeader="Linha"
      searchable={false}
      defaultExpanded={mobile ? 'none' : 'roots'}
      cellNavigation
      stickyLabel
      renderLabel={(n) => <Label node={n} provisional={report.isProvisional} classify={classify} />}
      canActivate={(n, c) => isDrillable(n.row) && drillable(c)}
      onActivateCell={(n, c) => onOpen({ row: n.row, month: monthOf(c) })}
      rowClassName={(n) => cn(n.row.kind === 'Subtotal' && 'bg-muted/60', n.row.kind === 'Memo' && 'text-muted-foreground')}
      cellClassName={(n, c) => {
        if (mobile) return c === 0 ? valueClass(n.row, n.row.values[monthIndex]) : undefined
        if (c < months.length) return valueClass(n.row, n.row.values[c])
        return c === months.length ? valueClass(n.row, n.row.total) : undefined
      }}
    />
  )
}
