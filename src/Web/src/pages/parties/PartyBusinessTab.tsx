import { useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { getGetPartyCollectionQueryKey, pausePartyCollection, resumePartyCollection, useGetPartyCollection, useGetPartyFinanceSummary, useListProjects, useListQuotes } from '@/api/generated/bemaqui'
import { NoticeList, PauseControls, PauseHistory } from '@/pages/finance/receivables/CollectionPanel'
import { Money } from '@/components/ui/money'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { QUOTE_STATUS } from '../quotes/quotes'
import { PartyBusinessBlock as Block } from './PartyBusinessBlock'

const days = (v: number) => `${v.toLocaleString('pt-BR')} dias`

/** Aba "Comercial e financeiro" (FIN-06b CA 5 a 7): cada bloco chama o módulo dono, com a permissão dele; sem custo nem margem. */
export function PartyBusinessTab({ partyId }: { partyId: string }) {
  const canQuotes = useCan('quotes.view')
  const canProjects = useCan('projects.view')
  const canFinance = useCan('finance.view')
  const quotes = useListQuotes({ partyId, pageSize: 10 }, { query: { enabled: canQuotes } })
  const projects = useListProjects({ partyId, pageSize: 10 }, { query: { enabled: canProjects } })
  const summary = useGetPartyFinanceSummary(partyId, { query: { enabled: canFinance } })
  const collection = useGetPartyCollection(partyId, { query: { enabled: canFinance } })
  const client = useQueryClient()
  const s = summary.data
  const more = (to: string, label: string) => <Link to={to as never} className="text-xs font-semibold text-primary hover:underline">{label}</Link>
  return (
    <div className="space-y-6 text-sm">
      {canQuotes ? (
        <Block title="Orçamentos" query={quotes} empty={(quotes.data?.items.length ?? 0) === 0} link={more('/app/orcamentos', 'Ver orçamentos')}>
          <ul className="divide-y divide-border rounded-md border border-border bg-card">
            {quotes.data?.items.map((q) => (
              <li key={q.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
                <Link to={'/app/orcamentos/$id' as never} params={{ id: q.id } as never} className="font-semibold text-primary hover:underline">{q.number}{q.revision > 0 ? ` rev. ${q.revision}` : ''}</Link>
                <span className="flex items-center gap-2"><span className="tabular-nums">{formatDate(q.createdAt.slice(0, 10))}</span><Money value={q.totalPrice} /><StatusChip tone={QUOTE_STATUS[q.status]?.tone ?? 'neutral'}>{QUOTE_STATUS[q.status]?.label ?? q.status}</StatusChip></span>
              </li>
            ))}
          </ul>
        </Block>
      ) : null}
      {canProjects ? (
        <Block title="Contratos e projetos" query={projects} empty={(projects.data?.items.length ?? 0) === 0} link={more('/app/projetos', 'Ver projetos')}>
          <ul className="divide-y divide-border rounded-md border border-border bg-card">
            {projects.data?.items.map((p) => (
              <li key={p.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
                <span><Link to={'/app/projetos/$id' as never} params={{ id: p.id } as never} className="font-semibold text-primary hover:underline">{p.number}</Link> <span className="text-muted-foreground">{p.name}</span></span>
                <Money value={p.contractValue} />
              </li>
            ))}
          </ul>
        </Block>
      ) : null}
      {canFinance ? (
        <Block title="Títulos e pontualidade" query={summary} empty={false} link={more(`/app/financeiro/receber?cliente=${partyId}`, 'Ver títulos a receber')}>
          {s ? (
            <dl className="grid grid-cols-2 gap-3 rounded-md border border-border bg-card p-4 sm:grid-cols-4">
              <div><dt className="text-xs text-muted-foreground">A receber em aberto</dt><dd><Money value={s.receivables.open} /></dd></div>
              <div><dt className="text-xs text-muted-foreground">A receber vencido</dt><dd><Money value={s.receivables.overdue} /></dd></div>
              <div><dt className="text-xs text-muted-foreground">Recebido em 12 meses</dt><dd><Money value={s.receivables.paidLast12m} /></dd></div>
              <div><dt className="text-xs text-muted-foreground">A pagar em aberto</dt><dd><Money value={s.payables.open} /></dd></div>
              <div><dt className="flex items-center text-xs text-muted-foreground">Atraso médio<Dica nome="Atraso médio" texto="Média de dias entre o vencimento e a baixa que quitou, ponderada pelo valor, nos últimos 12 meses." /></dt><dd className="tabular-nums">{s.paymentStats.hasHistory ? days(s.paymentStats.avgDelayDays) : 'Sem histórico'}</dd></div>
              <div><dt className="flex items-center text-xs text-muted-foreground">% pago com atraso<Dica nome="% pago com atraso" texto="Parte do valor quitado nos últimos 12 meses que foi paga depois do vencimento." /></dt><dd className="tabular-nums">{s.paymentStats.hasHistory ? `${s.paymentStats.pctPaidLate.toLocaleString('pt-BR')}%` : '—'}</dd></div>
              <div><dt className="text-xs text-muted-foreground">Pago a ele em 12 meses</dt><dd><Money value={s.payables.paidLast12m} /></dd></div>
              <div><dt className="text-xs text-muted-foreground">Atraso médio da empresa</dt><dd className="tabular-nums">{days(s.paymentStats.tenantAvgDelayDays)}</dd></div>
            </dl>
          ) : null}
        </Block>
      ) : null}
      {canFinance ? (
        <Block title="Lembretes de cobrança" query={collection} empty={false}>
          {collection.data ? (
            <div className="space-y-3">
              <p>
                <span className="text-xs text-muted-foreground">Lembretes enviados no mês</span> <b className="tabular-nums">{collection.data.sentThisMonth.toLocaleString('pt-BR')}</b>
                {!collection.data.ruleActive ? <span className="ml-2 text-xs text-muted-foreground">A régua está desligada.</span> : null}
              </p>
              <PauseControls active={collection.data.activePause} canEdit={collection.data.canEdit} subject="deste cliente"
                onPause={async (reason, until) => { client.setQueryData(getGetPartyCollectionQueryKey(partyId), await pausePartyCollection(partyId, { reason, until })) }}
                onResume={async () => { client.setQueryData(getGetPartyCollectionQueryKey(partyId), await resumePartyCollection(partyId)) }} />
              {collection.data.notices.length > 0 ? <NoticeList notices={collection.data.notices.slice(0, 10)} showNumber /> : <p className="text-muted-foreground">Nenhum lembrete enviado a este cliente.</p>}
              <PauseHistory pauses={collection.data.pauses.filter((p) => !p.active)} />
            </div>
          ) : null}
        </Block>
      ) : null}
    </div>
  )
}
