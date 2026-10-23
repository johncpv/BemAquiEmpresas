import { useState } from 'react'
import { useCompareBoms, useGetOrderBomHistory } from '@/api/generated/bemaqui'
import type { BomRevisionSummary } from '@/api/generated/model'
import { describeChanges } from '@/components/history/describe-changes'
import { inputClass } from '@/components/form/form-utils'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { BOM_DICAS, BOM_STATUS, qty } from './boms'

const CHANGE: Record<string, { label: string; tone: 'ok' | 'crit' | 'warn' | 'neutral' }> = {
  Added: { label: 'Incluído', tone: 'ok' }, Removed: { label: 'Removido', tone: 'crit' }, Changed: { label: 'Alterado', tone: 'warn' }, Unchanged: { label: 'Igual', tone: 'neutral' },
}

/** "Revisões" (CA 15): cada revisão com a situação, quem liberou e quando, e a comparação lado a lado de duas delas. */
export function BomRevisions({ revisions }: { revisions: BomRevisionSummary[] }) {
  const [from, setFrom] = useState(revisions[1]?.id ?? '')
  const [to, setTo] = useState(revisions[0]?.id ?? '')
  const comparing = !!from && !!to && from !== to
  const comparison = useCompareBoms({ from, to }, { query: { enabled: comparing } })
  if (revisions.length === 0) return <EmptyState title="Esta OS ainda não tem revisões de BOM." description="A primeira revisão nasce em Criar BOM." />
  const label = (r: BomRevisionSummary) => `Revisão ${r.revision} · ${BOM_STATUS[r.status]?.label ?? r.status}`
  return (
    <div className="space-y-4">
      <ul aria-label="Revisões da BOM" className="space-y-2">
        {revisions.map((r) => (
          <li key={r.id} className="flex flex-wrap items-center gap-x-3 gap-y-1 rounded-md border border-border bg-card px-3 py-2 text-sm">
            <b className="font-semibold">Revisão {r.revision}</b>
            <span className="inline-flex items-center">
              <StatusChip tone={BOM_STATUS[r.status]?.tone ?? 'neutral'}>{BOM_STATUS[r.status]?.label ?? r.status}</StatusChip>
              {r.status === 'Superseded' ? <Dica nome="Substituída" texto={BOM_DICAS.substituida} /> : null}
            </span>
            <span className="tabular-nums text-muted-foreground">{r.lineCount} {r.lineCount === 1 ? 'item' : 'itens'}</span>
            <span className="text-muted-foreground">{r.releasedAt ? `Liberada em ${formatDateTime(r.releasedAt)}${r.releasedByName ? ` por ${r.releasedByName}` : ''}` : `Criada em ${formatDateTime(r.createdAt)}`}</span>
          </li>
        ))}
      </ul>
      {revisions.length > 1 ? (
        <section aria-labelledby="comparar-titulo" className="space-y-2">
          <h3 id="comparar-titulo" className="text-base font-semibold">Comparar revisões</h3>
          <div className="grid gap-2 sm:grid-cols-2">
            <label className="space-y-1 text-sm font-medium">De
              <select value={from} onChange={(e) => setFrom(e.target.value)} className={inputClass}>{revisions.map((r) => <option key={r.id} value={r.id}>{label(r)}</option>)}</select>
            </label>
            <label className="space-y-1 text-sm font-medium">Para
              <select value={to} onChange={(e) => setTo(e.target.value)} className={inputClass}>{revisions.map((r) => <option key={r.id} value={r.id}>{label(r)}</option>)}</select>
            </label>
          </div>
          {!comparing ? <p className="text-sm text-muted-foreground">Escolha duas revisões diferentes.</p> : comparison.isPending ? <Skeleton className="h-24" /> : comparison.isError ? (
            <ErrorState title="Não foi possível comparar." onRetry={() => comparison.refetch()} />
          ) : (
            <ul aria-label="Diferenças entre as revisões" className="space-y-2">
              {comparison.data.lines.map((l) => (
                <li key={l.itemId} className="grid gap-1 rounded-md border border-border bg-card px-3 py-2 text-sm sm:grid-cols-[minmax(0,2fr)_minmax(0,1fr)_minmax(0,1fr)]">
                  <span><StatusChip tone={CHANGE[l.change]?.tone ?? 'neutral'}>{CHANGE[l.change]?.label ?? l.change}</StatusChip> <b className="font-semibold">{l.itemCode}</b> {l.itemName}</span>
                  <span className="tabular-nums text-muted-foreground">Rev. {comparison.data.fromRevision}: {l.fromQuantity === null || l.fromQuantity === undefined ? '—' : `${qty(l.fromQuantity, l.fromUnitCode ?? undefined)} · perda ${qty(l.fromScrapPct)}% · OS ${qty(l.fromRequired, l.stockUnitCode)}`}</span>
                  <span className="tabular-nums">Rev. {comparison.data.toRevision}: {l.toQuantity === null || l.toQuantity === undefined ? '—' : `${qty(l.toQuantity, l.toUnitCode ?? undefined)} · perda ${qty(l.toScrapPct)}% · OS ${qty(l.toRequired, l.stockUnitCode)}`}</span>
                </li>
              ))}
            </ul>
          )}
        </section>
      ) : null}
    </div>
  )
}

