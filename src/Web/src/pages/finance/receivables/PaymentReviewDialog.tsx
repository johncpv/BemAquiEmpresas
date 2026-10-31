import { useState } from 'react'
import { reviewPayment, useGetPaymentReview } from '@/api/generated/bemaqui'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { formatDate } from '@/lib/format'
import { APPROVAL_TEXT, DIFFERENCE_TEXT, differenceKind, pickOption } from './payment-review'

interface Props { slipId: string; open: boolean; onOpenChange: (open: boolean) => void; onDone: () => void }

/**
 * "Conferir pagamento" (BNK-02 CA 9): o pagamento que o Inter confirmou com diferença. Cada opção mostra o impacto (saldo antes e depois);
 * a mais provável vem escolhida (desconto dentro do limite quando pagou a menor até a data do desconto). Desconto acima do limite da
 * FIN-01 só para quem aprova. Em telas estreitas o diálogo ocupa a tela inteira.
 */
export function PaymentReviewDialog({ slipId, open, onOpenChange, onDone }: Props) {
  const query = useGetPaymentReview(slipId, { query: { enabled: open, retry: false } })
  const [chosen, setChosen] = useState<string | null>(null)
  const draft = query.data
  const option = draft ? pickOption(draft.options, chosen, draft.suggestedDecision) : undefined
  const kind = draft ? differenceKind(draft.payment.difference) : 'equal'

  return (
    <ConfirmDialog
      size="lg"
      open={open}
      onOpenChange={(o) => { if (o) setChosen(null); onOpenChange(o) }}
      verb={option?.label ?? 'Conferir pagamento'}
      done="Pagamento conferido"
      title="Conferir pagamento"
      description={draft ? `Título ${draft.payment.receivableNumber} · ${draft.payment.partyName}` : undefined}
      onReload={() => query.refetch()}
      preconditions={[
        { ok: !!option, text: 'Escolha o que fazer com o pagamento.' },
        { ok: !option || option.allowed, text: APPROVAL_TEXT },
      ]}
      impact={option ? (
        <dl className="grid grid-cols-2 gap-1 text-sm tabular-nums">
          <dt>Saldo antes</dt><dd className="text-right"><Money value={option.balanceBefore} /></dd>
          {option.discount > 0 ? <><dt>Desconto</dt><dd className="text-right"><Money value={option.discount} /></dd></> : null}
          {option.interest > 0 ? <><dt>Juros</dt><dd className="text-right"><Money value={option.interest} /></dd></> : null}
          <dt>Saldo depois</dt><dd className="text-right font-semibold"><Money value={option.balanceAfter} /></dd>
        </dl>
      ) : undefined}
      onConfirm={async () => {
        if (!draft || !option) return
        await reviewPayment(slipId, { decision: option.decision, version: draft.payment.version })
        onDone()
      }}
    >
      {query.isPending ? <Skeleton className="h-32" /> : query.isError || !draft ? (
        <ErrorState message="Não foi possível abrir o pagamento. Ele pode já ter sido conferido." onRetry={() => void query.refetch()} />
      ) : (
        <div className="space-y-3">
          <dl className="grid grid-cols-2 gap-1 text-sm tabular-nums">
            <dt className="text-muted-foreground">Valor do boleto</dt><dd className="text-right"><Money value={draft.payment.slipAmount} /></dd>
            <dt className="text-muted-foreground">Valor pago</dt><dd className="text-right font-semibold"><Money value={draft.payment.paidAmount} /></dd>
            <dt className="text-muted-foreground">Data do crédito</dt><dd className="text-right">{draft.payment.creditDate ? formatDate(draft.payment.creditDate) : '—'}</dd>
            <dt className="text-muted-foreground">Diferença</dt>
            <dd className="text-right"><Money value={Math.abs(draft.payment.difference)} /> {DIFFERENCE_TEXT[kind]}</dd>
          </dl>
          {draft.payment.reason ? <p className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">{draft.payment.reason}</p> : null}
          <fieldset className="space-y-2">
            <legend className="text-sm font-medium">O que fazer</legend>
            {draft.options.map((o) => (
              <label key={o.decision} className="flex min-h-11 cursor-pointer items-center gap-3 rounded-md border border-border px-3 py-2 text-sm has-[:checked]:border-primary">
                <input type="radio" name="decisao" value={o.decision} checked={option?.decision === o.decision} onChange={() => setChosen(o.decision)} className="size-4" />
                <span className="flex-1">
                  <span className="font-semibold">{o.label}</span>
                  <span className="block text-xs text-muted-foreground">Saldo fica em <Money value={o.balanceAfter} />{o.requiresApproval ? ' · pede quem aprova' : ''}</span>
                </span>
              </label>
            ))}
          </fieldset>
          {!draft.receivableOpen ? <p className="text-sm text-muted-foreground">O título não está em aberto: a entrada fica registrada na conta, sem baixa. Combine a devolução com o cliente.</p> : null}
        </div>
      )}
    </ConfirmDialog>
  )
}
