import { Field, FormGridItem } from '@/components/form/form'
import { MoneyInput } from '@/components/form/inputs'
import { Money } from '@/components/ui/money'
import { PAYABLE_DICAS, type RetentionDraft, retentionSum } from './payables'


/**
 * Retenções da nota (FIN-02 CA 4): sem cálculo; o líquido a pagar aparece ao vivo. Os campos entram direto na FormGrid da
 * seção (ISS 3 · INSS 3 · IR 3 · PIS/COFINS/CSLL 3 / líquido), sem grade dentro de grade (ADR 0044).
 */
export function RetentionFields({ value, onChange, amount, errors, disabled }: { value: RetentionDraft; onChange: (r: RetentionDraft) => void; amount: number; errors: Partial<Record<string, string>>; disabled?: boolean }) {
  const set = (patch: Partial<RetentionDraft>) => onChange({ ...value, ...patch })
  return (
    <>
      <Field label="ISS" size="sm" error={errors.retentionIss} dica={PAYABLE_DICAS.retencoes}>{(p) => <MoneyInput {...p} value={value.iss} disabled={disabled} onChange={(iss) => set({ iss })} />}</Field>
      <Field label="INSS" size="sm" error={errors.retentionInss}>{(p) => <MoneyInput {...p} value={value.inss} disabled={disabled} onChange={(inss) => set({ inss })} />}</Field>
      <Field label="IR" size="sm" error={errors.retentionIr}>{(p) => <MoneyInput {...p} value={value.ir} disabled={disabled} onChange={(ir) => set({ ir })} />}</Field>
      <Field label="PIS/COFINS/CSLL" size="sm" error={errors.retentionPcc}>{(p) => <MoneyInput {...p} value={value.pcc} disabled={disabled} onChange={(pcc) => set({ pcc })} />}</Field>
      <FormGridItem>
        <p aria-live="polite" className="rounded-md border border-border bg-muted px-3 py-2 text-sm tabular-nums">
          Valor líquido a pagar: <b><Money value={Math.max(0, amount - retentionSum(value))} /></b>
        </p>
      </FormGridItem>
    </>
  )
}
