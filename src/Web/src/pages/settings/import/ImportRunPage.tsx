import { Link, useParams } from '@tanstack/react-router'
import { useGetImportRun } from '@/api/generated/bemaqui'
import type { EntitySummary, ImportRunDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { PageHeading } from '@/shell/PageHeading'
import { entityLabel, MODE_LABELS, STATUS } from './labels'
import { PendingTable } from './PendingTable'

const TOTALS: [keyof EntitySummary, string][] = [
  ['read', 'Lidos'], ['imported', 'Importados'], ['updated', 'Atualizados'], ['unchanged', 'Sem alteração'], ['merged', 'Mesclados'],
  ['linked', 'Vinculados'], ['skipped', 'Ignorados'], ['conflicts', 'Conflitos'], ['errors', 'Erros'], ['warnings', 'Avisos'],
]

/** Detalhe de uma execução (MIG-01a CA 17 e 19): totais por entidade e pendências com motivo e link para o registro. */
export function ImportRunPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const query = useGetImportRun(id)

  if (query.isPending) return <div className="space-y-3"><Skeleton className="h-10 w-2/3" /><Skeleton className="h-40" /><Skeleton className="h-64" /></div>
  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return <ErrorState title={notFound ? 'Execução não encontrada.' : 'Não foi possível carregar a execução.'} message={notFound ? 'Volte à lista de importações.' : undefined} onRetry={notFound ? undefined : () => query.refetch()} />
  }

  const run = query.data
  return (
    <>
      <PageHeading title={`Importação de ${formatDateTime(run.startedAt)}`}>
        <p className="mt-2"><Link to="/app/configuracoes/importacao" className="text-sm font-semibold text-primary">Voltar às importações</Link></p>
      </PageHeading>
      <RunSummary run={run} />
      <Totals entities={run.entities} />
      <h2 className="mb-2 mt-6 text-base font-semibold">Pendências</h2>
      {run.purgedAt ? (
        <p role="status" className="rounded-md border border-border bg-card p-4 text-sm text-muted-foreground">
          A staging desta execução foi expurgada em {formatDateTime(run.purgedAt)} (LGPD). Os totais ficam; as pendências não.
        </p>
      ) : <PendingTable run={run} />}
    </>
  )
}

function RunSummary({ run }: { run: ImportRunDetail }) {
  return (
    <section aria-label="Resumo da execução" className="mb-5 grid gap-3 rounded-md border border-border bg-card p-4 text-sm sm:grid-cols-2">
      <p><span className="text-muted-foreground">Situação: </span><StatusChip tone={STATUS[run.status]?.tone ?? 'neutral'}>{STATUS[run.status]?.label ?? run.status}</StatusChip></p>
      <p><span className="text-muted-foreground">Modo: </span><Tag>{MODE_LABELS[run.mode] ?? run.mode}</Tag>{run.mode === 'DryRun' ? <Dica texto="Simulação: tudo foi executado e desfeito no fim. Nada foi gravado nos cadastros." /> : null}</p>
      <p><span className="text-muted-foreground">Operador: </span>{run.operator}</p>
      <p><span className="text-muted-foreground">Snapshot extraído em: </span><span className="tabular-nums">{run.snapshotTakenAt ? formatDateTime(run.snapshotTakenAt) : '—'}</span></p>
      {run.acceptedAt ? <p><span className="text-muted-foreground">Aceite da carga: </span><span className="tabular-nums">{formatDateTime(run.acceptedAt)}</span></p> : null}
      {run.failure ? <p role="alert" className="text-crit-ink sm:col-span-2">{run.failure}</p> : null}
    </section>
  )
}

function Totals({ entities }: { entities: EntitySummary[] }) {
  if (entities.length === 0) return <p className="text-sm text-muted-foreground">Nenhuma entidade foi processada nesta execução.</p>
  return (
    // Rolável no celular: focável e com nome, para quem usa teclado (axe scrollable-region-focusable).
    <div role="region" aria-label="Totais por entidade, role para o lado" tabIndex={0} className="overflow-x-auto rounded-md border border-border bg-card">
      <table className="w-full border-collapse text-sm" aria-label="Totais por entidade">
        <thead className="border-b border-border text-xs text-muted-foreground">
          <tr>
            <th scope="col" className="px-3 py-2 text-left font-semibold">Entidade</th>
            {TOTALS.map(([key, label]) => <th key={key} scope="col" className="px-3 py-2 text-right font-semibold">{label}</th>)}
          </tr>
        </thead>
        <tbody>
          {entities.map((e) => (
            <tr key={e.entity} className="border-b border-border last:border-0">
              <th scope="row" className="px-3 py-2 text-left font-semibold">{entityLabel(e.entity)}</th>
              {TOTALS.map(([key]) => <td key={key} className="px-3 py-2 text-right tabular-nums">{Number(e[key] ?? 0).toLocaleString('pt-BR')}</td>)}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
