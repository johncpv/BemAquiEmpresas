import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { useListNonConformities } from '@/api/generated/bemaqui'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { PageHeading } from '@/shell/PageHeading'
import { ProductionTabs } from '../ProductionTabs'
import { ageLabel, NC_SITUATIONS, NC_STATUS, QUALITY_DICAS } from './quality'

const action = 'inline-flex min-h-9 items-center rounded-md border border-border px-3 text-sm font-semibold text-primary'
const pill = 'rounded-full border px-3 py-1 text-sm font-semibold aria-pressed:border-primary aria-pressed:bg-primary-soft'

/**
 * Não conformidades (/app/producao/nao-conformidades, CA 11): número, OS, projeto, etapa, responsável, situação e idade. Filtro padrão
 * "Aguardando você" (eu sou o responsável e não está encerrada); "Todas" e a situação ficam no endereço (?filtro=todas&situacao=).
 * A 400 px: número, OS, responsável e situação.
 */
export function NonConformitiesPage() {
  const navigate = useNavigate()
  const search = useSearch({ strict: false }) as { filtro?: string; situacao?: string; pagina?: number }
  const all = search.filtro === 'todas'
  const page = Number(search.pagina) > 0 ? Number(search.pagina) : 1
  const query = useListNonConformities({ filter: all ? 'todas' : undefined, situation: search.situacao || undefined, page, pageSize: 25 })
  const go = (next: Partial<{ filtro: string; situacao: string; pagina: number }>) =>
    void navigate({ to: '/app/producao/nao-conformidades', search: (prev: Record<string, unknown>) => ({ ...prev, pagina: undefined, ...next }) } as never)
  let body
  if (query.isPending) body = <Skeleton className="h-40" />
  else if (query.isError) body = <ErrorState title="Não foi possível carregar as não conformidades." onRetry={() => query.refetch()} />
  else if (query.data.items.length === 0) {
    body = (
      <EmptyState title="Nenhuma não conformidade aberta." description={all ? 'As reprovações na qualidade aparecem aqui, com a OS e o responsável.' : 'Nenhuma NC com você como responsável aguardando ação.'}
        action={all ? <Link to="/app/producao/qualidade" className={action}>Ver a fila de qualidade</Link> : <button type="button" className={action} onClick={() => go({ filtro: 'todas' })}>Ver todas</button>} />
    )
  } else {
    const pages = Math.max(1, Math.ceil(query.data.total / query.data.pageSize))
    body = (
      <>
        <div className="overflow-x-auto rounded-md border border-border bg-card">
          <table className="w-full text-sm">
            <caption className="sr-only">Não conformidades</caption>
            <thead className="border-b border-border text-left text-xs uppercase text-muted-foreground">
              <tr><th className="p-2">Número</th><th className="p-2">OS</th><th className="p-2 max-sm:hidden">Projeto</th><th className="p-2 max-sm:hidden">Etapa</th><th className="p-2">Responsável</th><th className="p-2">Situação</th><th className="p-2 text-right max-sm:hidden">Idade</th></tr>
            </thead>
            <tbody className="divide-y divide-border">
              {query.data.items.map((n) => (
                <tr key={n.id} className="align-top">
                  <td className="p-2"><Link to="/app/producao/nao-conformidades/$id" params={{ id: n.id }} className="font-semibold text-primary tabular-nums hover:underline">{n.number}</Link>
                    <span className="block max-w-56 truncate text-xs text-muted-foreground">{n.description}</span></td>
                  <td className="p-2"><Link to="/app/producao/os/$id" params={{ id: n.orderId }} search={{ aba: 'qualidade' } as never} className="tabular-nums text-primary hover:underline">{n.orderNumber}</Link></td>
                  <td className="p-2 tabular-nums max-sm:hidden">{n.projectNumber}</td>
                  <td className="p-2 max-sm:hidden">{n.stepCode} · {n.stepName}</td>
                  <td className="p-2">{n.responsibleName}</td>
                  <td className="p-2"><StatusChip tone={NC_STATUS[n.status].tone}>{NC_STATUS[n.status].label}</StatusChip></td>
                  <td className="p-2 text-right tabular-nums max-sm:hidden">{ageLabel(n.ageDays)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {pages > 1 ? (
          <div className="mt-3 flex items-center gap-2 text-sm">
            <button type="button" disabled={page <= 1} onClick={() => go({ pagina: page - 1 })} className="rounded-md border border-border px-3 py-1.5 font-semibold disabled:opacity-50">Anterior</button>
            <span className="tabular-nums">{page} de {pages}</span>
            <button type="button" disabled={page >= pages} onClick={() => go({ pagina: page + 1 })} className="rounded-md border border-border px-3 py-1.5 font-semibold disabled:opacity-50">Próxima</button>
          </div>
        ) : null}
      </>
    )
  }
  return (
    <>
      <PageHeading title="Produção: Não conformidades" />
      <ProductionTabs />
      <div className="mb-3 flex flex-wrap items-center gap-2">
        <div role="group" aria-label="De quem" className="flex flex-wrap gap-2">
          <button type="button" className={pill} aria-pressed={!all} onClick={() => go({ filtro: undefined })}>Aguardando você{query.data ? ` (${query.data.mineOpen})` : ''}</button>
          <button type="button" className={pill} aria-pressed={all} onClick={() => go({ filtro: 'todas' })}>Todas</button>
        </div>
        <label className="flex items-center gap-2 text-sm">
          <span className="font-medium">Situação</span>
          <select value={search.situacao ?? ''} onChange={(e) => go({ situacao: e.target.value || undefined })} className="min-h-9 rounded-md border border-input bg-card px-2">
            <option value="">Todas</option>
            {NC_SITUATIONS.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
          </select>
        </label>
        <Dica nome="Não conformidade" texto={QUALITY_DICAS.naoConformidade} />
      </div>
      {body}
    </>
  )
}