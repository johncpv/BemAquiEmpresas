import { Link } from '@tanstack/react-router'
import { Copy } from 'lucide-react'
import { useState } from 'react'
import { activatePaymentNotice, rotatePaymentNotice, useGetPaymentNotice } from '@/api/generated/bemaqui'
import type { PaymentNoticeActivation } from '@/api/generated/model'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDateTime, formatInstantDate } from '@/lib/format'
import { NOTICE_HINTS, NOTICE_OFF_TEXT, modeLabel, reportText } from './payment-notice'

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex flex-wrap justify-between gap-x-4 gap-y-0.5 text-sm">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="min-w-0 break-all text-right font-medium tabular-nums">{children}</dd>
    </div>
  )
}

/**
 * Seção "Aviso de pagamento" da conta do Inter (BNK-02 CA 1 a 3, 11 e 12; padrão 6): situação do aviso, modo do endereço, última entrega,
 * última conferência e o relatório. "Ativar aviso de pagamento" e "Trocar endereço do aviso" pedem o 2FA de novo (o passo do código abre
 * sozinho); o endereço completo aparece uma única vez, com "Copiar", para o cadastro manual no Internet Banking.
 */
export function PaymentNoticeSection({ bankAccountId, canEdit, tested }: { bankAccountId: string; canEdit: boolean; tested: boolean }) {
  const query = useGetPaymentNotice(bankAccountId, { query: { retry: false } })
  const [shown, setShown] = useState<PaymentNoticeActivation | null>(null)
  const [copied, setCopied] = useState(false)

  const apply = async (rotate: boolean) => {
    const result = rotate ? await rotatePaymentNotice(bankAccountId) : await activatePaymentNotice(bankAccountId)
    setShown(result)
    setCopied(false)
    await query.refetch()
  }

  const copy = async (url: string) => {
    try {
      await navigator.clipboard.writeText(url)
      setCopied(true)
      notifySuccess('Endereço copiado')
    } catch {
      setCopied(false)
    }
  }

  return (
    <section aria-label="Aviso de pagamento" className="space-y-2 rounded-md border border-border bg-card p-4 lg:col-span-2">
      <h2 className="flex items-center text-sm font-semibold uppercase tracking-wide text-muted-foreground">Aviso de pagamento<Dica nome="Aviso de pagamento" texto={NOTICE_HINTS.notice} /></h2>
      {query.isPending ? <Skeleton className="h-24" /> : query.isError || !query.data ? (
        <ErrorState message="Não foi possível carregar a situação do aviso. Tente de novo." onRetry={() => void query.refetch()} />
      ) : (() => {
        const view = query.data
        return (
          <>
            {view.active ? (
              <p><StatusChip tone="ok">{`Aviso de pagamento ativo desde ${view.since ? formatInstantDate(view.since) : '—'}`}</StatusChip></p>
            ) : (
              <p role="status" className="rounded-md border border-warn bg-warn-soft px-3 py-2 text-sm text-warn-ink">{NOTICE_OFF_TEXT}</p>
            )}
            <dl className="space-y-1">
              <Row label="Modo do endereço">{modeLabel(view.mode)}<Dica nome="Modo do endereço" texto={NOTICE_HINTS.mode} /></Row>
              {view.maskedUrl ? <Row label="Endereço">{view.maskedUrl}</Row> : null}
              {view.rotatedAt ? <Row label="Endereço trocado em">{formatDateTime(view.rotatedAt)}</Row> : null}
              {view.previousValidUntil ? <Row label="Endereço anterior aceito até">{formatDateTime(view.previousValidUntil)}</Row> : null}
              <Row label="Última entrega">{view.lastDeliveryAt ? formatDateTime(view.lastDeliveryAt) : 'nenhuma ainda'}</Row>
              <Row label="Última conferência">{view.lastReconcileAt ? formatDateTime(view.lastReconcileAt) : 'ainda não rodou'}</Row>
              {view.lastReconcile ? <Row label="Resultado">{reportText(view.lastReconcile)}</Row> : null}
            </dl>
            {view.reconcileFailures > 0 ? (
              <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">
                A conferência falhou {view.reconcileFailures === 1 ? '1 vez' : `${view.reconcileFailures} vezes seguidas`}{view.lastReconcileError ? `: ${view.lastReconcileError}` : '.'} Teste a conexão e confira o certificado.
              </p>
            ) : null}
            {view.pendingReview > 0 ? (
              <p className="text-sm"><Link to={'/app/financeiro/receber' as never} search={{ filtro: 'a-conferir' } as never} className="font-semibold text-primary hover:underline">{view.pendingReview === 1 ? '1 pagamento a conferir' : `${view.pendingReview} pagamentos a conferir`}</Link></p>
            ) : null}
            {shown ? (
              <div role="status" className="space-y-2 rounded-md border border-border bg-muted px-3 py-2 text-sm">
                <p className="font-semibold">{shown.message}. Este é o endereço completo e ele aparece só agora:</p>
                <p className="break-all font-mono text-xs">{shown.url}</p>
                <div className="flex flex-wrap items-center gap-2">
                  <button type="button" onClick={() => void copy(shown.url)} className="inline-flex min-h-11 items-center gap-1 rounded-md border border-border px-3 font-semibold"><Copy aria-hidden="true" className="size-4" />{copied ? 'Copiado' : 'Copiar'}</button>
                  <span className="text-muted-foreground">O endereço já foi cadastrado no Inter. Copie só se precisar cadastrá-lo à mão no Internet Banking.</span>
                </div>
              </div>
            ) : null}
            {canEdit ? (
              <div className="flex flex-wrap gap-2">
                {view.active ? (
                  <ConfirmDialog verb="Trocar endereço do aviso" done="Endereço trocado" title="Trocar o endereço do aviso de pagamento?"
                    description="Um endereço novo é cadastrado no Inter. O atual continua aceito por 24 horas e depois é recusado. Os Proprietários são avisados."
                    onConfirm={() => apply(true)}
                    trigger={<button type="button" className="min-h-11 rounded-md border border-border px-3 text-sm font-semibold">Trocar endereço do aviso</button>} />
                ) : (
                  <ConfirmDialog verb="Ativar aviso de pagamento" done="Aviso de pagamento ativo" title="Ativar o aviso de pagamento do Inter?"
                    description="O sistema cadastra no Inter o endereço que recebe o aviso de cada boleto pago. O título só baixa depois de o sistema reconsultar o boleto no banco."
                    preconditions={[{ ok: tested, text: 'Conexão com o Inter testada e funcionando.' }]}
                    onConfirm={() => apply(false)}
                    trigger={<button type="button" className="min-h-11 rounded-md bg-primary px-3 text-sm font-semibold text-primary-foreground">Ativar aviso de pagamento</button>} />
                )}
              </div>
            ) : null}
          </>
        )
      })()}
    </section>
  )
}
