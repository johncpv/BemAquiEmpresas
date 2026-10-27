import { Link } from '@tanstack/react-router'
import { Receipt } from 'lucide-react'
import { useState } from 'react'
import type { ColumnDef } from '@tanstack/react-table'
import { useListReceivables } from '@/api/generated/bemaqui'
import type { ReceivableDetail } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { Money } from '@/components/ui/money'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { formatDate } from '@/lib/format'
import { Can } from '@/pages/ModuleGate'
import { PageHeading } from '@/shell/PageHeading'
import { FinanceTabs } from '../FinanceTabs'
import { Dica } from '@/help/Dica'
import { useCan } from '@/pages/use-can'
import { BatchBillingDialog } from './BatchBillingDialog'
import { slipStatus } from './billing-slips'
import { DeadLetterBanner } from './DeadLetterBanner'
import { awaits, RECEIVABLE_DICAS, situation, SITUATION_OPTIONS, today } from './receivables'

const PERIODS = [
  { value: 'ontem', label: 'Venceram ontem' },
  { value: 'mes', label: 'Este mês' },
  { value: '30', label: 'Próximos 30 dias' },
  { value: 'proximo-mes', label: 'Próximo mês' },
]

/** Vencimento por período (filtro na URL): devolve dueFrom e dueTo em aaaa-mm-dd. */
function period(value: string | undefined): { dueFrom?: string; dueTo?: string } {
  const now = today()
  const [y, m] = now.split('-').map(Number)
  const iso = (d: Date) => d.toISOString().slice(0, 10)
  if (value === 'ontem') { const y1 = iso(new Date(Date.parse(now + 'T00:00:00Z') - 86_400_000)); return { dueFrom: y1, dueTo: y1 } }
  if (value === 'mes') return { dueFrom: iso(new Date(Date.UTC(y, m - 1, 1))), dueTo: iso(new Date(Date.UTC(y, m, 0))) }
  if (value === 'proximo-mes') return { dueFrom: iso(new Date(Date.UTC(y, m, 1))), dueTo: iso(new Date(Date.UTC(y, m + 1, 0))) }
  if (value === '30') return { dueFrom: now, dueTo: iso(new Date(Date.parse(now + 'T00:00:00Z') + 30 * 86_400_000)) }
  return {}
}

