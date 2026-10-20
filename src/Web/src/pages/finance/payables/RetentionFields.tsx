import { Field } from '@/components/form/form'
import { MoneyInput } from '@/components/form/inputs'
import { Money } from '@/components/ui/money'
import { PAYABLE_DICAS, type RetentionDraft, retentionSum } from './payables'


/** Retenções da nota (FIN-02 CA 4): sem cálculo; o líquido a pagar aparece ao vivo. */
export function RetentionFields({ value, onChange, amount, errors, disabled }: { value: RetentionDraft; onChange: (r: RetentionDraft) => void; amount: number; errors: Partial<Record<string, string>>; disabled?: boolean }) {
  const set = (patch: Partial<RetentionDraft>) => onChange({ ...value, ...patch })
  return (
    <fieldset disabled={disabled} className="space-y-3">
      <div className="grid gap-3 sm:grid-cols-4">
        <Field label="ISS" error={errors.retentionIss} dica={PAYABLE_DICAS.retencoes}>{(p) => <MoneyInput {...p} value={value.iss} onChange={(iss) => set({ iss })} />}</Field>
        <Field label="INSS" error={errors.retentionInss}>{(p) => <MoneyInput {...p} value={value.inss} onChange={(inss) => set({ inss })} />}</Field>
        <Field label="IR" error={errors.retentionIr}>{(p) => <MoneyInput {...p} value={value.ir} onChange={(ir) => set({ ir })} />}</Field>
        <Field label="PIS/COFINS/CSLL" error={errors.retentionPcc}>{(p) => <MoneyInput {...p} value={value.pcc} onChange={(pcc) => set({ pcc })} />}</Field>
      </div>
      <p aria-live="polite" className="rounded-md border border-border bg-muted px-3 py-2 text-sm tabular-nums">
        Valor líquido a pagar: <b><Money value={Math.max(0, amount - retentionSum(value))} /></b>
      </p>
    </fieldset>
  )
}
