import { Link } from '@tanstack/react-router'
import { useGetDashboardProjectResultCell } from '@/api/generated/bemaqui'
import type { ProjectResultEntry } from '@/api/generated/model'
import { Money } from '@/components/ui/money'
import { SidePanel } from '@/components/ui/side-panel'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { formatDate } from '@/lib/format'
import { ENTRY_KIND } from '@/pages/projects/margins/margins'
import { monthLong } from '../owner'

/** Célula aberta: a linha (revenue ou a natureza), o rótulo, o mês (nulo = o período todo) e o valor mostrado. */
export interface ResultCellTarget {
  line: string
  label: string
  month: string | null
  value: number
}

const KIND: Record<string, string> = { ...ENTRY_KIND, Receivable: 'Título a receber', DownPayment: 'Sinal reconhecido' }

function Item({ entry }: { entry: ProjectResultEntry }) {
  const title = entry.link ? <Link to={entry.link as never} className="font-semibold text-primary hover:underline">{entry.document}</Link> : <span className="font-semibold">{entry.document}</span>
  return (
    <li className="flex flex-wrap items-start justify-between gap-2 py-2">
      <div className="min-w-0 space-y-0.5">
        <p className="text-xs text-muted-foreground">{KIND[entry.kind] ?? entry.kind}{entry.date ? ` · ${formatDate(entry.date)}` : ''}{typeof entry.hours === 'number' ? ` · ${entry.hours.toLocaleString('pt-BR')} h` : ''}</p>
        {title}
        {entry.note ? <p className="text-xs text-muted-foreground">{entry.note}</p> : null}
      </div>
      <Money value={entry.amount} />
    </li>
  )
}

/**
 * Drill-down da célula do resultado por projeto (DSH-01b CA 8): os títulos e sinais da receita ou os lançamentos do realizado da natureza
 * (horas agregadas por OS e centro), com o total igual ao da célula. Em tela cheia a 400 px.
 */
export function ResultCellPanel({ projectId, from, to, target, onClose }: { projectId: string; from: string; to: string; target: ResultCellTarget; onClose: () => void }) {
  const query = useGetDashboardProjectResultCell(projectId, { line: target.line, month: target.month ?? undefined, from, to })
  let body
  if (query.isPending) body = <Skeleton className="h-40" />
  else if (query.isError) body = <ErrorState title="Não foi possível abrir os documentos da célula." message="Verifique a conexão e tente de novo." onRetry={() => void query.refetch()} />
  else if (query.data.entries.length === 0) body = <EmptyState title="Nenhum documento soma esta célula." description="Receita entra pela emissão do título; custo, pela data da baixa, do apontamento, da nota ou da emissão do título." />
  else body = <ul className="divide-y divide-border text-sm" aria-label="Documentos da célula">{query.data.entries.map((e, i) => <Item key={`${e.kind}-${e.document}-${i}`} entry={e} />)}</ul>

  return (
    <SidePanel open onOpenChange={(o) => { if (!o) onClose() }} title={target.label} description={target.month ? monthLong(target.month) : 'Período inteiro'}
      footer={query.data ? <p className="flex w-full items-center justify-between text-sm font-semibold"><span>Total da célula</span><Money value={query.data.total} /></p> : undefined}>
      {body}
    </SidePanel>
  )
}
