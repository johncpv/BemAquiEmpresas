import { useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from '@tanstack/react-router'
import { getGetContractQueryKey, useGetContract } from '@/api/generated/bemaqui'
import { RecordHistory } from '@/components/history/RecordHistory'
import { Money } from '@/components/ui/money'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { UrlTabs } from '@/components/ui/url-tabs'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { CancelContractDialog } from './CancelContractDialog'
import { ContractDocuments } from './ContractDocuments'
import { AmendmentsTab } from './amendments/AmendmentsTab'
import { ContractItems } from './ContractItems'
import { CONTRACT_DICAS, CONTRACT_STATUS, receivablesState } from './contracts'

/** Contrato (COM-04a CA 11, 16): resumo, itens e marcos do snapshot, contrato assinado e histórico. Só leitura. */
export function ContractPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const client = useQueryClient()
  const canApprove = useCan('projects.approve')
  const query = useGetContract(id, {
    query: { refetchInterval: (q) => (q.state.data && q.state.data.receivables === 0 && Date.now() - Date.parse(q.state.data.createdAt) < 120_000 ? 5_000 : false) },
  })
  if (query.isPending) return <Skeleton className="h-96" />
  if (query.isError) return <ErrorState title="Não foi possível carregar o contrato." onRetry={() => query.refetch()} />
  const c = query.data
  const parcels = c.status === 'Active' ? receivablesState(c.receivables, c.createdAt) : 'ready'
  const cancellable = canApprove && c.status === 'Active' && c.projectStatus !== 'Delivered' && c.projectStatus !== 'Closed'
  return (
    <>
      <PageHeading title={`${c.number} — ${c.partyName}`}>
        <div className="mt-2 flex flex-wrap items-center gap-2">
          <StatusChip tone={CONTRACT_STATUS[c.status]?.tone ?? 'neutral'}>{CONTRACT_STATUS[c.status]?.label ?? c.status}</StatusChip>
          {c.isOnboarding ? <Tag>Implantação</Tag> : null}
          {c.projectId ? <Link to={'/app/projetos/$id' as never} params={{ id: c.projectId } as never} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Ver projeto {c.projectNumber}</Link> : null}
          {cancellable ? <CancelContractDialog contract={c} onDone={() => client.invalidateQueries({ queryKey: getGetContractQueryKey(id) })} /> : null}
        </div>
      </PageHeading>
      {c.status === 'Cancelled' ? <p role="note" className="mb-4 rounded-md border border-border bg-muted px-3 py-2 text-sm">Contrato cancelado{c.cancelReason ? `: ${c.cancelReason}` : ''}. Só para consulta.</p> : null}
      {c.status === 'Completed' ? <p role="note" className="mb-4 rounded-md border border-border bg-muted px-3 py-2 text-sm">Contrato concluído: projeto entregue e parcelas quitadas.</p> : null}
      {parcels === 'pending' ? <p role="status" className="mb-4 rounded-md border border-border bg-muted px-3 py-2 text-sm">Gerando parcelas no financeiro…</p> : null}
      {parcels === 'slow' ? <p role="status" className="mb-4 rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">As parcelas estão demorando. Avisaremos quando estiverem prontas.</p> : null}
      <dl className="mb-4 grid gap-3 rounded-md border border-border bg-card p-4 text-sm sm:grid-cols-4">
        <div><dt className="flex items-center text-xs text-muted-foreground">Valor vigente<Dica nome="Valor vigente" texto="Valor original do contrato mais os aditivos aprovados. O contrato original não muda." /></dt><dd><Money value={c.currentValue ?? c.totalValue} className="font-semibold" />{c.currentValue !== null && c.currentValue !== undefined && c.currentValue !== c.totalValue ? <span className="block text-xs text-muted-foreground">Original <Money value={c.totalValue} /></span> : null}</dd></div>
        <div><dt className="flex items-center text-xs text-muted-foreground">Assinatura<Dica nome="Data de assinatura" texto={CONTRACT_DICAS.assinatura} /></dt><dd className="tabular-nums">{formatDate(c.signedAt)}</dd></div>
        <div><dt className="flex items-center text-xs text-muted-foreground">Conta de receita<Dica nome="Conta de receita das parcelas" texto={CONTRACT_DICAS.receita} /></dt><dd>{c.revenueAccount ?? '—'}</dd></div>
        <div><dt className="text-xs text-muted-foreground">Orçamento de origem</dt><dd><Link to={'/app/orcamentos/$id' as never} params={{ id: c.quoteId } as never} className="font-semibold text-primary hover:underline">{c.quoteNumber}{c.quoteRevision > 0 ? ` rev. ${c.quoteRevision}` : ''}</Link></dd></div>
      </dl>
      <UrlTabs label="Seções do contrato" fallback="resumo" tabs={[
        { id: 'resumo', label: 'Resumo', content: (
          <dl className="grid gap-3 text-sm sm:grid-cols-2">
            <div><dt className="text-xs text-muted-foreground">Cliente</dt><dd>{c.partyName}{c.partyDocument ? <span className="block text-xs tabular-nums text-muted-foreground">{c.partyDocument}</span> : null}</dd></div>
            <div><dt className="text-xs text-muted-foreground">Projeto</dt><dd>{c.projectName ?? '—'}</dd></div>
            <div><dt className="text-xs text-muted-foreground">Prazo de entrega prometido</dt><dd>{c.snapshot.deliveryDays ? `${c.snapshot.deliveryDays} dias corridos` : '—'}</dd></div>
            <div><dt className="text-xs text-muted-foreground">Observações do orçamento</dt><dd>{c.snapshot.notes || '—'}</dd></div>
          </dl>
        ) },
        { id: 'itens', label: 'Itens e marcos', content: <ContractItems contract={c} /> },
        { id: 'aditivos', label: 'Aditivos', content: <AmendmentsTab contractId={id} active={c.status === 'Active'} /> },
        { id: 'documentos', label: 'Documentos', content: <ContractDocuments contractId={id} /> },
        { id: 'historico', label: 'Histórico de alterações', content: <RecordHistory entity="Contract" id={id} /> },
      ]} />
    </>
  )
}
