import type { CostType, ProjectMarginLine, ProjectTaxBasis } from '@/api/generated/model'
import { Money } from '@/components/ui/money'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { cn } from '@/lib/utils'
import { PROJECT_DICAS } from '../projects'
import { COLUMN_LABEL, costLabel, type CostColumn, MARGIN_DICAS, pct1 } from './margins'

interface CostLinesTableProps {
  label: string
  lines: ProjectMarginLine[]
  total: ProjectMarginLine
  taxBasis: ProjectTaxBasis
  /** Abre o drill-down da célula (CA 13 e 15). */
  onCell: (costType: CostType, column: CostColumn) => void
  /** Natureza destacada (o chip "Margem abaixo" leva a ela). */
  highlight?: string
}

const TAX_MARK: Record<ProjectTaxBasis, string> = { Budgeted: 'Imposto orçado', Invoices: 'Imposto das notas + orçado no restante' }

function Deviation({ line }: { line: ProjectMarginLine }) {
  const up = line.deviation > 0
  return <span className={up ? 'tabular-nums text-crit-ink' : 'tabular-nums'}>{up ? '+' : ''}<Money value={line.deviation} /> ({up ? '+' : ''}{pct1(line.deviationPct)})</span>
}

/** Marca "Calculado na implantação" (padrão 11 da Fase 3, PRD-04 CA 13) e a do imposto projetado (DSH-02 CA 5). */
function Marks({ line, taxBasis }: { line: ProjectMarginLine; taxBasis: ProjectTaxBasis }) {
  return (
    <>
      {line.includesBackfill ? <span className="ml-1 inline-flex items-center align-middle"><Tag>Calculado na implantação</Tag><Dica nome="Calculado na implantação" texto={PROJECT_DICAS.implantacao} /></span> : null}
      {line.costType === 'Tax' ? <span className="ml-1 inline-flex items-center align-middle"><Tag>{TAX_MARK[taxBasis]}</Tag>{taxBasis === 'Budgeted' ? <Dica nome="Imposto orçado" texto={MARGIN_DICAS.impostoOrcado} /> : null}</span> : null}
    </>
  )
}

function Cell({ line, column, onCell }: { line: ProjectMarginLine; column: CostColumn; onCell: CostLinesTableProps['onCell'] }) {
  const value = line[column]
  return (
    <button type="button" onClick={() => onCell(line.costType, column)} aria-label={`${COLUMN_LABEL[column]} de ${costLabel(line.costType)}: ver origem`}
      className="min-h-[44px] rounded px-1 text-right underline decoration-dotted underline-offset-4 hover:text-primary sm:min-h-0">
      <Money value={value} />
    </button>
  )
}

const head = (text: string, dica?: string) => <span className="inline-flex items-center">{text}{dica ? <Dica nome={text} texto={dica} /> : null}</span>

/**
 * Tabela por natureza (DSH-02 CA 1, 10, 13 e 15; padrão 3): orçado, realizado, comprometido, projetado e desvio; orçado, realizado e
 * comprometido abrem o drill-down. A 400 px, cartões com orçado, realizado e projetado; comprometido e desvio na linha expansível.
 */
export function CostLinesTable({ label, lines, total, taxBasis, onCell, highlight }: CostLinesTableProps) {
  return (
    <>
      <table className="hidden w-full text-sm sm:table" aria-label={label}>
        <thead className="text-xs text-muted-foreground">
          <tr>
            <th className="py-1 text-left">Natureza</th>
            <th className="py-1 text-right">Orçado</th>
            <th className="py-1 text-right">{head('Realizado', MARGIN_DICAS.realizado)}</th>
            <th className="py-1 text-right">{head('Comprometido', MARGIN_DICAS.comprometido)}</th>
            <th className="py-1 text-right">{head('Projetado', MARGIN_DICAS.projetado)}</th>
            <th className="py-1 text-right">{head('Desvio', MARGIN_DICAS.desvio)}</th>
          </tr>
        </thead>
        <tbody>
          {lines.map((line) => (
            <tr key={line.costType} data-highlight={line.costType === highlight || undefined} className={cn('border-t border-border', line.costType === highlight && 'bg-warn-soft')}>
              <td className="py-1.5">{costLabel(line.costType)}<Marks line={line} taxBasis={taxBasis} /></td>
              <td className="text-right"><Cell line={line} column="budgeted" onCell={onCell} /></td>
              <td className="text-right"><Cell line={line} column="realized" onCell={onCell} /></td>
              <td className="text-right"><Cell line={line} column="committed" onCell={onCell} /></td>
              <td className="text-right"><Money value={line.projected} /></td>
              <td className="text-right"><Deviation line={line} /></td>
            </tr>
          ))}
          <tr className="border-t border-border font-semibold">
            <td className="py-1.5">Total</td>
            <td className="text-right"><Money value={total.budgeted} /></td>
            <td className="text-right"><Money value={total.realized} /></td>
            <td className="text-right"><Money value={total.committed} /></td>
            <td className="text-right"><Money value={total.projected} /></td>
            <td className="text-right"><Deviation line={total} /></td>
          </tr>
        </tbody>
      </table>
      <ul className="space-y-2 sm:hidden" aria-label={`${label} em cartões`}>
        {lines.map((line) => (
          <li key={line.costType} data-highlight={line.costType === highlight || undefined} className={cn('rounded-md border border-border bg-card p-3', line.costType === highlight && 'border-warn bg-warn-soft')}>
            <b>{costLabel(line.costType)}</b><Marks line={line} taxBasis={taxBasis} />
            <p className="flex flex-wrap items-center gap-x-1">Orçado <Cell line={line} column="budgeted" onCell={onCell} /> · Realizado <Cell line={line} column="realized" onCell={onCell} /></p>
            <p>Projetado <Money value={line.projected} /></p>
            <details className="mt-1">
              <summary className="min-h-[44px] cursor-pointer py-2 text-xs font-semibold text-primary">Comprometido e desvio</summary>
              <p className="flex items-center gap-1">Comprometido <Cell line={line} column="committed" onCell={onCell} /></p>
              <p>Desvio <Deviation line={line} /></p>
            </details>
          </li>
        ))}
        <li className="rounded-md border border-border bg-card p-3 font-semibold">
          <b>Total</b>
          <p>Orçado <Money value={total.budgeted} /> · Realizado <Money value={total.realized} /></p>
          <p>Projetado <Money value={total.projected} /></p>
          <details className="mt-1">
            <summary className="min-h-[44px] cursor-pointer py-2 text-xs font-semibold text-primary">Comprometido e desvio</summary>
            <p>Comprometido <Money value={total.committed} /></p>
            <p>Desvio <Deviation line={total} /></p>
          </details>
        </li>
      </ul>
    </>
  )
}
