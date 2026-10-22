import { Link } from '@tanstack/react-router'
import { useListOpenLaborEntries } from '@/api/generated/bemaqui'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { formatDateTime } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { pauseLabel } from '@/pages/shopfloor/shopfloor'
import { PageHeading } from '@/shell/PageHeading'
import { hours } from '../orders/fabrication'
import { ProductionTabs } from '../ProductionTabs'

const action = 'inline-flex min-h-9 items-center rounded-md border border-border px-3 text-sm font-semibold text-primary'

/**
 * "Apontamentos abertos há mais de {x} h" (PRD-03a CA 22): funcionário, OS, etapa, início e a última pausa, do mais antigo para o
 * mais novo. Com production.approve, cada linha leva direto a "Corrigir apontamento" ou "Concluir etapa" na aba Apontamentos da OS.
 */
export function OpenLaborPage() {
  const query = useListOpenLaborEntries()
  const canApprove = useCan('production.approve')
  const limit = query.data?.hours ?? 12
  return (
    <>
      <PageHeading title="Produção: Apontamentos abertos" />
      <ProductionTabs />
      <p className="mb-3 text-sm text-muted-foreground">Apontamentos abertos há mais de {limit} h. Normalmente é um operador que esqueceu de finalizar ou pausar no fim do turno.</p>
      {query.isPending ? <Skeleton className="h-40" /> : query.isError ? <ErrorState title="Não foi possível carregar os apontamentos abertos." onRetry={() => query.refetch()} /> : query.data.items.length === 0 ? (
        <EmptyState title={`Nenhum apontamento aberto há mais de ${limit} h.`} description="Quando alguém esquecer de finalizar a etapa, ela aparece aqui para você corrigir ou concluir."
          action={<Link to="/app/producao/kanban" className={action}>Ver o Kanban</Link>} />
      ) : (
        <div className="overflow-x-auto rounded-md border border-border bg-card">
          <table className="w-full text-sm">
            <caption className="sr-only">Apontamentos abertos há mais de {limit} h</caption>
            <thead className="border-b border-border text-left text-xs uppercase text-muted-foreground">
              <tr><th className="p-2">Funcionário</th><th className="p-2">OS</th><th className="p-2">Etapa</th><th className="p-2">Início</th><th className="p-2 text-right">Aberto</th><th className="p-2 max-sm:hidden">Última pausa</th><th className="p-2"><span className="sr-only">Ações</span></th></tr>
            </thead>
            <tbody className="divide-y divide-border">
              {query.data.items.map((e) => (
                <tr key={e.id} className="align-top">
                  <td className="p-2 font-medium">{e.employeeName}</td>
                  <td className="p-2"><Link to="/app/producao/os/$id" params={{ id: e.orderId }} search={{ aba: 'apontamentos' } as never} className="font-semibold text-primary tabular-nums hover:underline">{e.orderNumber}</Link></td>
                  <td className="p-2">{e.stepCode} · {e.stepName}</td>
                  <td className="p-2 tabular-nums">{formatDateTime(e.startedAt)}</td>
                  <td className="p-2 text-right tabular-nums">{hours(e.openHours)} h</td>
                  <td className="p-2 max-sm:hidden">{e.lastPauseReason ? `${pauseLabel(e.lastPauseReason)}${e.lastPauseAt ? `, ${formatDateTime(e.lastPauseAt)}` : ''}` : '—'}</td>
                  <td className="p-2">
                    {canApprove ? (
                      <span className="flex flex-wrap gap-2">
                        <Link to="/app/producao/os/$id" params={{ id: e.orderId }} search={{ aba: 'apontamentos', corrigir: e.id } as never} className={action}>Corrigir apontamento</Link>
                        <Link to="/app/producao/os/$id" params={{ id: e.orderId }} search={{ aba: 'apontamentos', concluir: e.stepId } as never} className={action}>Concluir etapa</Link>
                      </span>
                    ) : null}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  )
}