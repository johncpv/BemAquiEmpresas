import { useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useState } from 'react'
import { setHourBankAgreement, useGetHourBankAgreement } from '@/api/generated/bemaqui'
import type { EmployeeDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DialogMessage } from '@/components/ui/dialog-message'
import { Skeleton } from '@/components/ui/states'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { todayInSaoPaulo } from './time-clock'
import { monthOf, TIMESHEET_DICAS } from './timesheet'

/**
 * "Tem acordo de banco de horas" (RH-01c CA 2): a data do acordo, vista com hr.view_sensitive e editada com hr.edit + hr.view_sensitive. O
 * banco vale a partir dela; sem acordo, extras e faltas saem como totais do período para a folha.
 */
export function HourBankAgreement({ employee }: { employee: EmployeeDetail }) {
  const canSensitive = useCan('hr.view_sensitive')
  const canEdit = useCan('hr.edit') && canSensitive
  const client = useQueryClient()
  const query = useGetHourBankAgreement(employee.id, { query: { enabled: canSensitive, retry: (count, error) => !(error instanceof ApiError && error.status < 500) && count < 2 } })
  const [editing, setEditing] = useState(false)
  const [has, setHas] = useState(false)
  const [date, setDate] = useState('')
  const sheetLink = <Link to="/app/rh/ponto/espelho/$employeeId" params={{ employeeId: employee.id }} search={{ mes: monthOf(todayInSaoPaulo()) } as never} className="text-sm font-semibold text-primary">Ver o espelho do mês</Link>
  if (!canSensitive) return <div className="rounded-md border border-border bg-card px-3 py-2">{sheetLink}</div>
  if (query.isPending) return <Skeleton className="h-16" />
  if (query.isError) return null
  const view = query.data
  return (
    <section aria-label="Banco de horas" className="flex flex-wrap items-center justify-between gap-3 rounded-md border border-border bg-card px-3 py-2">
      <div className="text-sm">
        <p className="inline-flex items-center font-semibold">Acordo de banco de horas<Dica nome="Acordo de banco de horas" texto={TIMESHEET_DICAS.acordo} /></p>
        <p className="text-muted-foreground">
          {view.agreementOn ? `Tem acordo desde ${formatDate(view.agreementOn)}.` : 'Sem acordo: extras e faltas saem como totais do período para a folha.'}
          {!view.bankEnabled ? ' O banco de horas está desligado em Configurações → Parâmetros → RH.' : ''}
        </p>
      </div>
      <span className="flex flex-wrap items-center gap-2">
        {sheetLink}
        {canEdit ? (
          <button type="button" onClick={() => { setHas(!!view.agreementOn); setDate(view.agreementOn ?? todayInSaoPaulo()); setEditing(true) }} className="min-h-11 rounded-md border border-border px-3 text-sm font-semibold">Alterar acordo</button>
        ) : null}
      </span>
      <ConfirmDialog verb="Salvar acordo" done="Acordo salvo" title={`Acordo de banco de horas de ${employee.name}`} open={editing} onOpenChange={setEditing}
        description="O banco vale a partir da data do acordo, só para períodos ainda em aberto. Confira o modelo do acordo com o jurídico."
        onConfirm={async () => {
          if (has && !date) throw new DialogMessage('Informe a data do acordo.')
          await setHourBankAgreement(employee.id, { agreementOn: has ? date : null, version: view.version })
          await client.invalidateQueries()
        }}>
        <label className="flex min-h-11 items-center gap-2 text-sm">
          <input type="checkbox" checked={has} onChange={(e) => setHas(e.target.checked)} className="size-4 accent-primary" />
          Tem acordo de banco de horas
        </label>
        {has ? (
          <Field label="Data do acordo" required>
            {(p) => <input {...p} type="date" value={date} onChange={(e) => setDate(e.target.value)} className={inputClass + ' max-w-48'} />}
          </Field>
        ) : null}
      </ConfirmDialog>
    </section>
  )
}
