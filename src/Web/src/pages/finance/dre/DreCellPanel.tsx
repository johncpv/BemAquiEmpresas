import { Link } from '@tanstack/react-router'
import { ShieldCheck } from 'lucide-react'
import { useState } from 'react'
import { useGetDreCell } from '@/api/generated/bemaqui'
import type { DreCellItem } from '@/api/generated/model'
import { inputClass } from '@/components/form/form-utils'
import { Money } from '@/components/ui/money'
import { SidePanel } from '@/components/ui/side-panel'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { formatDate } from '@/lib/format'
import { KIND_LABELS } from './dre'
import type { DreCellTarget } from './DreTable'

const SORTS = [
  { value: 'date', label: 'Data (mais antiga)' },
  { value: '-date', label: 'Data (mais recente)' },
  { value: '-amount', label: 'Maior valor' },
  { value: 'amount', label: 'Menor valor' },
  { value: 'number', label: 'Número' },
] as const

function Item({ item }: { item: DreCellItem }) {
  if (item.protected) {
    return (
      <li className="space-y-1 rounded-md border border-border bg-muted p-3">
        <p className="flex items-center gap-1.5 text-xs font-semibold text-muted-foreground"><ShieldCheck aria-hidden="true" className="size-3.5" />Dados protegidos</p>
        <div className="flex flex-wrap items-start justify-between gap-2">
          <p className="font-semibold">{item.description}</p>
          <Money value={item.amount} />
        </div>
        {item.note ? <p className="text-xs text-muted-foreground">{item.note}</p> : null}
      </li>
    )
  }

  const title = `${item.number ? `${item.number}` : KIND_LABELS[item.kind] ?? item.kind}${item.description ? ` — ${item.description}` : ''}`
  return (
    <li className="flex flex-wrap items-start justify-between gap-2 py-2">
      <div className="min-w-0 space-y-0.5">
        <p className="text-xs text-muted-foreground">{KIND_LABELS[item.kind] ?? item.kind} · {formatDate(item.date)}</p>
        {item.link ? <Link to={item.link as never} className="font-semibold text-primary hover:underline">{title}</Link> : <span className="font-semibold">{title}</span>}
        {item.counterparty ? <p className="text-xs text-muted-foreground">{item.counterparty}</p> : null}
        {item.note ? <p className="text-xs text-muted-foreground">{item.note}</p> : null}
      </div>
      <Money value={item.amount} />
    </li>
  )
}

/**
 * Drill-down da célula (CA 13 e 14): os documentos que somam o valor, cada um com o link do documento de origem, e o total igual
 * ao da célula. Folha sem permissão de dado sensível aparece agregada, no bloco "Dados protegidos". Em tela cheia a 400 px.
 */
export function DreCellPanel({ target, from, to, onClose }: { target: DreCellTarget; from: string; to: string; onClose: () => void }) {
  const [sort, setSort] = useState<string>('date')
  const row = target.row
  const query = useGetDreCell({
    accountId: row.kind === 'Account' ? row.accountId ?? undefined : undefined,
    group: row.kind === 'Group' ? row.group ?? undefined : undefined,
    month: target.month ?? undefined,
    from,
    to,
    sort,
  })
  let body
  if (query.isPending) body = <Skeleton className="h-40" />
  else if (query.isError) body = <ErrorState title="Não foi possível abrir os documentos da célula." onRetry={() => query.refetch()} />
  else if (query.data.items.length === 0) body = <EmptyState title="Nenhum documento soma esta célula." description="Os títulos e lançamentos entram aqui pela data de competência." />
  else {
    body = (
      <div className="space-y-3">
        <label className="block space-y-1 text-sm font-medium">Ordenar por
          <select value={sort} onChange={(e) => setSort(e.target.value)} className={inputClass}>{SORTS.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}</select>
        </label>
        <ul className="divide-y divide-border text-sm" aria-label="Documentos da célula">
          {query.data.items.map((i, n) => <Item key={`${i.kind}-${i.id ?? n}-${i.date}-${n}`} item={i} />)}
        </ul>
        {query.data.truncated ? <p className="text-xs text-muted-foreground">Mostrando os primeiros 1.000 documentos; o total considera todos.</p> : null}
      </div>
    )
  }

  return (
    <SidePanel open onOpenChange={(o) => { if (!o) onClose() }} title={query.data?.label ?? row.label} description={target.month ? undefined : 'Período inteiro'}
      footer={query.data ? <p className="flex w-full items-center justify-between text-sm font-semibold"><span>Total da célula</span><Money value={query.data.total} /></p> : undefined}>
      {body}
    </SidePanel>
  )
}
