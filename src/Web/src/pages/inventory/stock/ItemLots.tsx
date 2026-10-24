import { useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useState } from 'react'
import { correctItemLot, getListItemLotsQueryKey, useListItemLots } from '@/api/generated/bemaqui'
import type { ItemLotRow } from '@/api/generated/model'
import { inputClass } from '@/components/form/form-utils'
import { DataTable, type TableQuery } from '@/components/data-table/DataTable'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { EmptyState } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDate, formatInstantDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { formatQuantity } from './stock'
import { correctionProblem, expiryChip } from './traceability'

/** Corrigir lote (CA 6): número (só sem saída) e validade, com motivo; "Salvar correção" → "Lote corrigido". */
function CorrectLot({ itemId, lot }: { itemId: string; lot: ItemLotRow }) {
  const client = useQueryClient()
  const [number, setNumber] = useState(lot.lotNumber)
  const [expiry, setExpiry] = useState(lot.expiry ?? '')
  const [reason, setReason] = useState('')
  const reset = () => { setNumber(lot.lotNumber); setExpiry(lot.expiry ?? ''); setReason('') }
  const problem = correctionProblem(lot, number, expiry, reason)
  return (
    <ConfirmDialog verb="Salvar correção" done="Lote corrigido" title={`Corrigir o lote ${lot.lotNumber}`} onOpen={reset}
      description={lot.hasOutflow ? 'Este lote já teve saída: só a validade pode ser corrigida.' : 'Corrija o número ou a validade digitados errado. O motivo fica no histórico.'}
      preconditions={problem ? [{ ok: false, text: problem }] : []}
      onConfirm={async () => {
        await correctItemLot(itemId, lot.id, { lotNumber: number.trim(), expiry: expiry || null, reason: reason.trim() })
        await client.invalidateQueries({ queryKey: getListItemLotsQueryKey(itemId) })
      }}
      trigger={<button type="button" className="text-sm font-semibold text-primary underline">Corrigir</button>}>
      <div className="space-y-3">
        <label className="block text-sm font-semibold">
          Número do lote
          <input value={number} onChange={(e) => setNumber(e.target.value)} disabled={lot.hasOutflow} maxLength={40} className={`${inputClass} mt-1 w-full font-normal`} />
        </label>
        <label className="block text-sm font-semibold">
          Validade
          <input type="date" value={expiry} onChange={(e) => setExpiry(e.target.value)} className={`${inputClass} mt-1 w-full font-normal`} />
        </label>
        <label className="block text-sm font-semibold">
          Motivo
          <textarea value={reason} onChange={(e) => setReason(e.target.value)} maxLength={500} rows={2} placeholder="Ex.: digitado errado no recebimento" className={`${inputClass} mt-1 w-full font-normal`} />
        </label>
      </div>
    </ConfirmDialog>
  )
}

/**
 * Aba "Lotes" do item (EST-04 CA 5 e 6): cada lote com fornecedor, entrada, validade (chip "Vence em {n} dias" até 30 dias, "Vencido"),
 * saldo e locais; "Rastrear" abre o destino do lote e "Corrigir" (inventory.edit) troca o número sem saída e a validade. A 400 px ficam
 * lote, validade e saldo; fornecedor, entrada e locais vão para a linha expansível (padrão 8).
 */
export function ItemLots({ itemId, tracksLot }: { itemId: string; tracksLot: boolean }) {
  const canEdit = useCan('inventory.edit')
  const lots = useListItemLots(itemId, { query: { enabled: tracksLot } })
  if (!tracksLot) return <EmptyState className="mt-3" title="Este item não controla lote." description="Para controlar lote, marque o item em Dados antes da próxima entrada." />
  const items = lots.data ?? []
  const query: TableQuery<ItemLotRow> = { data: lots.data ? { items, page: 1, pageSize: Math.max(items.length, 1), total: items.length } : undefined, isPending: lots.isPending, isError: lots.isError, error: lots.error, refetch: lots.refetch }
  const places = (l: ItemLotRow) => l.balances.length === 0 ? 'Sem saldo' : l.balances.map((b) => `${b.locationName} ${formatQuantity(b.onHand)}`).join(' · ')
  const columns: ColumnDef<ItemLotRow>[] = [
    { id: 'lot', header: 'Lote', cell: ({ row: { original: l } }) => <span className="font-semibold tabular-nums">{l.lotNumber}</span> },
    {
      id: 'expiry', header: 'Validade', cell: ({ row: { original: l } }) => {
        const chip = expiryChip(l.expiryState, l.daysToExpiry)
        return (
          <span className="inline-flex flex-wrap items-center gap-1.5 tabular-nums">
            {l.expiry ? formatDate(l.expiry) : '—'}
            {chip ? <StatusChip tone={chip.tone}>{chip.label}</StatusChip> : null}
          </span>
        )
      },
    },
    { id: 'onHand', header: 'Saldo', meta: { numeric: true }, cell: ({ row: { original: l } }) => formatQuantity(l.onHand) },
    { id: 'supplier', header: 'Fornecedor', meta: { secondary: true }, cell: ({ row: { original: l } }) => l.supplierName ?? '—' },
    { id: 'received', header: 'Entrada', meta: { secondary: true }, cell: ({ row: { original: l } }) => <span className="tabular-nums">{l.receivedAt ? formatInstantDate(l.receivedAt) : '—'}</span> },
    { id: 'places', header: 'Local', meta: { secondary: true }, cell: ({ row: { original: l } }) => places(l) },
    {
      id: 'actions', header: 'Ações', meta: { secondary: true }, cell: ({ row: { original: l } }) => (
        <span className="inline-flex gap-3">
          <Link to={'/app/estoque/rastreabilidade' as never} search={{ item: itemId, lote: l.id } as never} className="text-sm font-semibold text-primary underline">Rastrear</Link>
          {canEdit ? <CorrectLot itemId={itemId} lot={l} /> : null}
        </span>
      ),
    },
  ]
  return (
    <div className="mt-3">
      <DataTable label="Lotes do item" columns={columns} query={query} searchable={false} getRowId={(l) => l.id}
        rowClassName={(l) => l.expiryState === 'Expired' ? 'border-l-4 border-l-crit' : l.expiryState === 'Expiring' ? 'border-l-4 border-l-warn' : ''}
        expandable={{
          label: (l) => `detalhes do lote ${l.lotNumber}`,
          render: (l) => (
            <div className="space-y-1 text-sm">
              <p>{l.supplierName ?? 'Sem fornecedor'} · entrada {l.receivedAt ? formatInstantDate(l.receivedAt) : '—'} · {places(l)}</p>
              {l.correctionReason ? <p className="text-muted-foreground">Corrigido: {l.correctionReason}</p> : null}
              <p className="flex gap-3">
                <Link to={'/app/estoque/rastreabilidade' as never} search={{ item: itemId, lote: l.id } as never} className="font-semibold text-primary underline">Rastrear</Link>
                {canEdit ? <CorrectLot itemId={itemId} lot={l} /> : null}
              </p>
            </div>
          ),
        }}
        empty={{ title: 'Nenhum lote ainda.', description: 'O lote nasce na primeira entrada com o número (recebimento, entrada manual ou saldo inicial).', action: <Link to={'/app/estoque/saldos' as never} className="text-sm font-semibold text-primary">Ver saldos</Link> }} />
    </div>
  )
}
