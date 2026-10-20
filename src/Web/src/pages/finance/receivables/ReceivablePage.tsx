import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { getGetReceivableQueryKey, getListReceivableSettlementsQueryKey, useGetReceivable } from '@/api/generated/bemaqui'
import type { ReceivableDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { RecordHistory } from '@/components/history/RecordHistory'
import { Money } from '@/components/ui/money'
import { NextSteps } from '@/components/ui/next-steps'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { useUrlTab } from '@/components/ui/url-tab'
import { UrlTabs } from '@/components/ui/url-tabs'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { CancelDialog, DueDateDialog, WriteOffDialog } from './ReceivableDialogs'
import { ReceivableDataForm } from './ReceivableDataForm'
import { ReceivableSettlements } from './ReceivableSettlements'
import { situation } from './receivables'
import { SettleDialog } from './SettleDialog'

type Dialog = 'settle' | 'writeoff' | 'due' | 'cancel' | null
const button = 'rounded-md border border-border px-3 py-2 text-sm font-semibold'

/** Título a receber (FIN-01, molde "detalhe com abas"): resumo, ações com efeito financeiro e abas Dados, Baixas e Histórico. */
export function ReceivablePage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const search = useSearch({ strict: false }) as Record<string, unknown>
  const navigate = useNavigate()
  const client = useQueryClient()
  const query = useGetReceivable(id)
  const canEdit = useCan('finance.edit')
  const canCreate = useCan('finance.create')
  const [, setTab] = useUrlTab('dados')
  const [dialog, setDialog] = useState<Dialog>(null)
  const [steps, setSteps] = useState<'created' | 'paid' | null>(search.novo === '1' ? 'created' : null)

  if (query.isPending) return <div className="space-y-3"><Skeleton className="h-10 w-2/3" /><Skeleton className="h-96" /></div>
  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return <ErrorState title={notFound ? 'Título não encontrado.' : 'Não foi possível carregar o título.'} onRetry={notFound ? undefined : () => query.refetch()} />
  }

  const r = query.data
  const s = situation(r)
  const open = r.status === 'Open' || r.status === 'PartiallyPaid'
  const reload = () => query.refetch()
  const done = (next: ReceivableDetail) => {
    client.setQueryData(getGetReceivableQueryKey(id), next)
    void client.invalidateQueries({ queryKey: getListReceivableSettlementsQueryKey(id) })
    setDialog(null)
    if (next.status === 'Paid') setSteps('paid')
  }
  const shown = steps === 'created'
    ? [...(canEdit && open ? [{ label: 'Dar baixa', onSelect: () => setDialog('settle') }] : []), ...(canCreate ? [{ label: 'Novo título para este cliente', onSelect: () => void navigate({ to: '/app/financeiro/receber/novo' }) }] : [])]
    : steps === 'paid' ? [{ label: 'Ver os títulos em aberto', onSelect: () => void navigate({ to: '/app/financeiro/receber', search: { situacao: 'aberto' } as never }) }] : []
  const facts: [string, React.ReactNode][] = [
    ['Valor', <Money key="v" value={r.amount} />], ['Recebido', <Money key="p" value={r.paidAmount} />], ['Saldo', <Money key="b" value={r.balance} />],
    ['Vencimento', r.dueDate ? formatDate(r.dueDate) : '—'], ['Emissão', formatDate(r.issueDate)], ['Conta', r.accountName],
  ]

  return (
    <>
      <PageHeading title={`${r.number} — ${r.partyName}`}>
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <div className="flex flex-wrap items-center gap-1.5">
            <StatusChip tone={s.tone}>{s.label}</StatusChip>
            {r.origin === 'Opening' ? <Tag>Implantação</Tag> : r.origin === 'Contract' ? <Tag>Contrato</Tag> : null}
            <span className="text-sm text-muted-foreground">{r.description}</span>
            <Link to="/app/financeiro/receber" className="ml-2 text-sm font-semibold text-primary">Voltar à lista</Link>
          </div>
          {canEdit ? (
            <div className="flex flex-wrap gap-2">
              {open ? <button type="button" onClick={() => setDialog('settle')} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Dar baixa</button> : null}
              {open ? <button type="button" onClick={() => setDialog('writeoff')} className={button}>Quitar o saldo com desconto</button> : null}
              {r.status === 'Open' ? <button type="button" onClick={() => setDialog('due')} className={button}>Alterar vencimento</button> : null}
              {r.status !== 'Cancelled' ? <button type="button" onClick={() => setDialog('cancel')} className={button + ' text-crit-ink'}>Cancelar título</button> : null}
            </div>
          ) : null}
        </div>
      </PageHeading>
      {shown.length > 0 ? <div className="mb-4"><NextSteps onDismiss={() => setSteps(null)} steps={shown} /></div> : null}
      {r.status === 'Paid' || r.status === 'Cancelled' ? (
        <p role="note" className="mb-4 rounded-md border border-border bg-muted px-3 py-2 text-sm">
          {r.status === 'Paid' ? 'Título pago: para mudar, estorne a baixa na aba Baixas.' : `Título cancelado: ${r.cancelReason ?? ''}`}
        </p>
      ) : null}
      <dl className="mb-4 grid grid-cols-2 gap-3 rounded-md border border-border bg-card p-4 text-sm sm:grid-cols-3 lg:grid-cols-6">
        {facts.map(([label, value]) => <div key={label}><dt className="text-muted-foreground">{label}</dt><dd className="font-semibold tabular-nums">{value}</dd></div>)}
      </dl>
      <UrlTabs label="Seções do título" fallback="dados" tabs={[
        { id: 'dados', label: 'Dados', keepMounted: true, content: <ReceivableDataForm key={r.version} receivable={r} canEdit={canEdit && r.status !== 'Cancelled'} onSaved={done} /> },
        { id: 'baixas', label: 'Baixas', content: <ReceivableSettlements receivable={r} onDone={done} onReload={reload} /> },
        { id: 'historico', label: 'Histórico de alterações', content: <RecordHistory entity="Receivable" id={r.id} /> },
      ]} />
      {dialog === 'settle' ? <SettleDialog receivable={r} open onOpenChange={(o) => !o && setDialog(null)} onDone={done} onReload={reload} /> : null}
      {dialog === 'writeoff' ? <WriteOffDialog receivable={r} open onOpenChange={(o) => !o && setDialog(null)} onDone={done} onReload={reload} /> : null}
      {dialog === 'due' ? <DueDateDialog receivable={r} open onOpenChange={(o) => !o && setDialog(null)} onDone={done} onReload={reload} /> : null}
      {dialog === 'cancel' ? <CancelDialog receivable={r} open onOpenChange={(o) => !o && setDialog(null)} onDone={done} onReload={reload} onGoToSettlements={() => { setDialog(null); setTab('baixas') }} /> : null}
    </>
  )
}
