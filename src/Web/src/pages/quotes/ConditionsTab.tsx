import { useState } from 'react'
import { quoteMilestonesFromTerm, saveQuoteConditions, useGetQuoteConditions, useListQuotePaymentTerms } from '@/api/generated/bemaqui'
import type { QuoteConditions, QuoteDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput } from '@/components/form/inputs'
import { RowsEditor } from '@/components/form/rows-editor'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { Money } from '@/components/ui/money'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { Dica } from '@/help/Dica'
import { milestoneColumns } from './milestone-columns'
import { amounts, emptyMilestone, equalPercents, fromView, type MilestoneDraft, remaining, toInput } from './milestones'

/** Aba Condições (COM-01b CA 1 a 8): marcos com % e R$ lado a lado, o que falta para 100% e a validade. */
export function ConditionsTab({ quote, editable, refresh }: { quote: QuoteDetail; editable: boolean; refresh: () => Promise<void> }) {
  const query = useGetQuoteConditions(quote.id)
  if (query.isPending) return <Skeleton className="h-48" />
  if (query.isError) return <ErrorState onRetry={() => query.refetch()} />
  return <ConditionsForm key={query.data.version} quote={quote} conditions={query.data} editable={editable} refresh={async () => { await query.refetch(); await refresh() }} />
}

function ConditionsForm({ quote, conditions, editable, refresh }: { quote: QuoteDetail; conditions: QuoteConditions; editable: boolean; refresh: () => Promise<void> }) {
  const terms = useListQuotePaymentTerms()
  const [rows, setRows] = useState<MilestoneDraft[]>(() => conditions.milestones.map(fromView))
  const [valid, setValid] = useState(conditions.validUntil ?? '')
  const [term, setTerm] = useState(conditions.defaultPaymentTermId ?? '')
  const [error, setError] = useState<string | undefined>()
  const percents = rows.map((r) => (r.percent ? Number(r.percent) : 0))
  const sum = percents.reduce((a, b) => a + b, 0)
  const values = amounts(quote.totalPrice, percents)
  const missing = remaining(percents)
  const save = async () => {
    setError(undefined)
    try {
      const saved = await saveQuoteConditions(quote.id, { milestones: rows.map(toInput), validUntil: valid || null, version: quote.version })
      notifySuccess('Condições salvas')
      if (saved.warning) setError(saved.warning)
      await refresh()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar as condições.')
    }
  }

  const adjustLast = () => setRows(rows.map((r, i) => (i === rows.length - 1 ? { ...r, percent: String(Math.round(((r.percent ? Number(r.percent) : 0) + 100 - sum) * 10000) / 10000) } : r)))
  return (
    <fieldset disabled={!editable} className="max-w-4xl space-y-4">
      <FormError message={error} />
      <label className="block max-w-xs space-y-1 text-sm font-medium">
        <span className="inline-flex items-center">Validade<Dica nome="Validade" texto="Até quando o preço vale. Depois disso, o orçamento não é enviado sem ajustar a data." /></span>
        <DateInput value={valid} onChange={setValid} />
      </label>
      {rows.length === 0 ? <p className="text-sm text-muted-foreground">Defina como o cliente vai pagar.</p> : null}
      <RowsEditor noun="marco" rows={rows} onChange={setRows} newRow={emptyMilestone} duplicate disabled={!editable} columns={milestoneColumns(values)}
        summary={<span>Soma {sum.toLocaleString('pt-BR', { maximumFractionDigits: 4 })}% · <Money value={values.reduce((a, b) => a + b, 0)} />{missing ? <b className="ml-2 text-warn-ink">{missing}</b> : null}</span>} />
      {editable ? (
        <div className="flex flex-wrap items-end gap-2">
          <button type="button" disabled={rows.length === 0} onClick={() => setRows(rows.map((r, i) => ({ ...r, percent: String(equalPercents(rows.length)[i]) })))} className="rounded-md border border-border px-3 py-1.5 text-sm font-semibold disabled:opacity-50">Dividir igualmente</button>
          <button type="button" disabled={rows.length === 0 || !missing} onClick={adjustLast} className="rounded-md border border-border px-3 py-1.5 text-sm font-semibold disabled:opacity-50">Ajustar o último</button>
          <label className="space-y-1 text-sm font-medium">Condição de pagamento
            <select value={term} onChange={(e) => setTerm(e.target.value)} className={inputClass}><option value="">Escolha</option>{(terms.data ?? []).map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}</select>
          </label>
          <ConfirmDialog verb="Substituir marcos" done="Marcos substituídos" title="Preencher pela condição de pagamento?" description="Os marcos atuais são trocados pelas parcelas da condição, no evento Assinatura do contrato."
            preconditions={[{ ok: !!term, text: 'Condição escolhida.' }]}
            onConfirm={async () => { const list = await quoteMilestonesFromTerm(quote.id, term); setRows(list.map(fromView)) }}
            trigger={<button type="button" className="rounded-md border border-border px-3 py-1.5 text-sm font-semibold">Preencher pela condição de pagamento</button>} />
          <button type="button" onClick={() => void save()} className="ml-auto rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar condições</button>
        </div>
      ) : null}
      <p className="flex flex-wrap items-center text-xs text-muted-foreground">
        Gatilho<Dica nome="Gatilho" texto="O que libera a cobrança do marco: uma data fixa ou um evento do projeto." />
        · Prazo após o evento<Dica nome="Prazo após o evento" texto="Dias corridos depois do evento para o vencimento da parcela." />
      </p>
    </fieldset>
  )
}
