import { Link, useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import { useListPaymentsToReview } from '@/api/generated/bemaqui'
import { Money } from '@/components/ui/money'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { DIFFERENCE_TEXT, differenceKind, REVIEW_DICAS, REVIEW_EMPTY } from './payment-review'
import { PaymentReviewDialog } from './PaymentReviewDialog'

/**
 * Filtro "Pagamentos a conferir" de Financeiro → A receber (BNK-02 CA 9): os boletos pagos com diferença (PaidPendingReview), com título,
 * cliente, valor do boleto, valor pago, data e diferença. O link da notificação (<c>conferir</c>) abre o diálogo direto. A 400 px, título,
 * valor pago e diferença ficam sempre visíveis.
 */
export function PaymentReviewList({ open: initial }: { open?: string }) {
  const query = useListPaymentsToReview()
  const canEdit = useCan('finance.edit')
  const navigate = useNavigate()
  const [open, setOpen] = useState<string | null>(initial ?? null)
  const close = () => {
    setOpen(null)
    if (initial) void navigate({ to: '.', search: (p: Record<string, unknown>) => ({ ...p, conferir: undefined }), replace: true } as never)
  }

  if (query.isPending) return <div className="space-y-2">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-14" />)}</div>
  if (query.isError) return <ErrorState message="Não foi possível carregar os pagamentos a conferir. Tente de novo em instantes." onRetry={() => void query.refetch()} />
  const items = query.data.items
  return (
    <>
      {items.length === 0 ? (
        <EmptyState title={REVIEW_EMPTY} action={<Link to={'/app/financeiro/receber' as never} className="text-sm font-semibold text-primary">Ver todos os títulos</Link>} />
      ) : (
        <div className="overflow-x-auto rounded-md border border-border bg-card">
          <table className="w-full text-sm" aria-label="Pagamentos a conferir">
            <thead className="border-b border-border text-xs text-muted-foreground">
              <tr>
                <th className="px-3 py-2 text-left">Título</th>
                <th className="hidden px-3 py-2 text-left sm:table-cell">Cliente</th>
                <th className="hidden px-3 py-2 text-right sm:table-cell">Valor do boleto</th>
                <th className="px-3 py-2 text-right">Valor pago</th>
                <th className="hidden px-3 py-2 text-left sm:table-cell">Data</th>
                <th className="px-3 py-2 text-right">Diferença</th>
                <th className="px-3 py-2"><span className="sr-only">Ações</span></th>
              </tr>
            </thead>
            <tbody>
              {items.map((r) => (
                <tr key={r.slipId} className="border-b border-l-4 border-b-border border-l-warn last:border-b-0">
                  <td className="px-3 py-2">
                    <Link to={`/app/financeiro/receber/${r.receivableId}` as never} className="font-mono font-semibold text-primary hover:underline">{r.receivableNumber}</Link>
                    <span className="mt-0.5 flex items-center"><StatusChip tone="warn">Aguardando conferência</StatusChip><Dica nome="Aguardando conferência" texto={REVIEW_DICAS.aguardando} /></span>
                  </td>
                  <td className="hidden px-3 py-2 sm:table-cell">{r.partyName}</td>
                  <td className="hidden px-3 py-2 text-right sm:table-cell"><Money value={r.slipAmount} /></td>
                  <td className="px-3 py-2 text-right font-semibold"><Money value={r.paidAmount} /></td>
                  <td className="hidden px-3 py-2 tabular-nums sm:table-cell">{r.creditDate ? formatDate(r.creditDate) : '—'}</td>
                  <td className="px-3 py-2 text-right"><Money value={Math.abs(r.difference)} /> <span className="text-xs text-muted-foreground">{DIFFERENCE_TEXT[differenceKind(r.difference)]}</span></td>
                  <td className="px-3 py-2 text-right">
                    {canEdit ? <button type="button" onClick={() => setOpen(r.slipId)} className="min-h-11 rounded-md bg-primary px-3 text-sm font-semibold text-primary-foreground">Conferir pagamento</button> : null}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {open && canEdit ? <PaymentReviewDialog slipId={open} open onOpenChange={(o) => { if (!o) close() }} onDone={() => { close(); void query.refetch() }} /> : null}
    </>
  )
}
