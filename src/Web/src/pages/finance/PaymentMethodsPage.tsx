import { useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useState } from 'react'
import { applySuggestedPaymentMethods, getListPaymentMethodsQueryKey, useListPaymentMethods } from '@/api/generated/bemaqui'
import type { PaymentMethodDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { notifySuccess } from '@/components/ui/messages'
import { StatusChip } from '@/components/ui/status-chip'
import { Can } from '@/pages/ModuleGate'
import { PageHeading } from '@/shell/PageHeading'
import { describeFee, TYPE_LABELS } from './payment-methods'

const LIST = '/app/financeiro/formas-de-pagamento'

/**
 * Financeiro → Formas de pagamento (PAG-01): tipo, conta de destino, adquirente e as taxas vigentes; as sugeridas pelo sistema mostram o
 * chip "A confirmar" até alguém conferir. Vazio oferece "Nova forma de pagamento" e "Usar as formas mais comuns".
 */
export function PaymentMethodsPage() {
  const [search] = useTableSearch()
  const { inativos } = useTableFilters([])
  const client = useQueryClient()
  const query = useListPaymentMethods({ includeInactive: inativos === 'true', page: search.page, pageSize: search.pageSize })
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const suggest = async () => {
    setBusy(true)
    setError(null)
    try {
      await applySuggestedPaymentMethods()
      notifySuccess('Formas de pagamento criadas')
      void client.invalidateQueries({ queryKey: getListPaymentMethodsQueryKey() })
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível criar as formas. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }

  const columns: ColumnDef<PaymentMethodDetail>[] = [
    { id: 'name', header: 'Nome', cell: ({ row: { original: m } }) => m.name },
    { id: 'type', header: 'Tipo', cell: ({ row: { original: m } }) => TYPE_LABELS[m.type] },
    { id: 'account', header: 'Conta de destino', cell: ({ row: { original: m } }) => m.destinationAccountName ?? '—' },
    { id: 'acquirer', header: 'Adquirente', cell: ({ row: { original: m } }) => m.acquirerName ?? '—' },
    {
      id: 'fees', header: 'Taxa e prazo', cell: ({ row: { original: m } }) => {
        const first = m.currentFees[0]
        if (!first) return <span className="text-muted-foreground">Sem taxa</span>
        return <span className="tabular-nums">{describeFee(first)}{m.currentFees.length > 1 ? ` e mais ${m.currentFees.length - 1}` : ''}</span>
      },
    },
    {
      id: 'status', header: 'Situação', cell: ({ row: { original: m } }) => (
        <span className="flex flex-wrap gap-1">
          <StatusChip tone={m.isActive ? 'ok' : 'neutral'}>{m.isActive ? 'Ativa' : 'Inativa'}</StatusChip>
          {m.confirmed ? null : <StatusChip tone="warn">A confirmar</StatusChip>}
        </span>
      ),
    },
  ]

  const suggestButton = (
    <Can perm="finance.create">
      <button type="button" disabled={busy} onClick={() => void suggest()} className="rounded-md border border-border px-3 py-2 text-sm font-semibold disabled:opacity-60">Usar as formas mais comuns</button>
    </Can>
  )

  return (
    <>
      <PageHeading title="Financeiro: Formas de pagamento" description="Como a loja recebe: dinheiro, Pix, cartões e vales, com a taxa e o prazo de cada maquininha." actions={
        <Can perm="finance.create">
          <Link to={`${LIST}/nova` as never} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Nova forma de pagamento</Link>
        </Can>
      } />
      {error ? <p role="alert" className="mb-3 text-sm text-crit-ink">{error}</p> : null}
      <DataTable label="Formas de pagamento" columns={columns} query={query} getRowId={(m) => m.id} rowHref={(m) => `${LIST}/${m.id}`} inactiveFilter searchable={false}
        empty={{
          title: 'Cadastre a primeira forma de pagamento.',
          description: 'Use as formas mais comuns (Dinheiro, Pix e cartões) e depois informe a maquininha, a taxa e o prazo de cada uma.',
          action: (
            <span className="flex flex-wrap justify-center gap-2">
              {suggestButton}
              <Can perm="finance.create"><Link to={`${LIST}/nova` as never} className="px-3 py-2 text-sm font-semibold text-primary">Nova forma de pagamento</Link></Can>
            </span>
          ),
        }} />
    </>
  )
}