/** Financeiro → A receber (FIN-01): o que os clientes devem, por vencimento; "Vencido" calculado; totais do filtro no rodapé. */
export function ReceivablesPage() {
  const [search] = useTableSearch()
  const { situacao, vencimento, cliente, contrato, boleto } = useTableFilters(['situacao', 'vencimento', 'cliente', 'contrato', 'boleto'])
  const query = useListReceivables({ q: search.q, situation: situacao || undefined, partyId: cliente || undefined, contractId: contrato || undefined, withBillingSlip: boleto === 'com' ? true : undefined, ...period(vencimento), page: search.page, pageSize: search.pageSize, sort: search.sort })
  const table = { ...query, data: query.data?.page }
  const totals = query.data?.totals
  const canEdit = useCan('finance.edit')
  const [selected, setSelected] = useState<string[]>([])
  const [batch, setBatch] = useState(false)
  const tooMany = selected.length > 50

  const columns: ColumnDef<ReceivableDetail>[] = [
    { id: 'number', header: 'Número', meta: { sortField: 'number' }, cell: ({ row: { original: r } }) => {
      const slip = r.billingSlipStatus ? slipStatus(r.billingSlipStatus) : null
      return <span className="inline-flex items-center gap-1.5 font-mono">{r.number}{slip ? <Receipt role="img" aria-label={slip.label} className="size-4 text-muted-foreground" /> : null}</span>
    } },
    { id: 'party', header: 'Cliente', cell: ({ row: { original: r } }) => r.partyName },
    { id: 'description', header: 'Descrição', meta: { secondary: true }, cell: ({ row: { original: r } }) => <span className="inline-flex items-center gap-1.5">{r.description}{r.origin === 'Opening' ? <Tag>Implantação</Tag> : null}</span> },
    { id: 'due', header: 'Vencimento', meta: { sortField: 'dueDate' }, cell: ({ row: { original: r } }) => <span className="tabular-nums">{r.dueDate ? formatDate(r.dueDate) : '—'}</span> },
    { id: 'amount', header: 'Valor', meta: { numeric: true, secondary: true, sortField: 'amount' }, cell: ({ row: { original: r } }) => <Money value={r.amount} /> },
    { id: 'balance', header: 'Saldo', meta: { numeric: true }, cell: ({ row: { original: r } }) => <Money value={r.balance} /> },
    { id: 'status', header: 'Situação', cell: ({ row: { original: r } }) => {
      const s = situation(r)
      const wait = awaits(r)
      return <span className="flex flex-col items-start gap-0.5"><StatusChip tone={s.tone}>{s.label}</StatusChip>{wait ? <span className="text-xs text-muted-foreground">{wait}</span> : null}</span>
    } },
  ]

  return (
    <>
      <PageHeading title="Financeiro: A receber">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">O que os clientes devem à empresa, por vencimento.</p>
          <div className="flex flex-wrap gap-2">
            <Link to="/app/financeiro/receber/descontos-e-cancelamentos" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Descontos e cancelamentos</Link>
            <Can perm="finance.create">
              <Link to="/app/financeiro/receber/novo" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Novo título a receber</Link>
            </Can>
          </div>
        </div>
      </PageHeading>
      <FinanceTabs />
      <DeadLetterBanner />
      {contrato ? (
        <p role="note" className="mb-3 flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
          Mostrando as parcelas de um contrato.<Dica nome="Previsto" texto={RECEIVABLE_DICAS.previsto} />
          <Link to={'/app/financeiro/receber' as never} search={((prev: Record<string, unknown>) => ({ ...prev, contrato: undefined })) as never} className="font-semibold text-primary hover:underline">Ver todos os títulos</Link>
        </p>
      ) : null}
      {cliente ? (
        <p role="note" className="mb-3 flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
          Mostrando os títulos de um cliente, vindo da Inadimplência.
          <Link to={'/app/financeiro/receber' as never} search={((prev: Record<string, unknown>) => ({ ...prev, cliente: undefined })) as never} className="font-semibold text-primary hover:underline">Ver todos os clientes</Link>
        </p>
      ) : null}
      {canEdit && selected.length > 0 ? (
        <div className="mb-3 flex flex-wrap items-center gap-2">
          <button type="button" disabled={tooMany} onClick={() => setBatch(true)} className="min-h-11 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">Emitir boletos</button>
          {tooMany ? <span role="status" className="text-sm text-warn-ink">Selecione até 50 títulos por vez.</span> : null}
        </div>
      ) : null}
      {batch ? <BatchBillingDialog ids={selected} open onOpenChange={(o) => !o && setBatch(false)} onDone={() => { setBatch(false); setSelected([]); void query.refetch() }} /> : null}
      <DataTable
        label="Títulos a receber"
        columns={columns}
        query={table}
        getRowId={(r) => r.id}
        rowHref={(r) => `/app/financeiro/receber/${r.id}`}
        selectable={canEdit ? { selected, onChange: setSelected, rowLabel: (r) => `título ${r.number}` } : undefined}
        rowClassName={(r) => (r.isOverdue ? 'border-l-4 border-l-crit' : undefined)}
        searchPlaceholder="Buscar pelo número ou descrição"
        filters={[{ key: 'situacao', label: 'Situação', options: SITUATION_OPTIONS }, { key: 'vencimento', label: 'Vencimento', options: PERIODS }, { key: 'boleto', label: 'Boleto', options: [{ value: 'com', label: 'Com boleto' }] }]}
        totals={totals ? { balance: <span className="block">Em aberto <Money value={totals.open} /><br /><span className="text-crit-ink">Vencido <Money value={totals.overdue} /></span></span> } : undefined}
        empty={{
          title: 'Lance o primeiro título a receber ou aprove um orçamento para gerar as parcelas do contrato.',
          description: 'Cada título tem cliente, conta de receita e vencimento. Dá para parcelar pela condição de pagamento.',
          action: <Can perm="finance.create"><Link to="/app/financeiro/receber/novo" className="text-sm font-semibold text-primary">Novo título a receber</Link></Can>,
        }}
      />
    </>
  )
}
