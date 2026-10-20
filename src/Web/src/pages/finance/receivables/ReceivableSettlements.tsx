import { useState } from 'react'
import { useListReceivableSettlements } from '@/api/generated/bemaqui'
import type { ReceivableDetail, SettlementView } from '@/api/generated/model'
import { Money } from '@/components/ui/money'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDate, formatDateTime } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { ReverseDialog } from './ReceivableDialogs'

/** Aba Baixas (CA 16 e 27): baixas, quitações e estornos com usuário e data; estornar com motivo. */
export function ReceivableSettlements({ receivable, onDone, onReload }: { receivable: ReceivableDetail; onDone: (r: ReceivableDetail) => void; onReload: () => Promise<unknown> }) {
  const query = useListReceivableSettlements(receivable.id)
  const canEdit = useCan('finance.edit')
  const [reversing, setReversing] = useState<SettlementView | null>(null)
  if (query.isPending) return <Skeleton className="h-40" />
  if (query.isError) return <ErrorState onRetry={() => query.refetch()} />
  if (query.data.length === 0) return <EmptyState title="Nenhuma baixa ainda." description="Use Dar baixa quando o cliente pagar, no todo ou em parte." />
  return (
    <>
      <div className="overflow-x-auto rounded-md border border-border bg-card">
        <table className="w-full text-sm" aria-label="Baixas do título">
          <thead className="border-b border-border text-xs text-muted-foreground">
            <tr>
              <th className="px-3 py-2 text-left">Data</th>
              <th className="px-3 py-2 text-left">Tipo</th>
              <th className="hidden px-3 py-2 text-left sm:table-cell">Conta</th>
              <th className="px-3 py-2 text-right">Principal</th>
              <th className="hidden px-3 py-2 text-right sm:table-cell">Juros + multa</th>
              <th className="hidden px-3 py-2 text-right sm:table-cell">Desconto</th>
              <th className="px-3 py-2 text-right">Entrou na conta</th>
              <th className="px-3 py-2 text-left">Situação</th>
              <th className="px-3 py-2"><span className="sr-only">Ações</span></th>
            </tr>
          </thead>
          <tbody>
            {query.data.map((s) => (
              <tr key={s.id} className="border-b border-border last:border-0">
                <td className="px-3 py-2 tabular-nums">{formatDate(s.date)}</td>
                <td className="px-3 py-2">{s.kind === 'DiscountWriteOff' ? 'Quitação com desconto' : 'Recebimento'}</td>
                <td className="hidden px-3 py-2 sm:table-cell">{s.bankAccountName ?? '—'}</td>
                <td className="px-3 py-2 text-right"><Money value={s.principal} /></td>
                <td className="hidden px-3 py-2 text-right sm:table-cell"><Money value={s.interest + s.fine} /></td>
                <td className="hidden px-3 py-2 text-right sm:table-cell"><Money value={s.discount} /></td>
                <td className="px-3 py-2 text-right"><Money value={s.total} /></td>
                <td className="px-3 py-2">
                  {s.reversedAt ? <StatusChip tone="neutral">Estornada</StatusChip> : <StatusChip tone="ok">Ativa</StatusChip>}
                  <span className="block text-xs text-muted-foreground">{formatDateTime(s.createdAt)}{s.reversalReason ? ` · ${s.reversalReason}` : ''}</span>
                </td>
                <td className="px-3 py-2 text-right">
                  {canEdit && !s.reversedAt ? <button type="button" onClick={() => setReversing(s)} className="rounded-md border border-border px-2.5 py-1 text-sm font-semibold text-crit-ink">Estornar baixa</button> : null}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {reversing ? <ReverseDialog receivable={receivable} settlement={reversing} open onOpenChange={(o) => { if (!o) setReversing(null) }} onDone={(r) => { setReversing(null); onDone(r); void query.refetch() }} onReload={onReload} /> : null}
    </>
  )
}
