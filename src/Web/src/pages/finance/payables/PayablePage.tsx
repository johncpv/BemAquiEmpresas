import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { cancelPayable, getGetPayableQueryKey, useGetFinanceLimits, useGetPayable } from '@/api/generated/bemaqui'
import type { PayableDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { RecordHistory } from '@/components/history/RecordHistory'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DialogMessage } from '@/components/ui/dialog-message'
import { Money } from '@/components/ui/money'
import { NextSteps } from '@/components/ui/next-steps'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { UrlTabs } from '@/components/ui/url-tabs'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { PayableDataForm } from './PayableDataForm'
import { PayableMethodTab } from './PayableMethodTab'
import { ORIGIN_LABELS, payableSituation } from './payables'

/** Título a pagar (FIN-02, detalhe com abas): Dados (com retenções), Forma de pagamento e Histórico; faixa de modo leitura em OP. */
export function PayablePage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const search = useSearch({ strict: false }) as Record<string, unknown>
  const navigate = useNavigate()
  const client = useQueryClient()
  const query = useGetPayable(id)
  const canEdit = useCan('finance.edit')
  const canCreate = useCan('finance.create')
  const canApprove = useCan('finance.approve')
  const limits = useGetFinanceLimits()
  const [steps, setSteps] = useState(search.novo === '1')
  const [reason, setReason] = useState('')

  if (query.isPending) return <div className="space-y-3"><Skeleton className="h-10 w-2/3" /><Skeleton className="h-96" /></div>
  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return <ErrorState title={notFound ? 'Título não encontrado.' : 'Não foi possível carregar o título.'} onRetry={notFound ? undefined : () => query.refetch()} />
  }

  const p = query.data
  const s = payableSituation(p)
  const inOrder = p.status === 'AwaitingApproval' || p.status === 'Approved'
  const closed = p.status === 'Paid' || p.status === 'Cancelled'
  const editable = ['Manual', 'Recurring', 'Opening'].includes(p.origin)
  const setPayable = (next: PayableDetail) => client.setQueryData(getGetPayableQueryKey(id), next)
  const overLimit = !!limits.data && p.netAmount > limits.data.cancelApprovalAmount
  const facts: [string, React.ReactNode][] = [
    ['Valor', <Money key="a" value={p.amount} />], ['Retenções', <Money key="r" value={p.amount - p.netAmount} />], ['Líquido', <Money key="n" value={p.netAmount} />],
    ['Vencimento', p.dueDate ? formatDate(p.dueDate) + (p.isEstimate ? ' (estimado)' : '') : '—'], ['Emissão', formatDate(p.issueDate)], ['Conta', p.accountName],
  ]

  return (
    <>
      <PageHeading title={`${p.number} — ${p.partyName}`}>
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <div className="flex flex-wrap items-center gap-1.5">
            <StatusChip tone={s.tone}>{s.label}</StatusChip>
            {ORIGIN_LABELS[p.origin] ? <Tag>{ORIGIN_LABELS[p.origin]}</Tag> : null}
            <span className="text-sm text-muted-foreground">{p.description}</span>
            <Link to="/app/financeiro/pagar" className="ml-2 text-sm font-semibold text-primary">Voltar à lista</Link>
          </div>
          {canEdit && !closed && !inOrder && editable ? (
            <ConfirmDialog size="lg" destructive verb="Cancelar título" done="Título cancelado" title={`Cancelar o título ${p.number}?`} description="Ele sai dos totais em aberto e continua consultável. O Proprietário é avisado."
              onOpen={() => setReason('')} onReload={() => query.refetch()}
              preconditions={[{ ok: !overLimit || canApprove, text: 'Valor acima do limite de cancelamento: só quem aprova em Caixa e pagamentos pode cancelar.' }]}
              impact={<dl className="grid grid-cols-2 gap-1"><dt>Valor cancelado</dt><dd className="text-right"><Money value={p.netAmount} /></dd></dl>}
              onConfirm={async () => { if (!reason.trim()) throw new DialogMessage('Informe o motivo.'); setPayable(await cancelPayable(p.id, { reason: reason.trim(), version: p.version })) }}
              trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold text-crit-ink">Cancelar título</button>}>
              <Field label="Motivo" required hint="Até 200 caracteres. Fica no histórico.">{(f) => <input {...f} value={reason} maxLength={200} onChange={(e) => setReason(e.target.value)} className={inputClass} />}</Field>
            </ConfirmDialog>
          ) : null}
        </div>
      </PageHeading>
      {steps && canCreate ? <div className="mb-4"><NextSteps onDismiss={() => setSteps(false)} steps={[{ label: 'Novo título para este fornecedor', onSelect: () => void navigate({ to: '/app/financeiro/pagar/novo' }) }]} /></div> : null}
      {inOrder ? <p role="note" className="mb-4 rounded-md border border-border bg-muted px-3 py-2 text-sm">Este título está numa ordem de pagamento. Para mudar valor ou dados de pagamento, retire-o da ordem.</p> : null}
      {closed ? <p role="note" className="mb-4 rounded-md border border-border bg-muted px-3 py-2 text-sm">{p.status === 'Paid' ? 'Título pago.' : `Título cancelado: ${p.cancelReason ?? ''}`}</p> : null}
      <dl className="mb-4 grid grid-cols-2 gap-3 rounded-md border border-border bg-card p-4 text-sm sm:grid-cols-3 lg:grid-cols-6">
        {facts.map(([label, value]) => <div key={label}><dt className="text-muted-foreground">{label}</dt><dd className="font-semibold tabular-nums">{value}</dd></div>)}
      </dl>
      <UrlTabs label="Seções do título" fallback="dados" tabs={[
        { id: 'dados', label: 'Dados', keepMounted: true, content: <PayableDataForm key={p.version} payable={p} canEdit={canEdit && !closed} onSaved={setPayable} /> },
        { id: 'forma-de-pagamento', label: 'Forma de pagamento', content: <PayableMethodTab payable={p} canEdit={canEdit && !closed} onChanged={() => void query.refetch()} /> },
        { id: 'historico', label: 'Histórico de alterações', content: <RecordHistory entity="Payable" id={p.id} /> },
      ]} />
    </>
  )
}
