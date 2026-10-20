import { useQueryClient } from '@tanstack/react-query'
import { useNavigate, useSearch } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useState } from 'react'
import { Link } from '@tanstack/react-router'
import { getListBankAccountsQueryKey, useGetBankBalances, useListBankAccounts } from '@/api/generated/bemaqui'
import type { BankAccountDetail } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { Money } from '@/components/ui/money'
import { NextSteps } from '@/components/ui/next-steps'
import { StatusChip } from '@/components/ui/status-chip'
import { ImportedTag } from '@/components/ui/imported-tag'
import { Tag } from '@/components/ui/tag'
import { Can } from '@/pages/ModuleGate'
import { PageHeading } from '@/shell/PageHeading'
import { BankAccountPanel } from './BankAccountPanel'
import { FinanceTabs } from './FinanceTabs'

/**
 * Financeiro → Contas e caixas (CAD-05b CA 1 a 10): lista com saldo inicial (ou a Tag "Saldo inicial não informado") e a
 * conta padrão; edição no painel lateral. Agência e conta já vêm mascaradas do servidor para quem só vê.
 */
export function BankAccountsPage() {
  const [search] = useTableSearch()
  const { inativos } = useTableFilters([])
  const client = useQueryClient()
  const navigate = useNavigate()
  const { conta } = useSearch({ strict: false }) as { conta?: string }
  const query = useListBankAccounts({ q: search.q, includeInactive: inativos === 'true', page: search.page, pageSize: search.pageSize })
  const balances = useGetBankBalances()
  const balanceOf = (id: string) => balances.data?.accounts?.find((b) => b.id === id)?.currentBalance
  const [editing, setEditing] = useState<{ account: BankAccountDetail | null; focusBalance?: boolean } | null>(null)
  const [steps, setSteps] = useState(false)
  const [balanceHint, setBalanceHint] = useState(true)
  const fromUrl = conta ? query.data?.items.find((a) => a.id === conta) : undefined
  const panel = editing ?? (fromUrl ? { account: fromUrl } : null)
  const close = () => { setEditing(null); if (conta) void navigate({ to: '.', search: (p: Record<string, unknown>) => ({ ...p, conta: undefined }) } as never) }

  const columns: ColumnDef<BankAccountDetail>[] = [
    { id: 'name', header: 'Nome', cell: ({ row: { original: a } }) => (
      <span className="flex flex-wrap items-center gap-1.5">
        <button type="button" onClick={() => setEditing({ account: a })} className="font-semibold text-primary hover:underline">{a.name}</button>
        <ImportedTag source={a.importSource} />
      </span>
    ) },
    { id: 'kind', header: 'Tipo', cell: ({ row: { original: a } }) => (a.kind === 'Bank' ? 'Banco' : 'Caixa') },
    { id: 'bank', header: 'Banco, agência e conta', meta: { secondary: true }, cell: ({ row: { original: a } }) => (a.kind === 'Bank' ? <span className="tabular-nums">{a.bankCode} {a.bankName} · {a.agency} · {a.account}</span> : '—') },
    {
      id: 'opening', header: 'Saldo inicial', meta: { numeric: true }, cell: ({ row: { original: a } }) => (a.openingBalance === null
        ? <Tag>Saldo inicial não informado</Tag>
        : <Money value={a.openingBalance} highlightNegative />),
    },
    {
      id: 'current', header: 'Saldo atual', meta: { numeric: true }, cell: ({ row: { original: a } }) => (a.isActive && balanceOf(a.id) !== undefined
        ? <Link to={'/app/financeiro/contas/$id/extrato' as never} params={{ id: a.id } as never} aria-label={`Extrato de ${a.name}`} className="hover:underline"><Money value={balanceOf(a.id) ?? 0} highlightNegative /></Link>
        : '—'),
    },
    {
      id: 'status', header: 'Situação', cell: ({ row: { original: a } }) => (
        <span className="flex flex-wrap gap-1">
          {a.isDefault ? <StatusChip tone="info">Padrão</StatusChip> : null}
          <StatusChip tone={a.isActive ? 'ok' : 'neutral'}>{a.isActive ? 'Ativa' : 'Inativa'}</StatusChip>
        </span>
      ),
    },
  ]
  const missing = query.data?.items.find((a) => a.isActive && a.openingBalance === null)

  return (
    <>
      <PageHeading title="Financeiro: Contas e caixas">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">Contas bancárias e caixas da empresa, com o saldo de partida.</p>
          <Can perm="finance.create">
            <button type="button" onClick={() => setEditing({ account: null })} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Nova conta bancária</button>
          </Can>
        </div>
      </PageHeading>
      <FinanceTabs />
      <div className="mb-3 space-y-2">
        {steps ? <NextSteps steps={[{ label: 'Cadastrar condições de pagamento', onSelect: () => void navigate({ to: '/app/financeiro/condicoes-de-pagamento' }) }]} onDismiss={() => setSteps(false)} /> : null}
        {missing && balanceHint ? (
          <Can perm="finance.edit">
            <NextSteps title="Falta o saldo" steps={[{ label: `Informar saldo inicial de ${missing.name}`, onSelect: () => setEditing({ account: missing, focusBalance: true }) }]} onDismiss={() => setBalanceHint(false)} />
          </Can>
        ) : null}
      </div>
      <DataTable totals={typeof balances.data?.consolidated === 'number' ? { current: <span>Consolidado <Money value={balances.data.consolidated} highlightNegative /></span> } : undefined} label="Contas e caixas" columns={columns} query={query} getRowId={(a) => a.id} inactiveFilter searchPlaceholder="Buscar pelo nome"
        empty={{
          title: 'Cadastre a primeira conta bancária ou o caixa da empresa.',
          description: 'O saldo inicial é opcional: dá para informar depois.',
          action: <Can perm="finance.create"><button type="button" onClick={() => setEditing({ account: null })} className="text-sm font-semibold text-primary">Nova conta bancária</button></Can>,
        }} />
      {panel ? (
        <BankAccountPanel account={panel.account} focusBalance={panel.focusBalance} onClose={close}
          onSaved={(created) => { void client.invalidateQueries({ queryKey: getListBankAccountsQueryKey() }); if (created && (query.data?.total ?? 0) === 0) setSteps(true); close() }} />
      ) : null}
    </>
  )
}
