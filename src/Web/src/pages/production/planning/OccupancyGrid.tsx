import type { FactoryLoadRow, FactoryLoadWeek } from '@/api/generated/model'
import { StatusChip } from '@/components/ui/status-chip'
import { TreeTable, type TreeColumn, type TreeNode } from '@/components/ui/tree-table'
import { bandChip, cellLabel, hoursOf, percentText, weekLabel } from './load'

interface LoadNode extends TreeNode {
  row: FactoryLoadRow
}

/**
 * Tabela analítica da ocupação (PLN-04 CA 7, padrão 3 das Fases 4 a 8) sobre a `TreeTable` da FIN-08 (D18): coluna do centro fixa na
 * rolagem horizontal (`stickyLabel`) e navegação por célula (`cellNavigation`: setas, Home/End na linha, Ctrl+Home/End no canto, Enter ou
 * Espaço abre o drill-down por `onActivateCell`). O nome da célula para o leitor de tela é a frase inteira (centro, semana, horas,
 * percentual e estado).
 */
export function OccupancyGrid({ label, weeks, rows, onOpen }: {
  label: string
  weeks: FactoryLoadWeek[]
  /** Os centros e, por último, a linha "Fábrica" (ou o total do filtro). */
  rows: FactoryLoadRow[]
  onOpen: (row: FactoryLoadRow, weekIndex: number) => void
}) {
  const nodes: LoadNode[] = rows.map((row) => ({ id: row.workCenterId ?? 'fabrica', label: row.name, row }))
  const columns: TreeColumn<LoadNode>[] = weeks.map((w, i) => ({
    key: w.start,
    header: weekLabel(w),
    numeric: true,
    className: 'min-w-32 align-top',
    cell: (node) => {
      const cell = node.row.cells[i]
      const chip = bandChip(cell.band)
      return (
        <span data-band={cell.band}>
          <span className="sr-only">{cellLabel(node.row, w, cell)}</span>
          <span aria-hidden="true" className="flex flex-col items-end">
            <span className="text-base font-semibold">{percentText(cell.percent)}</span>
            <span className="text-xs text-muted-foreground">{hoursOf(cell)}</span>
            {chip ? <StatusChip tone={chip.tone} className="mt-1">{chip.label}</StatusChip> : null}
          </span>
        </span>
      )
    },
  }))
  return (
    <TreeTable nodes={nodes} label={label} columns={columns} labelHeader="Centro de trabalho" searchable={false} defaultExpanded="none" cellNavigation stickyLabel
      onActivateCell={(node, column) => onOpen(node.row, column)}
      renderLabel={(node) => (
        <span className="block min-w-36">
          <span className="block">{node.row.name}</span>
          {node.row.line ? <span className="block text-xs text-muted-foreground">{node.row.line}</span> : null}
        </span>
      )}
      rowClassName={(node) => (node.row.workCenterId === null ? 'border-t-2 font-semibold' : undefined)}
      cellClassName={(node, column) => (node.row.cells[column]?.band === 'Over' ? 'bg-crit-soft text-crit-ink' : undefined)} />
  )
}
