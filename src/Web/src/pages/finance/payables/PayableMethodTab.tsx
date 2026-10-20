import { useQueryClient } from '@tanstack/react-query'
import { AlertTriangle } from 'lucide-react'
import { useState } from 'react'
import { getGetPayablePaymentMethodQueryKey, setPayablePaymentMethod, useGetPayablePaymentMethod } from '@/api/generated/bemaqui'
import type { PayableDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { FormError } from '@/components/form/form'
import { notifySuccess } from '@/components/ui/messages'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { formatDateTime } from '@/lib/format'
import { emptyMethod, METHOD_LABELS, toDetails } from './payables'
import { PaymentMethodFields } from './PaymentMethodFields'

/** Aba Forma de pagamento (FIN-02 CA 6 a 8): dados mascarados, avisos âmbar e a troca (travada em OP). */
export function PayableMethodTab({ payable, canEdit, onChanged }: { payable: PayableDetail; canEdit: boolean; onChanged: () => void }) {
  const client = useQueryClient()
  const query = useGetPayablePaymentMethod(payable.id)
  const [editing, setEditing] = useState(false)
  const [method, setMethod] = useState(emptyMethod())
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const inOrder = payable.status === 'AwaitingApproval' || payable.status === 'Approved'
  if (query.isPending) return <Skeleton className="h-40" />
  if (query.isError) return <ErrorState onRetry={() => query.refetch()} />
  const view = query.data
  const d = view?.details

  const save = async () => {
    try {
      const saved = await setPayablePaymentMethod(payable.id, { paymentDetails: toDetails(method), version: payable.version })
      client.setQueryData(getGetPayablePaymentMethodQueryKey(payable.id), saved)
      notifySuccess('Forma de pagamento salva')
      setEditing(false)
      onChanged()
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar a forma de pagamento.', fields: {} })
    }
  }

  const warnings = [view?.holderWarning, view?.boletoWarning].filter(Boolean) as string[]
  return (
    <div className="max-w-3xl space-y-4">
      {warnings.map((w) => <p key={w} role="status" className="flex items-start gap-2 rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink"><AlertTriangle aria-hidden="true" className="mt-0.5 size-4" />{w}</p>)}
      {d ? (
        <dl className="grid gap-2 rounded-md border border-border bg-card p-4 text-sm sm:grid-cols-2">
          <div><dt className="text-muted-foreground">Tipo</dt><dd className="font-semibold">{d.kind ? METHOD_LABELS[d.kind] : '—'}</dd></div>
          {d.pixKey ? <div><dt className="text-muted-foreground">Chave Pix</dt><dd className="font-mono">{d.pixKey}</dd></div> : null}
          {d.barcode ? <div className="sm:col-span-2"><dt className="text-muted-foreground">Linha digitável</dt><dd className="break-all font-mono">{d.barcode}</dd></div> : null}
          {d.bankCode ? <div><dt className="text-muted-foreground">Banco, agência e conta</dt><dd className="tabular-nums">{d.bankCode} · {d.agency} · {d.account}</dd></div> : null}
          {d.holderName ? <div><dt className="text-muted-foreground">Titular</dt><dd>{d.holderName} · {d.holderDocument}</dd></div> : null}
          {d.description ? <div><dt className="text-muted-foreground">Como paga</dt><dd>{d.description}</dd></div> : null}
          {payable.paymentMethodChangedAt ? <div><dt className="text-muted-foreground">Alterada em</dt><dd className="tabular-nums">{formatDateTime(payable.paymentMethodChangedAt)}</dd></div> : null}
        </dl>
      ) : <p className="text-sm text-muted-foreground">Ainda sem forma de pagamento. Ela é obrigatória para o título entrar numa ordem de pagamento.</p>}
      {inOrder ? <p className="text-sm text-muted-foreground">Em ordem de pagamento: para trocar a forma, retire o título da ordem.</p> : null}
      {canEdit && !inOrder && !editing ? <button type="button" onClick={() => { setMethod(emptyMethod()); setError(null); setEditing(true) }} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">{d ? 'Trocar forma de pagamento' : 'Informar forma de pagamento'}</button> : null}
      {editing ? (
        <div className="space-y-3 rounded-md border border-border bg-card p-4">
          <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
          <PaymentMethodFields value={method} onChange={setMethod} errors={Object.fromEntries(Object.entries(error?.fields ?? {}).map(([k, v]) => [k, v[0]]))} />
          <div className="flex justify-end gap-2">
            <button type="button" onClick={() => setEditing(false)} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
            <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar forma de pagamento</button>
          </div>
        </div>
      ) : null}
    </div>
  )
}
