import { useState } from 'react'
import type { PaymentTermDetail, PaymentTermInput } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { PercentInput } from '@/components/form/inputs'
import { RowsEditor } from '@/components/form/rows-editor'
import { UnsavedChangesGuard } from '@/components/form/UnsavedChangesGuard'
import { type InstallmentRow, percentSum, splitEqually, toInstallments, toRows } from './terms'
import { TermPreview } from './TermPreview'

const pt = new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 4, maximumFractionDigits: 4 })

/** Por que as parcelas ainda não salvam (CA 11 e 12): a mesma regra do servidor, mostrada antes de enviar. */
function problem(rows: InstallmentRow[]): string | null {
  if (rows.length === 0) return 'Informe ao menos uma parcela.'
  const days = rows.map((r) => Number(r.days))
  if (rows.some((r) => r.days === '' || !Number.isInteger(Number(r.days)) || Number(r.days) < 0)) return 'Os dias das parcelas precisam ser 0 ou mais.'
  if (days.some((d, i) => i > 0 && d <= days[i - 1])) return 'Os dias das parcelas precisam ser crescentes.'
  const sum = percentSum(rows)
  return sum === 100 ? null : `A soma das parcelas precisa ser 100%. Hoje está em ${pt.format(sum).replace(/,?0+$/, '')}%.`
}

/** Condição em seções (CA 11 a 13): nome, parcelas no RowsEditor com "Dividir igualmente" e a prévia ao lado. */
export function PaymentTermForm({ term, canEdit, onSubmit }: { term: PaymentTermDetail | null; canEdit: boolean; onSubmit: (input: PaymentTermInput) => Promise<void> }) {
  const initial = { name: term?.name ?? '', rows: term ? toRows(term.installments) : [{ days: '30', percent: '100.0000' }] }
  const [name, setName] = useState(initial.name)
  const [rows, setRows] = useState<InstallmentRow[]>(initial.rows)
  const [tried, setTried] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const dirty = name !== initial.name || JSON.stringify(rows) !== JSON.stringify(initial.rows)
  const invalid = problem(rows)
  const nameError = name.trim().length === 0 ? 'Informe o nome da condição.' : undefined

  const save = async () => {
    setTried(true)
    if (invalid || nameError) return false
    try {
      await onSubmit({ name: name.trim(), installments: toInstallments(rows) })
      setError(null)
      return true
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar a condição.')
      return false
    }
  }

  return (
    <form id="condicao" onSubmit={(e) => { e.preventDefault(); void save() }} noValidate className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_minmax(0,22rem)]">
      <UnsavedChangesGuard when={dirty && canEdit} onSave={save} />
      <fieldset disabled={!canEdit} className="space-y-4">
        <FormError message={error ?? undefined} />
        <Field label="Nome" required error={tried ? nameError : undefined} hint="Ex.: 30/60 dias, Entrada + 30.">
          {(p) => <input {...p} value={name} onChange={(e) => setName(e.target.value)} maxLength={100} className={inputClass} />}
        </Field>
        <div className="space-y-2">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h2 className="text-base font-semibold">Parcelas</h2>
            <button type="button" onClick={() => setRows(splitEqually(rows))} disabled={rows.length === 0} className="rounded-md border border-border px-3 py-1.5 text-sm font-semibold">Dividir igualmente</button>
          </div>
          <RowsEditor noun="parcela" rows={rows} onChange={setRows} min={1} max={24} disabled={!canEdit}
            newRow={() => ({ days: String((Number(rows.at(-1)?.days) || 0) + 30), percent: null })}
            headers={['Dias a partir da data base', 'Percentual']}
            summary={`Soma: ${pt.format(percentSum(rows))}%`}
            renderRow={(row, index, update) => [
              <input key="d" aria-label={`Dias da parcela ${index + 1}`} value={row.days} onChange={(e) => update({ days: e.target.value.replace(/\D/g, '') })} inputMode="numeric" className={inputClass} />,
              <PercentInput key="p" aria-label={`Percentual da parcela ${index + 1}`} scale={4} value={row.percent} onChange={(v) => update({ percent: v })} />,
            ]} />
          {tried && invalid ? <p role="alert" className="text-sm text-crit-ink">{invalid}</p> : null}
        </div>
      </fieldset>
      <TermPreview installments={toInstallments(rows)} valid={!invalid} />
    </form>
  )
}
