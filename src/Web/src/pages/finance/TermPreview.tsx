import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { previewPaymentTerm } from '@/api/generated/bemaqui'
import type { Installment } from '@/api/generated/model'
import { Field } from '@/components/form/form'
import { DateInput, MoneyInput } from '@/components/form/inputs'
import { Money } from '@/components/ui/money'
import { formatDate } from '@/lib/format'

const today = () => new Date().toLocaleDateString('sv-SE', { timeZone: 'America/Sao_Paulo' })

/**
 * Prévia da divisão (CA 13): o servidor divide, com a mesma conta que os títulos vão usar (CA 14). A última parcela absorve
 * o arredondamento; o vencimento é em dias corridos, sem ajuste de dia não útil.
 */
export function TermPreview({ installments, valid }: { installments: Installment[]; valid: boolean }) {
  const [amount, setAmount] = useState<string | null>('1000.00')
  const [baseDate, setBaseDate] = useState(today())
  const ready = valid && !!amount && Number(amount) > 0 && !!baseDate
  const query = useQuery({
    queryKey: ['payment-term-preview', installments, amount, baseDate],
    queryFn: () => previewPaymentTerm({ installments, amount: Number(amount), baseDate }),
    enabled: ready,
  })

  return (
    <section aria-label="Prévia das parcelas" className="space-y-3 rounded-md border border-border bg-card p-4">
      <h2 className="text-base font-semibold">Prévia das parcelas</h2>
      <div className="grid gap-3 sm:grid-cols-2">
        <Field label="Valor de exemplo">{(p) => <MoneyInput {...p} value={amount} onChange={setAmount} />}</Field>
        <Field label="Data base" hint="Normalmente a data da emissão do título.">{(p) => <DateInput {...p} value={baseDate} onChange={setBaseDate} />}</Field>
      </div>
      {!ready ? (
        <p className="text-sm text-muted-foreground">Ajuste as parcelas para somar 100% e veja aqui como o valor se divide.</p>
      ) : query.isPending ? (
        <p className="text-sm text-muted-foreground">Calculando…</p>
      ) : query.isError ? (
        <p role="alert" className="text-sm text-crit-ink">Não foi possível calcular a prévia. Confira as parcelas.</p>
      ) : (
        <table className="w-full text-sm" aria-label="Parcelas da prévia">
          <thead className="text-xs text-muted-foreground">
            <tr><th scope="col" className="py-1 text-left font-semibold">Parcela</th><th scope="col" className="py-1 text-left font-semibold">Vencimento</th><th scope="col" className="py-1 text-right font-semibold">Valor</th></tr>
          </thead>
          <tbody>
            {query.data.map((p) => (
              <tr key={p.number} className="border-t border-border">
                <td className="py-1 tabular-nums">{p.number}</td>
                <td className="py-1 tabular-nums">{formatDate(p.dueDate)}</td>
                <td className="py-1 text-right"><Money value={p.amount} /></td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  )
}
