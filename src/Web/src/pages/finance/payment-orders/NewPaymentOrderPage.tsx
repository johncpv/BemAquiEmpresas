import { useQueries } from '@tanstack/react-query'
import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { getGetPayableQueryKey, getPayable, submitPaymentOrder } from '@/api/generated/bemaqui'
import type { PayableDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { Field, FormError } from '@/components/form/form'
import { DateInput, MoneyInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { Dica } from '@/help/Dica'
import { PageHeading } from '@/shell/PageHeading'
import { toNumber } from '../receivables/receivables'
import { METHOD_LABELS } from '../payables/payables'
import { ORDER_DICAS } from './payment-orders'

/** Nova ordem de pagamento (FIN-03a CA 2 e 3): os títulos escolhidos em A pagar, com o valor a pagar e os acréscimos previstos. */
export function NewPaymentOrderPage() {
  const { titulos } = useSearch({ strict: false }) as { titulos?: string }
  const ids = (titulos ?? '').split(',').filter(Boolean)
  const queries = useQueries({ queries: ids.map((id) => ({ queryKey: getGetPayableQueryKey(id), queryFn: () => getPayable(id) })) })
  if (ids.length === 0) return <EmptyState title="Nenhum título escolhido." description="Selecione os títulos em A pagar." action={<Link to={'/app/financeiro/pagar' as never} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Ir para A pagar</Link>} />
  if (queries.some((q) => q.isPending)) return <Skeleton className="h-64 max-w-4xl" />
  if (queries.some((q) => q.isError)) return <ErrorState title="Não foi possível carregar os títulos." onRetry={() => queries.forEach((q) => void q.refetch())} />
  return <OrderForm payables={queries.map((q) => q.data as PayableDetail)} />
}

interface Line { amount: string | null; charges: string | null }

function OrderForm({ payables }: { payables: PayableDetail[] }) {
  const navigate = useNavigate()
  const idempotency = useIdempotencyKey()
  const [lines, setLines] = useState<Line[]>(() => payables.map((p) => ({ amount: String(p.balance), charges: null })))
  const [date, setDate] = useState(() => payables.map((p) => p.dueDate).filter((d): d is string => !!d).sort()[0] ?? '')
  const [errors, setErrors] = useState<Record<number, string>>({})
  const [general, setGeneral] = useState<string | undefined>()
  const total = lines.reduce((n, l) => n + toNumber(l.amount) + toNumber(l.charges), 0)
  const set = (i: number, patch: Partial<Line>) => setLines((ls) => ls.map((l, k) => (k === i ? { ...l, ...patch } : l)))
  const local = Object.fromEntries(payables.flatMap((p, i) => {
    const a = toNumber(lines[i].amount)
    if (!p.paymentMethod) return [[i, `Informe a forma de pagamento do título ${p.number}.`]]
    return a <= 0 || a > p.balance ? [[i, 'O valor a pagar vai de R$ 0,01 até o saldo líquido.']] : []
  })) as Record<number, string>
  const shown = { ...local, ...errors }

  const submit = async () => {
    try {
      const saved = await submitPaymentOrder({ items: payables.map((p, i) => ({ payableId: p.id, amount: toNumber(lines[i].amount), expectedCharges: toNumber(lines[i].charges) })), expectedDate: date || null }, idempotency.options)
      idempotency.renew()
      await navigate({ to: '/app/financeiro/ordens-de-pagamento/$id', params: { id: saved.summary.id } } as never)
    } catch (e) {
      if (e instanceof ApiError) {
        const byLine: Record<number, string> = {}
        for (const [path, msgs] of Object.entries(e.fields)) { const m = /^items\[(\d+)\]/.exec(path); if (m) byLine[Number(m[1])] = msgs[0] }
        setErrors(byLine)
        setGeneral(Object.keys(byLine).length ? undefined : e.message)
      }

      throw e
    }
  }

  return (
    <>
      <PageHeading title="Nova ordem de pagamento"><p className="mt-2 text-muted-foreground">Confira o que vai ser pago e envie para aprovação.</p></PageHeading>
      <div className="max-w-4xl space-y-4">
        <FormError message={general} />
        <div className="overflow-x-auto rounded-md border border-border bg-card">
          <table className="w-full text-sm" aria-label="Títulos da ordem">
            <thead className="border-b border-border text-xs text-muted-foreground">
              <tr><th className="px-3 py-2 text-left">Título</th><th className="px-3 py-2 text-right">Saldo líquido</th><th className="px-3 py-2 text-left"><span className="inline-flex items-center">Valor a pagar<Dica nome="Pagamento parcial" texto={ORDER_DICAS.parcial} /></span></th><th className="px-3 py-2 text-left"><span className="inline-flex items-center">Acréscimos previstos<Dica nome="Acréscimos previstos" texto={ORDER_DICAS.acrescimos} /></span></th></tr>
            </thead>
            <tbody>
              {payables.map((p, i) => (
                <tr key={p.id} aria-invalid={shown[i] ? true : undefined} className="border-b border-border align-top last:border-0">
                  <td className="px-3 py-2"><b>{p.number}</b> — {p.partyName}<span className="block text-xs text-muted-foreground">{p.paymentMethod ? METHOD_LABELS[p.paymentMethod] : 'Sem forma de pagamento'}</span>{shown[i] ? <span role="alert" className="block text-xs text-crit-ink">{shown[i]}</span> : null}</td>
                  <td className="px-3 py-2 text-right"><Money value={p.balance} /></td>
                  <td className="px-3 py-2"><MoneyInput aria-label={`Valor a pagar de ${p.number}`} value={lines[i].amount} onChange={(v) => { setErrors({}); set(i, { amount: v }) }} /></td>
                  <td className="px-3 py-2"><MoneyInput aria-label={`Acréscimos previstos de ${p.number}`} value={lines[i].charges} onChange={(v) => set(i, { charges: v })} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <div className="flex flex-wrap items-end justify-between gap-3">
          <Field label="Data prevista do pagamento" hint="Sugestão: o menor vencimento.">{(f) => <DateInput {...f} value={date} onChange={setDate} />}</Field>
          <p aria-live="polite" className="flex items-center text-sm">Total aprovado<Dica nome="Total aprovado" texto={ORDER_DICAS.aprovado} />: <Money value={total} className="ml-1 font-semibold" /></p>
        </div>
        <div className="flex justify-end gap-2">
          <Link to={'/app/financeiro/pagar' as never} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</Link>
          <ConfirmDialog verb="Enviar para aprovação" done="Enviada para aprovação" title="Enviar a ordem para aprovação?" description="Os títulos ficam travados até a decisão. Quem aprova recebe o aviso no sino."
            impact={<p className="text-sm">{payables.length} {payables.length === 1 ? 'título' : 'títulos'}, total aprovado <Money value={total} /></p>}
            preconditions={[{ ok: Object.keys(local).length === 0, text: 'Todos os títulos com forma de pagamento e valor até o saldo.' }]}
            onConfirm={submit}
            trigger={<button type="button" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Enviar para aprovação</button>} />
        </div>
      </div>
    </>
  )
}
