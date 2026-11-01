import { Plus, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { simulateTaxes } from '@/api/generated/bemaqui'
import type { RecipientType, SimulateTaxRequest, TaxItem, TaxOperation, TaxSimulationView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { MoneyInput, NcmInput, PercentInput, QuantityInput } from '@/components/form/inputs'
import { SidePanel } from '@/components/ui/side-panel'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatMoney } from '@/lib/format'
import { DICAS, firstErrors, OPERATION_LABEL, OPERATIONS, percent, RECIPIENT_LABEL, RECIPIENTS, STATES, TAX_LABEL } from './tax-rules'

const STORAGE = 'bq.fiscal.simulacao'

type Input = Omit<SimulateTaxRequest, 'ruleVersionId' | 'issueDate'>

const blankItem = (): TaxItem => ({ description: '', ncm: '', origin: 0, cest: null, quantity: 1, unitPrice: 1000, discount: 0, freight: 0, otherExpenses: 0, ipiRate: 0 })

/** A última simulação deste navegador (até a FIS-03a guardar o último item vendido); sem ela, um item em branco. */
function lastInput(operation: TaxOperation): Input {
  try {
    const saved = localStorage.getItem(STORAGE)
    if (saved) return { ...(JSON.parse(saved) as Input), operation }
  } catch {
    // sem armazenamento local: começa em branco
  }
  return { operation, recipientState: 'SP', recipientType: 'Contributor', finalConsumer: false, items: [blankItem()] }
}

const num = (v: string | null) => (v == null || v === '' ? 0 : Number(v))

/**
 * "Simular" em painel lateral (FIS-02b CA 10, padrão da FND-09d): operação, destinatário e itens; mostra a regra aplicada a cada item, as
 * bases, os impostos, os totais e o que entra no custo do projeto. Nada é gravado e o Emissor Fiscal não é chamado. Com a versão aberta
 * (ruleVersionId), simula aquela versão, mesmo em rascunho. A 400 px ocupa a tela cheia e os itens viram cartões.
 */
export function TaxSimulationPanel({ open, onOpenChange, ruleVersionId, operation = 'OwnProductionSale' }: {
  open: boolean
  onOpenChange: (open: boolean) => void
  ruleVersionId?: string
  operation?: TaxOperation
}) {
  const [input, setInput] = useState<Input>(() => lastInput(operation))
  const [result, setResult] = useState<TaxSimulationView | null>(null)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [formError, setFormError] = useState<string>()
  const [busy, setBusy] = useState(false)
  const setItem = (i: number, patch: Partial<TaxItem>) => setInput((v) => ({ ...v, items: v.items.map((it, n) => (n === i ? { ...it, ...patch } : it)) }))

  const run = async () => {
    setBusy(true)
    setErrors({})
    setFormError(undefined)
    try {
      const view = await simulateTaxes({ ...input, ruleVersionId: ruleVersionId ?? null })
      setResult(view)
      try { localStorage.setItem(STORAGE, JSON.stringify(input)) } catch { /* sem armazenamento local */ }
    } catch (e) {
      setResult(null)
      if (e instanceof ApiError) {
        setErrors(firstErrors(e.fields))
        setFormError(e.message)
      } else {
        setFormError('Não foi possível simular. Tente de novo.')
      }
    } finally {
      setBusy(false)
    }
  }

  return (
    <SidePanel open={open} onOpenChange={onOpenChange} title="Simular nota" description="Nada é gravado e o Emissor Fiscal não é chamado."
      footer={<button type="button" disabled={busy} onClick={() => void run()} className="min-h-11 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">{busy ? 'Simulando…' : 'Simular'}</button>}>
      <form onSubmit={(e) => { e.preventDefault(); void run() }} noValidate aria-label="Dados da simulação" className="space-y-3">
        <FormError message={formError} />
        <Field label="Operação">
          {(p) => (
            <select {...p} value={input.operation} onChange={(e) => setInput((v) => ({ ...v, operation: e.target.value as TaxOperation }))} className={inputClass}>
              {OPERATIONS.map((o) => <option key={o} value={o}>{OPERATION_LABEL[o]}</option>)}
            </select>
          )}
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="UF do cliente" error={errors.recipientState}>
            {(p) => (
              <select {...p} value={input.recipientState} onChange={(e) => setInput((v) => ({ ...v, recipientState: e.target.value }))} className={inputClass}>
                {STATES.map((s) => <option key={s} value={s}>{s}</option>)}
              </select>
            )}
          </Field>
          <Field label="Tipo de cliente">
            {(p) => (
              <select {...p} value={input.recipientType} onChange={(e) => setInput((v) => ({ ...v, recipientType: e.target.value as RecipientType }))} className={inputClass}>
                {RECIPIENTS.map((r) => <option key={r} value={r}>{RECIPIENT_LABEL[r]}</option>)}
              </select>
            )}
          </Field>
        </div>
        <label className="flex min-h-11 items-center gap-2 text-sm">
          <input type="checkbox" checked={input.finalConsumer} onChange={(e) => setInput((v) => ({ ...v, finalConsumer: e.target.checked }))} className="size-4 accent-primary" />
          Consumidor final
        </label>

        <h3 className="pt-1 text-sm font-semibold">Itens</h3>
        {input.items.map((item, i) => (
          <fieldset key={i} className="space-y-2 rounded-md border border-border p-3">
            <legend className="px-1 text-xs font-semibold text-muted-foreground">Item {i + 1}</legend>
            <Field label="Descrição">{(p) => <input {...p} value={item.description ?? ''} onChange={(e) => setItem(i, { description: e.target.value })} className={inputClass} />}</Field>
            <div className="grid grid-cols-2 gap-2">
              <Field label="NCM" error={errors[`items[${i}].ncm`]}>{(p) => <NcmInput {...p} value={item.ncm ?? ''} onChange={(v) => setItem(i, { ncm: v })} />}</Field>
              <Field label="Origem" hint="0 nacional; 1, 2, 3 e 8 importada.">
                {(p) => (
                  <select {...p} value={item.origin} onChange={(e) => setItem(i, { origin: Number(e.target.value) })} className={inputClass}>
                    {[0, 1, 2, 3, 4, 5, 6, 7, 8].map((o) => <option key={o} value={o}>{o}</option>)}
                  </select>
                )}
              </Field>
              <Field label="Quantidade" error={errors[`items[${i}].quantity`]}>{(p) => <QuantityInput {...p} value={String(item.quantity)} onChange={(v) => setItem(i, { quantity: num(v) })} />}</Field>
              <Field label="Valor unitário">{(p) => <MoneyInput {...p} value={String(item.unitPrice)} onChange={(v) => setItem(i, { unitPrice: num(v) })} />}</Field>
              <Field label="Desconto" error={errors[`items[${i}].discount`]}>{(p) => <MoneyInput {...p} value={String(item.discount ?? 0)} onChange={(v) => setItem(i, { discount: num(v) })} />}</Field>
              <Field label="Frete">{(p) => <MoneyInput {...p} value={String(item.freight ?? 0)} onChange={(v) => setItem(i, { freight: num(v) })} />}</Field>
              <Field label="Outras despesas">{(p) => <MoneyInput {...p} value={String(item.otherExpenses ?? 0)} onChange={(v) => setItem(i, { otherExpenses: num(v) })} />}</Field>
              <Field label="IPI da TIPI" error={errors[`items[${i}].ipiRate`]} hint="Alíquota do NCM na TIPI.">
                {(p) => <PercentInput {...p} scale={4} value={item.ipiRate == null ? '' : String(item.ipiRate)} onChange={(v) => setItem(i, { ipiRate: v == null ? null : Number(v) })} />}
              </Field>
            </div>
            {input.items.length > 1 ? (
              <button type="button" onClick={() => setInput((v) => ({ ...v, items: v.items.filter((_, n) => n !== i) }))} className="inline-flex min-h-11 items-center gap-1 text-sm font-semibold text-muted-foreground">
                <Trash2 aria-hidden="true" className="size-4" />Remover item
              </button>
            ) : null}
          </fieldset>
        ))}
        <button type="button" onClick={() => setInput((v) => ({ ...v, items: [...v.items, blankItem()] }))} className="inline-flex min-h-11 items-center gap-1 text-sm font-semibold text-primary">
          <Plus aria-hidden="true" className="size-4" />Adicionar item
        </button>
      </form>

      {result ? <SimulationResult view={result} /> : (
        <p className="mt-4 rounded-md border border-dashed border-border p-3 text-sm text-muted-foreground">Preencha o cliente e os itens e clique em Simular para ver a nota com a regra de cada item.</p>
      )}
    </SidePanel>
  )
}

function SimulationResult({ view }: { view: TaxSimulationView }) {
  const c = view.calculation
  return (
    <section aria-label="Nota simulada" aria-live="polite" className="mt-4 space-y-3">
      {c.blocks.map((b) => (
        <div key={b} role="alert" className="rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm font-semibold text-crit-ink">
          {b}<Dica nome="DIFAL" texto={DICAS.difal} />
        </div>
      ))}
      {view.warnings.map((w) => <p key={w} role="status" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">{w}</p>)}
      {c.items.map((item) => (
        <article key={item.index} className="rounded-md border border-border bg-card p-3 text-sm">
          <p className="font-semibold">{item.description || `Item ${item.index + 1}`}</p>
          <p className="text-xs text-muted-foreground">Regra: {item.ruleName} (versão {item.ruleVersion}){item.cfop ? ` · CFOP ${item.cfop}` : ''}</p>
          <ul className="mt-2 space-y-1">
            {item.taxes.map((t) => (
              <li key={t.code} className="flex flex-wrap justify-between gap-2 tabular-nums">
                <span>{TAX_LABEL[t.code] ?? t.code}{t.cst ? ` · CST ${t.cst}` : ''} <span className="text-muted-foreground">{percent(t.rate)} de {formatMoney(t.base)}</span></span>
                <span className="font-semibold">{formatMoney(t.amount)}{t.inProjectCost ? <StatusChip tone="info" className="ml-2">custo</StatusChip> : null}</span>
              </li>
            ))}
          </ul>
        </article>
      ))}
      <dl className="grid grid-cols-[1fr_auto] gap-x-3 gap-y-1 rounded-md border border-border bg-card p-3 text-sm tabular-nums">
        <dt>Produtos e serviços</dt><dd className="text-right">{formatMoney(c.products)}</dd>
        {c.totals.map((t) => (
          <div key={t.code} className="contents"><dt className="text-muted-foreground">{TAX_LABEL[t.code] ?? t.code}</dt><dd className="text-right">{formatMoney(t.amount)}</dd></div>
        ))}
        <dt className="font-semibold">Total da nota</dt><dd className="text-right font-semibold">{formatMoney(c.invoiceTotal)}</dd>
        <dt className="flex items-center font-semibold">Entra no custo do projeto<Dica nome="Entra no custo do projeto" texto={DICAS.custo} /></dt><dd className="text-right font-semibold">{formatMoney(c.costTax)}</dd>
      </dl>
    </section>
  )
}