const ACTIONS: Record<string, string> = { Created: 'Criou', Updated: 'Alterou', Deleted: 'Excluiu' }
const ENTITY: Record<string, string> = { Bom: 'BOM', BomLine: 'Linha da BOM' }

/** "Histórico de alterações" da BOM da OS (CA 7 e 21): todas as revisões e linhas, inclusive o rascunho descartado, com antes e depois. */
export function BomHistory({ orderId }: { orderId: string }) {
  const [page, setPage] = useState(1)
  const query = useGetOrderBomHistory(orderId, { page, pageSize: 20 })
  if (query.isPending) return <div className="space-y-2">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-10" />)}</div>
  if (query.isError) return <ErrorState title="Não foi possível carregar o histórico." onRetry={() => query.refetch()} />
  const data = query.data
  if (data.items.length === 0) return <EmptyState title="Sem alterações registradas." description="Criar, salvar, liberar, revisar e descartar a BOM aparecem aqui, com quem fez e quando." />
  const pages = Math.max(1, Math.ceil(data.total / data.pageSize))
  return (
    <div>
      <ol className="space-y-3">
        {data.items.map((entry) => (
          <li key={entry.id} className="rounded-md border border-border bg-card p-3 text-sm">
            <p>
              <b className="font-semibold">{ACTIONS[entry.action] ?? entry.action}</b> <span>{ENTITY[entry.entity] ?? entry.entity}</span>
              <span className="text-muted-foreground"> · {entry.userId ? 'usuário ' + entry.userId.slice(0, 8) : entry.origin ?? 'sistema'} · </span>
              <time className="tabular-nums text-muted-foreground" dateTime={entry.at}>{formatDateTime(entry.at)}</time>
            </p>
            <ul className="mt-1 text-muted-foreground">{describeChanges(entry.changes).map((line) => <li key={line}>{line}</li>)}</ul>
          </li>
        ))}
      </ol>
      {pages > 1 ? (
        <div className="mt-3 flex items-center gap-2 text-sm">
          <button type="button" disabled={page <= 1} onClick={() => setPage(page - 1)} className="rounded-md border border-border px-3 py-1.5 font-semibold disabled:opacity-50">Anterior</button>
          <span className="tabular-nums">{page} de {pages}</span>
          <button type="button" disabled={page >= pages} onClick={() => setPage(page + 1)} className="rounded-md border border-border px-3 py-1.5 font-semibold disabled:opacity-50">Próxima</button>
        </div>
      ) : null}
    </div>
  )
}
