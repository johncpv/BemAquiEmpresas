import { useState } from 'react'
import type { OpeningStatus } from '@/api/generated/model'
import { setOpeningCutover } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { Field } from '@/components/form/form'
import { DateInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { formatDate } from '@/lib/format'
import { today } from '../receivables/receivables'
import { OPENING_DICAS, useCanFinance } from './opening'

interface StepProps { status: OpeningStatus; refresh: () => Promise<void>; onNext: () => void }

/** Passo 1 (CA 1 e 2): a data de corte, sugerida no último dia do mês anterior; só o Proprietário define. */
export function CutoverStep({ status, refresh, onNext }: StepProps) {
  const canApprove = useCanFinance('approve')
  const [date, setDate] = useState(status.cutoverDate ?? status.suggestedCutover)
  const [error, setError] = useState<string | undefined>()
  const locked = !!status.completedAt || (status.hasMovement && !!status.cutoverDate)
  const readOnly = !canApprove || locked
  const local = !date ? 'Informe a data de corte.' : date > today() ? 'A data de corte não pode ser futura.' : undefined

  return (
    <section aria-labelledby="corte-titulo" className="max-w-xl space-y-3 rounded-md border border-border bg-card p-4">
      <h2 id="corte-titulo" className="text-[15px] font-semibold">Data de corte</h2>
      {!canApprove ? <p role="note" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">Quem define a data de corte é o Proprietário.</p> : null}
      {locked && !status.completedAt ? <p role="note" className="text-sm text-muted-foreground">A data de corte não muda depois do primeiro movimento.</p> : null}
      <Field label="Data de corte" dica={OPENING_DICAS.corte} error={error ?? local} hint="Vale a posição no fim desse dia. Sugestão: o último dia do mês anterior.">
        {(p) => <DateInput {...p} value={date} disabled={readOnly} onChange={(v) => { setError(undefined); setDate(v) }} />}
      </Field>
      <div className="flex flex-wrap justify-end gap-2">
        {status.cutoverDate ? <button type="button" onClick={onNext} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Ir para os saldos</button> : null}
        {readOnly ? null : (
          <ConfirmDialog
            verb="Definir data de corte"
            done="Data de corte definida"
            title={`Definir a data de corte em ${date ? formatDate(date) : '—'}?`}
            description="Os saldos e os títulos em aberto que você lançar a seguir valem para o fim desse dia. Depois do primeiro movimento, a data não muda mais."
            onConfirm={async () => {
              try {
                await setOpeningCutover({ cutoverDate: date })
                await refresh()
                onNext()
              } catch (e) {
                if (e instanceof ApiError && e.fields.cutoverDate) setError(e.fields.cutoverDate[0])
                throw e
              }
            }}
            trigger={<button type="button" disabled={!!local} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-50">Definir data de corte</button>}
          />
        )}
      </div>
    </section>
  )
}
